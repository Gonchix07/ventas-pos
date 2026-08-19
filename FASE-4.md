# Fase 4 — Facturación (COMPLETADA)

La saga de emisión de comprobantes sobre la operación finalizada en Fase 3: reservar número →
cobrar → CAE (o CAEA en contingencia) → persistir → imprimir. Es la fase más delicada del
proyecto (dinero + fiscal), verificada con especial cuidado en robustez transaccional.

## Dominio (lógica pura)

`src/Pos.Domain/Services/FacturacionReglas.cs`:
- **`DesglioIva`**: neto/IVA a partir de un importe que ya incluye el impuesto.
- **`ReintentosCaeReglas`**: cuándo pasar a contingencia CAEA (según `Configuraciones.ReintentosCae`).
- **`NumeroComprobanteFormatter`**: formato `PPPP-NNNNNNNN`.
- **`ValidacionPagos`**: la suma de pagos cubre el total (tolerancia de un centavo).

**37/37 tests de dominio** (acumulado de todas las fases).

## Backend — `IFacturacionService` / `FacturacionController` (`/api/v1/facturacion`)

| Endpoint | Función |
|---|---|
| `POST /facturacion/emitir` | Ejecuta la saga completa sobre una operación **Finalizada** |
| `GET /facturacion/{id}` | Consulta el comprobante persistido |
| `POST /facturacion/{id}/reimprimir` | Reimpresión (no afecta el estado fiscal) |

### La saga, paso a paso
1. **Validaciones**: operación existe, no facturada antes (**idempotencia**: `YA_FACTURADA`),
   finalizada, con líneas, pagos cubren el total.
2. **Transacción de BD** (`BeginTransactionAsync`):
   a. **Reservar número** — `UPDATE Numeros SET Valor=Valor+1 OUTPUT INSERTED.Valor` ejecutado
      por ADO.NET directo dentro de la misma transacción de EF (bloqueo pesimista real, serializa
      emisiones concurrentes sobre el mismo punto de venta).
   b. **Cobrar** cada medio de pago vía `IPaymentProviderFactory` (llamada externa, fuera de la
      transacción de BD). Si un pago falla → se **compensan** (anulan) los pagos ya aprobados y se
      revierte la transacción — el número reservado **vuelve a estar libre** (verificado: sin
      huecos en la numeración tras un rechazo).
   c. **CAE**: se solicita con reintentos (`Configuraciones.ReintentosCae`); si se agota el
      límite, pasa a **contingencia** y solicita **CAEA** del período.
   d. **Persistir** cabecera + detalle (vía navegación, no directo al `DbSet`, para que EF resuelva
      el orden de inserción con claves compuestas) + movimientos de caja/pago; `Operacion` pasa a
      `Facturada`.
   e. **Commit**.
3. **Imprimir** (best-effort, fuera de la transacción): un fallo de impresión no invalida la venta
   ya facturada — el comprobante queda `Persistido` en vez de `Impreso`, con reimpresión disponible.

## Bugs reales encontrados y corregidos durante la verificación
1. **`MovimientoPago.IdMovPagos`** es identity (PK de una sola columna) — se estaba asignando a
   mano con `Max()+1`, mismo patrón de bug que en el seeder de Fase 0/1. Corregido: se deja que la
   BD lo genere.
2. **Mezcla de colección en memoria con `IQueryable` de EF** en un `join` (`operacion.Detalles`
   contra `DbSet`s) — no es traducible ni async-enumerable. Corregido: se resuelve primero la
   consulta a la BD (presentación→artículo→modo IVA) y el cruce con las líneas se hace después
   con LINQ a objetos.
3. **`SqlQuery<T>` sobre un `UPDATE...OUTPUT`** — EF intenta componerlo como subconsulta `SELECT`
   al encadenar `.FirstAsync()`, y un `UPDATE` no es "componible". Corregido: se ejecuta por
   ADO.NET directo (`DbCommand`) reutilizando la conexión/transacción de EF.
4. **Inserción de detalle sin navegación** — agregar `DetalleComprobante` directo al `DbSet` sin
   vincularlo por navegación a la cabecera (con claves compuestas asignadas a mano) arriesgaba el
   orden de inserción. Corregido: se agrega vía `cabecera.Detalles.Add(...)`.

## Verificación end-to-end (contra `POS-Ventas`)

**Por API (script Python, ante ausencia de `curl` con JSON prolijo en este entorno):**
- Operación → línea → finalizar → **emitir** → CAE `77777777300001`, número `0003-00000001`,
  neto+IVA=total exacto → **consultar** → **reimprimir** → **doble emisión bloqueada** (`YA_FACTURADA`, 409).
- **Compensación real**: pago con monto que dispara el rechazo del mock (regla `.99`) → saga
  aborta, operación sigue `Finalizada`, y la siguiente emisión exitosa usa el **número consecutivo
  siguiente sin huecos** (prueba de que el rollback deshizo la reserva).

**En el navegador** (login `cajero1`, resuelve caja por PC): identificar cliente → escanear
`ART001` → **Cobrar** → pantalla de cobro con Efectivo precargado por el total exacto →
**Confirmar cobro y facturar** → ticket con **`0003-00000003`**, **CAE**, Neto/IVA/Total y
"✓ Impreso".

## Pendiente / futuro (fuera de alcance de esta fase)
- **Notas de crédito** (devolución total/parcial, diferencia de precio) — requiere permiso Supervisor.
- **Anulaciones y refacturaciones**.
- **Modo Presupuesto** (comprobante no fiscal, sin CAE ni pago obligatorio).
- **Contingencia CAEA no ejercitada end-to-end**: el mock fiscal nunca falla, así que el camino de
  reintentos→CAEA está probado por tests de dominio (`ReintentosCaeReglas`) pero no disparado en
  vivo. Se puede agregar un modo de falla configurable al mock si se necesita demostrarlo.
- Selector de **letra de comprobante** (A/B) según condición de IVA del cliente — hoy fija en B.

## Próximo: Fase 5 — Cierres y Tesorería
Arqueo X completo, rendición final y cierre Z (irreversible) sobre los movimientos de caja y pago
ya registrados en esta fase.
