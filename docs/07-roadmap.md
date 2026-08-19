# 07 — Roadmap por fases

Orden sugerido de construcción. Cada fase deja algo demostrable y testeado. Los esfuerzos son
estimaciones gruesas (equipo chico) para dimensionar, no compromisos.

## Fase 0 — Fundación (scaffolding)  · ~1 sem
- Solución .NET (Domain / Application / Infrastructure / Api) + proyecto React (Vite+TS).
- `PosDbContext` + migración inicial con el esquema de [`02-modelo-datos.md`].
- Auth JWT + roles/permisos + `MovimientosAuditoria`.
- CI: build + tests + análisis estático; Swagger; Serilog; health checks.
- Adaptadores **Mock** registrados por configuración.

## Fase 1 — Datos maestros y ABM  · ~2 sem
- CRUD: Empresas, Sucursales, Puntos de Venta, Cajas, Puestos, Usuarios/Roles.
- CRUD: Artículos, Presentaciones, Barras, Sectores/Líneas/Familias, ModosIva.
- CRUD: Clientes, Autorizados, Clusters, Tarjetas, Condiciones IVA.
- CRUD: Listas de Precios, Precios, Convenios; carga de Padrones.
- Banco de imágenes (lectura por CodigoInterno).

## Fase 2 — Motor de precios y ofertas  · ~2 sem
- `CalculadoraPrecios` (prioridad de listas + convenio).
- `MotorOfertas` (alcance, acumulación, excepciones, tipos descuento/combo/bonificación).
- Servicios SRS: Precio, Convenio, Ofertas Artículo, Ofertas Medios de Pago.
- **Batería de tests de negocio** (casos de borde de precios/ofertas/redondeo/percepciones).

## Fase 3 — Caja (vertical slice)  · ~3 sem
- Apertura/lote, identificación de cliente, cola de lectura de artículos (front), operación.
- Finalización + código de operación (barra/QR), anulación parcial, recuperación ante caída.
- Arqueo X, rendición parcial, consulta de precio, avisos configurables, bloqueo por inactividad.
- Frontend del módulo Caja (UX de escaneo/cola).

## Fase 4 — Facturación (saga + mocks fiscales)  · ~3 sem
- Emisión (electrónica/fiscal/presupuesto, A/B), saga con compensación, numeración serializada.
- `MockFiscalService` (CAE) + contingencia **CAEA** + `MockFiscalPrinter`.
- Medios de pago (mocks MODO/MP/CuentaDNI/tarjeta) + efectivo/redondeo + cuenta corriente/límite.
- Notas de crédito, anulaciones, reimpresiones, refacturaciones, comprobantes asociados.
- Idempotencia + outbox.

## Fase 5 — Cierres y Tesorería  · ~2 sem
- Cierre Z (irreversible), diferencias/motivos, validación de tesorería.
- Dashboard por sucursal/total, vista de cajas, acumulados, cierres por cajero.
- Reporte de cierre por mail (mock), fiscalización/subida CAEA.

## Fase 6 — Etiquetas  · ~1 sem
- Armado de listas (por artículo o por sector/línea/familia), tipos fleje/A4/A5, impresión.

## Fase 7 — Hardening y realidad  · continuo
- Reemplazo de adaptadores Mock por **reales**: ARCA (WSFEv1/CAEA), Hasar/iCARD, MODO, MercadoPago,
  Cuenta DNI, SMTP real, `IErpGateway` (si se decide integrar ERP para tablas `(*)`).
- Pruebas de concurrencia/carga en numeración y cierres; pentest; DR/backup.

---

## Riesgos y decisiones pendientes
| Tema | Estado | Nota |
|------|--------|------|
| Integración ERP (tablas `(*)`) | **Pendiente** | Hoy BD propia; a futuro detrás de `IErpGateway`. |
| Certificados ARCA / homologación | **Pendiente** | Requiere CUIT, PV habilitado y certificados. |
| Wrapper iCARD Hasar (contrato real) | **Pendiente** | Falta documentación del wrapper local. |
| MODO / MercadoPago (contrato real) | **Pendiente** | Endpoints locales citados; falta doc/credenciales. |
| Multi-lista en etiquetas | **A futuro** | SRS: "repensar tema de múltiples listas". |

## Próximo paso propuesto
Cuando aprobemos este diseño, arrancar por **Fase 0 + esquema MS SQL** (migración EF Core inicial),
que es la base sobre la que se apoya todo lo demás.
