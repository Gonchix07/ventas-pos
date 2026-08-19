"""
Transforma D:\Escritorio\Articulos.xlsx al modelo del POS.

Decisiones tomadas con el usuario (2026-08-10):
 - Familias SEPARADAS por sector (152 pares sector+familia, nombres repetidos permitidos).
 - Artículos SUSPENDIDO se importan con Activo = 0.
 - Se borran los datos de prueba previos del catálogo.
 - Se corrigen solo las Ñ inequívocas.

Regla de presentaciones (indicada por el usuario):
 - código EAN → presentación de 1 unidad
 - código DUN → presentación de `unidxbulto` unidades
"""
import re
import pandas as pd

ORIGEN = r"D:\Escritorio\Articulos.xlsx"

# --- Correcciones de Ñ: solo patrones inequívocos, para no romper guiones legítimos
# como "MED-NARAN". Se aplican sobre el texto en mayúsculas del archivo.
FIX_ENIE = [
    (r"\bPA-O\b", "PAÑO"), (r"\bPA-OS\b", "PAÑOS"),
    (r"\bBA-O\b", "BAÑO"), (r"\bBA-OS\b", "BAÑOS"), (r"\bBA-ERA\b", "BAÑERA"),
    (r"\bA-O\b", "AÑO"), (r"\bA-OS\b", "AÑOS"), (r"\bA-EJO\b", "AÑEJO"),
    (r"CA-UELAS", "CAÑUELAS"), (r"\bCA-A\b", "CAÑA"), (r"\bCA-ITAS?\b", lambda m: m.group(0).replace("-", "Ñ")),
    (r"\bPE-A", "PEÑA"), (r"\bSE-OR", "SEÑOR"), (r"\bNI-O", "NIÑO"), (r"\bNI-A", "NIÑA"),
    (r"\bMU-ECA", "MUÑECA"), (r"\bMO-O\b", "MOÑO"), (r"\bDU-A", "DUÑA"),
    (r"\bESPA-A\b", "ESPAÑA"), (r"\bESPA-OL", "ESPAÑOL"),
    (r"\bVI-A\b", "VIÑA"), (r"\bVI-EDO", "VIÑEDO"), (r"\bPI-A\b", "PIÑA"),
]


def corregir_enie(texto):
    """Devuelve (texto_corregido, cambio_bool)."""
    if not isinstance(texto, str):
        return texto, False
    original = texto
    for patron, reemplazo in FIX_ENIE:
        texto = re.sub(patron, reemplazo, texto)
    return texto, texto != original


# unidad_MTdida → enum UnidadMedida (0=Ninguna, 1=Kilogramo, 2=Litro).
# `contenido` ya viene expresado en Kg/Lt aunque la etiqueta diga GR/CC (verificado:
# un artículo de "500 GR" trae contenido 0.50), así que GR→Kg y ML/CC→Lt sin convertir.
UNIDAD = {"KG": 1, "GR": 1, "LT": 2, "ML": 2, "CC": 2}

IVA = {"21%": 1, "10,5%": 2}


def normalizar_barra(v):
    """
    Limpia el código de barra tal como viene del ERP: espacios sobrantes (698 casos), espacios
    duros U+00A0 (4) y una barra final suelta. NO se tocan los puntos ni los guiones: hay códigos
    internos legítimos con formato `000-000-0559`.
    """
    s = str(v).replace("\xa0", " ").strip().rstrip("/").strip()
    return s


def cargar():
    # `codigo` se lee como texto: hay códigos con ceros a la izquierda y con guiones que se
    # perderían/romperían al convertirlos a número.
    df = pd.read_excel(ORIGEN, sheet_name="articulos", dtype={"codigo": object})
    df["codigo"] = df["codigo"].map(normalizar_barra)
    for c in ("desc_articulo", "desc_corta", "linea", "sector", "familia", "estado", "tipo"):
        df[c] = df[c].astype("string").str.strip()
    # El código 0 con el nombre en blanco también significa "sin clasificar" (3 artículos):
    # se unifica con los nulos para que no quede un sector/familia fantasma con id 0.
    for c in ("cod_sector", "cod_familia"):
        df.loc[df[c] == 0, c] = pd.NA
    return df


