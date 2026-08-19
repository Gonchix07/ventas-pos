using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class PercepcionesReglasTests
{
    [Fact]
    public void PercepcionIva21_SuperaElMinimo_SeCalculaAl3Porciento()
    {
        // 3500 (mínimo real cargado hoy) * 3% = 105, así que con un neto de 4000 el 3% da 120 > 3500? No:
        // el mínimo se compara contra el IMPORTE de la percepción, no contra el neto. Con neto=200000,
        // 3% = 6000, que sí supera el mínimo de 3500.
        var percepcion = PercepcionesReglas.CalcularPercepcionIva21(200_000m, 3500m);
        Assert.Equal(6000m, percepcion);
    }

    [Fact]
    public void PercepcionIva21_NoSuperaElMinimo_DevuelveCero()
    {
        // neto=50000 → 3% = 1500, no supera el mínimo de 3500.
        var percepcion = PercepcionesReglas.CalcularPercepcionIva21(50_000m, 3500m);
        Assert.Equal(0m, percepcion);
    }

    [Fact]
    public void PercepcionIva21_ExactoElMinimo_NoSeAplica()
    {
        // 3% de 116666.67 ≈ 3500.00 exacto → "supera" es estricto, no "supera o iguala".
        var percepcion = PercepcionesReglas.CalcularPercepcionIva21(116_666.67m, 3500.00m);
        Assert.Equal(0m, percepcion);
    }

    [Fact]
    public void PercepcionIva105_SuperaElMinimo_SeCalculaAl1Porciento()
    {
        // neto=200000 * 1% = 2000, supera el mínimo real de 1250.
        var percepcion = PercepcionesReglas.CalcularPercepcionIva105(200_000m, 1250m);
        Assert.Equal(2000m, percepcion);
    }

    [Fact]
    public void PercepcionIva105_NoSuperaElMinimo_DevuelveCero()
    {
        var percepcion = PercepcionesReglas.CalcularPercepcionIva105(50_000m, 1250m);
        Assert.Equal(0m, percepcion);
    }

    [Fact]
    public void PercepcionIibb_ConAlicuotaDelPadron_SeCalculaSobreElNetoTotal()
    {
        // Padrón trae 2,50 (= 2,5%, NO 0,025) sobre un neto total de 50000 → 1250, supera el mínimo de 100.
        var percepcion = PercepcionesReglas.CalcularPercepcionIibb(50_000m, 2.50m, 100m);
        Assert.Equal(1250m, percepcion);
    }

    [Fact]
    public void PercepcionIibb_SinAlicuotaDePadron_DevuelveCero()
    {
        var percepcion = PercepcionesReglas.CalcularPercepcionIibb(50_000m, 0m, 100m);
        Assert.Equal(0m, percepcion);
    }

    [Fact]
    public void PercepcionIibb_NoSuperaElMinimo_DevuelveCero()
    {
        // 2% de 1000 = 20, no supera el mínimo de 100.
        var percepcion = PercepcionesReglas.CalcularPercepcionIibb(1_000m, 2m, 100m);
        Assert.Equal(0m, percepcion);
    }

    [Fact]
    public void PercepcionIibb_AlicuotaNegativa_DevuelveCero()
    {
        var percepcion = PercepcionesReglas.CalcularPercepcionIibb(50_000m, -1m, 100m);
        Assert.Equal(0m, percepcion);
    }
}
