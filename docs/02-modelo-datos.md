# 02 — Modelo de datos (MS SQL Server)

Modelo relacional derivado del SRS. Se respetan los nombres/campos indicados y se **completan**
claves primarias/foráneas, tipos e índices que el SRS deja implícitos. Las observaciones de diseño
van marcadas con 🔧.

> Convenciones: PK en **negrita**, FK con `→ Tabla`. Montos = `DECIMAL(18,4)` (nunca `float`).
> IDs de negocio compuestos por sucursal se modelan con `idSucursal` en la PK (multi-sucursal).
> Todas las tablas transaccionales llevan columnas de auditoría técnica: `CreatedAtUtc`,
> `CreatedBy`, `RowVersion` (`rowversion` para concurrencia optimista).

---

## Seguridad y permisos

### Usuarios
| Campo | Tipo | Notas |
|-------|------|-------|
| **idUsuario** | INT IDENTITY | |
| Usuario | NVARCHAR(50) | UNIQUE |
| Clave | NVARCHAR(255) | 🔧 hash **PBKDF2/BCrypt**, nunca texto plano |
| idRol | INT | → Roles |

### Roles
`**idRol**` INT · `Descripcion` NVARCHAR(50)

### Modulos
`**idModulo**` INT · `Descripcion` NVARCHAR(50)

### Permisos
`**idPermiso**` INT IDENTITY · `idRol → Roles` · `idModulo → Modulos` · 🔧 `puedeVer/puedeEditar/esEspecial` BIT
> Un rol tiene N permisos (uno por módulo). Los "permisos especiales" del Supervisor (anulaciones, NC, X/Z) se modelan como flags.

### MovimientosAuditoria  🔒 *sin FK, independiente*
| Campo | Tipo |
|-------|------|
| **idMovimiento** | BIGINT IDENTITY |
| FechaUtc | DATETIME2 |
| idUsuario | INT *(valor, sin FK)* |
| Modulo | NVARCHAR(50) |
| Accion | NVARCHAR(100) |
| Entidad / EntidadId | NVARCHAR |
| DatosAntes / DatosDespues | NVARCHAR(MAX) (JSON) |
| Ip / Puesto | NVARCHAR |
> Requisito SRS: **totalmente independiente**, para que la auditoría sobreviva a cualquier borrado o cambio de esquema.

---

## Catálogo de artículos

### Articulos
`**idArticulo**` INT IDENTITY · `codigoInterno` NVARCHAR(30) UNIQUE · `Descripcion` NVARCHAR(200) ·
`idSector → Sectores` · `idLinea → Lineas` · `idFamilia → Familias` · `idModoIva → ModosIva` ·
🔧 `Activo` BIT

### Presentaciones
`**idPresentacion**` INT IDENTITY · `idArticulo → Articulos` · `UnidadXBulto` DECIMAL(18,4)
> Un artículo tiene N presentaciones (unidad, bulto/DUN). El **precio se asocia a la presentación** vía `Precios`.

### Barras
`**idBarra**` INT IDENTITY · `idPresentacion → Presentaciones` · `codigoBarra` NVARCHAR(20) UNIQUE
> 🔧 SRS lista PK `idPresentacion` pero una presentación puede tener **varios** EAN13 + un DUN → se agrega `idBarra`.
> Tipo de barra (EAN13 / DUN) se infiere o se guarda en `idTipoBarra` (13 vs 14 díg.).

### Sectores · Lineas · Familias · ModosIva
Tablas de catálogo simples: `**id**` INT · `Descripcion`.
`ModosIva` agrega la **alícuota** y sirve para percepciones (ver Configuraciones).

---

## Clientes

### Clientes
| Campo | Tipo | Notas |
|-------|------|-------|
| **idCliente** | INT IDENTITY | |
| codigoInt | NVARCHAR(30) | UNIQUE |
| CUIT | CHAR(11) | validado (dígito verificador) |
| Documento | NVARCHAR(20) | DNI |
| Descripcion | NVARCHAR(200) | razón social / nombre |
| idCondIva | INT | → CondicionesIva |
| permitePresupuesto | BIT | |
| Domicilio | NVARCHAR(120) | texto plano (calle y número juntos), como venía del ERP anterior |
| Localidad | NVARCHAR(60) | |
| CodigoPostal | NVARCHAR(8) | admite el CPA completo (`B7600ABC`), no solo los 4 dígitos viejos |
| Email | NVARCHAR(120) | |

