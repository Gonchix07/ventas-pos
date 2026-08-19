# 05 — Capa fiscal y medios de pago (Ports & Adapters)

En esta fase **todo se implementa como interfaz (puerto) con adaptador MOCK**. Los adaptadores
reales se agregan luego sin tocar Application/Domain.

## 1. Puertos (interfaces en `Pos.Application/Abstractions`)

### `IFiscalService`
```csharp
Task<ResultadoCae> SolicitarCaeAsync(ComprobanteFiscal cmp, CancellationToken ct);
Task<ResultadoCaea> ObtenerCaeaAsync(int idEmpresa, PeriodoFiscal periodo, CancellationToken ct);
Task<ResultadoCaea> InformarComprobantesCaeaAsync(int idEmpresa, IEnumerable<ComprobanteFiscal> lote, CancellationToken ct);
Task<EstadoServicioFiscal> PingAsync(CancellationToken ct);
```
- **CAE**: comprobante electrónico online (ARCA/AFIP WSFEv1).
- **CAEA**: código anticipado por período (backup/contingencia). Se **informan** los comprobantes
  emitidos con CAEA cuando el servicio online vuelve (tesorería: "fiscalización y subida CAEA").

### `IFiscalPrinter` (Hasar / iCARD)
```csharp
Task<ResultadoImpresion> ImprimirFiscalAsync(ComprobanteFiscal cmp, CancellationToken ct);
Task<ResultadoImpresion> ImprimirNotaCreditoAsync(...);
Task<ResultadoZ> CierreZAsync(...);
Task<ResultadoX> ArqueoXAsync(...);
```
> SRS: **iCARD (Hasar, wrapper local)** — se accede vía HTTP a un servicio local (LAN). El adaptador
> real hablará con el wrapper; el mock devuelve tickets simulados.

### `IPaymentProvider` (uno por fuente)
```csharp
Task<ResultadoPago> CobrarAsync(SolicitudPago req, CancellationToken ct);   // autoriza
Task<ResultadoPago> AnularAsync(string idPago, CancellationToken ct);        // void/reversa
Task<EstadoPago> ConsultarAsync(string idPago, CancellationToken ct);
```
Implementaciones (fuente de `TiposPago`):
| Fuente | Adaptador | Endpoint SRS | Fase 1 |
|--------|-----------|--------------|--------|
| Efectivo | `EfectivoProvider` | — (interno, con redondeo) | real (interno) |
| Tarjetas | `TarjetaHasarProvider` | iCARD local | mock |
| Billetera MODO | `ModoProvider` | `http://localhost:8888/swagger/ui` | mock |
| Billetera MercadoPago | `MercadoPagoProvider` | `http://localhost:8888/api/compramp` | mock |
| Cuenta DNI | `CuentaDniProvider` | (wrapper local) | mock |
| Transferencia | `TransferenciaProvider` | conciliación bancaria | mock |
| Cuenta corriente | `CuentaCorrienteProvider` | interno (límite de crédito) | real (interno) |

### Otros puertos
- `IImageBank` → banco de imágenes `https://portal.hergo.com.ar:8099/Imagenes/<CodigoInterno>_0.JPG`.
- `IErpGateway` → stub (fase futura, tablas `(*)`).
- `IMailSender` → reporte de cierre por mail (mock imprime a log/carpeta en fase 1).

## 2. Adaptadores MOCK (fase 1)

- `MockFiscalService`: genera un **CAE ficticio** (14 díg.) + vencimiento; simula latencia y un modo
  "caída" configurable para probar reintentos y el salto a CAEA.
- `MockFiscalPrinter`: genera un PDF/HTML del ticket en carpeta local en vez de imprimir.
- `MockPaymentProvider`: aprueba/rechaza según reglas de test (p.ej. monto terminado en `.99` → rechazo)
  para ejercitar compensaciones de la saga.

Selección de adaptador por **configuración/entorno** (DI): `Fiscal:Provider=Mock|Arca`,
`Payments:Modo:Provider=Mock|Real`, etc. En `appsettings.Development.json` todo es Mock.

## 3. Flujo de contingencia CAE → CAEA

```
Emitir → SolicitarCae()
   ├─ OK → guardar CAE, imprimir
   └─ Falla (timeout/servicio caído)
        ├─ reintentar hasta N (config "Limite de reintentos por CAE inaccesible")
        └─ agotado → usar CAEA del período (IFiscalService.ObtenerCaeaAsync ya precargado)
                     → comprobante en estado "Contingencia"
                     → cuando ARCA vuelve: InformarComprobantesCaeaAsync (tesorería)
```

## 4. Certificados

- **Certificado CAE por empresa** (`/admin/certificados`): en fase 1 se modela el metadato (alias,
  vencimiento) sin operar; el adaptador real usará el certificado X.509 desde almacén seguro.
- **Obtención de CAEA por período/empresa**: acción administrativa que precarga el CAEA vigente.

## 5. Seguridad de la capa de pagos

- Nunca se almacenan datos completos de tarjeta (PCI): el POS delega en iCARD/gateway y guarda solo
  `Cupones` (nro cupón/lote) e identificadores de transacción.
- Toda operación de pago es **idempotente** (§ transacciones) y auditada.
