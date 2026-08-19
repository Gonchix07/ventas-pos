using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class NotaCreditoReglasTests
{
    [Fact]
    public void SaldoAnulable_descuenta_las_notas_previas()
    {
        Assert.Equal(4000m, NotaCreditoReglas.SaldoAnulable(10000m, 6000m));
    }

    [Fact]
    public void SaldoAnulable_nunca_es_negativo()
    {
        Assert.Equal(0m, NotaCreditoReglas.SaldoAnulable(10000m, 12000m));
    }

    [Fact]
    public void ImporteAcreditable_rechaza_cero_y_negativos()
    {
        Assert.False(NotaCreditoReglas.ImporteAcreditable(0m, 1000m));
        Assert.False(NotaCreditoReglas.ImporteAcreditable(-50m, 1000m));
    }

    [Fact]
    public void ImporteAcreditable_rechaza_lo_que_excede_el_saldo()
    {
        Assert.False(NotaCreditoReglas.ImporteAcreditable(1000.02m, 1000m));
        Assert.True(NotaCreditoReglas.ImporteAcreditable(1000m, 1000m));
    }

    [Fact]
    public void ImporteAcreditable_tolera_el_centavo_de_redondeo()
    {
        Assert.True(NotaCreditoReglas.ImporteAcreditable(1000.01m, 1000m));
    }

    [Fact]
    public void LineasAnulables_excluye_las_ya_acreditadas()
    {
        var lineas = new[]
        {
            new LineaOriginal(1, 100m, 0.21m, YaAnulada: false),
            new LineaOriginal(2, 200m, 0.21m, YaAnulada: true),
            new LineaOriginal(3, 300m, 0.105m, YaAnulada: false),
        };
        var r = NotaCreditoReglas.LineasAnulables(lineas);
        Assert.Equal(new long[] { 1, 3 }, r.Select(l => l.IdDetalle));
    }

    [Fact]
    public void Prorratear_reparte_segun_la_proporcion_de_cada_alicuota()
    {
        // 75% al 21% y 25% al 10,5% → un ajuste de 1000 se parte 750 / 250.
        var lineas = new[]
        {
            new LineaOriginal(1, 7500m, 0.21m, false),
            new LineaOriginal(2, 2500m, 0.105m, false),
        };
        var r = NotaCreditoReglas.Prorratear(1000m, lineas);

        Assert.Equal(2, r.Count);
        Assert.Equal(750m, r.Single(x => x.Alicuota == 0.21m).Importe);
        Assert.Equal(250m, r.Single(x => x.Alicuota == 0.105m).Importe);
    }

    [Fact]
    public void Prorratear_suma_exactamente_el_monto_pedido_pese_al_redondeo()
    {
        // Tres alícuotas en proporciones que no dividen exacto: el último tramo absorbe el resto.
        var lineas = new[]
        {
            new LineaOriginal(1, 100m, 0.21m, false),
            new LineaOriginal(2, 100m, 0.105m, false),
            new LineaOriginal(3, 100m, 0.27m, false),
        };
        var r = NotaCreditoReglas.Prorratear(100m, lineas);
        Assert.Equal(100m, r.Sum(x => x.Importe));
    }

    [Fact]
    public void Prorratear_con_una_sola_alicuota_devuelve_el_monto_entero()
    {
        var lineas = new[] { new LineaOriginal(1, 5000m, 0.21m, false) };
        var r = NotaCreditoReglas.Prorratear(1234.56m, lineas);
        Assert.Single(r);
        Assert.Equal(0.21m, r[0].Alicuota);
        Assert.Equal(1234.56m, r[0].Importe);
    }

    [Fact]
    public void Prorratear_sin_lineas_no_devuelve_tramos()
    {
        Assert.Empty(NotaCreditoReglas.Prorratear(1000m, Array.Empty<LineaOriginal>()));
    }

    [Fact]
    public void NotaCredito_usa_una_serie_distinta_de_la_de_facturas()
    {
        Assert.NotEqual(NumeradorIds.Factura(2), NumeradorIds.NotaCredito(2));
        // Y dos puntos de venta distintos no comparten serie de NC.
        Assert.NotEqual(NumeradorIds.NotaCredito(2), NumeradorIds.NotaCredito(3));
    }
}
