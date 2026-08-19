# 06 — Flujos operativos

Detalle de los flujos principales del SRS ("Operaciones de caja — operación ideal").

## 1. Flujo ideal de caja

```
1. Login cajero/supervisor  →  resuelve idCaja por IP de origen del request (PuestosCaja.ip)
2. Apertura de caja         →  nuevo lote (uno por día); si ya hay lote abierto, se retoma
3. (opcional) Ingreso de efectivo / cambio
4. Identificación de cliente
   - Un único campo: tarjeta / DNI / código / nombre / domicilio / escaneo barra de papel
   - 1 resultado → selección directa;  N resultados → elegir
   - Muestra datos + lista de precios asociada + convenio
5. Lectura de artículos (manual o escaneo)
   - Cantidad opcional antes de escanear (se limpia al procesar)
   - Cada lectura entra en COLA (hilo); worker consulta precio + ofertas y agrega línea
   - Muestra imagen + detalle del artículo
   - Artículo no encontrado → ADVERTENCIA y STOP de la cola hasta resolver
6. Ofertas (hilo) → se recalculan por cada nuevo artículo (servicio "Ofertas Artículo")
7. Anulación parcial de línea (por selección o por lectura de barra)
8. Finalización de operación
   - Cálculo de valores finales al ticket según cliente + descuentos aplicados
   - Nro de operación + impresión de código con barra/QR
9. (opcional) Apertura de módulo de facturación en otra terminal
   - Lee la operación por su código → muestra detalle y totales
10. Selección de medios de pago
    - Descuentos por medio de pago (ofertas al cierre)
    - Redondeo por efectivo
    - Validación/anulación de pago (anulación → validación de Supervisor)
    - Adaptadores: iCARD (tarjetas/CuentaDNI), MODO, MercadoPago, transferencia, cta. cte. (límite)
11. Facturación según tipo de operación (electrónica/fiscal/presupuesto, A/B)
    - Procesa pagos → solicita CAE (o CAEA) → persiste → imprime comprobantes asociados
12. Cierre de rendición final
    - Confirmación manual de valores por medio de pago
    - Confirmación de cierre (NO se puede anular) → cierre Z → idCierre
    - Carga de justificación de diferencias + observaciones
    - Impresión de comprobante para firmar
```

### Alternativas / excepciones
- **Rendición parcial** (retiro de efectivo) → emite **X**.
- **Deslogueo** de caja física y **logueo en otra** manteniendo el lote (recuperación ante caída).
- **Modo consulta de precio** (pop-up de escaneo, sin operar).
- **Avisos configurables**: proximidad a límite de efectivo en caja; corte de facturación a
  Consumidor Final; bloqueo por inactividad.
- **Nota de Crédito** asociada a factura (Supervisor): devolución total/parcial o diferencia de precio.

## 2. Resolución de precio (prioridad de listas)

```
Precio(idPresentacion, idCliente):
  listas = ListasVigentes(sucursal) ordenadas por prioridad:
     1) Folder
     2) Temporales vigentes (por fecha)
     3) Base
  precioBase = primera lista que tenga precio para la presentación
  si el cliente tiene Convenio → aplicar lista/descuento de convenio (si corresponde)
  devolver { precioVigente, precioConvenio }
```

## 3. Aplicación de ofertas

```
Para la operación (cliente + líneas):
  ofertas = CabeceraOfertas vigentes (fechaInicio..fechaFin, sucursal)
            filtradas por AlcanceOfertas (cluster del cliente / sector-línea-familia-artículo)
            menos Excepciones
  ordenar por acumulable / no acumulable
  aplicar AccionOfertas por tipo:
     - Descuento  → % o monto sobre línea(s)
     - Bonificación → N + M gratis (cantidadMin / cantidadBonif)
     - Combo/Canasta → condición sobre conjunto de artículos → precio/descuento del combo
  registrar "ofertas aplicadas" por línea (trazabilidad)

Al CIERRE del ticket:
  ofertas por MEDIO DE PAGO → descuento adicional sobre el total según medio elegido
```

## 4. Cierre Z (rendición final) — invariantes

- Es **irreversible** (SRS). Se ejecuta en una transacción + cierre Z en la impresora fiscal.
- Acumula por medio de pago (`CierresLotesCaja`), calcula **diferencia** vs. lo esperado y exige
  **motivo** si hay diferencia.
- Queda `verificaTesoreria = false` hasta la **validación de tesorería**.
- Dispara (vía outbox) el **envío de reporte por mail**.

## 5. Nota de crédito

```
NC(idFactura, tipo: total|parcial|diferencia):
  requiere permiso Supervisor
  crea comprobante con signo negativo, letra correspondiente
  ComprobantesAsociados(idFacturaOrigen, idNC)
  ajusta cuenta corriente / devuelve pago según corresponda
  solicita CAE de la NC + imprime
```

## 6. Etiquetas (repositor)

```
1. Buscar/escanear productos  o  seleccionar Sector/Línea/Familia completa
2. Armar lista
3. Elegir tipo de etiqueta: Fleje / A4 / A5  + impresora
4. Precio único o diferencial (a futuro: múltiples listas)
5. Imprimir
```
