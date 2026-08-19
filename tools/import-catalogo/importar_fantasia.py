"""
Carga el nombre de fantasía de los clientes desde tools/fantasia.csv (export del ERP).

El archivo trae una fila por cliente (`codigo`,`nomfant`) y se cruza con `Clientes.CodigoInt`,
que conserva el código del ERP tal cual (con los ceros a la izquierda: "05102").

Trampas del archivo, ya resueltas acá:
  - **No es UTF-8**: viene en cp1252 (las Ñ y los apóstrofes ´ se rompen si se lee como UTF-8).
    A diferencia del Articulos.xlsx, este archivo SÍ conserva las Ñ, así que no hay que corregirlas.
  - La enorme mayoría de las filas no tiene nombre de fantasía y el ERP lo representa de varias
    formas: vacío, ".", "-", "..", "*", "@", "1". Se descarta todo lo que no tenga ninguna letra.
  - Hay ~2.800 códigos repetidos, pero ninguno con dos valores distintos (se verificó): se toma
    el primero con contenido.
  - Hay algún valor con caracteres de control (\\x02) al principio.

Uso:  python importar_fantasia.py [--dry-run]

Requiere la migración `NombreFantasiaCliente` aplicada.
"""
import csv
import sys
import unicodedata
from datetime import datetime, timezone

import pyodbc

ORIGEN = r"..\fantasia.csv"
CONN = ("DRIVER={ODBC Driver 17 for SQL Server};SERVER=192.168.4.9;DATABASE=POS-Ventas;"
        "UID=claude;PWD=Claude*2026;TrustServerCertificate=yes;Encrypt=yes")

AHORA = datetime.now(timezone.utc).replace(tzinfo=None)
POR = "import:fantasia.csv"
LARGO_MAX = 60  # igual que Cliente.NombreFantasia en el modelo


def normalizar(valor: str) -> str | None:
    """Devuelve el nombre de fantasía limpio, o None si la fila no tiene uno de verdad."""
    v = "".join(ch for ch in valor if unicodedata.category(ch)[0] != "C").strip()
    # Sin ninguna letra no es un nombre: son los rellenos del ERP (".", "-", "*", "@", "1"…).
    if not any(ch.isalpha() for ch in v):
        return None
    return v[:LARGO_MAX]


def leer():
    with open(ORIGEN, encoding="cp1252", newline="") as f:
        lector = csv.DictReader(f)
        if lector.fieldnames != ["codigo", "nomfant"]:
            raise AssertionError(f"Cabecera inesperada: {lector.fieldnames}")

        valores: dict[str, str] = {}
        conflictos, filas = [], 0
        for fila in lector:
            filas += 1
            cod = (fila["codigo"] or "").strip()
            nom = normalizar(fila["nomfant"] or "")
            if not cod or nom is None:
                continue
            if cod in valores and valores[cod] != nom:
                conflictos.append((cod, valores[cod], nom))
                continue  # gana el primero
            valores[cod] = nom
    return filas, valores, conflictos


def main(dry_run=False):
    filas, valores, conflictos = leer()
    print(f"Archivo: {filas:,} filas, {len(valores):,} con nombre de fantasía.")
    if conflictos:
        print(f"  ! {len(conflictos)} código(s) repetidos con valores distintos (gana el primero):")
        for c in conflictos[:10]:
            print(f"    {c[0]}: '{c[1]}' vs '{c[2]}'")

    cx = pyodbc.connect(CONN, autocommit=False)
    cur = cx.cursor()
    try:
        cur.execute("SELECT IdCliente, CodigoInt, NombreFantasia FROM Clientes")
        actuales = {r.CodigoInt.strip(): (r.IdCliente, r.NombreFantasia) for r in cur.fetchall()}
        print(f"Clientes en la BD: {len(actuales):,}")

        cambios = []
        for cod, nom in valores.items():
            fila = actuales.get(cod)
            if fila is None:
                continue
            id_cliente, actual = fila
            if (actual or "") != nom:
                cambios.append((nom, AHORA, POR, id_cliente))

        sin_cliente = [c for c in valores if c not in actuales]
        print(f"A actualizar: {len(cambios):,} | ya estaban iguales: {len(valores) - len(cambios) - len(sin_cliente):,}"
              f" | del archivo sin cliente en la BD: {len(sin_cliente):,}")
        if sin_cliente:
            print("  ejemplos sin cliente:", sorted(sin_cliente)[:10])

        cur.fast_executemany = True
        sql = "UPDATE Clientes SET NombreFantasia = ?, UpdatedAtUtc = ?, UpdatedBy = ? WHERE IdCliente = ?"
        for i in range(0, len(cambios), 1000):
            cur.executemany(sql, cambios[i:i + 1000])

        if dry_run:
            cx.rollback()
            print("\n--dry-run: se revirtió todo, la base quedó igual.")
            return

        cx.commit()
        cur.execute("SELECT COUNT(*) FROM Clientes WHERE NombreFantasia IS NOT NULL")
        print(f"\nListo. Clientes con nombre de fantasía: {cur.fetchone()[0]:,}")
    except Exception:
        cx.rollback()
        print("\nERROR: se revirtió todo.")
        raise
    finally:
        cx.close()


if __name__ == "__main__":
    main(dry_run="--dry-run" in sys.argv)
