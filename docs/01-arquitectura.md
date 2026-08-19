# 01 — Arquitectura general

## 1. Objetivo

Sistema POS para comercio **mayorista** con clientes identificados, facturación fiscal
(ARCA CAE/CAEA), impresoras fiscales Hasar, motor de ofertas y convenios, listas de precios
con prioridad, múltiples medios de pago y gestión completa de caja y tesorería.

El sistema maneja **dinero y facturación fiscal**, por lo que la robustez transaccional, la
trazabilidad (auditoría) y la disponibilidad (modo contingencia CAEA) son requisitos de primer
orden — ver [`04-seguridad-transacciones.md`](04-seguridad-transacciones.md).

## 2. Estilo arquitectónico

**Clean Architecture / Onion** con separación en capas y **Ports & Adapters (hexagonal)** para
todo lo que sea integración externa (fiscal, pagos, banco de imágenes, ERP futuro, mail).

```
┌──────────────────────────────────────────────────────────────┐
│  FRONTEND  (React + Vite + TS)                                 │
│  Módulos: Caja · Facturación · Tesorería · Etiquetas · ABM     │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTPS / REST (JWT)
┌───────────────────────────▼──────────────────────────────────┐
│  API  (ASP.NET Core Web API)                                   │
│  Controllers → MediatR (Commands/Queries) → Validación         │
├────────────────────────────────────────────────────────────────
│  APPLICATION  (casos de uso, DTOs, interfaces/puertos)         │
├────────────────────────────────────────────────────────────────
│  DOMAIN  (entidades, reglas: ofertas, precios, redondeo, IVA)  │
├────────────────────────────────────────────────────────────────
│  INFRASTRUCTURE                                                │
│   • EF Core + MS SQL (repos, UnitOfWork, transacciones)        │
│   • Adaptadores: Fiscal(ARCA/Hasar) · Pagos · Imágenes · Mail  │
└───────────────────────────┬──────────────────────────────────┘
        ┌───────────────────┼─────────────────────┐
        ▼                   ▼                     ▼
   ┌─────────┐      ┌───────────────┐     ┌──────────────┐
   │ MS SQL  │      │ Adaptadores   │     │ iCARD local  │
   │ Server  │      │ MOCK (fase 1) │     │ (Hasar) fut. │
   └─────────┘      └───────────────┘     └──────────────┘
```

### Por qué esta separación
- **Domain** no conoce EF ni ARCA: las reglas de negocio (cálculo de precio final, aplicación de
  ofertas y convenios, redondeo, percepciones de IVA) son testeables sin base de datos ni red.
- **Ports & Adapters**: `IFiscalService`, `IPaymentProvider`, `IImageBank`, `IErpGateway`,
  `IMailSender` tienen implementación **Mock** en esta fase y **Real** en una fase futura,
  sin tocar el resto del sistema.

## 3. Estructura de la solución .NET

```
Pos.sln
├── src/
│   ├── Pos.Domain/                 # Entidades, value objects, reglas puras
│   │   ├── Entities/               # Articulo, Cliente, Comprobante, MovimientoCaja...
│   │   ├── Enums/                  # TipoComprobante, EstadoCaja, FuentePago...
│   │   ├── ValueObjects/           # Cuit, CodigoBarra(EAN13/DUN), Money
│   │   └── Services/               # MotorOfertas, CalculadoraPrecios, Redondeo, IVA
│   │
│   ├── Pos.Application/            # Casos de uso (CQRS con MediatR)
│   │   ├── Common/                 # Behaviors: Validation, Transaction, Auditoría, Logging
│   │   ├── Abstractions/           # PUERTOS: IFiscalService, IPaymentProvider, IErpGateway...
│   │   ├── Caja/                   # AbrirCaja, ProcesarOperacion, ArqueoX, CerrarZ...
│   │   ├── Facturacion/            # EmitirComprobante, NotaCredito, Reimpresion...
│   │   ├── Catalogo/               # Articulos, Precios, Ofertas, Convenios (queries)
│   │   ├── Clientes/
│   │   ├── Tesoreria/
│   │   └── Etiquetas/
│   │
│   ├── Pos.Infrastructure/         # EF Core + adaptadores
│   │   ├── Persistence/            # PosDbContext, configs Fluent, migraciones, repos, UoW
│   │   ├── Fiscal/                 # ArcaCaeAdapter (mock/real), HasarPrinterAdapter
│   │   ├── Payments/               # ModoAdapter, MercadoPagoAdapter, CuentaDniAdapter (mock)
│   │   ├── Imaging/                # BancoImagenesAdapter (portal Hergo)
│   │   ├── Erp/                    # ErpGateway (stub — fase futura)
│   │   └── Mail/                   # SmtpMailSender / MockMailSender
│   │
│   └── Pos.Api/                    # ASP.NET Core: Controllers, DI, JWT, Swagger, middlewares
│
├── tests/
│   ├── Pos.Domain.Tests/           # Reglas de negocio (ofertas, precios, redondeo)
│   ├── Pos.Application.Tests/      # Casos de uso con adaptadores fake
│   └── Pos.Api.IntegrationTests/   # WebApplicationFactory + SQL en contenedor/localdb
│
└── frontend/                       # React + Vite + TS (workspace aparte)
    ├── src/modules/{caja,facturacion,tesoreria,etiquetas,admin}
    ├── src/shared/{api,auth,ui,hooks}
    └── ...
```