def construir(df):
    reporte = {}

    # ---------- Correcciones de texto ----------
    cambios_texto = []
    for col in ("desc_articulo", "desc_corta", "linea", "sector", "familia"):
        nuevos, flags = zip(*df[col].map(lambda v: corregir_enie(v if isinstance(v, str) else v)))
        antes = df[col].copy()
        df[col] = list(nuevos)
        for a, b, f in zip(antes, nuevos, flags):
            if f:
                cambios_texto.append((col, a, b))
    reporte["cambios_enie"] = sorted(set(cambios_texto))

    # ---------- Lookups ----------
    sectores = (df[["cod_sector", "sector"]].dropna().drop_duplicates()
                .astype({"cod_sector": int}).sort_values("cod_sector"))
    lineas = (df[["cod_linea", "linea"]].dropna().drop_duplicates()
              .astype({"cod_linea": int}).sort_values("cod_linea"))
    # Familia = par (sector, familia): el código de familia se repite entre sectores.
    familias = (df[["cod_sector", "cod_familia", "familia"]].dropna().drop_duplicates()
                .astype({"cod_sector": int, "cod_familia": int})
                .sort_values(["cod_sector", "cod_familia"]).reset_index(drop=True))
    familias["IdFamilia"] = familias.index + 1
    fam_key = {(r.cod_sector, r.cod_familia): r.IdFamilia for r in familias.itertuples()}

    # Fila "sin clasificar" para los artículos sin sector/familia.
    ID_SECTOR_NA = 999
    ID_LINEA_NA = 9999
    ID_FAMILIA_NA = len(familias) + 1

    # ---------- Artículos ----------
    # El IdArticulo es una secuencia propia: 250 códigos del ERP (carnicería y productos por kilo)
    # superan el máximo de un int (llegan a 60.000.003.133), así que no sirven como PK. El código
    # original se conserva en CodigoInterno, que es texto.
    art = df.drop_duplicates("cod_articulo").sort_values("cod_articulo").copy()
    art["_id"] = range(1, len(art) + 1)
    id_por_cod = dict(zip(art["cod_articulo"], art["_id"]))

    um = art["unidad_MTdida"].astype("string").str.upper().str.strip()
    articulos = pd.DataFrame({
        "IdArticulo": art["_id"],
        "CodigoInterno": art["cod_articulo"].astype("int64").astype(str),
        "Descripcion": art["desc_articulo"].fillna("SIN DESCRIPCION"),
        "IdSector": art["cod_sector"].fillna(ID_SECTOR_NA).astype(int),
        "IdLinea": art["cod_linea"].fillna(ID_LINEA_NA).astype(int),
        "IdFamilia": [
            fam_key.get((s, f), ID_FAMILIA_NA) if pd.notna(s) and pd.notna(f) else ID_FAMILIA_NA
            for s, f in zip(art["cod_sector"], art["cod_familia"])
        ],
        "IdModoIva": art["iva"].map(IVA).fillna(1).astype(int),
        "Activo": (art["estado"] == "ACTIVO").astype(int),
        "UnidadMedida": um.map(UNIDAD).fillna(0).astype(int),
        "ContenidoNetoUnitario": art["contenido"].where(art["contenido"] > 0),
    })
    articulos["Descripcion"] = articulos["Descripcion"].str.slice(0, 400)
    articulos["CodigoInterno"] = articulos["CodigoInterno"].str.slice(0, 30)

    # ---------- Presentaciones y barras ----------
    # EAN → 1 unidad ; DUN → unidxbulto. Un DUN con unidxbulto=1 NO crea una presentación
    # aparte (sería idéntica a la del EAN): su código se suma a la misma presentación.
    uxb_art = dict(zip(art["cod_articulo"], art["unidxbulto"].astype(int)))
    ticket = dict(zip(art["cod_articulo"],
                      art["desc_corta"].fillna(art["desc_articulo"]).fillna("")))

    # Barras: la BD tiene índice ÚNICO en CodigoBarra, así que hay que resolver los repetidos.
    df_b = df[["cod_articulo", "codigo", "tipo", "unidxbulto"]].copy()

    # Cuando el MISMO código está en artículos distintos suele ser el mismo producto cargado dos
    # veces (código viejo y nuevo). Se conserva en el artículo de código MÁS ALTO, que en este ERP
    # es el más reciente, y se informa para poder revisarlo a mano.
    dup_entre_art = (df_b.groupby("codigo").cod_articulo.nunique() > 1)
    conflictivos = set(dup_entre_art[dup_entre_art].index)
    reporte["barras_en_varios_articulos"] = sorted(
        (cod, sorted(df_b[df_b.codigo == cod].cod_articulo.unique()))
        for cod in conflictivos
    )

    antes = len(df_b)
    df_b = (df_b.sort_values(["codigo", "cod_articulo"], ascending=[True, False])
                .drop_duplicates(subset=["codigo"], keep="first"))
    reporte["barras_duplicadas_descartadas"] = antes - len(df_b)

    presentaciones = []   # (IdPresentacion, IdArticulo, UnidadXBulto, DescripcionTicket)
    barras = []           # (IdPresentacion, CodigoBarra, Tipo)
    pres_id = 0
    pres_por_art = {}     # cod_articulo -> {unidad_x_bulto: id_presentacion}

    for cod_art, grupo in df_b.groupby("cod_articulo", sort=True):
        id_art = id_por_cod[cod_art]
        uxb_bulto = max(int(uxb_art.get(cod_art, 1)), 1)
        mapa = {}
        for fila in grupo.itertuples():
            unidades = 1 if fila.tipo == "EAN" else uxb_bulto
            if unidades not in mapa:
                pres_id += 1
                mapa[unidades] = pres_id
                sufijo = "" if unidades == 1 else f" X{unidades}"
                presentaciones.append((pres_id, id_art, unidades,
                                       (str(ticket.get(cod_art, ""))[:40] + sufijo).strip()))
            barras.append((mapa[unidades], fila.codigo, 1 if fila.tipo == "EAN" else 2))
        pres_por_art[id_art] = mapa

    # Artículos que quedaron sin ninguna presentación (no tenían filas de barra utilizables).
    sin_pres = set(articulos["IdArticulo"]) - set(pres_por_art)
    cod_por_id = {v: k for k, v in id_por_cod.items()}
    for id_art in sorted(sin_pres):
        pres_id += 1
        presentaciones.append((pres_id, id_art, 1,
                               str(ticket.get(cod_por_id[id_art], ""))[:40].strip()))
    reporte["articulos_sin_barras_con_presentacion_default"] = len(sin_pres)

    presentaciones = pd.DataFrame(presentaciones,
                                  columns=["IdPresentacion", "IdArticulo", "UnidadXBulto", "DescripcionTicket"])
    barras = pd.DataFrame(barras, columns=["IdPresentacion", "CodigoBarra", "Tipo"])
    barras.insert(0, "IdBarra", range(1, len(barras) + 1))

    # Lookups finales, con las filas "sin clasificar".
    # OJO: hay que reset_index() antes de concatenar. Estos frames arrastran el índice original
    # del Excel, así que un `.loc[len(df)]` no agrega una fila: PISA la que tenga esa etiqueta.
    def con_fallback(datos, col_id, col_desc, id_na, desc_na, usado):
        out = (pd.DataFrame({col_id: datos[col_id], "Descripcion": datos[col_desc]})
               .reset_index(drop=True))
        if usado:
            out = pd.concat([out, pd.DataFrame([{col_id: id_na, "Descripcion": desc_na}])],
                            ignore_index=True)
        return out

    sectores_out = con_fallback(sectores.rename(columns={"cod_sector": "IdSector"}),
                                "IdSector", "sector", ID_SECTOR_NA, "SIN SECTOR",
                                (articulos["IdSector"] == ID_SECTOR_NA).any())
    lineas_out = con_fallback(lineas.rename(columns={"cod_linea": "IdLinea"}),
                              "IdLinea", "linea", ID_LINEA_NA, "SIN LINEA",
                              (articulos["IdLinea"] == ID_LINEA_NA).any())
    familias_out = con_fallback(familias, "IdFamilia", "familia",
                                ID_FAMILIA_NA, "SIN FAMILIA",
                                (articulos["IdFamilia"] == ID_FAMILIA_NA).any())

    # Red de seguridad: ningún artículo puede apuntar a un lookup que no exista (fue justo lo que
    # falló contra la BD la primera vez, por el bug del índice de arriba).
    for nombre, col, validos in (("sector", "IdSector", set(sectores_out["IdSector"])),
                                 ("linea", "IdLinea", set(lineas_out["IdLinea"])),
                                 ("familia", "IdFamilia", set(familias_out["IdFamilia"]))):
        faltan = set(articulos[col]) - validos
        if faltan:
            raise AssertionError(f"Hay artículos con {nombre} inexistente: {sorted(faltan)[:10]}")
    if sectores_out["IdSector"].duplicated().any() or lineas_out["IdLinea"].duplicated().any() \
            or familias_out["IdFamilia"].duplicated().any():
        raise AssertionError("Hay IDs duplicados en los lookups.")

    # Rangos y largos, para fallar acá y no a mitad del INSERT contra la BD.
    INT32 = 2_147_483_647
    for nombre, serie in (("IdArticulo", articulos["IdArticulo"]), ("IdSector", articulos["IdSector"]),
                          ("IdLinea", articulos["IdLinea"]), ("IdFamilia", articulos["IdFamilia"]),
                          ("IdPresentacion", presentaciones["IdPresentacion"]),
                          ("IdBarra", barras["IdBarra"])):
        if serie.max() > INT32:
            raise AssertionError(f"{nombre} se pasa de int32: max={serie.max()}")
    if articulos["CodigoInterno"].str.len().max() > 30:
        raise AssertionError("CodigoInterno supera 30 caracteres.")
    if barras["CodigoBarra"].str.len().max() > 20:
        raise AssertionError("CodigoBarra supera 20 caracteres.")
    if barras["CodigoBarra"].duplicated().any():
        raise AssertionError("Hay códigos de barra duplicados (la BD tiene índice único).")
    if set(presentaciones["IdArticulo"]) - set(articulos["IdArticulo"]):
        raise AssertionError("Hay presentaciones apuntando a artículos inexistentes.")
    if set(barras["IdPresentacion"]) - set(presentaciones["IdPresentacion"]):
        raise AssertionError("Hay barras apuntando a presentaciones inexistentes.")

    return articulos, presentaciones, barras, sectores_out, lineas_out, familias_out, reporte


