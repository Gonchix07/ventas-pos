using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.Infrastructure.Persistence;

namespace Pos.Api.Health;

public class DbHealthCheck : IHealthCheck
{
    private readonly PosDbContext _db;
    public DbHealthCheck(PosDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var ok = await _db.Database.CanConnectAsync(ct);
            return ok ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("No se puede conectar a la BD");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