## 4. Módulos funcionales (mapeo SRS → módulo)

| Módulo | Perfiles | Contenido SRS |
|--------|----------|---------------|
| **Login / Auth** | Todos | Login, roles, permisos por módulo |
| **Caja** | Cajero, Supervisor | Apertura, identificación cliente, lectura de artículos, cola de procesamiento, ofertas, operación, arqueo X, rendición, deslogueo, consulta de precio, NC |
| **Facturación** | Cajero, Supervisor | CAE, CAEA (backup), fiscal (Hasar LAN), comandera no fiscal, presupuestos, anulaciones, reimpresiones, refacturaciones |
| **Tesorería / Reportes** | Administrador, Tesorero | Dashboard por sucursal y total, vista de cajas, acumulados por medio de pago, validación de cierres, envío de reporte por mail, fiscalización CAEA |
| **Etiquetas** | Administrador, Tesorero, Repositor | Búsqueda/escaneo, armado de lista, selección por sector/línea/familia, tipos fleje/A4/A5 |
| **Administración (ABM)** | Administrador | CRUD artículos, clientes, precios, ofertas, clusters, convenios, medios de pago, empresas, sucursales, puntos de venta, números, usuarios, roles, padrones, configuraciones |

## 5. Frontend — organización

- **React + Vite + TypeScript**, enrutado por módulo, con *guard* de rutas según permisos.
- **Estado de servidor** con TanStack Query (cache, reintentos, invalidación) — clave para el
  catálogo (artículos/precios/ofertas) y para el estado de caja.
- **Cola de procesamiento de artículos** (requisito SRS "hilo"): en el cliente se modela como una
  cola con *worker* que consume lecturas de barra, consulta precio+ofertas y agrega a la operación;
  ante artículo no encontrado, **detiene** la cola hasta resolución.
- **Escaneo**: entrada de teclado tipo *wedge* (lector de barras) + soporte de cámara opcional.
  Un mismo campo de identificación busca cliente por tarjeta / DNI / código / nombre / domicilio.
- **Impresión**: comandera no fiscal vía servicio local; comprobantes fiscales vía Hasar (adaptador).
- **UX de dinero**: montos siempre con `decimal` en backend; el front nunca hace aritmética fiscal —
  solo muestra lo que el backend calcula (evita descuadres por punto flotante).

## 6. Multi-sucursal / multi-empresa

El modelo es **multi-empresa y multi-sucursal** (Empresas → Sucursales; casi todas las tablas
transaccionales llevan `idSucursal` como parte de la clave). El backend resuelve el contexto de
sucursal/caja a partir del **puesto de caja** (PC actual) y del usuario logueado. Los certificados
fiscales (CAE) y la obtención de CAEA son **por empresa**.

## 7. Observabilidad y auditoría

- **Auditoría**: tabla `MovimientosAuditoria` **sin FK, totalmente independiente** (requisito SRS),
  escrita por un *behavior* de MediatR en cada comando relevante (quién, qué, cuándo, payload).
- **Logging estructurado** (Serilog) + correlación por request.
- **Health checks** de: base de datos, adaptador fiscal, adaptadores de pago, iCARD local.
