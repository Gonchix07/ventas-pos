# Fase 6 — Etiquetas (COMPLETADA)

Última fase del alcance original del SRS: búsqueda/escaneo de artículos, armado de lista por
sector/línea/familia, e impresión en **fleje** (Zebra 9×4cm) o **A4/A5**, con layouts fieles a
plantillas reales de Hergo.

## Insumo real: 3 muestras de etiqueta

El usuario compartió 3 imágenes reales (fleje + 2 A4/A5) que revelaron datos y reglas que el
modelo original no tenía:

1. **Precios por tipo de tarjeta** ("AZUL"/"ROJA" en la muestra) — se resolvió que corresponden a
   los `TipoTarjeta` ya modelados en Fase 1 (cada uno con `IdListaPrecio` asociado).
2. **"Precio por Kg/Lt"** — requiere el **contenido neto de una unidad individual** del artículo
   (ej. 1 Kg, 0,75 Lt), dato que no existía. Verificado por aritmética exacta contra los 3 ejemplos:
   `precio mostrado ÷ contenido neto unitario`.
3. **"Precio sin impuestos nacionales"** — coincide exacto con el **neto de IVA** (`precio ÷ 1,21`
   en los 3 casos, sin impuesto interno). Se generalizó restando también `ImpuestoInterno` si lo
   hubiera (dato que ya existía en `Precio` pero nunca se usaba).
4. **"Compra mínima"** — se infiere de ofertas de tipo Bonificación vigentes con alcance sobre el
   artículo (reutiliza el criterio de alcance del motor de ofertas de Fase 2, sin la dimensión
   cluster porque en etiquetas no hay cliente).

## Esquema: nuevo dato en `Articulo`

Migración `AgregarUnidadMedidaArticulo`: `UnidadMedida` (enum: Ninguna/Kilogramo/Litro) y
`ContenidoNetoUnitario` (decimal?). Se extendió el ABM de Artículos (Fase 1, backend y frontend)
para cargarlos.

## Dominio (lógica pura)

`src/Pos.Domain/Services/EtiquetaCalculos.cs`:
- **`PrecioPorUnidadMedida`**: precio ÷ contenido neto unitario (null si no aplica).
- **`PrecioSinImpuestosNacionales`**: reutiliza `DesglioIva` de la Fase 4.

**52/52 tests de dominio**, incluyendo **verificación exacta contra los 3 valores reales** de las
muestras (ej. Fernet Branca: `$14899,90 ÷ 0,75 Lt = $19866,53` ✅).

## Backend — `IEtiquetaService` / `EtiquetasController` (`/api/v1/etiquetas`, rol Repositor/Tesorero/Administrador)

| Endpoint | Función |
|---|---|
| `GET /etiquetas/buscar?q=` | Por código, barra o descripción |
| `GET /etiquetas/por-clasificacion` | Selección masiva por sector/línea/familia completo |
| `GET /etiquetas/clasificaciones` | Sectores/líneas/familias (lookup accesible a Repositor) |
| `GET /etiquetas/sucursales` | Sucursales (lookup accesible a Repositor) |
| `POST /etiquetas/generar` | Calcula los datos de cada etiqueta (precio base, precios por
  tarjeta, precio por unidad de medida, sin impuestos, compra mínima, código de barra) |

## Frontend — `EtiquetasPage` (`/etiquetas`)

- Búsqueda/escaneo + selección por clasificación completa → lista armada (con opción de quitar).
- Selector de sucursal y formato (**Fleje / A4 / A5**).
- Vista de impresión **HTML/CSS fiel a las 3 plantillas reales**, con `@page` ajustado al tamaño
  físico exacto (`90mm 40mm` para el fleje, `A4`/`A5` para los grandes) y botón **Imprimir**
  (`window.print()`) — imprime de verdad en las impresoras reales del usuario, sin necesidad de
  mockear un puerto de impresora ficticio (a diferencia de fiscal/pagos, aquí no hace falta).

## Verificación end-to-end (contra `POS-Ventas`)

- Se cargó `UnidadMedida=Kilogramo, ContenidoNetoUnitario=1` en `ART001` y se confirmó que ya
  tenía un precio de `TipoTarjeta "Tarjeta Socio"` (de pruebas de Fase 1).
- **Por API**: búsqueda por código, selección por sector completo, y `generar` devolvió el precio
  base, el precio de "TARJETA SOCIO", precio por Kg y sin impuestos — todos coherentes.
- **En el navegador**: armado de lista con 2 presentaciones → generación en **Fleje** (layout
  compacto con "Tarj. TARJETA SOCIO", precio por Kg, sin impuestos, footer de compra mínima) → vuelta
  atrás conservando la lista → generación en **A4** (layout grande con nombre de tarjeta en
  mayúsculas, precio grande, detalle y footer con código/barra) — **ambos coinciden con las
  plantillas reales que compartió el usuario**.

### Nota de verificación (no es un defecto de la app)
Durante la prueba en navegador, un token JWT viejo (de una sesión anterior) había quedado en
`localStorage` y causó 403 al entrar a Etiquetas con un rol que no correspondía; se limpió y se
volvió a loguear. No es un bug de la aplicación.

## Pendiente / futuro
- Impresión real contra la impresora Zebra (esta fase deja el HTML/CSS listo para imprimir desde
  el navegador; la integración de bajo nivel con el driver Zebra, si se necesitara, queda fuera de
  alcance).
- Ajustar el criterio de selección de "compra mínima" si en producción aparecen casos con más de
  una oferta de bonificación superpuesta (hoy se toma la primera que matchea).

## Estado del proyecto — alcance original completo
Con esta fase se cierran los **6 módulos** descriptos en el SRS original: Caja, Facturación,
Tesorería, Etiquetas, Administración y Auth. Quedan pendientes de fases anteriores: notas de
crédito/anulaciones (Fase 4), envío de reporte por mail y fiscalización CAEA (Fase 5) — mejoras
incrementales, no bloqueantes.
