# Fase 5 — Cierres y Tesorería (COMPLETADA)

Cierra el ciclo de caja: **arqueo X** (vista, no persiste), **cierre Z** (irreversible, con
diferencias justificadas) y el **dashboard de tesorería** con validación de cierres.

## Dominio (lógica pura)

`src/Pos.Domain/Services/CierreCajaReglas.cs`:
- **`AcumuladorPagos`**: agrupa movimientos de pago por medio (base del arqueo/cierre).
- **`DiferenciaCierreReglas`**: declarado vs. esperado, con tolerancia de un centavo; determina
  si hace falta motivo.
- **`CierreLoteReglas`**: un lote sólo puede cerrarse si está `Abierto` (SRS: "no se puede anular").

**45/45 tests de dominio** (acumulado de todas las fases).

## Corrección de esquema necesaria: `Operacion` no sabía su caja/lote

Al construir el arqueo descubrí que `Operacion` (Fase 3) nunca guardaba `IdCaja`/`IdLote`, así que
los `MovimientoCaja` de la Fase 4 quedaban con esos campos en `0` — inútiles para agrupar por lote.
Se agregaron las columnas (migración `AgregarCajaLoteAOperacion`) y se corrigió:
- `CajaService.CrearOperacionAsync` ahora persiste `IdCaja`/`IdLote` de la operación.
- `FacturacionService` usa `operacion.IdCaja`/`IdLote` (y `ICurrentUser.IdUsuario`) reales en
  `MovimientoCaja`, en vez de los `0` que quedaban antes.
- `CajaService.AbrirCajaAsync` ahora también guarda `IdUsuarioApertura` (antes quedaba en `0`).

## Backend

### `ICierreCajaService` / `CierresController` (`/api/v1/caja`)
| Endpoint | Función |
|---|---|
| `GET /caja/arqueo-x` | Acumulado por medio de pago del lote abierto **de hoy** (no persiste) |
| `POST /caja/cierre-z` | Cierre irreversible: declara montos, exige motivo si hay diferencia |
| `GET /caja/motivos-diferencia` | Lookup accesible a Cajero (no sólo Administrador) |

### `ITesoreriaService` / `TesoreriaController` (`/api/v1/tesoreria`, rol Tesorero/Administrador)
| Endpoint | Función |
|---|---|
| `GET /tesoreria/dashboard` | Cajas (abiertas/cerradas) + acumulado del día por medio |
| `GET /tesoreria/cierres` | Listado de cierres por lote y medio de pago |
| `POST /tesoreria/cierres/{idLote}/validar` | Marca `VerificaTesoreria=true` con motivo/observación |
| `GET /tesoreria/motivos-cierre` | Lookup accesible a Tesorero |

## Bug real encontrado y corregido en la verificación
**`ObtenerLoteAbiertoAsync` no filtraba por fecha**: al reintentar un cierre después de que el lote
de hoy ya estaba cerrado, el servicio encontró y cerró por error un **lote viejo sin cerrar de un
día anterior** (dejado por sesiones de prueba previas). Corregido: sólo se considera "el lote
actual" el que tiene `Estado=Abierto` **y** `FechaApertura.Date == hoy`. Verificado explícitamente
con un test dirigido: tras el fix, un lote de ayer no se toca — arqueo/cierre devuelven
`SIN_LOTE_ABIERTO` en vez de operar sobre datos equivocados.

### Secuela: el índice único no acompañó a esa decisión

Ese fix dejó a la app razonando "un lote abierto por caja+cajero **por día**", pero el índice
`IX_LotesCaja_UnAbiertoPorCajaYCajero` seguía enforceando "un solo lote abierto **de por vida**"
(filtro `[Estado]=1`, sin el día en la clave). Consecuencia: un lote que quedó abierto un día
anterior — justamente lo que este fix decidió no tocar — hacía fallar **toda apertura futura** de ese
cajero en esa caja con un choque de clave duplicada, y sin salida posible desde la app, porque
cierre/arqueo solo miran el lote de hoy. Se corrigió agregando el día a la clave del índice
(migración `LoteAbiertoPorCajaCajeroYDia`: columna calculada persistida `DiaApertura` +
`IX_LotesCaja_UnAbiertoPorCajaCajeroYDia`), y `CajaService.AbrirCajaAsync` ahora traduce la
violación del índice a `LOTE_YA_ABIERTO` (409) en vez de dejar escapar un 500.

### Cierre administrativo de lotes pendientes

Cerrado el hueco que dejaba lo anterior: un lote abierto de un día previo ya no se puede cerrar desde
Caja, así que ahora lo regulariza **Administrador o Tesorero** desde el módulo de Tesorería
(`GET /tesoreria/lotes-pendientes`, `POST /tesoreria/lotes-pendientes/{idLote}/cerrar`).

