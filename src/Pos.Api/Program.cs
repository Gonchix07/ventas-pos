using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Pos.Api.Common;
using Pos.Api.Health;
using Pos.Application;
using Pos.Application.Abstractions;
using Pos.Infrastructure;
using Pos.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ----- Límite de tamaño de request (mitigación de DoS simple) -----
// Esta API es casi toda JSON; la única carga de archivo es el certificado CAE (.pfx/.p12, unos
// pocos KB) en /admin/empresas/{id}/certificado. 2 MB sigue siendo generoso para ambos casos y
// muy por debajo del default de Kestrel (~30 MB), que dejaba pasar bodies enormes sin necesidad real.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

// ----- Serilog -----
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ----- Cadena de conexión: fail-fast y trazabilidad -----
// El valor real nunca está en un appsettings commiteado: sale de user-secrets en Development o de la
// variable de entorno ConnectionStrings__Pos. Los user-secrets se leen del perfil del usuario que
// ejecuta el proceso y sólo en Development, así que un proceso hosteado (IIS, servicio de Windows,
// otra PC) no los ve. Sin este chequeo eso terminaba en una conexión silenciosa a la BD del default
// y el primer error recién aparecía request adentro.
var connStr = builder.Configuration.GetConnectionString("Pos");
var connOrigen = ((IConfigurationRoot)builder.Configuration).Providers
    .LastOrDefault(p => p.TryGet("ConnectionStrings:Pos", out var v) && !string.IsNullOrWhiteSpace(v))
    ?.ToString() ?? "ningún proveedor de configuración";
if (string.IsNullOrWhiteSpace(connStr))
{
    throw new InvalidOperationException(
        $"ConnectionStrings:Pos no está configurada. Ambiente '{builder.Environment.EnvironmentName}', " +
        $"usuario del proceso '{Environment.UserName}'. En Development configurala con " +
        "'dotnet user-secrets set \"ConnectionStrings:Pos\" \"...\"' desde src/Pos.Api; en cualquier otro " +
        "ambiente (o si el proceso corre bajo otra cuenta de Windows, como un pool de IIS o un servicio) " +
        "usá la variable de entorno ConnectionStrings__Pos. Ver README.");
}

// ----- Capas -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

// ----- Data Protection: cifra la contraseña del certificado CAE en reposo -----
// Persistida a disco (no al perfil de usuario/registro, que varía según qué cuenta corre el
// proceso — un pool de IIS, un servicio de Windows) para que sobreviva reinicios y sea la misma
// clave la use el proceso que sea. Vive junto a los certificados, fuera de wwwroot.
var storageRoot = builder.Configuration["Storage:CertificadosPath"] ?? "App_Data/certificados";
if (!Path.IsPathRooted(storageRoot))
    storageRoot = Path.Combine(builder.Environment.ContentRootPath, storageRoot);
var keysPath = Path.Combine(Path.GetDirectoryName(storageRoot) ?? storageRoot, "keys");
Directory.CreateDirectory(keysPath);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("PosMayorista");
// DPAPI a nivel máquina: sin esto, las claves quedan en texto plano en disco (solo con permisos de
// archivo como protección) y, además, sirven igual sin importar qué cuenta de Windows corra el
// proceso — necesario porque no siempre es la misma (dev vs. IIS vs. servicio). "A nivel máquina"
// porque esto vive en un servidor por cliente: no hace falta que sea la misma cuenta de usuario,
// alcanza con que sea la misma PC.
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// El filtro corre dentro del scope de cada request y se resuelve por DI (constructor con
// IAuditLogger) vía TypeFilterAttribute — no hace falta registrarlo aparte en el contenedor.
builder.Services.AddControllers(o => o.Filters.Add<Pos.Api.Common.AuditoriaActionFilter>());

// ----- JWT -----
const string ClavePlaceholder = "CAMBIAR-ESTA-CLAVE-EN-PRODUCCION-MIN-32-CARACTERES-1234";
var jwtKey = builder.Configuration["Jwt:Key"] ?? ClavePlaceholder;
if (!builder.Environment.IsDevelopment() && (jwtKey == ClavePlaceholder || jwtKey.Length < 32))
{
    // Fail-fast: nunca arrancar fuera de Development con la clave placeholder o una clave débil.
    // La clave real se configura vía variable de entorno Jwt__Key o user-secrets.
    throw new InvalidOperationException(
        "Jwt:Key no está configurada (o es el valor placeholder) fuera de Development. " +
        "Configurá una clave real de al menos 32 caracteres vía variable de entorno Jwt__Key.");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "Pos",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "PosClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// ----- Swagger con Bearer -----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "POS Mayorista API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

// ----- CORS -----
// Orígenes permitidos configurables por ambiente (Cors:AllowedOrigins en appsettings o env var Cors__AllowedOrigins__0, __1, ...).
// En Development, si no se configuró nada, se asume el Vite dev server local.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment() ? new[] { "http://localhost:5173" } : Array.Empty<string>());
if (!builder.Environment.IsDevelopment() && corsOrigins.Length == 0)
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins no está configurado fuera de Development. Configurá los orígenes reales del frontend.");
}
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader().AllowAnyMethod()));

// ----- Rate limiting (mitigación de fuerza bruta en /auth/login) -----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// ----- Health checks -----
builder.Services.AddHealthChecks()
    .AddCheck<DbHealthCheck>("database")
    .AddCheck<FiscalHealthCheck>("fiscal");

var app = builder.Build();

// Queda asentado en el log a qué SQL Server se conectó y de qué proveedor de configuración salió el
// dato (sin credenciales): el síntoma "arrancó contra otra BD" se diagnostica leyendo esta línea.
var connInfo = new SqlConnectionStringBuilder(connStr);
Log.Information("BD destino: servidor {Servidor}, base {Base} — configuración tomada de {Origen}",
    connInfo.DataSource, connInfo.InitialCatalog, connOrigen);

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// ----- Migración + seed inicial (guardado si la BD no está disponible) -----
if (builder.Configuration.GetValue("Seed:Enabled", true))
{
    using var scope = app.Services.CreateScope();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        const string PasswordPlaceholder = "Admin123!";
        var adminPwd = builder.Configuration["Seed:AdminPassword"] ?? PasswordPlaceholder;
        if (!app.Environment.IsDevelopment() && adminPwd == PasswordPlaceholder)
        {
            Log.Warning(
                "ATENCIÓN: se está sembrando el usuario admin con la clave por defecto ({Placeholder}) fuera de Development. " +
                "Configurá Seed:AdminPassword vía variable de entorno y cambiá la clave del usuario admin apenas puedas.",
                PasswordPlaceholder);
        }
        await DbSeeder.SeedAsync(db, hasher, adminPwd);
        Log.Information("Migración y seed completados.");
    }
    catch (Exception ex)
    {
        // La API arranca igual (una caja no debe quedar sin UI porque la BD tardó en levantar), pero
        // esto es un Error, no un Warning: si falla acá, todos los requests van a fallar después.
        Log.Error(ex,
            "No se pudo migrar/seed la BD al inicio contra servidor {Servidor}, base {Base}. " +
            "La API arranca igual, pero las operaciones contra la BD van a fallar.",
            connInfo.DataSource, connInfo.InitialCatalog);
    }
}

app.Run();
