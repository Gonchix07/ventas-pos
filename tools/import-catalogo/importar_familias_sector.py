"""
Relaciona cada Familia con su Sector a partir de familia.xls (tabla maestra del ERP).

Contexto: hasta ahora `Familias` era una tabla plana {IdFamilia, Descripcion}. La relación con el
sector existía de hecho (el import de artículos creó UNA familia por par sector+familia) pero no
estaba guardada, así que los combos del ABM mostraban las 153 familias juntas, con nombres repetidos
entre sectores (DESODORANTES está en PERFUMERIA y en LIMPIEZA; POR KILO dos veces en VERDULERIA).

Qué hace, todo en UNA transacción:
  1. Da de alta los sectores del archivo que falten (con IDENTITY_INSERT, para conservar el código
     del ERP como IdSector, igual que hizo el import del catálogo).
  2. Completa `Familias.IdSector` de las familias existentes. El sector NO se deduce por nombre
     (se repiten): se toma de los artículos ya cargados, que son la fuente de verdad de con qué
     par sector+familia se creó cada fila. Una familia usada en más de un sector queda en NULL
     (hoy solo pasa con "SIN FAMILIA", que es el cajón de los artículos sin clasificar).
  3. Da de alta las familias del archivo que no existan en la BD (las que todavía no tienen ningún
     artículo). El par (sector, nombre) es la clave para saber si ya está.

Uso:  python importar_familias_sector.py [--dry-run]

Requiere que la migración `FamiliaPorSector` ya esté aplicada (crea la columna IdSector).
"""
import sys
from datetime import datetime, timezone

import pyodbc
import xlrd

ORIGEN = "familia.xls"
CONN = ("DRIVER={ODBC Driver 17 for SQL Server};SERVER=192.168.4.9;DATABASE=POS-Ventas;"
        "UID=claude;PWD=Claude*2026;TrustServerCertificate=yes;Encrypt=yes")

AHORA = datetime.now(timezone.utc).replace(tzinfo=None)
POR = "import:familia.xls"


def leer_archivo():
    """[(cod_sector, sector, cod_familia, familia)] desde el .xls (formato viejo BIFF -> xlrd)."""
    sh = xlrd.open_workbook(ORIGEN).sheet_by_index(0)
    cab = [str(c).strip().lower() for c in sh.row_values(0)]
    esperado = ["cod_familia", "familia", "cod_sector", "sector"]
    if cab != esperado:
        raise AssertionError(f"Cabecera inesperada: {cab} (esperaba {esperado})")

    filas, vistos = [], set()
    for i in range(1, sh.nrows):
        cf, fam, cs, sec = sh.row_values(i)
        # Los códigos vienen como texto con ceros a la izquierda ("014").
        cf, cs = int(str(cf).strip()), int(str(cs).strip())
        fam, sec = str(fam).strip(), str(sec).strip()
        if not fam or not sec:
            raise AssertionError(f"Fila {i + 1} con familia o sector vacío: {sh.row_values(i)}")
        if (cs, cf) in vistos:
            raise AssertionError(f"Par sector={cs} familia={cf} duplicado en el archivo (fila {i + 1})")
        vistos.add((cs, cf))
        filas.append((cs, sec, cf, fam))
    return filas


