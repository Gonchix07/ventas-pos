# Importadores del ERP viejo → POS-Ventas

Dos cargas independientes: el **catálogo** (artículos, presentaciones, barras, clasificaciones)
y los **precios** por lista.

```bash
pip install pandas openpyxl pyodbc xlrd

python importar.py --dry-run          # catálogo: transforma y valida, sin tocar la base
python importar.py                    # catálogo: importa de verdad

python importar_precios.py --dry-run  # precios (requiere el catálogo ya cargado)
python importar_precios.py
```

El catálogo va primero: los precios se enganchan por `CodigoInterno` del artículo.

`transformar.py` hace toda la lectura/limpieza y **no** toca la base; `importar.py` la carga
en una única transacción (si algo falla, rollback completo).

## Cómo se interpreta el archivo

El Excel trae **una fila por código de barra**, no por artículo.

| Excel | POS | Nota |
|---|---|---|
| `cod_articulo` | `Articulos.CodigoInterno` | texto: 250 códigos superan el rango de un `int` (llegan a 60.000.003.133), por eso el `IdArticulo` es una secuencia propia |
| `desc_articulo` | `Articulos.Descripcion` | |
| `estado` | `Articulos.Activo` | `SUSPENDIDO` → inactivo |
| `cod_sector` / `cod_linea` | `IdSector` / `IdLinea` | se conservan los códigos del ERP como Id |
| `cod_familia` | `IdFamilia` | **no** es un código global: se repite entre sectores, así que se crea una familia por par (sector, familia) |
| `iva` | `IdModoIva` | `21%`→1, `10,5%`→2 |
| `unidad_MTdida` | `Articulos.UnidadMedida` | KG/GR→Kilogramo, LT/ML/CC→Litro, resto→Ninguna |
| `contenido` | `ContenidoNetoUnitario` | ya viene en Kg/Lt aunque la etiqueta diga GR/CC |
| `codigo` + `tipo` | `Barras` | **EAN → presentación de 1 unidad; DUN → presentación de `unidxbulto`** |

## Decisiones tomadas (2026-08-10, acordadas con el usuario)

- Familias **separadas por sector** (153, incluye "SIN FAMILIA"); se aceptan nombres repetidos.
- Artículos `SUSPENDIDO` se importan **inactivos** (no se descartan).
- Se **borra** el catálogo previo (era solo data de prueba).
- Se corrigen las **Ñ** perdidas solo en patrones inequívocos (`PA-O`→`PAÑO`, `CA-UELAS`→`CAÑUELAS`,
  `A-OS`→`AÑOS`…), sin tocar guiones legítimos como `MED-NARAN`.

## Limpiezas que aplica sobre datos sucios del origen

- Códigos de barra con espacios sobrantes (698), espacio duro `U+00A0` (4) y una barra final.
  Se leen como **texto** para no perder ceros a la izquierda ni romper los códigos con guiones
  (`000-000-0559`).
- `cod_sector`/`cod_familia` en `0` con nombre en blanco se tratan como "sin clasificar".
- Códigos de barra repetidos: la BD tiene índice único, así que se descartan los duplicados.
  Cuando el mismo código aparece en **artículos distintos** (7 casos, producto cargado dos veces
  con código viejo y nuevo) queda en el `cod_articulo` más alto, que es el más reciente.
  El script los lista al terminar.

## Validaciones antes de escribir

Falla antes de tocar la base si: un artículo apunta a un sector/línea/familia inexistente, hay
IDs duplicados en los lookups, algún Id se pasa de `int32`, `CodigoInterno` supera 30 caracteres,
`CodigoBarra` supera 20, hay barras duplicadas, o hay presentaciones/barras huérfanas.

---

# Precios (`precios.xls` → tabla `Precios`)

Archivo `.xls` viejo (BIFF, hace falta `xlrd`), **una fila por artículo** y una columna por lista:
`codigo`, `descrip`, `azul`, `roja`, `folder`. El `codigo` es el `cod_articulo` rellenado con
ceros a 13 dígitos. Un **0 significa "sin precio"** y no se carga.

| Columna | Lista en la BD |
|---|---|
| `azul` | AZUL (Base) |
| `roja` | ROJA (Base) |
| `folder` | FOLDER AGO (Folder) |

## El precio del archivo es UNITARIO (no del bulto)

Cada presentación se carga como `precio_unitario × unidadXBulto`, la misma regla que usa el
editor de precios de la app (`Pos.Domain.Services.PrecioPorBulto`).

Cómo se determinó, porque el archivo no lo aclara: el aceite **AC NATURA** aparece en 4 packs
distintos y solo bajo la hipótesis "por unidad" el precio por litro converge.

| Pack | Contenido | Precio | $/L si es por unidad | $/L si fuera por bulto |
|---|---|---|---|---|
| 15×0,9 | 0,9 L | 3.599,90 | **4.000** | 267 |
| 12×1,5 | 1,5 L | 5.799,90 | **3.867** | 322 |
| 6×3,0 | 3,0 L | 11.799,90 | **3.933** | 656 |
| 4×5,0 | 5,0 L | 18.999,90 | **3.800** | 950 |

Los snacks confirman lo mismo (3D MEGA QUESO: 30.433 vs 29.999 $/kg en dos packs distintos).

## Qué NO entra

- **1.044 filas sin artículo** en el catálogo (791 traían precio): son códigos que no están en
  `Articulos.xlsx`, incluidos items `C CAMBIO`. No hay artículo al que colgarles el precio.
- **1 código repetido** en el archivo (19926, con dos precios ROJA distintos): gana el último.
- Los artículos del catálogo sin fila de precio quedan sin precio (~4.300).
