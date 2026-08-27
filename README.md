# Sistema de Venta POS — Mayorista

Especificación de arquitectura y diseño para el Sistema de Venta POS de comercio mayorista.
Esta entrega es **solo diseño/arquitectura** (no incluye código todavía).

## Stack objetivo

| Capa | Tecnología |
|------|-----------|
| Frontend | React (Vite + TypeScript), diseño moderno |
| Backend | .NET 8 (C#) — ASP.NET Core Web API + EF Core |
| Base de datos | Microsoft SQL Server (propia, *standalone*) |
| Fiscal / Pagos | Puertos + adaptadores (implementaciones **mock** en esta fase) |

## Decisiones de esta fase

1. **Backend .NET + EF Core** (mismo ecosistema que los wrappers fiscales Windows/Hasar).
2. **Base de datos propia** en MS SQL — las tablas de datos maestros marcadas `(*)` en el SRS (Artículos, Clientes, Precios, Empresas, Sucursales, Padrones) se administran desde la app; la integración con ERP queda para una fase futura detrás de una interfaz.
3. **Fiscal y medios de pago mockeados** detrás de interfaces (Ports & Adapters), para desarrollar el flujo completo sin certificados ni hardware reales.

## Índice de documentación

| Doc | Contenido |
|-----|-----------|
| [`docs/01-arquitectura.md`](docs/01-arquitectura.md) | Visión general, estilo arquitectónico, estructura de la solución .NET, módulos |
| [`docs/02-modelo-datos.md`](docs/02-modelo-datos.md) | Modelo de datos completo (todas las tablas del SRS), claves, relaciones y correcciones |
| [`docs/03-apis.md`](docs/03-apis.md) | Catálogo de APIs de servicios, contratos de request/response |
| [`docs/04-seguridad-transacciones.md`](docs/04-seguridad-transacciones.md) | Seguridad, auth/roles/permisos, integridad transaccional y numeración fiscal |
| [`docs/05-fiscal-pagos.md`](docs/05-fiscal-pagos.md) | Diseño de la capa fiscal (CAE/CAEA/Hasar) y medios de pago (MODO/MP/Cuenta DNI) |
| [`docs/06-flujos-caja.md`](docs/06-flujos-caja.md) | Flujos operativos: caja, ofertas, cierre Z, notas de crédito |
| [`docs/07-roadmap.md`](docs/07-roadmap.md) | Roadmap por fases y estimación de esfuerzo |

## Estado de implementación

- **Fase 0** ✅ Fundación — ver [`FASE-0.md`](FASE-0.md)
- **Fase 1** ✅ Datos maestros / ABM — ver [`FASE-1.md`](FASE-1.md)
- **Fase 2** ✅ Motor de precios y ofertas — ver [`FASE-2.md`](FASE-2.md)
- **Fase 3** ✅ Módulo de Caja — ver [`FASE-3.md`](FASE-3.md)
- **Fase 4** ✅ Facturación (saga CAE/CAEA) — ver [`FASE-4.md`](FASE-4.md)
- **Fase 5** ✅ Cierres y Tesorería — ver [`FASE-5.md`](FASE-5.md)
- **Fase 6** ✅ Etiquetas — ver [`FASE-6.md`](FASE-6.md) — **alcance original del SRS completo**

## Perfiles de usuario

- **Administrador** — ABM completo y configuraciones.
- **Cajero** — operativa de caja (cobros).
- **Supervisor** — cajero con permisos especiales (anulaciones, NC).
- **Tesorero** — cierres, validaciones y reportes.
- **Repositor** — impresión de etiquetas de precios.

## Correr todo junto (desarrollo)

`.\scripts\iniciar-dev.ps1` levanta el backend (`dotnet run`) y el frontend (`npm run dev`), cada
uno en su propia ventana. Con `-Instalar` corre `npm install` antes si `frontend\node_modules` no
existe todavía (primera vez que se clona el repo); con `-SinNavegador` no abre el navegador solo al
final. Requiere los user-secrets ya configurados (ver debajo) — si el puerto de alguno ya está en
uso, avisa y no lo relanza.

## Configuración de secretos (desarrollo)

`appsettings.json`/`appsettings.Development.json` **no** contienen credenciales reales — se cargan vía [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) para no commitearlas. Para correr localmente contra la BD real:

```bash
cd src/Pos.Api
dotnet user-secrets set "ConnectionStrings:Pos" "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True"
dotnet user-secrets set "Seed:AdminPassword" "<clave-real-del-admin>"
```

Los user-secrets se leen **del perfil del usuario de Windows que ejecuta el proceso** y **sólo cuando el ambiente es `Development`**. Un proceso hosteado (pool de IIS, servicio de Windows, otra PC) no los ve: ahí la cadena va por variable de entorno. Si falta, la API ya no arranca — corta al inicio con el mensaje que dice qué ambiente y qué usuario está corriendo:

```powershell
$env:ConnectionStrings__Pos = "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True"
```

Al arrancar, el log deja la línea `BD destino: servidor ..., base ... — configuración tomada de ...`, que dice contra qué SQL Server quedó conectada y de qué archivo o variable salió el dato.

En un ambiente real (piloto/producción) estos mismos valores, más `Jwt:Key` (mínimo 32 caracteres, aleatoria) y `Cors:AllowedOrigins:0` (URL del frontend), se configuran como **variables de entorno** del proceso (`ConnectionStrings__Pos`, `Jwt__Key`, `Seed__AdminPassword`, `Cors__AllowedOrigins__0`), nunca en un `appsettings.*.json` commiteado. La app falla al arrancar fuera de `Development` si `Jwt:Key` o `Cors:AllowedOrigins` no están configurados.
