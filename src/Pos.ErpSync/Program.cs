using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Services.ErpSync;
using Serilog;

// Proceso de consola de vida corta: se dispara desde Windows Task Scheduler cada pocos minutos,
// decide si a cada fuente (Lookups/Articulos/Presentaciones/CodBarras/Clientes) ya le toca correr
// según ErpSync:FrecuenciaMinutos (ver ErpSyncRunner), hace su trabajo y termina. No hay loop propio:
// así cambiar la frecuencia es solo editar appsettings/env vars, sin tocar el disparador del SO.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/erpsync-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    // Mismo criterio fail-fast que Pos.Api (Program.cs): sin esto, una connection string vacía
    // termina en un error confuso recién adentro de la primera consulta, no acá donde se puede
    // decir exactamente qué falta configurar.
    var posConnStr = config.GetConnectionString("Pos");
    if (string.IsNullOrWhiteSpace(posConnStr))
        throw new InvalidOperationException(
            "ConnectionStrings:Pos no está configurada. Usá la variable de entorno ConnectionStrings__Pos.");

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSerilog());
    services.AddDbContext<PosDbContext>(opt => opt.UseSqlServer(posConnStr,
        sql => sql.MigrationsAssembly(typeof(PosDbContext).Assembly.FullName)));
    services.AddErpSync(config);

    await using var provider = services.BuildServiceProvider();
    using var scope = provider.CreateScope();

    // Falla fuerte y visible si hay migraciones pendientes: correr el sync contra un esquema
    // desactualizado (p. ej. sin las columnas CodigoErp/IdErp todavía) produciría errores confusos
    // fila por fila en vez de este único mensaje claro al arrancar.
    var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
    var pendientes = await db.Database.GetPendingMigrationsAsync();
    if (pendientes.Any())
        throw new InvalidOperationException(
            $"Hay {pendientes.Count()} migración(es) pendiente(s) en la base de Pos. " +
            "Corré Pos.Api (que migra al arrancar) o `dotnet ef database update` antes del sync.");

    var runner = scope.ServiceProvider.GetRequiredService<ErpSyncRunner>();
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30)); // watchdog: no debería tardar tanto
    await runner.EjecutarAsync(cts.Token);

    Log.Information("Pos.ErpSync terminó OK.");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Pos.ErpSync terminó con error.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
