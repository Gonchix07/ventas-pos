"""
Carga el catálogo transformado en POS-Ventas.

Todo corre en UNA transacción: si algo falla no queda el catálogo a medias.
Se usa IDENTITY_INSERT para conservar los códigos del ERP como IdSector/IdLinea/IdArticulo,
así el número que ve el usuario en el sistema viejo sigue siendo el mismo acá.

Uso:  python importar.py [--dry-run]
"""
import sys
from datetime import datetime, timezone

import pyodbc

import transformar as T

CONN = ("DRIVER={ODBC Driver 17 for SQL Server};SERVER=192.168.4.9;DATABASE=POS-Ventas;"
        "UID=claude;PWD=Claude*2026;TrustServerCertificate=yes;Encrypt=yes")

AHORA = datetime.now(timezone.utc).replace(tzinfo=None)
POR = "import:Articulos.xlsx"


def lote(cur, sql, filas, tam=1000):
    """Inserta en tandas; fast_executemany hace un solo round-trip por tanda."""
    cur.fast_executemany = True
    for i in range(0, len(filas), tam):
        cur.executemany(sql, filas[i:i + tam])


def main(dry_run=False):
    print("Transformando el Excel…")
    df = T.cargar()
    arts, pres, barr, sec, lin, fam, rep = T.construir(df)
    print(f"  sectores={len(sec)} lineas={len(lin)} familias={len(fam)} "
          f"articulos={len(arts):,} presentaciones={len(pres):,} barras={len(barr):,}")

    if dry_run:
        print("\n--dry-run: no se toca la base.")
        return

    cx = pyodbc.connect(CONN, autocommit=False)
    cur = cx.cursor()
    try:
        print("\nBorrando el catálogo de prueba…")
        for tabla in ("Precios", "Barras", "Presentaciones", "Articulos", "Sectores", "Lineas", "Familias"):
            cur.execute(f"DELETE FROM {tabla}")
            print(f"  {tabla}: {cur.rowcount} fila(s) borradas")

        print("\nInsertando lookups…")
        for tabla, col, datos in (
            ("Sectores", "IdSector", sec),
            ("Lineas", "IdLinea", lin),
            ("Familias", "IdFamilia", fam),
        ):
            cur.execute(f"SET IDENTITY_INSERT {tabla} ON")
            lote(cur, f"INSERT INTO {tabla} ({col}, Descripcion, CreatedAtUtc, CreatedBy) VALUES (?,?,?,?)",
                 [(int(r[0]), str(r[1]), AHORA, POR) for r in datos.itertuples(index=False)])
            cur.execute(f"SET IDENTITY_INSERT {tabla} OFF")
            print(f"  {tabla}: {len(datos):,}")

        print("\nInsertando artículos…")
        cur.execute("SET IDENTITY_INSERT Articulos ON")
        lote(cur, """INSERT INTO Articulos
                (IdArticulo, CodigoInterno, Descripcion, IdSector, IdLinea, IdFamilia, IdModoIva,
                 Activo, UnidadMedida, ContenidoNetoUnitario, CreatedAtUtc, CreatedBy)
                VALUES (?,?,?,?,?,?,?,?,?,?,?,?)""",
             [(int(r.IdArticulo), r.CodigoInterno, r.Descripcion, int(r.IdSector), int(r.IdLinea),
               int(r.IdFamilia), int(r.IdModoIva), int(r.Activo), int(r.UnidadMedida),
               None if r.ContenidoNetoUnitario != r.ContenidoNetoUnitario else float(r.ContenidoNetoUnitario),
               AHORA, POR)
              for r in arts.itertuples(index=False)])
        cur.execute("SET IDENTITY_INSERT Articulos OFF")
        print(f"  Articulos: {len(arts):,}")

        print("Insertando presentaciones…")
        cur.execute("SET IDENTITY_INSERT Presentaciones ON")
        lote(cur, """INSERT INTO Presentaciones
                (IdPresentacion, IdArticulo, UnidadXBulto, DescripcionTicket, CreatedAtUtc, CreatedBy)
                VALUES (?,?,?,?,?,?)""",
             [(int(r.IdPresentacion), int(r.IdArticulo), float(r.UnidadXBulto),
               r.DescripcionTicket or None, AHORA, POR)
              for r in pres.itertuples(index=False)])
        cur.execute("SET IDENTITY_INSERT Presentaciones OFF")
        print(f"  Presentaciones: {len(pres):,}")

        print("Insertando códigos de barra…")
        cur.execute("SET IDENTITY_INSERT Barras ON")
        lote(cur, """INSERT INTO Barras (IdBarra, IdPresentacion, CodigoBarra, Tipo, CreatedAtUtc, CreatedBy)
                VALUES (?,?,?,?,?,?)""",
             [(int(r.IdBarra), int(r.IdPresentacion), str(r.CodigoBarra), int(r.Tipo), AHORA, POR)
              for r in barr.itertuples(index=False)])
        cur.execute("SET IDENTITY_INSERT Barras OFF")
        print(f"  Barras: {len(barr):,}")

        # Reposicionar los identity para que las altas nuevas no choquen con lo importado.
        print("\nReposicionando identities…")
        for tabla in ("Sectores", "Lineas", "Familias", "Articulos", "Presentaciones", "Barras"):
            cur.execute(f"DBCC CHECKIDENT ('{tabla}', RESEED)")

        cx.commit()
        print("\nOK — commit hecho.")
    except Exception:
        cx.rollback()
        print("\nERROR — se hizo rollback, la base quedó como estaba.")
        raise
    finally:
        cx.close()


if __name__ == "__main__":
    main(dry_run="--dry-run" in sys.argv)
