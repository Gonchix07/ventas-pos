using Pos.Domain.Enums;
using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class CierreCajaReglasTests
{
    [Fact]
    public void Acumulador_SumaPorMedioDePago()
    {
        var movs = new[]
        {
            new MovimientoPagoPlano(1, 1000m, 0m),
            new MovimientoPagoPlano(1, 500m, 0.20m),
            new MovimientoPagoPlano(2, 2000m, 0m),
        };
        var r = AcumuladorPagos.Acumular(movs);
        Assert.Equal(2, r.Count);
        Assert.Equal(1500m, r.First(a => a.IdMedioPago == 1).Total);
        Assert.Equal(0.20m, r.First(a => a.IdMedioPago == 1).Redondeo);
        Assert.Equal(2000m, r.First(a => a.IdMedioPago == 2).Total);
    }

    [Fact]
    public void Acumulador_SinMovimientos_ListaVacia()
    {
        Assert.Empty(AcumuladorPagos.Acumular(Array.Empty<MovimientoPagoPlano>()));
    }

    [Fact]
    public void Diferencia_ExactoNoRequiereMotivo()
    {
        var r = DiferenciaCierreReglas.Evaluar(1000m, 1000m);
        Assert.Equal(0m, r.Diferencia);
        Assert.False(r.RequiereMotivo);
    }

    [Fact]
    public void Diferencia_DentroDeTolerancia_NoRequiereMotivo()
    {
        var r = DiferenciaCierreReglas.Evaluar(1000.005m, 1000m);
        Assert.False(r.RequiereMotivo);
    }

    [Fact]
    public void Diferencia_Faltante_RequiereMotivo()
    {
        var r = DiferenciaCierreReglas.Evaluar(950m, 1000m);
        Assert.Equal(-50m, r.Diferencia);
        Assert.True(r.RequiereMotivo);
    }

    [Fact]
    public void Diferencia_Sobrante_RequiereMotivo()
    {
        var r = DiferenciaCierreReglas.Evaluar(1050m, 1000m);
        Assert.Equal(50m, r.Diferencia);
        Assert.True(r.RequiereMotivo);
    }

    [Fact]
    public void CierreLote_AbiertoPuedeCerrarse()
    {
        Assert.True(CierreLoteReglas.PuedeCerrarse(EstadoLote.Abierto));
    }

    [Fact]
    public void CierreLote_CerradoNoPuedeVolverACerrarse()
    {
        Assert.False(CierreLoteReglas.PuedeCerrarse(EstadoLote.Cerrado));
    }

    // ---- Lote pendiente de un día anterior (cierre administrativo desde Tesorería) ----

    private static readonly DateTime Ahora = new(2026, 8, 11, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void LotePendiente_AbiertoDeUnDiaAnterior_EsPendiente()
    {
        Assert.True(CierreLoteReglas.EsLotePendienteDeDiaAnterior(
            EstadoLote.Abierto, new DateTime(2026, 8, 10, 23, 59, 0, DateTimeKind.Utc), Ahora));
    }

    [Fact]
    public void LotePendiente_AbiertoDeHoy_NoEsPendiente()
    {
        // El lote del día en curso lo cierra su cajero con el Z normal desde Caja.
        Assert.False(CierreLoteReglas.EsLotePendienteDeDiaAnterior(
            EstadoLote.Abierto, new DateTime(2026, 8, 11, 0, 5, 0, DateTimeKind.Utc), Ahora));
    }

    [Fact]
    public void LotePendiente_CerradoDeUnDiaAnterior_NoEsPendiente()
    {
        // Ya tuvo su cierre Z: el cierre es irreversible, no vuelve a la lista de pendientes.
        Assert.False(CierreLoteReglas.EsLotePendienteDeDiaAnterior(
            EstadoLote.Cerrado, new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc), Ahora));
    }

    [Fact]
    public void LotePendiente_AbiertoAlFiloDeLaMedianoche_EsPendiente()
    {
        // 00:00 de hoy ya es "hoy"; 23:59:59 de ayer todavía es pendiente. Verifica que la
        // comparación sea por día y no por una diferencia de horas.
        Assert.False(CierreLoteReglas.EsLotePendienteDeDiaAnterior(
            EstadoLote.Abierto, new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc), Ahora));
        Assert.True(CierreLoteReglas.EsLotePendienteDeDiaAnterior(
            EstadoLote.Abierto, new DateTime(2026, 8, 10, 23, 59, 59, DateTimeKind.Utc), Ahora));
    }
}
