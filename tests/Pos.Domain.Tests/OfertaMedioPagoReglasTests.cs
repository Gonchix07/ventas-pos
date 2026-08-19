using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class OfertaMedioPagoReglasTests
{
    [Fact]
    public void CalcularDescuento_SinTope_AplicaElPorcentaje()
    {
        // 1000 * 10% = 100, muy por debajo del tope de 500.
        var d = OfertaMedioPagoReglas.CalcularDescuento(1000m, 10m, 500m);
        Assert.Equal(100m, d);
    }

    [Fact]
    public void CalcularDescuento_SuperaElTope_QuedaTopeado()
    {
        // 10000 * 10% = 1000, pero el tope es 150.
        var d = OfertaMedioPagoReglas.CalcularDescuento(10000m, 10m, 150m);
        Assert.Equal(150m, d);
    }

    [Fact]
    public void CalcularDescuento_MontoOPorcentajeCero_NoDescuenta()
    {
        Assert.Equal(0m, OfertaMedioPagoReglas.CalcularDescuento(0m, 10m, 500m));
        Assert.Equal(0m, OfertaMedioPagoReglas.CalcularDescuento(1000m, 0m, 500m));
    }

    [Fact]
    public void CalcularDescuento_TopeCero_NoLimitaElPorcentaje()
    {
        // Tope 0 = "sin tope cargado", no "tope en $0" (una oferta real siempre exige tope > 0 en
        // el ABM, pero la regla no debe devolver 0 por un dato mal cargado).
        var d = OfertaMedioPagoReglas.CalcularDescuento(1000m, 10m, 0m);
        Assert.Equal(100m, d);
    }

    [Fact]
    public void Resolver_PrefiereElPlanEspecificoPorSobreElGeneral()
    {
        var ofertas = new[]
        {
            new OfertaMedioPagoDef(IdMedioPago: 5, IdPlanCuota: null, Porcentaje: 5m, TopeMaximo: 100m),
            new OfertaMedioPagoDef(IdMedioPago: 5, IdPlanCuota: 3, Porcentaje: 15m, TopeMaximo: 300m),
        };
        var r = OfertaMedioPagoReglas.Resolver(ofertas, idMedioPago: 5, idPlan: 3);
        Assert.Equal(15m, r?.Porcentaje);
    }

    [Fact]
    public void Resolver_SinPlanEspecifico_CaeALaGeneralDelMedio()
    {
        var ofertas = new[]
        {
            new OfertaMedioPagoDef(IdMedioPago: 5, IdPlanCuota: null, Porcentaje: 5m, TopeMaximo: 100m),
            new OfertaMedioPagoDef(IdMedioPago: 5, IdPlanCuota: 3, Porcentaje: 15m, TopeMaximo: 300m),
        };
        var r = OfertaMedioPagoReglas.Resolver(ofertas, idMedioPago: 5, idPlan: 6);
        Assert.Equal(5m, r?.Porcentaje);
    }

    [Fact]
    public void Resolver_MedioSinCuotasYSoloHayOfertaDeUnPlanPuntual_NoAplica()
    {
        // Medio sin cuotas (idPlan null, ej. Efectivo/Débito) no cae en una oferta pensada para un
        // plan de tarjeta puntual — solo matchea una general (IdPlanCuota null) de ese medio.
        var ofertas = new[] { new OfertaMedioPagoDef(IdMedioPago: 5, IdPlanCuota: 3, Porcentaje: 15m, TopeMaximo: 300m) };
        var r = OfertaMedioPagoReglas.Resolver(ofertas, idMedioPago: 5, idPlan: null);
        Assert.Null(r);
    }

    [Fact]
    public void Resolver_SinOfertasParaElMedio_DevuelveNull()
    {
        var ofertas = new[] { new OfertaMedioPagoDef(IdMedioPago: 5, IdPlanCuota: null, Porcentaje: 5m, TopeMaximo: 100m) };
        var r = OfertaMedioPagoReglas.Resolver(ofertas, idMedioPago: 9, idPlan: null);
        Assert.Null(r);
    }
}