def main(dry_run=False):
    filas = leer_archivo()
    sectores_arch = {cs: sec for cs, sec, _, _ in filas}
    print(f"Archivo: {len(filas)} pares familia+sector, {len(sectores_arch)} sectores.")

    cx = pyodbc.connect(CONN, autocommit=False)
    cur = cx.cursor()
    try:
        cur.execute("SELECT IdSector, Descripcion FROM Sectores")
        sectores_bd = {r.IdSector: r.Descripcion for r in cur.fetchall()}

        # --- 1) Sectores faltantes -------------------------------------------------------------
        faltan_sec = sorted(set(sectores_arch) - set(sectores_bd))
        if faltan_sec:
            print("\nSectores nuevos:")
            cur.execute("SET IDENTITY_INSERT Sectores ON")
            for cs in faltan_sec:
                print(f"  + {cs} {sectores_arch[cs]}")
                cur.execute("INSERT INTO Sectores (IdSector, Descripcion, CreatedAtUtc, CreatedBy) "
                            "VALUES (?,?,?,?)", cs, sectores_arch[cs], AHORA, POR)
                sectores_bd[cs] = sectores_arch[cs]
            cur.execute("SET IDENTITY_INSERT Sectores OFF")
        else:
            print("\nSectores: no falta ninguno.")

        for cs, nombre in sorted(sectores_arch.items()):
            if sectores_bd.get(cs) != nombre:
                print(f"  ! sector {cs}: archivo='{nombre}' bd='{sectores_bd.get(cs)}' "
                      f"(se respeta el nombre de la BD)")

        # --- 2) Relación de las familias existentes ---------------------------------------------
        # El sector sale de los artículos: es con lo que se armó cada familia en el import.
        cur.execute("""SELECT IdFamilia, MIN(IdSector) AS IdSector, COUNT(DISTINCT IdSector) AS Sectores
                       FROM Articulos GROUP BY IdFamilia""")
        sector_de = {r.IdFamilia: (r.IdSector, r.Sectores) for r in cur.fetchall()}

        cur.execute("SELECT IdFamilia, Descripcion, IdSector FROM Familias ORDER BY IdFamilia")
        familias_bd = cur.fetchall()

        actualizadas, ambiguas, sin_articulos = 0, [], []
        for f in familias_bd:
            info = sector_de.get(f.IdFamilia)
            if info is None:
                sin_articulos.append(f)
                continue
            id_sector, cuantos = info
            if cuantos > 1:
                ambiguas.append((f, cuantos))
                continue
            if f.IdSector != id_sector:
                cur.execute("UPDATE Familias SET IdSector = ?, UpdatedAtUtc = ?, UpdatedBy = ? "
                            "WHERE IdFamilia = ?", id_sector, AHORA, POR, f.IdFamilia)
                actualizadas += 1

        print(f"\nFamilias relacionadas: {actualizadas} actualizada(s) de {len(familias_bd)}.")
        for f, cuantos in ambiguas:
            print(f"  ~ {f.IdFamilia} '{f.Descripcion}': usada en {cuantos} sectores -> queda sin sector")
        for f in sin_articulos:
            print(f"  ~ {f.IdFamilia} '{f.Descripcion}': sin artículos -> no se puede deducir, "
                  f"queda como está ({f.IdSector})")

        # --- 3) Familias del archivo que no están en la BD --------------------------------------
        cur.execute("SELECT IdSector, UPPER(LTRIM(RTRIM(Descripcion))) AS Nombre FROM Familias "
                    "WHERE IdSector IS NOT NULL")
        existentes = {(r.IdSector, r.Nombre) for r in cur.fetchall()}

        nuevas = [(cs, fam) for cs, _, _, fam in filas if (cs, fam.upper()) not in existentes]
        if nuevas:
            print(f"\nFamilias nuevas ({len(nuevas)}):")
            for cs, fam in nuevas:
                print(f"  + [{cs} {sectores_bd[cs]}] {fam}")
                cur.execute("INSERT INTO Familias (Descripcion, IdSector, CreatedAtUtc, CreatedBy) "
                            "VALUES (?,?,?,?)", fam, cs, AHORA, POR)
        else:
            print("\nFamilias nuevas: ninguna.")

        # Las que están en la BD pero no en el archivo: se informan, no se tocan (tienen artículos).
        del_archivo = {(cs, fam.upper()) for cs, _, _, fam in filas}
        cur.execute("SELECT IdFamilia, Descripcion, IdSector FROM Familias WHERE IdSector IS NOT NULL "
                    "AND CreatedBy <> ?", POR)
        huerfanas = [r for r in cur.fetchall()
                     if (r.IdSector, r.Descripcion.strip().upper()) not in del_archivo]
        if huerfanas:
            print(f"\nFamilias de la BD que no figuran en el archivo ({len(huerfanas)}, se dejan como están):")
            for r in huerfanas:
                print(f"  ~ {r.IdFamilia} [{r.IdSector} {sectores_bd.get(r.IdSector)}] {r.Descripcion}")

        if dry_run:
            cx.rollback()
            print("\n--dry-run: se revirtió todo, la base quedó igual.")
            return

        cx.commit()
        print("\nListo. Commit.")

        cur.execute("SELECT COUNT(*) FROM Familias")
        total = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM Familias WHERE IdSector IS NULL")
        sin_sector = cur.fetchone()[0]
        print(f"Familias: {total} ({total - sin_sector} con sector, {sin_sector} sin sector)")
    except Exception:
        cx.rollback()
        print("\nERROR: se revirtió todo.")
        raise
    finally:
        cx.close()


if __name__ == "__main__":
    main(dry_run="--dry-run" in sys.argv)
