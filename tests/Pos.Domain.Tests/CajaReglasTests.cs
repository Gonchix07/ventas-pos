using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class CajaReglasTests
{
    [Fact]
    public void Redondeo_HaciaArriba_CalculaAjustePositivo()
    {
        // 101.6 redondea a 102 → ajuste +0.4
        var r = RedondeoService.CalcularAjuste(101.6m, 1m);
        Assert.Equal(0.4m, r);
    }

    [Fact]
    public void Redondeo_HaciaAbajo_CalculaAjusteNegativo()
    {
        // 101.4 redondea a 101 → ajuste -0.4
        var r = RedondeoService.CalcularAjuste(101.4m, 1m);
        Assert.Equal(-0.4m, r);
    }

    [Fact]
    public void Redondeo_MontoExacto_SinAjuste()
    {
        Assert.Equal(0m, RedondeoService.CalcularAjuste(100.0m, 1m));
    }

    [Fact]
    public void Redondeo_RangoCero_NoAjusta()
    {
        Assert.Equal(0m, RedondeoService.CalcularAjuste(101.37m, 0m));
    }

    [Fact]
    public void Totales_SumaBrutoDescuentoYCalculaNeto()
    {
        var t = OperacionTotales.Calcular(new[] { (100m, 10m), (50m, 0m) });
        Assert.Equal(150m, t.Bruto);
        Assert.Equal(10m, t.Descuento);
        Assert.Equal(140m, t.Neto);
    }
}
