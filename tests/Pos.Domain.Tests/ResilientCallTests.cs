using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class ResilientCallTests
{
    [Fact]
    public async Task ConTimeoutAsync_OperacionRapida_DevuelveElResultado()
    {
        var resultado = await ResilientCall.ConTimeoutAsync(
            _ => Task.FromResult(42), TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(42, resultado);
    }

    [Fact]
    public async Task ConTimeoutAsync_OperacionQueNuncaTermina_TiraTimeoutException()
    {
        // await directo (no ContinueWith): así la cancelación del token se propaga como excepción
        // en vez de quedar "absorbida" por una continuación que corre igual pase lo que pase.
        static async Task<int> ColgarSeIndefinidamente(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        await Assert.ThrowsAsync<TimeoutException>(() =>
            ResilientCall.ConTimeoutAsync(ColgarSeIndefinidamente, TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task ConTimeoutAsync_OperacionQueTiraExcepcion_LaPropaga()
    {
        Task<int> Falla(CancellationToken ct) => throw new InvalidOperationException("boom");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResilientCall.ConTimeoutAsync<int>(Falla, TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task ConTimeoutYReintentosAsync_FallaYLuegoTieneExito_ReintentaHastaLograrlo()
    {
        var intentos = 0;
        Task<string> OperacionQueFallaUnaVez(CancellationToken ct)
        {
            intentos++;
            if (intentos < 3) throw new InvalidOperationException("transitorio");
            return Task.FromResult("ok");
        }

        var resultado = await ResilientCall.ConTimeoutYReintentosAsync(
            OperacionQueFallaUnaVez, TimeSpan.FromSeconds(5), maxIntentos: 5, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("ok", resultado);
        Assert.Equal(3, intentos);
    }

    [Fact]
    public async Task ConTimeoutYReintentosAsync_AgotaLosIntentos_TiraLaUltimaFalla()
    {
        var intentos = 0;
        Task<string> SiempreFalla(CancellationToken ct)
        {
            intentos++;
            throw new InvalidOperationException($"falla {intentos}");
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ResilientCall.ConTimeoutYReintentosAsync(
                SiempreFalla, TimeSpan.FromSeconds(5), maxIntentos: 3, TimeSpan.Zero, CancellationToken.None));

        Assert.Equal(3, intentos);
        Assert.Equal("falla 3", ex.Message);
    }

    [Fact]
    public async Task ConTimeoutYReintentosAsync_CancelacionExternaDelCaller_NoSeConfundeConTimeout()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task<int> Operacion(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(1);
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ResilientCall.ConTimeoutYReintentosAsync(
                Operacion, TimeSpan.FromSeconds(5), maxIntentos: 3, TimeSpan.Zero, cts.Token));
    }
}
