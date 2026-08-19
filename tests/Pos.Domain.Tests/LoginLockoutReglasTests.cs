using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class LoginLockoutReglasTests
{
    [Fact]
    public void EstaBloqueado_SinFechaDeBloqueo_Falso()
    {
        Assert.False(LoginLockoutReglas.EstaBloqueado(null, DateTime.UtcNow));
    }

    [Fact]
    public void EstaBloqueado_FechaFutura_Verdadero()
    {
        var ahora = DateTime.UtcNow;
        Assert.True(LoginLockoutReglas.EstaBloqueado(ahora.AddMinutes(5), ahora));
    }

    [Fact]
    public void EstaBloqueado_FechaPasada_Falso()
    {
        var ahora = DateTime.UtcNow;
        Assert.False(LoginLockoutReglas.EstaBloqueado(ahora.AddMinutes(-1), ahora));
    }

    [Fact]
    public void DebeBloquear_AntesDelMaximo_Falso()
    {
        Assert.False(LoginLockoutReglas.DebeBloquear(LoginLockoutReglas.MaxIntentosFallidos - 1));
    }

    [Fact]
    public void DebeBloquear_AlLlegarAlMaximo_Verdadero()
    {
        Assert.True(LoginLockoutReglas.DebeBloquear(LoginLockoutReglas.MaxIntentosFallidos));
    }

    [Fact]
    public void CalcularBloqueoHasta_SumaLaDuracionConfigurada()
    {
        var ahora = DateTime.UtcNow;
        var hasta = LoginLockoutReglas.CalcularBloqueoHasta(ahora);
        Assert.Equal(ahora.Add(LoginLockoutReglas.DuracionBloqueo), hasta);
    }

    [Fact]
    public void SiguienteIntento_Incrementa()
    {
        Assert.Equal(1, LoginLockoutReglas.SiguienteIntento(0));
        Assert.Equal(4, LoginLockoutReglas.SiguienteIntento(3));
    }
}
