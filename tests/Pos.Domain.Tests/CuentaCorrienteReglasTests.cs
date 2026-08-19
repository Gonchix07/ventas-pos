using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class CuentaCorrienteReglasTests
{
    [Fact]
    public void PuedeAprobar_DentroDelLimite_Verdadero()
    {
        Assert.True(CuentaCorrienteReglas.PuedeAprobar(saldoActual: 5000m, monto: 2000m, limiteCredito: 10000m));
    }

    [Fact]
    public void PuedeAprobar_JustoEnElLimite_Verdadero()
    {
        Assert.True(CuentaCorrienteReglas.PuedeAprobar(saldoActual: 8000m, monto: 2000m, limiteCredito: 10000m));
    }

    [Fact]
    public void PuedeAprobar_SuperaElLimite_Falso()
    {
        Assert.False(CuentaCorrienteReglas.PuedeAprobar(saldoActual: 9000m, monto: 2000m, limiteCredito: 10000m));
    }

    [Fact]
    public void PuedeAprobar_SinLimiteHabilitado_Falso()
    {
        Assert.False(CuentaCorrienteReglas.PuedeAprobar(saldoActual: 0m, monto: 1m, limiteCredito: 0m));
    }

    [Fact]
    public void CalcularSaldo_DebeMenosHaber()
    {
        Assert.Equal(300m, CuentaCorrienteReglas.CalcularSaldo(totalDebe: 1000m, totalHaber: 700m));
    }

    [Fact]
    public void CalcularSaldo_SinMovimientos_Cero()
    {
        Assert.Equal(0m, CuentaCorrienteReglas.CalcularSaldo(totalDebe: 0m, totalHaber: 0m));
    }
}
