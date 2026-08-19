# 04 — Seguridad, integridad transaccional y numeración fiscal

Este es el documento crítico: el sistema mueve **dinero** y emite **comprobantes fiscales**.
Los errores acá causan descuadres de caja, saltos de numeración o doble facturación.

## 1. Autenticación y autorización

- **JWT** de vida corta + *refresh token*. Claims: `idUsuario`, `idRol`, `idSucursal`, `idCaja`.
- **Contraseñas** con hash **BCrypt/PBKDF2** + salt (nunca texto plano — corrige el `Clave` del SRS).
- **Autorización por módulo/permiso**: atributo `[RequierePermiso(Modulo.Caja, Accion.Editar)]` en cada
  endpoint. Los permisos "especiales" (anulaciones, NC, cierre Z, validaciones) exigen rol Supervisor/Tesorero.
- **Autorización de Supervisor en línea**: operaciones sensibles dentro de la caja (anular pago,
  NC, forzar) piden **credencial de supervisor** en el momento (challenge), sin desloguear al cajero.
- **Bloqueo por inactividad**: deslogueo automático de la caja física tras timeout configurable.
- **Rate limiting** en login y en endpoints de pago.

## 2. Integridad transaccional (ACID)

Toda operación que toque dinero/stock/numeración se ejecuta dentro de una **transacción EF Core**
gestionada por un *behavior* de MediatR (`TransactionBehavior`), con estas reglas:

1. **Unit of Work por comando**: un `SaveChanges` único por caso de uso; o todo, o nada.
2. **Nivel de aislamiento**: `READ COMMITTED` por defecto; `SERIALIZABLE` o bloqueo explícito para la
   **asignación de número** (ver §4).
3. **Concurrencia optimista** con `rowversion` en: `Numeros`, `Precios`, `CuentasCorrientes`,
   `CierresLotesCaja`. Conflicto → reintento controlado o error claro.
4. **Idempotencia**: `Idempotency-Key` persistida; una segunda llamada con la misma clave devuelve el
   resultado original en vez de re-ejecutar (clave ante timeouts de red del cajero).
5. **Outbox pattern** para efectos externos: la emisión fiscal y el envío de mail se registran en una
   tabla *outbox* dentro de la misma transacción y se despachan luego, de modo que un fallo de red
   externo no deja la base inconsistente.

## 3. El problema del pago + facturación (saga)

Emitir un comprobante involucra pasos que **no** son todos transaccionales de BD (llamada a ARCA,
impresora Hasar, gateway de pago). Se modela como una **saga con compensación**:

```
1. Reservar número (tx BD, serializado)          ── compensa: liberar/marcar anulado
2. Autorizar pago(s) (gateway)                    ── compensa: reversa/void del pago
3. Solicitar CAE a ARCA (o CAEA si contingencia)  ── compensa: N/A (registrar y anular comprobante)
4. Persistir comprobante + movimientos (tx BD)    ── punto de no retorno
5. Imprimir (Hasar / comandera)                   ── reintentable; reimpresión disponible
```

Estados del comprobante: `Iniciado → PagoOk → CaeOk → Persistido → Impreso` / `Contingencia` / `Anulado`.
Si algo falla antes del paso 4, se ejecutan las compensaciones y **no** queda comprobante fiscal emitido.

## 4. Numeración fiscal (sin saltos, sin duplicados)

Requisito legal: la numeración por punto de venta debe ser **correlativa y sin huecos**.

- La tabla `Numeros(idSucursal, idPuntoVenta, numero)` se incrementa con **bloqueo pesimista**:
  `UPDATE Numeros SET numero = numero + 1 OUTPUT INSERTED.numero WHERE ...` dentro de la transacción,
  que serializa a los concurrentes sobre esa fila.
- El número **solo se consume** si la emisión llega al punto de no retorno; ante fallo previo se
  revierte la transacción (el número no se gastó). Ante fallo posterior a CAE, el comprobante queda
  registrado como `Anulado` conservando el número (ARCA ya lo asignó).
- **Reintentos por CAE inaccesible**: límite configurable (`Configuraciones`); superado el límite se
  pasa a **modo contingencia CAEA** (ver `05-fiscal-pagos.md`).

## 5. Redondeo y percepciones

- **Redondeo por efectivo**: rango configurable (`Configuraciones`); el `redondeo` se registra por
  `MovimientosPagos` y se acumula en el cierre (`redondeoAcumulado`). Nunca altera el neto/IVA fiscal.
- **Percepción IIBB**: se aplica según `PadronIngresosBrutos(CUIT)`.
- **Percepción IVA**: `%` por `ModoIva`, salvo CUIT en `PadronExcepcionPercepcionesIva`.
- Todo el cálculo fiscal es **server-side** con `decimal`; el frontend solo muestra.

## 6. Cuentas corrientes y límites

- Antes de cerrar un ticket contra cuenta corriente, se valida
  `saldo(idCliente,idSucursal) + total ≤ limiteCredito` dentro de la transacción.
- El asiento (`debe/haber`) se escribe en la misma tx que el comprobante.

## 7. Recuperación ante caídas

- **Operación persistida incrementalmente**: la operación de caja se guarda server-side; ante caída
  de red o de la PC, el cajero recupera la operación (`GET /caja/operaciones/{id}`) desde otra caja
  física manteniendo el mismo lote (requisito SRS).
- **Lote diario**: uno por caja/día; el reinicio no abre un lote nuevo si ya hay uno abierto.

## 8. Protección de datos

- Datos sensibles (CUIT, cupones) sin exponer en URLs/logs.
- Certificados fiscales cifrados en reposo (DPAPI/Key Vault); nunca en el repo.
- Auditoría independiente (`MovimientosAuditoria`) de toda alta/baja/modificación y de toda
  operación de caja y validación de tesorería.

## 9. Testing de robustez (definición de "hecho")

- Tests de concurrencia sobre `Numeros` (N hilos → sin duplicados, sin huecos).
- Tests de saga: fallo simulado en cada paso → estado consistente y compensación correcta.
- Tests de redondeo/percepciones con casos de borde.
- Tests de límite de crédito y descuadre de cierre.
