"""
Carga precios.xls en las listas de precios de POS-Ventas.

El archivo trae UNA fila por artículo y una columna por lista (azul / roja / folder), con el
precio de la UNIDAD SUELTA. Cada presentación se valoriza multiplicando por sus unidades por
bulto — la misma regla que usa el editor de precios de la app (Pos.Domain.Services.PrecioPorBulto).

Cómo se determinó que el precio es unitario y no del bulto: el mismo aceite AC NATURA aparece en
4 packs distintos (15x0.9L, 12x1.5L, 6x3L, 4x5L) y solo bajo la hipótesis "por unidad" el precio
por litro converge (~$3.800–4.000/L); bajo "por bulto" da entre $267 y $950/L.

Un 0 significa "sin precio" (indicado por el usuario) y no se carga.

Uso:  python importar_precios.py [--dry-run]
"""
import sys
from datetime import datetime, timezone
from decimal import Decimal, ROUND_HALF_UP

import pandas as pd
import pyodbc
import xlrd

ORIGEN = r"D:\Escritorio\precios.xls"

CONN = ("DRIVER={ODBC Driver 17 for SQL Server};SERVER=192.168.4.9;DATABASE=POS-Ventas;"
        "UID=claude;PWD=Claude*2026;TrustServerCertificate=yes;Encrypt=yes")

# columna del archivo -> CodigoInterno de la lista en la BD
LISTAS = {"azul": "AZUL", "roja": "ROJA", "folder": "FOLDER AGO"}

AHORA = datetime.now(timezone.utc).replace(tzinfo=None)
POR = "import:precios.xls"


def redondear(valor):
    """2 decimales, medio hacia arriba: igual que PrecioPorBulto en el dominio."""
    return Decimal(str(valor)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def leer_precios():
    sh = xlrd.open_workbook(ORIGEN).sheet_by_index(0)
    filas = [[sh.cell_value(r, c) for c in range(sh.ncols)] for r in range(1, sh.nrows)]
    df = pd.DataFrame(filas, columns=["codigo", "descrip", "azul", "roja", "folder"])
    # El código viene relleno con ceros a 13 dígitos; el CodigoInterno del artículo no los lleva.
    df["cod"] = df["codigo"].astype(str).str.strip().str.lstrip("0")
    # Un único código repetido en el archivo (19926) con distinto precio ROJA: gana el último.
    df = df.drop_duplicates(subset=["cod"], keep="last")
    return df


def main(dry_run=False):
    print("Leyendo precios.xls…")
    precios = leer_precios()
    print(f"  {len(precios):,} artículos con fila de precio")

    cx = pyodbc.connect(CONN, autocommit=False)
    cur = cx.cursor()
    try:
        # Listas destino
        cur.execute("SELECT CodigoInterno, IdListaPrecio FROM ListasPrecios")
        id_lista = {c: i for c, i in cur.fetchall()}
        faltan = [n for n in LISTAS.values() if n not in id_lista]
        if faltan:
            raise SystemExit(f"No existen estas listas en la BD: {faltan}")

        # Presentaciones por CodigoInterno de artículo
        cur.execute("""SELECT a.CodigoInterno, a.IdArticulo, p.IdPresentacion, p.UnidadXBulto
                       FROM Articulos a JOIN Presentaciones p ON p.IdArticulo = a.IdArticulo""")
        pres = {}
        for cod, id_art, id_pres, uxb in cur.fetchall():
            pres.setdefault(cod, []).append((id_art, id_pres, float(uxb)))
        print(f"  {len(pres):,} artículos en el catálogo con presentaciones")

        sin_articulo = precios[~precios["cod"].isin(pres.keys())]
        con_precio = sin_articulo[(sin_articulo.azul > 0) | (sin_articulo.roja > 0) | (sin_articulo.folder > 0)]
        print(f"  {len(sin_articulo):,} filas sin artículo en el catálogo "
              f"({len(con_precio):,} de ellas traían precio y se pierden)")

        filas = []
        por_lista = {}
        for col, nombre in LISTAS.items():
            id_l = id_lista[nombre]
            n = 0
            for fila in precios.itertuples():
                unitario = getattr(fila, col)
                if not unitario or unitario <= 0:      # 0 = sin precio
                    continue
                for id_art, id_pres, uxb in pres.get(fila.cod, ()):
                    filas.append((id_l, id_pres, id_art, float(redondear(unitario * uxb)), 0.0, AHORA, POR))
                    n += 1
            por_lista[nombre] = n
            print(f"  {nombre}: {n:,} precios de presentación")

        print(f"\nTotal a insertar: {len(filas):,}")
        if dry_run:
            print("--dry-run: no se toca la base.")
            return

        cur.execute("DELETE FROM Precios")
        print(f"Precios previos borrados: {cur.rowcount}")

        cur.fast_executemany = True
        sql = ("INSERT INTO Precios (IdListaPrecio, IdPresentacion, IdArticulo, PrecioFinal, "
               "ImpuestoInterno, CreatedAtUtc, CreatedBy) VALUES (?,?,?,?,?,?,?)")
        for i in range(0, len(filas), 2000):
            cur.executemany(sql, filas[i:i + 2000])

        cx.commit()
        print("\nOK — commit hecho.")
    except Exception:
        cx.rollback()
        print("\nERROR — rollback, la base quedó como estaba.")
        raise
    finally:
        cx.close()


if __name__ == "__main__":
    main(dry_run="--dry-run" in sys.argv)