> Los cuatro últimos se agregaron para la importación del padrón del ERP anterior (migración
> `DomicilioYContactoDeCliente`). Todos opcionales: en ese padrón el 3% no tiene domicilio y el 94%
> no tiene email. El importador es [`scripts/importar_padron_clientes.py`](../scripts/importar_padron_clientes.py).

### ClientesEnCuenta
`**idCliente**` + `**idSucursal**` (PK compuesta) · `limiteCredito` DECIMAL(18,4)
> Un mismo cliente/CUIT puede tener **cuentas por sucursal** con límite de crédito propio.

### ClusterClientes
`**idCluster**` INT · `idCliente → Clientes` · `Descripcion`
> Agrupa clientes para alcance de ofertas y descuentos por cluster.

### Autorizados
`**idAutorizado**` INT IDENTITY · `idCliente → Clientes` · `DNI` · `Descripcion`
> Personas habilitadas a comprar en la cuenta del cliente.

### TarjetasClientes
`**idCliente** + **idTipoTarjeta** + **NroTarjeta**` · → Clientes, → TiposTarjeta

### TiposTarjeta
`**idTipoTarjeta**` INT · `Descripcion` · `idListaPrecio → ListasPrecios`
> El tipo de tarjeta puede **asociar una lista de precios** (beneficio por tarjeta).

---

## Estructura comercial (empresa / sucursal / puntos de venta / cajas)

### Empresas
`**idEmpresa**` INT · `codigoInterno` · `Descripcion` · 🔧 `CUIT`, datos fiscales, ref. a certificado CAE.

### Sucursales
`**idSucursal**` INT · `idEmpresa → Empresas` · `Descripcion`

### PuntosVenta
`**idSucursal** + **idPuntoVenta**` · `idTipoPuntoVenta → TiposPuntoVenta` · `puntoVenta` INT *(nro ARCA)*

### TiposPuntoVenta
`**idSucursal** + **idTipoPuntoVenta**` · `Descripcion` · `tipoARCA` NVARCHAR *(CAE electrónico, fiscal, etc.)*

### PuestosCaja
`**idSucursal** + **idPuestoAsignado**` · `nombrePC` NVARCHAR(100) · `identificadorEquipo` NVARCHAR(450) *(único, nullable)* · `ip` NVARCHAR(45)
> Identifica la **PC física** por un GUID que el navegador genera solo y persiste en el perfil
> kiosco de esa PC (localStorage, ver `08-puesto-caja.md`) — no por su IP de LAN: la IP deja de ser
> confiable en cuanto hay NAT/VPN/proxy entre la PC y el servidor (sucursales remotas, o saltos
> entre VLANs en la propia LAN). `nombrePC` es solo una etiqueta libre del ABM. Login resuelve
> `idCaja` buscando el `PuestoCaja` cuyo `identificadorEquipo` coincide con el header `X-Puesto-Id`
> del request. `identificadorEquipo` queda `null` hasta que un Administrador, parado físicamente
> frente a esa PC, lo vincula desde el ABM ("Vincular este equipo"). `ip` se sigue guardando pero
> solo como dato informativo/auditoría.

### Cajas
`**idSucursal** + **idCaja**` · `idPuntoVenta → PuntosVenta` · `Descripcion` · `idPuestoAsignado → PuestosCaja` (nullable) · `admitePresupuesto` BIT
> `idPuestoAsignado` es **FK real** (compuesta `idSucursal+idPuestoAsignado`, `ON DELETE RESTRICT`)
> pero **opcional**: una caja puede no tener puesto asignado (roles Administrador/Tesorero operan
> con un fallback fijo sin puesto real), y un puesto puede no tener aún ninguna caja asociada. No es
> una relación 1 a 1 estricta — se evaluó fusionar ambas tablas y se descartó: `PuestoCaja` es
> identidad de **hardware** (cambia si se reemplaza la PC) y `Caja` es identidad **fiscal/lógica**
> (ligada a `PuntoVenta` y a los lotes de turno); mantenerlas separadas permite reasignar la PC física
> sin tocar el histórico fiscal de la caja, y que un cajero recupere su turno desde otra PC.

