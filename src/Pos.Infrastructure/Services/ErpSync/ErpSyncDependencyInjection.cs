using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Abstractions.ErpSync;
using Pos.Infrastructure.Adapters.Erp;

namespace Pos.Infrastructure.Services.ErpSync;

/// <summary>
/// Registro de las piezas del sync ERP. Separado de <see cref="Pos.Infrastructure.DependencyInjection.AddInfrastructure"/>
/// a propósito: solo lo usa el proceso de consola Pos.ErpSync. Si viviera en AddInfrastructure, Pos.Api
/// exigiría ConnectionStrings:Erp configurada para poder arrancar, sin necesitarla nunca.
/// </summary>
public static class ErpSyncDependencyInjection
{
    public static IServiceCollection AddErpSync(this IServiceCollection services, IConfiguration config)
    {
        var erpConnStr = config.GetConnectionString("Erp");
        if (string.IsNullOrWhiteSpace(erpConnStr))
            throw new InvalidOperationException(
                "ConnectionStrings:Erp no está configurada. Configurala vía variable de entorno " +
                "ConnectionStrings__Erp o en appsettings (nunca commiteada con la clave real).");

        services.AddSingleton(new ErpOptions { ConnectionString = erpConnStr });
        services.AddSingleton(new ErpSyncOptions
        {
            FrecuenciaMinutos = config.GetValue("ErpSync:FrecuenciaMinutos", 15),
            LoteSize = config.GetValue("ErpSync:LoteSize", 500),
            MaxLotesPorFuente = config.GetValue("ErpSync:MaxLotesPorFuente", 0)
        });

        services.AddSingleton<IErpLookupReader, SqlErpLookupReader>();
        services.AddSingleton<IErpArticuloReader, SqlErpArticuloReader>();
        services.AddSingleton<IErpClienteReader, SqlErpClienteReader>();
        services.AddScoped<ErpSyncRunner>();

        return services;
    }
}
