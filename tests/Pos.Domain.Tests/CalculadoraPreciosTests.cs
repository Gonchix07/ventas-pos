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
        Assert.Equal(80m, r.PrecioBase); // el folder gana también como "Precio" a mostrar, no la lista propia (95)
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
        Assert.Equal(75m, r.PrecioBase); // "Precio" en Caja: la lista propia, no PrecioVigente (100)
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

    // ---- Campaña de puntos-app: se suma al % del convenio (a pedido del negocio) ----

    [Fact]
    public void Campania_SolaSinConvenio_AplicaDescuentoPorcentual()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m) };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, convenio: null, campaniaDescuentoPorc: 15m);
        Assert.Equal(100m, r.PrecioVigente);
        Assert.Equal(85m, r.PrecioConvenio);
        Assert.True(r.AplicoConvenio);
    }

    [Fact]
    public void Campania_SeSumaAlPorcentajeDelConvenio()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m) };
        // Convenio 10% + campaña 15% = 25% sobre el precio de lista.
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(10m, null), campaniaDescuentoPorc: 15m);
        Assert.Equal(75m, r.PrecioConvenio);
        Assert.True(r.AplicoConvenio);
    }

    [Fact]
    public void Campania_SeSumaSobreLaListaPropiaDelConvenio()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3) };
        // Lista propia del convenio ($75) + campaña 20% sobre ESE precio, no sobre el de lista general.
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(0m, 75m, 4), campaniaDescuentoPorc: 20m);
        Assert.Equal(60m, r.PrecioConvenio);
        Assert.Equal(4, r.IdListaPrecio);
        Assert.True(r.AplicoConvenio);
        Assert.Equal(75m, r.PrecioBase); // "Precio" en Caja: la lista propia (75), no la general (100)
    }

    [Fact]
    public void PrecioBase_CasoReal_ListaDeTarjetaMasConvenioMasCampania()
    {
        // Caso real reportado: art. 355, lista general $20339,89, pero el cliente tiene tarjeta con
        // lista propia a $17999,90 (vía TipoTarjeta.IdListaPrecio, resguardo de PricingService cuando
        // el Convenio no tiene lista propia). Convenio 7% + campaña puntos-app 10% = 17%, aplicado
        // sobre la lista de la tarjeta, NO sobre la general — "Precio" en Caja debe mostrar $17999,90
        // (no $20339,89) y "Descuento" tiene que salir de ahí, no de la lista general.
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 20339.89m, 0m) };
        var convenio = new ConvenioInfo(7m, 17999.90m, 9);
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, convenio, campaniaDescuentoPorc: 10m);
        Assert.Equal(20339.89m, r.PrecioVigente); // lista general, informativo, no lo que se muestra
        Assert.Equal(17999.90m, r.PrecioBase); // esto es lo que Caja tiene que mostrar como "Precio"
        Assert.Equal(14939.9170m, r.PrecioConvenio); // 17999.90 × 0.83
    }

    [Fact]
    public void Campania_SumaConvenioNuncaSuperaCienPorciento()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m) };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, new ConvenioInfo(70m, null), campaniaDescuentoPorc: 60m);
        Assert.Equal(0m, r.PrecioConvenio); // clamp a 100%, nunca precio negativo
    }

    [Fact]
    public void Folder_NoAcumulaConCampaniaTampoco()
    {
        var candidatos = new[]
        {
            new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m, 3),
            new CandidatoPrecio(TipoListaPrecio.Folder, 1, null, null, 80m, 0m, 5),
        };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy, convenio: null, campaniaDescuentoPorc: 15m);
        Assert.Equal(80m, r.PrecioConvenio); // ni 68 (15% sobre el folder)
        Assert.False(r.AplicoConvenio);
    }

    [Fact]
    public void SinConvenioNiCampania_PrecioConvenioIgualAlVigente()
    {
        var candidatos = new[] { new CandidatoPrecio(TipoListaPrecio.Base, 1, null, null, 100m, 0m) };
        var r = CalculadoraPrecios.Resolver(candidatos, Hoy);
        Assert.Equal(100m, r.PrecioConvenio);
        Assert.False(r.AplicoConvenio);
        Assert.Equal(100m, r.PrecioBase);
    }
}
