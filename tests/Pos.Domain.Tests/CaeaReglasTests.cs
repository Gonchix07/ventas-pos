using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class CaeaReglasTests
{
    [Fact]
    public void PeriodoDe_primera_quincena()
    {
        var p = CaeaReglas.PeriodoDe(new DateTime(2026, 8, 1));
        Assert.Equal(new CaeaReglas.Periodo(2026, 8, 1), p);

        p = CaeaReglas.PeriodoDe(new DateTime(2026, 8, 15));
        Assert.Equal(1, p.Orden);
    }

    [Fact]
    public void PeriodoDe_segunda_quincena()
    {
        var p = CaeaReglas.PeriodoDe(new DateTime(2026, 8, 16));
        Assert.Equal(new CaeaReglas.Periodo(2026, 8, 2), p);

        p = CaeaReglas.PeriodoDe(new DateTime(2026, 8, 31));
        Assert.Equal(2, p.Orden);
    }

    [Fact]
    public void Vigente_incluye_ambos_extremos()
    {
        var desde = new DateTime(2026, 8, 16);
        var hasta = new DateTime(2026, 8, 31);
        Assert.True(CaeaReglas.Vigente(desde, desde, hasta));
        Assert.True(CaeaReglas.Vigente(hasta, desde, hasta));
        Assert.True(CaeaReglas.Vigente(new DateTime(2026, 8, 20), desde, hasta));
    }

    [Fact]
    public void Vigente_rechaza_fuera_de_rango()
    {
        var desde = new DateTime(2026, 8, 16);
        var hasta = new DateTime(2026, 8, 31);
        Assert.False(CaeaReglas.Vigente(new DateTime(2026, 8, 15), desde, hasta));
        Assert.False(CaeaReglas.Vigente(new DateTime(2026, 9, 1), desde, hasta));
    }
}
