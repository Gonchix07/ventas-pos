using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class InterfaseContableReglasTests
{
    [Theory]
    [InlineData(4, 1)]  // Consumidor Final
    [InlineData(1, 2)]  // Responsable Inscripto
    [InlineData(9, 3)]  // Responsable No Inscripto
    [InlineData(3, 4)]  // Exento/No Alcanzado
    [InlineData(2, 5)]  // Monotributista ("Régimen Simplificado")
    [InlineData(10, 6)] // Sujeto No Categorizado
    public void CondIva_MapeaLosIdsDelSeed(int idCondIva, int esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.CondIva(idCondIva));

    [Fact]
    public void CondIva_IdSinMapeoConocido_DevuelveNull() =>
        Assert.Null(InterfaseContableReglas.CondIva(999));

    [Fact]
    public void CondIva_SinCliente_DevuelveNull() =>
        Assert.Null(InterfaseContableReglas.CondIva(null));

    [Theory]
    [InlineData(1, "A", "FA")]
    [InlineData(1, "B", "FB")]
    [InlineData(-1, "A", "CA")]
    [InlineData(-1, "B", "CB")]
    public void TipoComprobante_CombinaSignoYLetra(int signo, string letra, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.TipoComprobante(signo, letra));

    [Theory]
    [InlineData(1, "0001")]
    [InlineData(10, "0010")]
    [InlineData(2222, "2222")]
    public void Prenum_CompletaCerosAIzquierdaA4Digitos(int numeroPuntoVenta, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.Prenum(numeroPuntoVenta));

    [Theory]
    [InlineData(32L, "00000032")]
    [InlineData(1L, "00000001")]
    public void Numero_CompletaCerosAIzquierdaA8Digitos(long numero, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.Numero(numero));

    [Theory]
    [InlineData("8104", "0000000008104")]
    [InlineData("123456789ABCD", "123456789ABCD")]
    public void Articulo_CompletaCerosAIzquierdaA13Digitos(string codigoInterno, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.Articulo(codigoInterno));

    [Theory]
    [InlineData(1, "00000001")]
    [InlineData(154, "00000154")]
    public void Reparto_UsaElNumeroDeOperacionA8Digitos(int idOperacion, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.Reparto(idOperacion));

    [Fact]
    public void Codconv_SinOferta_DevuelveNull() =>
        Assert.Null(InterfaseContableReglas.Codconv(null));

    [Theory]
    [InlineData(1, "00000001")]
    [InlineData(3021, "00003021")]
    public void Codconv_ConOferta_CompletaCerosAIzquierdaA8Digitos(int idOferta, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.Codconv(idOferta));

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ModoFact_1VentaNormal2CuentaCorriente(bool tieneCuentaCorriente, int esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.ModoFact(tieneCuentaCorriente));

    [Theory]
    [InlineData(false, "01")]
    [InlineData(true, "02")]
    public void CondVta_01Normal02CuentaCorriente(bool tieneCuentaCorriente, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.CondVta(tieneCuentaCorriente));

    [Fact]
    public void Hora_FormateaComoHHmm()
    {
        var fecha = new DateTime(2026, 8, 21, 14, 5, 32);
        Assert.Equal("14:05", InterfaseContableReglas.Hora(fecha));
    }

    [Fact]
    public void Plan_SinCuotas_DevuelveNull() =>
        Assert.Null(InterfaseContableReglas.Plan(null));

    [Theory]
    [InlineData(1, "001")]
    [InlineData(12, "012")]
    public void Plan_CompletaCerosAIzquierdaA3Digitos(int cuotas, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.Plan(cuotas));

    [Theory]
    [InlineData(1, "01")]
    [InlineData(23, "23")]
    public void CajaCodigo_CompletaCerosAIzquierdaA2Digitos(int idCaja, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.CajaCodigo(idCaja));

    [Theory]
    [InlineData(1, "01")]
    [InlineData(9, "09")]
    public void CajeroCodigo_CompletaCerosAIzquierdaA2Digitos(int idUsuario, string esperado) =>
        Assert.Equal(esperado, InterfaseContableReglas.CajeroCodigo(idUsuario));

    [Fact]
    public void DetalleCierre_ArmaElTextoConElFormatoConfirmado() =>
        Assert.Equal("Planilla Nro 08-00027843 01-Cajero1",
            InterfaseContableReglas.DetalleCierre(8, 27843, 1, "Cajero1"));

    [Fact]
    public void DetalleCierre_CortaA40CaracteresSiElNombreLoExcede()
    {
        var texto = InterfaseContableReglas.DetalleCierre(1, 1, 1, new string('X', 60));
        Assert.Equal(40, texto.Length);
    }

    [Fact]
    public void DetalleRetiro_ArmaElTextoConElFormatoConfirmado() =>
        Assert.Equal("Entrega Tesorería 01-Cajero1", InterfaseContableReglas.DetalleRetiro(1, "Cajero1"));

    [Fact]
    public void DetalleRetiro_CortaA40CaracteresSiElNombreLoExcede()
    {
        var texto = InterfaseContableReglas.DetalleRetiro(1, new string('X', 60));
        Assert.Equal(40, texto.Length);
    }

    // --- CodificarCantidadMovStock ---

    [Fact]
    public void CodificarCantidadMovStock_BultoCompleto_SueltasEnCero()
    {
        // 24 unidades, bulto de 12 → 2 bultos, 0 sueltas → "2.00".
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(24m, 12m, ventaPorPeso: false);
        Assert.Equal(2.00m, codificado);
    }

    [Fact]
    public void CodificarCantidadMovStock_ConSueltas_UsaDosDecimalesSiElBultoNoSupera99()
    {
        // 15 unidades, bulto de 12 → 1 bulto + 3 sueltas → "1.03".
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(15m, 12m, ventaPorPeso: false);
        Assert.Equal(1.03m, codificado);
    }

    [Fact]
    public void CodificarCantidadMovStock_BultoMayorA99_UsaTresDecimales()
    {
        // 250 unidades, bulto de 144 → 1 bulto + 106 sueltas → "1.106" (3 decimales porque 144 > 99).
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(250m, 144m, ventaPorPeso: false);
        Assert.Equal(1.106m, codificado);
    }

    [Fact]
    public void CodificarCantidadMovStock_BultoDe99Justo_UsaDosDecimales()
    {
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(100m, 99m, ventaPorPeso: false);
        Assert.Equal(1.01m, codificado);
    }

    [Fact]
    public void CodificarCantidadMovStock_SinBulto_EsLaCantidadTalCual()
    {
        // UnidadXBulto=1 (no viene en bulto): todo queda como "bultos" sueltos, sin parte decimal.
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(5m, 1m, ventaPorPeso: false);
        Assert.Equal(5.00m, codificado);
    }

    [Fact]
    public void CodificarCantidadMovStock_VentaPorPeso_KiloEnteroYGramosA3Decimales()
    {
        // Venta por peso: el UnidadXBulto del artículo NO se usa, siempre 3 decimales (kilo.gramos).
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(3.920m, 12m, ventaPorPeso: true);
        Assert.Equal(3.920m, codificado);
    }

    [Fact]
    public void CodificarCantidadMovStock_VentaPorPeso_RedondeaA3Decimales()
    {
        var codificado = InterfaseContableReglas.CodificarCantidadMovStock(2.12345m, 1m, ventaPorPeso: true);
        Assert.Equal(2.123m, codificado);
    }
}
