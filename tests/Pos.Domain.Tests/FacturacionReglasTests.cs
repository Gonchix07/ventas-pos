using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class FacturacionReglasTests
{
    [Fact]
    public void DesglioIva_21Porciento_CalculaNetoEIva()
    {
        var (neto, iva) = DesglioIva.Calcular(1210m, 0.21m);
        Assert.Equal(1000m, neto);
        Assert.Equal(210m, iva);
    }

    [Fact]
    public void DesglioIva_NetoMasIva_IgualaElTotal()
    {
        var (neto, iva) = DesglioIva.Calcular(1748.72m, 0.21m);
        Assert.Equal(1748.72m, Math.Round(neto + iva, 2));
    }

    [Fact]
    public void DesglioIva_AlicuotaCero_TodoEsNeto()
    {
        var (neto, iva) = DesglioIva.Calcular(500m, 0m);
        Assert.Equal(500m, neto);
        Assert.Equal(0m, iva);
    }

    [Fact]
    public void DesglioIva_ImporteCero_DevuelveCero()
    {
        var (neto, iva) = DesglioIva.Calcular(0m, 0.21m);
        Assert.Equal(0m, neto);
        Assert.Equal(0m, iva);
    }

    [Fact]
    public void Contingencia_SeActivaAlAlcanzarElLimite()
    {
        Assert.False(ReintentosCaeReglas.DebePasarAContingencia(2, 3));
        Assert.True(ReintentosCaeReglas.DebePasarAContingencia(3, 3));
        Assert.True(ReintentosCaeReglas.DebePasarAContingencia(5, 3));
    }

    [Fact]
    public void Contingencia_LimiteMinimoEsUno()
    {
        Assert.True(ReintentosCaeReglas.DebePasarAContingencia(1, 0));
    }

    [Fact]
    public void NumeroComprobante_FormateaConCeros()
    {
        Assert.Equal("0003-00000123", NumeroComprobanteFormatter.Formatear(3, 123));
        Assert.Equal("0001-00000001", NumeroComprobanteFormatter.Formatear(1, 1));
    }

    [Fact]
    public void ValidacionPagos_ExactoCubre()
    {
        Assert.True(ValidacionPagos.CubreElTotal(1000m, 1000m));
    }

    [Fact]
    public void ValidacionPagos_DentroDeTolerancia()
    {
        Assert.True(ValidacionPagos.CubreElTotal(999.995m, 1000m));
    }

    [Fact]
    public void ValidacionPagos_FueraDeTolerancia_NoCubre()
    {
        Assert.False(ValidacionPagos.CubreElTotal(990m, 1000m));
    }

    [Fact]
    public void ValidacionPagos_Sobrepago_CubreIgual()
    {
        // El sobrante ya no rechaza el pago: es vuelto (se valida aparte que venga de Efectivo).
        Assert.True(ValidacionPagos.CubreElTotal(1010m, 1000m));
    }

    [Fact]
    public void ValidacionPagos_NoEfectivoIgualAlTotal_Ok()
    {
        Assert.True(ValidacionPagos.NoEfectivoNoSuperaElTotal(1000m, 1000m));
    }

    [Fact]
    public void ValidacionPagos_NoEfectivoSuperaElTotal_Rechaza()
    {
        // Una tarjeta cargada de más no es vuelto válido: no se puede devolver plata en tarjeta.
        Assert.False(ValidacionPagos.NoEfectivoNoSuperaElTotal(1010m, 1000m));
    }

    [Fact]
    public void CalcularVuelto_SobrantePositivo()
    {
        Assert.Equal(150m, ValidacionPagos.CalcularVuelto(1150m, 1000m));
    }

    [Fact]
    public void CalcularVuelto_SinSobrante_DevuelveCero()
    {
        Assert.Equal(0m, ValidacionPagos.CalcularVuelto(1000m, 1000m));
        Assert.Equal(0m, ValidacionPagos.CalcularVuelto(990m, 1000m));
    }
}