- El **motivo de cierre es obligatorio** — a diferencia del Z del cajero, acá siempre hay que
  justificar por qué se regulariza el lote de otro usuario días después. Si además lo declarado no
  coincide con lo esperado, se exige motivo de diferencia igual que en el Z.
- **No se imprime Z fiscal**: la impresora vive en la caja física y este cierre se hace después desde
  otro puesto. La respuesta trae `referencia: null`.
- El lote del **día en curso queda excluido** (`LOTE_DEL_DIA`): ese lo cierra su cajero con la plata
  en la mano.
- Queda **pendiente de validación de tesorería** como cualquier otro cierre: cerrar y verificar los
  números siguen siendo dos pasos.
- `GET /tesoreria/motivos-diferencia` se agregó porque el lookup equivalente de Caja solo admite
  Cajero/Supervisor/Administrador y un Tesorero no podía leerlo.
- **El alcance es la sucursal, no la caja del puesto.** La sesión queda atada a una caja física según
  la IP de la PC (`LoginCommand`/`ResolverCajaPorIpAsync`), así que el chequeo `AsegurarCaja` que se
  puso en la primera versión hacía que un Tesorero sentado en una caja solo pudiera regularizar
  lotes de *esa* caja — inútil para el propósito de la función, y contradictorio con el listado, que
  muestra los lotes de todas las cajas de la sucursal. `AsegurarSucursal` sí se mantiene. El cierre Z
  del cajero (`/caja/cierre-z`) conserva su restricción por caja: ahí la plata está en una caja
  concreta.

**Corrección de esquema**: `CierresLotesCaja` tiene una fila **por medio de pago**, así que un lote
sin movimientos se cerraba sin generar ninguna fila — quedaba `Cerrado` sin rastro de quién lo cerró
ni con qué motivo. Se agregaron al lote `IdUsuarioCierre`, `IdMotivoCierre` y `ObservacionCierre`
(migración `CierreAdministrativoDeLote`), que es donde corresponde: el autor y el motivo son del
cierre, no de cada medio de pago.

**Limitación conocida**: un lote cerrado administrativamente **sin movimientos** no aparece en la
tabla "Cierres" de la UI, porque esa vista lista filas de `CierresLotesCaja` (una por medio) y ese
lote no genera ninguna. El rastro queda en el lote y en `MovimientosAuditoria`. Si hace falta verlo
en pantalla, hay que agregar una vista de lotes cerrados.

## Verificación end-to-end (contra `POS-Ventas`)

**Por API (script Python):**
- 2 ventas facturadas → arqueo X: `Efectivo $2550` acumulado ✅.
- Cierre Z declarando **$10 menos** sin motivo → bloqueado (`MOTIVO_REQUERIDO`) ✅.
- Mismo cierre **con motivo** → éxito, `numeroCierre=1`, diferencia `-$10` ✅.
- Reintentar cerrar el mismo lote → **bloqueado** (irreversible) ✅.
- Abrir otro lote el mismo día → bloqueado (`LOTE_YA_ABIERTO`) ✅.
- Dashboard de tesorería refleja la caja `Cerrado`, lote y total correctos ✅.
- Cajero sin acceso a `/tesoreria` → **403** ✅.

**En el navegador:**
- Panel de **Tesorería** (login admin) muestra cajas, acumulado del día y listado de cierres con
  sus diferencias; **validar cierre** desde la UI funciona (pasa a "Validado").
- Login `cajero1` → `/caja` respeta el límite de un lote por día: como el de hoy ya se cerró,
  muestra la pantalla de apertura bloqueada (no permite abrir otro).

## Frontend
- **`CajaPage`**: botones "Arqueo X" y "Cerrar caja (Z)" en el header (visibles con lote abierto).
  Arqueo X: tabla de acumulados + total general. Cierre Z: declaración editable por medio de pago,
  selector de motivo si hay diferencia, ticket final con detalle esperado/declarado/diferencia.
- **`TesoreriaPage`** (`/tesoreria`): dashboard de cajas + acumulado del día, listado de cierres con
  acción de validación inline.

## Pendiente / futuro
- **Envío automático del reporte de cierre por mail** (SRS) — el puerto `IMailSender` ya existe
  (mock) desde la Fase 0; falta conectarlo al evento de cierre Z.
- **Fiscalización y subida de comprobantes CAEA** — el puerto ya soporta `InformarComprobantesCaeaAsync`
  desde la Fase 4; falta el endpoint/flujo de tesorería que lo dispare.
- Selector de sucursal/caja múltiple en el dashboard (hoy sólo un filtro simple por sucursal).

## Próximo: Fase 6 — Etiquetas
Búsqueda/escaneo de artículos, armado de lista por sector/línea/familia, e impresión en formatos
fleje/A4/A5 — el último módulo del alcance original del SRS.