if __name__ == "__main__":
    df = cargar()
    arts, pres, barr, sec, lin, fam, rep = construir(df)
    print(f"Sectores:       {len(sec):,}")
    print(f"Lineas:         {len(lin):,}")
    print(f"Familias:       {len(fam):,}")
    print(f"Articulos:      {len(arts):,}  (activos {int(arts.Activo.sum()):,} / inactivos {int((1-arts.Activo).sum()):,})")
    print(f"Presentaciones: {len(pres):,}")
    print(f"Barras:         {len(barr):,}")
    print()
    print(f"Barras duplicadas descartadas: {rep['barras_duplicadas_descartadas']}")
    print(f"Articulos con presentacion default: {rep['articulos_sin_barras_con_presentacion_default']}")
    print(f"\nBarras compartidas entre articulos ({len(rep['barras_en_varios_articulos'])}) "
          f"-> queda en el cod_articulo mas alto:")
    for cod, arts_ in rep["barras_en_varios_articulos"]:
        print(f"   {cod}: articulos {arts_}  -> queda en {max(arts_)}")
    print(f"\nLargo de CodigoBarra: max {barr.CodigoBarra.str.len().max()} (limite 20)")
    print(f"Largo de CodigoInterno: max {arts.CodigoInterno.str.len().max()} (limite 30)")
    print(f"\nCorrecciones de N ({len(rep['cambios_enie'])}):")
    for col, a, b in rep["cambios_enie"][:15]:
        print(f"   [{col}] {a}  ->  {b}")
    if len(rep["cambios_enie"]) > 15:
        print(f"   ... y {len(rep['cambios_enie'])-15} mas")
    print("\nDistribucion de presentaciones por articulo:")
    print(pres.groupby("IdArticulo").size().value_counts().sort_index().to_string())
    print("\nMuestra:")
    print(pres.head(6).to_string(index=False))
    print(barr.head(6).to_string(index=False))