---

## Listas de precios y precios

### ListasPrecios
`**idListaPrecio**` INT · `idSucursal → Sucursales` · `codigoInterno` ·
🔧 `idTipoLista` (Folder / Temporal / Base) · `prioridad` INT · `fechaInicio` `fechaFin` (para temporales)
> **Prioridad de resolución** (SRS): Folder → Temporales (vigentes) → Base. El servicio de Precio recorre en ese orden.

### Precios
`**idListaPrecio** + **idArticulo**` *(🔧 recomendado idPresentacion)* · `precioFinal` DECIMAL(18,4) · `impuestoInterno` DECIMAL(18,4)
> 🔧 Dado que el precio depende de la **presentación** (unidad vs bulto), la PK correcta es
> `idListaPrecio + idPresentacion`. Se mantiene `idArticulo` como columna denormalizada para consultas.

### Convenios
`**idSucursal** + **idConvenio**` · `idCliente → Clientes` · `descuento` DECIMAL(9,4)
> Convenio del cliente: descuento adicional y/o lista asociada; ver reglas en `06-flujos-caja.md`.

---

## Motor de ofertas

### CabeceraOfertas
| Campo | Tipo |
|-------|------|
| **idSucursal + idOferta** | PK |
| idAlcance | → (alcance) |
| idAccion | → AccionOfertas |
| Descripcion | NVARCHAR(200) |
| fechaInicio / fechaFin | DATE |
| acumula | BIT |
| permiteConvenio | BIT |

### AlcanceOfertas
`**idOferta**` + criterios: `idCluster`, `idLinea`, `idSector`, `idFamilia`, `idArticulo` (nullable)
> Define **a qué** aplica: sucursal / cluster de clientes / sector-línea-familia-artículo. Con excepciones.

### AccionOfertas
`**idOferta**` · `idTipoOferta → TiposOferta` · `idPresentacion` · *(campos propios según tipo)*
`porcentaje` / `montoFijo` / `cantidadMin` / `cantidadBonif` ...

### TiposOferta
`**idTipoOferta**` · `Descripcion` — **Descuento**, **Combo**, **Bonificación** (SRS "Ejemplo de módulos de Oferta": Artículo, Canasta).

> El **MotorOfertas** (Domain) evalúa cada operación: filtra ofertas vigentes por alcance, resuelve
> acumulación/excepciones y calcula el efecto. Ofertas por **medio de pago** se aplican al **cierre del ticket**.

---

## Medios de pago

### MediosPago
`**idMedioPago**` INT · `Descripcion` · `idTipoPago → TiposPago`

### TiposPago
`**idTipoPago**` INT · `Descripcion` · `fuente` NVARCHAR(30)
> `fuente` = efectivo / tarjeta / billetera / transferencia / cuentaCorriente → determina el adaptador de pago.

### Cupones
`**idSucursal** + **idCupon**` · `idMedioPago → MediosPago` · `fecha` · `nroCupon` · `nroLote` · `idComprobante → CabeceraComprobantes`

### CuentasCorrientes
`**idSucursal** + **idCliente** + **idComprobante**` · `debe` DECIMAL(18,4) · `haber` DECIMAL(18,4)
> Mayor de cuenta corriente por cliente/sucursal. El saldo se controla contra `ClientesEnCuenta.limiteCredito`.

---

## Comprobantes y operaciones

### Operaciones  *(pre-ticket / carrito)*
`**idSucursal** + **idOperacion**` · `idCliente → Clientes` · `idPresentacion → Presentaciones` · `precio` · `descuento`
> Es la **operación de caja** antes de facturar (SRS: "detalle de número de operación", impresa con barra/QR).
> 🔧 En la práctica: `CabeceraOperaciones` (idSucursal, idOperacion, idCliente, estado, totales) +
> `DetalleOperaciones` (líneas con presentación, cantidad, precio, descuentos, ofertas aplicadas).

### CabeceraComprobantes
`**idSucursal** + **idComprobante**` · `idTipoComprobante → TiposComprobante` ·
🔧 `idCliente`, `idPuntoVenta`, `letra`, `numeroCompleto`, `fecha`, `neto`, `iva`, `percepciones`,
`total`, `CAE`, `CAEAVencimiento`, `estado` (Emitido/Anulado/Contingencia), `idOperacion`.

