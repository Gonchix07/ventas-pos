using Pos.Domain.Entities;
using Pos.Domain.Enums;

namespace Pos.Domain.Tests;

public class EntidadesTests
{
    [Fact]
    public void Presentacion_UnidadXBulto_PorDefecto_EsUno()
    {
        var p = new Presentacion();
        Assert.Equal(1m, p.UnidadXBulto);
    }

    [Fact]
    public void Barra_TipoPorDefecto_EsEan13()
    {
        var b = new Barra();
        Assert.Equal(TipoBarra.Ean13, b.Tipo);
    }

    [Fact]
    public void Comprobante_EstadoInicial_EsIniciado()
    {
        var c = new CabeceraComprobante();
        Assert.Equal(EstadoComprobante.Iniciado, c.Estado);
    }

    [Fact]
    public void TipoComprobante_SignoPorDefecto_EsPositivo()
    {
        var t = new TipoComprobante();
        Assert.Equal(1, t.Signo);
    }
}
