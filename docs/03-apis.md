# 03 — Catálogo de APIs de servicios

Basado en la sección "Arquitectura de API de servicios" del SRS. Todas las rutas van bajo
`/api/v1`, requieren **JWT** (salvo login) y validan **permiso de módulo**. Formato JSON.
Los montos viajan como `decimal` en string para evitar pérdida de precisión.

Convención de respuesta:
```jsonc
{ "ok": true, "data": { ... }, "error": null }
{ "ok": false, "data": null, "error": { "code": "CLIENTE_NO_ENCONTRADO", "message": "..." } }
```

---

## Autenticación

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/auth/login` | `{ usuario, clave, nombrePC }` → JWT + datos de caja resuelta por la PC |
| POST | `/auth/refresh` | Renueva token |
| POST | `/auth/logout` | Cierra sesión (deslogueo parcial mantiene el lote abierto) |
| GET | `/auth/permisos` | Módulos habilitados para el usuario *(SRS: servicio "Permisos")* |

---

## Catálogo (lectura para caja)

Estos endpoints corresponden 1:1 con los servicios del SRS.

| Servicio SRS | Método | Ruta | Entrada | Resultado |
|--------------|--------|------|---------|-----------|
| **Clientes** | GET | `/clientes` | filtros/paginado | Listado de clientes habilitados con datos completos y autorizados |
| **Cliente** | GET | `/clientes/{codigo}` | código cliente | Datos + `permitePresupuesto` |
| **Cliente (búsqueda)** | GET | `/clientes/buscar?q=` | tarjeta/DNI/código/nombre/domicilio | 1 → selección directa; N → lista |
| **Articulo** | GET | `/articulos/{codigo}` | código interno **o** barra | Descripción ticket, unidad×bulto, sector, línea, familia, imagen |
| **Convenio** | GET | `/convenios/{idCliente}` | código cliente | Código convenio, lista de precios y descripción |
| **Precio** | GET | `/precios` | `idPresentacion`, `idCliente/convenio` | Precio vigente + precio convenio (resuelve prioridad de listas) |
| **Ofertas Artículo** | POST | `/ofertas/articulos` | listado de artículos + precios + cliente | Descuentos, precios y **ofertas aplicadas** por línea |
| **Ofertas Medios de pago** | POST | `/ofertas/medios-pago` | listado + medio de pago | Descuentos por medio de pago (se aplican al cierre) |
| **Medios de Pago** | GET | `/medios-pago?idCliente=` | cliente | Medios disponibles para ese cliente |

> **Ofertas Artículo** y **Ofertas Medios de pago** son POST porque reciben el detalle completo de la
> operación y devuelven el mismo detalle **enriquecido** con las ofertas aplicadas (el motor corre en backend).

---

## Operaciones de caja

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/caja/apertura` | Abre lote del día (uno por día por caja) |
| POST | `/caja/movimiento-efectivo` | Ingreso de efectivo / cambio |
| POST | `/caja/operaciones` | Crea/actualiza la operación (carrito) → devuelve nro de operación + totales |
| GET | `/caja/operaciones/{id}` | Recupera operación (recuperación ante caída de red) |
| POST | `/caja/operaciones/{id}/anular-linea` | Anulación parcial (por selección o por barra) |
| POST | `/caja/operaciones/{id}/finalizar` | Cálculo final + código de operación (barra/QR) |
| POST | `/caja/arqueo-x` | Arqueo parcial (vista del lote) → emite **X** |
| POST | `/caja/rendicion-parcial` | Retiro de efectivo (X) |
| POST | `/caja/consulta-precio` | Modo consulta (pop-up escaneo) |
| POST | `/caja/rendicion-final` | Cierra rendición → dispara cierre **Z** |
| POST | `/caja/deslogueo` | Deslogueo de caja física (mantiene lote) |

---

## Facturación