### DetalleComprobantes
`**idDetalleComprobante**` IDENTITY · `idComprobante` · *(campos propios)*:
`idPresentacion`, `descripcionTicket`, `cantidad`, `precioUnit`, `descuento`, `alicuotaIva`, `importe`.

### TiposComprobante
`**idTipoComprobante**` · `Descripcion` · `letra` (A/B/…) · 🔧 `codigoARCA`, `signo` (+/− para NC).

### ComprobantesAsociados
`**idComprobanteOrigen** + **idComprobanteAsociado**`
> Relaciona NC/ND con la factura de origen (devolución total/parcial o diferencia de precio).

### Numeros
`**idSucursal** + **idNumero**` · `idPuntoVenta → PuntosVenta` · `numero` BIGINT
> Numeradores por punto de venta/tipo. La asignación de número es **transaccional y serializada**
> (ver `04-seguridad-transacciones.md`) para evitar saltos/duplicados fiscales.

---

## Caja (movimientos y cierres)

### MovimientosCaja
`**idSucursal** + **idMovCaja**` · `idUsuario` · `idCaja` · `idCmp`(comprobante) · `idLote` · `idMovPagos` · `estado`

### MovimientosPagos
`**idMovPagos**` · `idMedioPago → MediosPago` · `total` DECIMAL(18,4) · `redondeo` DECIMAL(18,4)

### CierresLotesCaja
| Campo | Tipo |
|-------|------|
| **idSucursal + idLote + idMedioPago** | PK |
| total | DECIMAL(18,4) |
| numeroCierre | INT |
| redondeoAcumulado | DECIMAL(18,4) |
| diferenciaTotal | DECIMAL(18,4) |
| idMotivoDiferencia → MotivosDiferencia | |
| ObservacionesCajero | NVARCHAR(500) |
| verificaTesoreria | BIT |
| idMotivoCierre → MotivosCierre | |
| ObservacionTesoreria | NVARCHAR(500) |

### MotivosDiferencia · MotivosCierre
`**id**` · `Descripcion`.

> Un **lote** = un turno de caja (apertura → cierre Z, uno por día). El cierre acumula por medio de
> pago, registra diferencias con motivo y queda pendiente de **validación de tesorería**.

---

## Datos fiscales y de configuración

### CondicionesIva
`**idCondIva**` · `Descripcion` · `letra` · `codigoInterno`

### PadronIngresosBrutos
`**CUIT**` (PK) · `percepcion` DECIMAL(9,4)

### PadronExcepcionPercepcionesIva
`**CUIT**` (PK)
> Clientes exceptuados de percepción de IVA.

### Configuraciones
`**idConfiguracion**` · `Descripcion` · `valor` NVARCHAR(200)
> Clave-valor para: límites de facturación a Consumidor Final, límite de efectivo en caja,
> reintentos por CAE inaccesible, rango de redondeo, % percepción por ModoIva, timeouts, etc.

---

## Notas de diseño transversales

1. **Presentación como unidad de precio/oferta**: se corrige el SRS para que `Precios`, `AccionOfertas`
   y `DetalleOperaciones`/`DetalleComprobantes` referencien `idPresentacion`, no `idArticulo`.
2. **PKs de negocio compuestas con `idSucursal`**: coherente con multi-sucursal; en EF Core se
   configuran claves compuestas con Fluent API.
3. **Concurrencia optimista** (`rowversion`) en precios, numeradores y cuentas corrientes.
4. **Índices sugeridos**: `Barras(codigoBarra)`, `Articulos(codigoInterno)`, `Clientes(CUIT)`,
   `Clientes(Documento)`, `Precios(idListaPrecio,idPresentacion)`, `CabeceraComprobantes(CAE)`,
   `CuentasCorrientes(idSucursal,idCliente)`.
5. **Tablas `(*)` del SRS** (Articulos, Clientes, Precios, Empresas, Sucursales, Padrones): en esta
   fase son propias; a futuro se sincronizan/leen del ERP detrás de `IErpGateway` sin cambiar el resto.
6. **Soft-delete** en maestros (`Activo` BIT) — los ABM son "bajas lógicas" para preservar integridad
   histórica de comprobantes.
