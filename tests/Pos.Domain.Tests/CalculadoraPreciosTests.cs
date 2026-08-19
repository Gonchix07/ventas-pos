using Pos.Domain.Enums;
using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class CalculadoraPreciosTests
{
    private static readonly DateTime Hoy = new(2026, 8, 7);

    [Fact]
    public void Folder_GanaSobre_TemporalYBase()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m),
            new CandidatoPrecio(TipoListaPrecio.Temporal, 5, Hoy.AddDays(-1), Hoy.AddDays(1), 90m, 0m),
            new CandidatoPrecio(TipoListaPrecio.Folder, 1, null, null, 80m, 0m),
        };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy);
        Assert.True(r.Encontrado);
        Assert.Equal(80m, r.PrecioVigente);
    }

    [Fact]
    public void Temporal_GanaSobreBase_SoloSiVigente()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m),
            new CandidatoPrecio(TipoListaPrecio.Temporal, 1, Hoy.AddDays(-2), Hoy.AddDays(2), 70m, 0m),
        };
        Assert.Equal(70m, CalculadoraPrecios.Resolver(candidatos, Hoy).PrecioVigente);
    }

    [Fact]
    public void Temporal_Vencida_NoAplica_CaeABase()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m),
            new CandidatoPrecio(TipoListaPrecio.Temporal, 9, Hoy.AddDays(-10), Hoy.AddDays(-1), 70m, 0m),
        };
        Assert.Equal(100m, CalculadoraPrecios.Resolver(candidatos, Hoy).PrecioVigente);
    }

    [Fact]
    public void MismoTipo_MayorPrioridadGana()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m),
            new CandidatoPrecio(TipoListaPrecio.Base, 5, null, null, 88m, 0m),
        };
        Assert.Equal(88m, CalculadoraPrecios.Resolver(candidatos, Hoy).PrecioVigente);
    }

    [Fact]
    public void SinCandidatos_NoEncontrado()
    {
        var r = CalculadoraPrecios.Resolver(Array.Empty<CandidatoPrecio>(), Hoy);
        Assert.False(r.Encontrado);
    }

    [Fact]
    public void Convenio_AplicaDescuentoPorcentual()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m) };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(10m, null));
        Assert.Equal(100m, r.PrecioVigente);
        Assert.Equal(90m, r.PrecioConvenio);
    }

    [Fact]
    public void Convenio_ConListaPropia_UsaEsePrecio()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m) };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(0m, 75m));
        Assert.Equal(75m, r.PrecioConvenio);
    }

    // ---- Folder vs convenio: la promoción no acumula con el convenio del cliente ----

    [Fact]
    public void Folder_NoAcumulaConConvenioPorcentual()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3),
            new CandidatoPrecio(TipoListaPrecio.Folder, 1, null, null, 80m, 0m, 5),
        };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(10m, null));
        Assert.Equal(80m, r.PrecioVigente);
        Assert.Equal(80m, r.PrecioConvenio); // ni 72 (10% sobre el folder) ni 90
        Assert.False(r.AplicoConvenio);
        Assert.Equal(5, r.IdListaPrecio);
    }

    [Fact]
    public void Folder_NoAcumulaConConvenioDeListaPropia()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3),
            new CandidatoPrecio(TipoListaPrecio.Folder, 1, null, null, 80m, 0m, 5),
        };
        // El convenio tiene lista propia (id 4, precio 95) y 7% de descuento: se ignora todo.
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(7m, 95m, 4));
        Assert.Equal(80m, r.PrecioConvenio);
        Assert.False(r.AplicoConvenio);
        Assert.Equal(5, r.IdListaPrecio);
    }

    [Fact]
    public void SinFolder_ElConvenioSigueAplicando()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3),
            new CandidatoPrecio(TipoListaPrecio.Temporal, 1, Hoy.AddDays(-1), Hoy.AddDays(1), 90m, 0m, 6),
        };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(10m, null));
        Assert.Equal(90m, r.PrecioVigente);
        Assert.Equal(81m, r.PrecioConvenio);
        Assert.True(r.AplicoConvenio);
        Assert.Equal(6, r.IdListaPrecio);
    }

    [Fact]
    public void Convenio_ConListaPropia_InformaLaListaDelConvenio()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3) };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(0m, 75m, 4));
        Assert.Equal(75m, r.PrecioConvenio);
        Assert.Equal(4, r.IdListaPrecio); // la lista cobrada es la del convenio, no la ganadora
        Assert.True(r.AplicoConvenio);
    }

    [Fact]
    public void FolderVencido_NoExiste_ComoTipo_PeroTemporalVencidaDejaPasarElConvenio()
    {
        // Una temporal vencida cae a Base: el convenio se aplica normalmente (no hay folder en juego).
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3),
            new CandidatoPrecio(TipoListaPrecio.Temporal, 9, Hoy.AddDays(-10), Hoy.AddDays(-1), 70m, 0m, 6),
        };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(20m, null));
        Assert.Equal(100m, r.PrecioVigente);
        Assert.Equal(80m, r.PrecioConvenio);
        Assert.True(r.AplicoConvenio);
    }
}
