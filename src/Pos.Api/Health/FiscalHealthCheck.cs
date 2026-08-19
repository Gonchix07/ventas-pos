using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.Application.Abstractions.Fiscal;

namespace Pos.Api.Health;

/// <summary>Health check del adaptador fiscal (mock en fase 1).</summary>
public class FiscalHealthCheck : IHealthCheck
{
    private readonly IFiscalService _fiscal;
    public FiscalHealthCheck(IFiscalService fiscal) => _fiscal = fiscal;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var estado = await _fiscal.PingAsync(ct);
        return estado.Disponible
            ? HealthCheckResult.Healthy(estado.Detalle)
            : HealthCheckResult.Unhealthy(estado.Detalle);
    }
}
