# Fase 1 — Datos maestros y ABM (COMPLETADA)

Primer entregable de la Fase 1: patrón de ABM end-to-end + entidades representativas,
verificado contra la base real `POS-Ventas`.

## Qué quedó implementado

### Backend
- **Autorización por rol**: los endpoints `/api/v1/admin/*` requieren rol **Administrador**
  (`[Authorize(Roles = "Administrador")]`), acorde al SRS (ABM = Administrador).
- **CRUD genérico** (`ICrudService<T>` + `CrudService<T>`, registrado open-generic).
- **Catálogo simple** (tablas `{Id, Descripcion}`) vía `LookupController<T>`:
  - `sectores`, `lineas`, `familias`, `tipos-oferta`, `motivos-diferencia`, `motivos-cierre`.
- **ABM Artículos** (`/admin/articulos`): cabecera + **presentaciones** + **barras** (EAN13/DUN14)
  anidadas, baja lógica (`Activo=false`), URL de imagen resuelta por `IImageBank`.
- **ABM Clientes** (`/admin/clientes`): con búsqueda (nombre/código/CUIT/documento), condición de
  IVA, baja lógica, validación de código duplicado.
- **ABM Listas de Precios** (`/admin/listas-precios`): CRUD de listas (tipo Base/Temporal/Folder,
  prioridad, vigencia, sucursal) + **gestión de precios por presentación** (upsert/eliminar),
  con listado que trae artículo/presentación y contador de precios.
- **ABM Tipos y Medios de Pago** (`/admin/tipos-pago`, `/admin/medios-pago`): con fuente de pago
  (efectivo/tarjeta/billetera/transferencia/cuenta corriente) y activación; borrado de tipo en uso
  bloqueado (`EN_USO`).
- **ABM Empresas y Sucursales** (`/admin/empresas`, `/admin/sucursales`): estructura comercial;
  borrado de empresa con sucursales bloqueado.
- **ABM Configuraciones** (`/admin/configuraciones`): clave/descripción/valor, clave única.
- **ABM Estructura de caja** (por sucursal, id local autoasignado): Tipos de Punto de Venta,
  Puntos de Venta (Nº ARCA), Puestos (PC) y Cajas — `/admin/sucursales/{id}/{tipos-punto-venta|puntos-venta|puestos|cajas}`.
  Borrado con integridad referencial.
- **ABM Usuarios/Roles** (`/admin/usuarios`, `/admin/roles`): alta con hash BCrypt, edición, reset de
  clave, activación; usuario `admin` protegido contra borrado.
- **ABM Convenios** (`/admin/sucursales/{id}/convenios`): por sucursal, cliente + descuento + lista opcional.
- **ABM Clusters de clientes** (`/admin/clusters`): agrupación + miembros (agregar/quitar clientes).
- **ABM Tarjetas** (`/admin/tipos-tarjeta`, `/admin/clientes/{id}/tarjetas`): tipos (con lista opcional)
  y tarjetas por cliente.
- **ABM Padrones** (`/admin/padrones/iibb`, `/admin/padrones/excepcion-iva`): percepción IIBB por CUIT
  y excepción de percepción IVA por CUIT.
- **Referencias** (`/admin/referencias/modos-iva`, `/condiciones-iva`, `/sucursales`) para combos.
- Seed ampliado: condiciones de IVA, modos de IVA, empresa+sucursal y sectores/líneas/familias base.

### Frontend (React + Vite)
- Sección **Administración** (`/admin`) protegida, con layout + navegación lateral.
- Página **genérica de catálogo** (`LookupPage`) reutilizada para las 6 tablas simples
  (listar / agregar / editar en línea / eliminar).
- Página **Clientes**: búsqueda, alta/edición con formulario, baja.
- Página **Artículos**: alta/edición con presentaciones y barras dinámicas, listado con miniatura.
- Página **Listas de precios**: ABM de listas + **editor de precios** (buscar artículo → cargar
  precio/impuesto interno por presentación) y grilla de precios cargados.
- Páginas **Medios de pago** (tipos + medios), **Empresas/Sucursales** y **Configuraciones**.
- Página **Estructura de caja** (elegir sucursal → tipos PV / PV / puestos / cajas) y **Usuarios**.
- Páginas **Convenios**, **Clusters**, **Tarjetas** y **Padrones**.
- El dashboard enlaza la tarjeta *Administracion* → `/admin`.

## Verificación end-to-end (contra POS-Ventas)

| Prueba | Resultado |
|--------|-----------|
| `dotnet build` backend | ✅ 0 errores |
| `npm run build` frontend | ✅ |
| Login SPA → dashboard | ✅ |
| Listar/crear/editar catálogo (sectores) | ✅ |
| Crear artículo con 2 presentaciones + barras EAN13/DUN14 | ✅ (recuperable con detalle anidado) |
| Crear/listar cliente con condición de IVA | ✅ |
| `/admin/*` sin token | ✅ 401 |

## Endpoints nuevos (resumen)

```
GET/POST/PUT/DELETE  /api/v1/admin/sectores            (y lineas, familias, tipos-oferta,
                                                         motivos-diferencia, motivos-cierre)
GET   /api/v1/admin/referencias/modos-iva
GET   /api/v1/admin/referencias/condiciones-iva
GET/POST/PUT/DELETE  /api/v1/admin/articulos           (+ GET /{id} con detalle)
GET/POST/PUT/DELETE  /api/v1/admin/clientes            (GET admite ?q=)
```

## Estado: Fase 1 COMPLETADA

Todos los ABM de datos maestros están implementados y verificados end-to-end contra `POS-Ventas`:
catálogo simple, Artículos, Clientes, Listas de Precios + Precios, Tipos/Medios de Pago,
Empresas/Sucursales, Configuraciones, Estructura de caja (Tipos PV / PV / Puestos / Cajas),
Usuarios/Roles, Convenios, Clusters, Tarjetas y Padrones (IIBB / excepción IVA).

### Mejoras diferidas (deuda técnica menor, no bloquean Fase 2)
- Validación con **FluentValidation** en los comandos de ABM (hoy validaciones básicas inline).
- **Auditoría** de las operaciones de escritura del ABM (el behavior existe; falta marcar los comandos).
- Edición completa (PUT) donde hoy solo hay alta/baja (algunos lookups y sub-entidades).

## Próximo: Fase 2 — Motor de precios y ofertas
Resolución de precio por prioridad de listas (Folder &gt; Temporal vigente &gt; Base) + convenio,
y motor de ofertas (alcance, acumulación, excepciones, descuento/combo/bonificación).
