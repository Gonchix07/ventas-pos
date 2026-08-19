using Pos.Domain.Services;

namespace Pos.Domain.Tests;

/// <summary>El precio de cada presentación se deriva de un único precio unitario × unidades por bulto.</summary>
public class PrecioPorBultoTests
{
    [Fact]
    public void UnidadSuelta_DejaElPrecioIgual()
    {
        Assert.Equal(1850.50m, PrecioPorBulto.Calcular(1850.50m, 1m));
    }

    [Fact]
    public void Pack_MultiplicaPorLasUnidadesDelBulto()
    {
        // Pack de 12 de un artículo de $1850.50 → 12 × 1850.50 = 22206.00
        Assert.Equal(22206.00m, PrecioPorBulto.Calcular(1850.50m, 12m));
    }

    [Fact]
    public void Redondea_A_DosDecimales_HaciaArriba()
    {
        // 33.333 × 3 = 99.999 → 100.00 (AwayFromZero, igual que el resto de los cálculos de plata)
        Assert.Equal(100.00m, PrecioPorBulto.Calcular(33.333m, 3m));
    }

    [Fact]
    public void MedioPunto_RedondeaAlejandoseDeCero()
    {
        // 1.125 × 1 = 1.125 → 1.13 (no 1.12 como haría el redondeo bancario por defecto de .NET)
        Assert.Equal(1.13m, PrecioPorBulto.Calcular(1.125m, 1m));
    }

    [Fact]
    public void PrecioCero_EsValido()
    {
        Assert.Equal(0m, PrecioPorBulto.Calcular(0m, 24m));
    }

    [Fact]
    public void UnidadXBultoFraccionaria_TambienMultiplica()
    {
        // Presentaciones por peso/volumen (ej. 0.5 = medio kilo).
        Assert.Equal(925.25m, PrecioPorBulto.Calcular(1850.50m, 0.5m));
    }

    [Fact]
    public void PrecioNegativo_Falla()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrecioPorBulto.Calcular(-1m, 1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void UnidadXBultoNoPositiva_Falla(int unidades)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrecioPorBulto.Calcular(100m, unidades));
    }
}
