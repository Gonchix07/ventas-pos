using Pos.Application.Abstractions.Fiscal;

namespace Pos.Infrastructure.Adapters;

/// <summary>Servicio fiscal simulado: genera CAE/CAEA ficticios. Reemplazar por ARCA WSFEv1.</summary>
public class MockFiscalService : IFiscalService
{
    public Task<ResultadoCae> SolicitarCaeAsync(ComprobanteFiscal cmp, CancellationToken ct)
    {
        var cae = GenerarNumerico(14, cmp.PuntoVenta * 100000 + cmp.Numero);
        return Task.FromResult(new ResultadoCae(true, cae, DateTime.UtcNow.AddDays(10), false, null));
    }

    public Task<ResultadoCaea> ObtenerCaeaAsync(int idEmpresa, PeriodoFiscal periodo, CancellationToken ct)
    {
        var caea = GenerarNumerico(14, idEmpresa * 1000 + periodo.Anio * 100 + periodo.Mes);
        var desde = new DateTime(periodo.Anio, periodo.Mes, 1);
        return Task.FromResult(new ResultadoCaea(true, caea, desde, desde.AddMonths(1).AddDays(-1), null));
    }

    public Task<ResultadoCaea> InformarComprobantesCaeaAsync(int idEmpresa, IEnumerable<ComprobanteFiscal> lote, CancellationToken ct)
        => Task.FromResult(new ResultadoCaea(true, null, null, null, null));

    public Task<EstadoServicioFiscal> PingAsync(CancellationToken ct)
        => Task.FromResult(new EstadoServicioFiscal(true, "Mock fiscal disponible"));

    private static string GenerarNumerico(int len, long seed)
    {
        var s = Math.Abs(seed).ToString().PadLeft(len, '7');
        return s.Length > len ? s[..len] : s;
    }
}

/// <summary>Impresora fiscal simulada (Hasar/iCARD). Reemplazar por wrapper local.</summary>
public class MockFiscalPrinter : IFiscalPrinter
{
    public Task<ResultadoImpresion> ImprimirFiscalAsync(ComprobanteFiscal cmp, CancellationToken ct)
        => Task.FromResult(new ResultadoImpresion(true, $"MOCK-FISCAL-{cmp.Numero}", null));

    public Task<ResultadoImpresion> ImprimirNotaCreditoAsync(ComprobanteFiscal cmp, CancellationToken ct)
        => Task.FromResult(new ResultadoImpresion(true, $"MOCK-NC-{cmp.Numero}", null));

    public Task<ResultadoImpresion> CierreZAsync(int idSucursal, int idCaja, CancellationToken ct)
        => Task.FromResult(new ResultadoImpresion(true, $"MOCK-Z-{idSucursal}-{idCaja}", null));

    public Task<ResultadoImpresion> ArqueoXAsync(int idSucursal, int idCaja, CancellationToken ct)
        => Task.FromResult(new ResultadoImpresion(true, $"MOCK-X-{idSucursal}-{idCaja}", null));
}
