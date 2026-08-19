# Fase 3 — Módulo de Caja (COMPLETADA)

El flujo operativo real: apertura de lote → identificación de cliente → cola de lectura de
artículos (con precios + ofertas de la Fase 2) → operación → finalización. Verificado en API y
en el navegador contra `POS-Ventas`.

## Dominio (lógica pura)

`src/Pos.Domain/Services/CajaReglas.cs`:
- **`RedondeoService`**: ajuste de redondeo por efectivo a un rango configurable.
- **`LoteCajaReglas`**: un lote por día por caja (SRS).
- **`OperacionTotales`**: suma bruto/descuento/neto de líneas ya resueltas.

**27/27 tests de dominio** (incluye Fase 2).

## Backend — `ICajaService` / `CajaController` (`/api/v1/caja`)

| Endpoint | Función |
|---|---|
| `POST /caja/apertura` | Abre lote (idempotente: si ya hay uno hoy, lo devuelve) |
| `GET /caja/lote-actual` | Lote abierto hoy para la caja |
| `GET /caja/clientes/buscar?q=` | Identificación por nombre/código/CUIT/documento/**tarjeta** |
| `GET /caja/articulos/buscar?codigo=` | Por código interno o **barra**; devuelve precio vigente + convenio |
| `POST /caja/operaciones` | Crea la operación (requiere lote abierto) |
| `GET /caja/operaciones/{id}` | Recupera operación (para caída/reanudación) |
| `POST /caja/operaciones/{id}/lineas` | Agrega línea; **recalcula ofertas de toda la operación** |
| `POST /caja/operaciones/{id}/lineas/{det}/anular` | Anulación parcial + recálculo |
| `POST /caja/operaciones/{id}/finalizar` | Cierra la operación (bloquea nuevas líneas) |
| `GET /caja/redondeo?total=` | Ajuste de redondeo sobre un total |

Reglas aplicadas: sin lote abierto no se puede operar; operación vacía no se puede finalizar;
operación finalizada/anulada rechaza nuevas líneas; el precio a usar es el de **convenio** si el
cliente tiene uno vigente, sino el **vigente** por prioridad de listas (Fase 2).

## Frontend — `CajaPage` (`/caja`)

- Apertura de caja con botón si no hay lote.
- Identificación de cliente (1 campo) con selección si hay varios resultados, o "Continuar sin cliente".
- **Cola de lectura de artículos**: input de cantidad (se limpia tras cada lectura) + código;
  cada lectura se procesa secuencialmente contra el backend; **artículo no encontrado detiene la
  cola** con aviso y botón "Descartar y continuar" (según SRS).
- Tabla de líneas con ofertas aplicadas visibles y anulación por línea.
- Totales en vivo (bruto/descuento/neto) y botón **Finalizar**.
- Ticket final con número de operación grande y botón "Nueva venta".

## Verificación end-to-end

**API (curl) contra `POS-Ventas`:**
apertura idempotente → identificación (nombre → convenio 5,5%) → búsqueda por código y por barra
(con y sin cliente) → operación con 2 líneas (recalculo de ofertas) → anulación de una línea →
recuperación → finalización → bloqueo post-cierre → redondeo.

**Navegador (login `cajero1`, resuelve caja por PC):**
apertura → identificación de "Distribuidora Norte SA" (convenio) → operación #3 → escaneo de
`ART001` x3 a precio de convenio con oferta 15% sector → código inexistente **detiene la cola**
con aviso → descartar → anular no probado en esta corrida (sí en API) → finalizar → ticket
`00000003` con totales correctos.

### Nota de verificación (no es un defecto de la app)
Durante la prueba, el envío de "Enter" por la herramienta de automatización no disparaba el
buscador (parecía un bug de timing de React). Instrumentando los eventos reales se confirmó que
la herramienta despacha un `keydown` con `key`/`code` **vacíos** — limitación del entorno de
prueba, no de la aplicación. Un `KeyboardEvent` nativo con `key:"Enter"` (equivalente a un teclado
o lector de barras real) disparó el flujo correctamente. Se mantiene la lectura defensiva del
valor del input en el handler de Enter como robustez adicional, sin que hubiera un bug real.

## Pendiente / futuro (fuera de alcance de Fase 3)
- **Arqueo X** completo y **rendición final / cierre Z** → Fase 5 (Tesorería).
- **Facturación** (CAE/CAEA, medios de pago) → Fase 4.
- Selector manual de sucursal/caja cuando el login no resuelve por PC (hoy usa fallback 1/1).
- Impresión de código de operación (barra/QR).

## Próximo: Fase 4 — Facturación
Saga de emisión (reservar número → pagar → CAE/CAEA → persistir → imprimir) sobre la operación
finalizada de esta fase.
