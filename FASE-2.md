# Fase 2 — Motor de precios y ofertas (COMPLETADA)

El corazón del negocio: resolución de precios por prioridad de listas + convenio, y motor de
ofertas por alcance/acumulación/excepciones. Lógica de dominio pura (testeable) + servicios que
cargan datos de la BD.

## Dominio (lógica pura, sin BD)

- **`CalculadoraPrecios`** (`src/Pos.Domain/Services/CalculadoraPrecios.cs`)
  - Prioridad SRS: **Folder > Temporal vigente > Base**; a igual tipo, mayor `Prioridad`.
  - Las listas **Temporales** respetan vigencia (`FechaInicio`/`FechaFin`).
  - **Convenio**: usa el precio de la lista del convenio si existe, y aplica el `descuento %`.
- **`MotorOfertas`** (`src/Pos.Domain/Services/MotorOfertas.cs`)
  - Alcance por **cluster del cliente / sector / línea / familia / artículo**; sin inclusiones = toda la sucursal.
  - **Excepciones** (excluyen), **acumulación** (acumulables se suman; no acumulables → gana el mayor).
  - Tipos: **Descuento** (% o monto fijo) y **Bonificación** (lleva N+M, paga N). *Combo: pendiente.*
  - El descuento nunca supera el bruto de la línea.

## Aplicación + API (autenticados, para el módulo de Caja)

| Método | Ruta | SRS |
|--------|------|-----|
| GET | `/api/v1/precios/resolver?idSucursal=&idPresentacion=&idCliente=` | **Precio** (vigente + convenio) |
| POST | `/api/v1/ofertas/articulos` | **Ofertas Artículo** (líneas enriquecidas) |

`PricingService` (Infrastructure) carga candidatos de precio, convenio, ofertas vigentes y los
clusters del cliente, y delega el cálculo en el dominio.

## Verificación

**Tests de dominio: 20/20** (`dotnet test`) — prioridad de listas, vigencia, convenio, descuento,
bonificación 3x2, acumulación, excepciones, tope de descuento.

**End-to-end contra `POS-Ventas`:**
| Caso | Resultado |
|------|-----------|
| Precio pres1 sin cliente (Base) | $1850,50 |
| Precio pres1 con convenio 5,5% | $1748,7225 |
| Lista **Folder** $1500 → gana por prioridad | $1500 |
| Oferta 10% por línea (Almacén) sobre 2 presentaciones | desc $450 y $990; total neto $12.960 |

## ABM de Ofertas (panel) — agregado al cierre de Fase 2
- Servicio+controlador `/api/v1/admin/sucursales/{id}/ofertas` (CRUD por sucursal con **alcances** y
  **acciones** anidados) + página **Ofertas** en el panel (acciones y alcances dinámicos).
- Seed de `TiposOferta`. Verificado e2e: oferta 15% por sector creada por el ABM y aplicada por el
  motor; con dos ofertas no acumulables (10% línea + 15% sector) gana la mayor (neto correcto).

### Bug corregido en el camino (Fase 1 — Artículos)
`ArticuloService.UpdateAsync` borraba y recreaba presentaciones/barras, lo que fallaba por (a) índice
único de código de barra y (b) FK desde `Precios`. Ahora el **update actualiza sólo la cabecera** del
artículo; la edición de presentaciones/barras de un artículo existente queda como endpoint dedicado
con merge por id (**pendiente de Fase 1**).

## Pendiente / futuro
- **Combo/Canasta** (condición sobre conjunto de artículos) en el motor.
- **Ofertas por medio de pago** (aplicadas al cierre del ticket) — requiere extender el esquema.
- Edición de **presentaciones/barras** de artículos existentes (merge por id).

## Próximo: Fase 3 — Módulo de Caja
Apertura de lote, identificación de cliente, cola de lectura de artículos, operación con precios +
ofertas ya resueltos por esta fase, arqueo X y recuperación.
