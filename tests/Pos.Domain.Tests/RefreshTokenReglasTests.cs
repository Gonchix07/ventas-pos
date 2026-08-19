using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class RefreshTokenReglasTests
{
    [Fact]
    public void EstaVencido_FechaFutura_Falso()
    {
        var ahora = DateTime.UtcNow;
        Assert.False(RefreshTokenReglas.EstaVencido(ahora.AddDays(1), ahora));
    }

    [Fact]
    public void EstaVencido_FechaPasada_Verdadero()
    {
        var ahora = DateTime.UtcNow;
        Assert.True(RefreshTokenReglas.EstaVencido(ahora.AddSeconds(-1), ahora));
    }

    [Fact]
    public void YaFueUsado_SinRevocar_Falso()
    {
        Assert.False(RefreshTokenReglas.YaFueUsado(null));
    }

    [Fact]
    public void YaFueUsado_ConFechaDeRevocacion_Verdadero()
    {
        Assert.True(RefreshTokenReglas.YaFueUsado(DateTime.UtcNow));
    }

    [Fact]
    public void EsValido_NiVencidoNiUsado_Verdadero()
    {
        var ahora = DateTime.UtcNow;
        Assert.True(RefreshTokenReglas.EsValido(null, ahora.AddDays(1), ahora));
    }

    [Fact]
    public void EsValido_Vencido_Falso()
    {
        var ahora = DateTime.UtcNow;
        Assert.False(RefreshTokenReglas.EsValido(null, ahora.AddSeconds(-1), ahora));
    }

    [Fact]
    public void EsValido_YaUsado_Falso()
    {
        var ahora = DateTime.UtcNow;
        Assert.False(RefreshTokenReglas.EsValido(ahora.AddMinutes(-1), ahora.AddDays(1), ahora));
    }
}
