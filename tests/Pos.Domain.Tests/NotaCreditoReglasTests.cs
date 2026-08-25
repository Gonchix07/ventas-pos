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
    public void RepartirPercepcion_reparte_segun_la_proporcion_de_cada_una_y_suma_exacto()
    {
        // Factura con 800 de percepción IVA 21%, 150 de IVA 10,5% y 50 de IIBB (1000 en total):
        // acreditar 100 tiene que repartirse en la misma proporción (80/15/5).
        var (iva21, iva105, iibb) = NotaCreditoReglas.RepartirPercepcion(100m, 800m, 150m, 50m);
        Assert.Equal(80m, iva21);
        Assert.Equal(15m, iva105);
        Assert.Equal(5m, iibb);
        Assert.Equal(100m, iva21 + iva105 + iibb);
    }

    [Fact]
    public void RepartirPercepcion_una_sola_base_devuelve_todo_ahi()
    {
        var (iva21, iva105, iibb) = NotaCreditoReglas.RepartirPercepcion(250m, 0m, 0m, 1000m);
        Assert.Equal(0m, iva21);
        Assert.Equal(0m, iva105);
        Assert.Equal(250m, iibb);
    }

    [Fact]
    public void RepartirPercepcion_sin_percepcion_original_no_reparte_nada()
    {
        var (iva21, iva105, iibb) = NotaCreditoReglas.RepartirPercepcion(100m, 0m, 0m, 0m);
        Assert.Equal(0m, iva21 + iva105 + iibb);
    }

    [Fact]
    public void RepartirPercepcion_monto_cero_o_negativo_no_reparte_nada()
    {
        var (iva21, iva105, iibb) = NotaCreditoReglas.RepartirPercepcion(0m, 800m, 150m, 50m);
        Assert.Equal(0m, iva21 + iva105 + iibb);
    }

    [Fact]
    public void RepartirPercepcion_cierra_exacto_pese_al_redondeo()
    {
        // Proporciones que no dan números redondos (100/3): el último tramo (IIBB) absorbe el resto.
        var (iva21, iva105, iibb) = NotaCreditoReglas.RepartirPercepcion(100m, 1m, 1m, 1m);
        Assert.Equal(100m, iva21 + iva105 + iibb);
    }

    [Fact]
    public void NotaCredito_usa_una_serie_distinta_de_la_de_facturas()
    {
        Assert.NotEqual(NumeradorIds.Factura(2, 6), NumeradorIds.NotaCredito(2, 8));
        // Y dos puntos de venta distintos no comparten serie de NC.
        Assert.NotEqual(NumeradorIds.NotaCredito(2, 8), NumeradorIds.NotaCredito(3, 8));
    }

    [Fact]
    public void Factura_A_y_B_del_mismo_punto_de_venta_no_comparten_serie()
    {
        Assert.NotEqual(NumeradorIds.Factura(1, 1), NumeradorIds.Factura(1, 6));
    }

    [Fact]
    public void NotaCredito_A_y_B_del_mismo_punto_de_venta_no_comparten_serie()
    {
        Assert.NotEqual(NumeradorIds.NotaCredito(1, 3), NumeradorIds.NotaCredito(1, 8));
    }

    [Fact]
    public void CantidadAcreditable_rechaza_cero_y_negativos()
    {
        Assert.False(NotaCreditoReglas.CantidadAcreditable(0m, 10m));
        Assert.False(NotaCreditoReglas.CantidadAcreditable(-1m, 10m));
    }

    [Fact]
    public void CantidadAcreditable_rechaza_lo_que_excede_lo_disponible()
    {
        Assert.False(NotaCreditoReglas.CantidadAcreditable(11m, 10m));
        Assert.True(NotaCreditoReglas.CantidadAcreditable(10m, 10m));
    }

    [Fact]
    public void CantidadAcreditable_acepta_una_cantidad_parcial()
    {
        Assert.True(NotaCreditoReglas.CantidadAcreditable(1m, 10m));
    }

    [Fact]
    public void ImporteProporcional_de_la_cantidad_completa_da_el_importe_original()
    {
        Assert.Equal(9300000m, NotaCreditoReglas.ImporteProporcional(9300000m, 10m, 10m));
    }

    [Fact]
    public void ImporteProporcional_reparte_segun_la_cantidad_pedida()
    {
        // 10 unidades a $930.000 el importe total ($9.300.000): anular solo 3 → $2.790.000.
        Assert.Equal(2790000m, NotaCreditoReglas.ImporteProporcional(9300000m, 10m, 3m));
    }

    [Fact]
    public void ImporteProporcional_con_cantidad_original_cero_no_divide_por_cero()
    {
        Assert.Equal(0m, NotaCreditoReglas.ImporteProporcional(0m, 0m, 1m));
    }
}
