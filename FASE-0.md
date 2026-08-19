# Fase 0 — Fundación (completada)

Scaffolding de la solución .NET + frontend React, esquema MS SQL completo, auth JWT con
roles/permisos, auditoría independiente, adaptadores mock y host de API con Swagger/Serilog/health.

## Estructura generada

```
pos-mayorista/
├── Pos.sln
├── src/
│   ├── Pos.Domain/           # ~50 entidades, enums, base auditable
│   ├── Pos.Application/       # puertos, MediatR (behaviors), auth (LoginCommand)
│   ├── Pos.Infrastructure/    # EF Core + PosDbContext + migración, repos, security, adaptadores mock
│   └── Pos.Api/               # ASP.NET Core: Program, controllers, JWT, Swagger, health
├── tests/Pos.Domain.Tests/    # tests de dominio (xUnit)
└── frontend/                  # React + Vite + TS (login + dashboard de módulos)
```

## Estado de verificación

| Check | Resultado |
|-------|-----------|
| `dotnet build Pos.sln` | ✅ 0 errores |
| Migración `InitialCreate` | ✅ **52 tablas** |
| `dotnet test` | ✅ 4/4 |
| `npm run build` (frontend) | ✅ |
| Arranque API + Swagger UI | ✅ (endpoints auth visibles, botón Authorize) |

## Requisitos para ejecutar con base de datos

Este equipo **no tiene SQL Server instalado**. Para correr con BD real hace falta una instancia
MS SQL (Express/LocalDB/servidor). Opciones:

- **SQL Server Express** o **LocalDB** (`sqllocaldb`), o
- Un servidor MS SQL existente (ajustar `ConnectionStrings:Pos` en `src/Pos.Api/appsettings.json`).

## Cómo levantar

**Backend** (aplica migración y crea el seed: roles, módulos, permisos, usuario `admin`):

```bash
dotnet run --project src/Pos.Api
```

- Swagger: `http://localhost:5038/swagger`
- Health: `http://localhost:5038/health`
- Usuario inicial: `admin` / `Admin123!` (configurable en `Seed:AdminPassword`)
- Para arrancar sin tocar la BD: variable de entorno `Seed__Enabled=false`

**Frontend**:

```bash
cd frontend
npm run dev
```

- App: `http://localhost:5173` (login → dashboard de módulos según permisos del rol)
- API destino configurable en `frontend/.env` (`VITE_API_URL`)

## Migraciones EF Core

```bash
# crear una nueva migración
dotnet ef migrations add <Nombre> --project src/Pos.Infrastructure --startup-project src/Pos.Api
# aplicar a la BD
dotnet ef database update --project src/Pos.Infrastructure --startup-project src/Pos.Api
```

## Notas de seguridad ya incorporadas

- Contraseñas con **BCrypt** (workFactor 12).
- **JWT** firmado; clave en `appsettings` (⚠️ cambiar en producción / mover a secreto).
- **Auditoría** de negocio independiente (behavior de MediatR sobre `IAuditableRequest`).
- **TransactionBehavior** para envolver comandos marcados `ITransactionalRequest`.
- Concurrencia optimista (`rowversion`) y precisión `decimal(18,4)` en todo el modelo.

## Siguiente paso (Fase 1)

CRUD de datos maestros y ABM (estructura comercial, catálogo, clientes, listas de precios, padrones).