| Servicio SRS | Método | Ruta | Descripción |
|--------------|--------|------|-------------|
| **Facturación** | POST | `/facturacion/emitir` | `{ modo: electronica\|fiscal\|presupuesto, tipo: A\|B, cliente, lineas, pagos }` → comprobante + CAE |
| **Estado de facturación** | GET | `/facturacion/{id}/estado` | Estado de la emisión (async si hay reintentos por CAE) |
| **Impresión** | POST | `/facturacion/{id}/imprimir` | Imprime comprobante; devuelve estado de impresión |
| **Nota de crédito** | POST | `/facturacion/nota-credito` | NC total/parcial o por diferencia de precio (requiere Supervisor) |
| — | POST | `/facturacion/{id}/anular` | Anulación total/parcial |
| — | POST | `/facturacion/{id}/reimprimir` | Reimpresión |
| — | POST | `/facturacion/refacturar` | Refacturación |
| — | POST | `/facturacion/presupuesto` | Presupuesto (comprobante no fiscal) |

---

## Tesorería / Reportes

| Servicio SRS | Método | Ruta | Descripción |
|--------------|--------|------|-------------|
| **Rendición final** | GET | `/tesoreria/rendicion?lote=&cajero=` | Estado/resultado de la rendición |
| Dashboard | GET | `/tesoreria/dashboard?sucursal=` | Estadísticas por sucursal y total |
| Cajas | GET | `/tesoreria/cajas` | Vista de todas las cajas (abiertas/cerradas) |
| Acumulados | GET | `/tesoreria/acumulados?medioPago=` | Acumulado por medio de pago |
| Cierres | GET | `/tesoreria/cierres?cajero=` | Cierres efectuados por cajero |
| Validar cierre | POST | `/tesoreria/cierres/{id}/validar` | Validación + motivo + observaciones |
| Lotes pendientes | GET | `/tesoreria/lotes-pendientes?idSucursal=` | Lotes que quedaron abiertos en días anteriores, con su acumulado esperado |
| Cerrar lote pendiente | POST | `/tesoreria/lotes-pendientes/{idLote}/cerrar?idSucursal=` | Cierre administrativo del lote de otro día: motivo de cierre obligatorio, sin Z fiscal |
| Motivos de diferencia | GET | `/tesoreria/motivos-diferencia` | Mismo lookup que `/caja/motivos-diferencia`, accesible al rol Tesorero |
| Reporte mail | POST | `/tesoreria/cierres/{id}/enviar-mail` | Envío automático del reporte de cierre |
| CAEA | POST | `/tesoreria/caea/fiscalizar` | Fiscalización y subida de comprobantes en CAEA |

---

## Etiquetas (repositor)

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/etiquetas/lista` | Arma lista (por artículos o por sector/línea/familia completa) |
| POST | `/etiquetas/imprimir` | `{ tipo: fleje\|A4\|A5, impresora, items }` |

---

## Administración (ABM) — patrón CRUD

CRUD estándar `GET/POST/PUT/DELETE(baja lógica)` para cada entidad del SRS:

`/admin/articulos` · `/admin/clientes` · `/admin/precios` · `/admin/ofertas` ·
`/admin/clusters` · `/admin/convenios` · `/admin/medios-pago` · `/admin/empresas` ·
`/admin/sucursales` · `/admin/puntos-venta` · `/admin/numeros` · `/admin/usuarios` ·
`/admin/roles` (asignación) · `/admin/padrones` (carga IIBB / excepción IVA) ·
`/admin/configuraciones` · `/admin/certificados` (CAE por empresa, obtención de CAEA).

---

## Auditoría

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/auditoria/movimientos?desde=&hasta=&usuario=&modulo=` | Reportes de auditoría de movimientos |

---

## Notas
- **Idempotencia**: los endpoints que emiten comprobantes o mueven dinero aceptan header
  `Idempotency-Key` para evitar doble emisión ante reintentos de red.
- **Versionado**: `/api/v1`; cambios incompatibles → `/v2`.
- **Documentación viva**: Swagger/OpenAPI generado por ASP.NET Core.
