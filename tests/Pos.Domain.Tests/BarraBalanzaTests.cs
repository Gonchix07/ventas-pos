using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class BarraBalanzaTests
{
    // Etiqueta real (Kretz): QUESO PUNTA AGUA, PLU 70522, PESO 3,920 kg, $/kg 8999,9,
    // IMPORTE 35279,6 (= 3,920 × 8999,9, lo que confirma que el peso es 3,920 y no 3,9206).
    private const string BarraReal = "2070522039206";

    [Fact]
    public void Lee_CodigoYPeso_DeUnaEtiquetaReal()
    {
        Assert.True(BarraBalanza.TryParse(BarraReal, out var r));
        Assert.Equal("070522", r.CodigoArticulo);
        Assert.Equal(3.920m, r.Peso);
    }

    [Fact]
    public void ElImporteDeLaEtiquetaCierraConElPesoLeido()
    {
        BarraBalanza.TryParse(BarraReal, out var r);
        Assert.Equal(35279.6m, Math.Round(r.Peso * 8999.9m, 1));
    }

    [Fact]
    public void SinVerificadorValido_LeeSeisDigitos_ComoDosEnterosYCuatroDecimales()
    {
        // Mismo contenido pero con un último dígito que no cierra como verificador: se cae a la
        // otra lectura, donde ese dígito es el 4º decimal del peso.
        Assert.True(BarraBalanza.TryParse("2070522039201", out var r));
        Assert.Equal(3.9201m, r.Peso);
    }

    [Theory]
    [InlineData("")]                 // vacío
    [InlineData("207052203920")]     // 12 dígitos
    [InlineData("20705220392066")]   // 14 dígitos
    [InlineData("7790000000017")]    // no arranca en 2
    [InlineData("20705220392A6")]    // no numérica
    [InlineData("2070522000000")]    // peso 0
    public void RechazaLoQueNoEsUnaBarraDeBalanza(string barra)
    {
        Assert.False(BarraBalanza.TryParse(barra, out _));
    }

    [Fact]
    public void ElCodigoSeBuscaConYSinCerosALaIzquierda()
    {
        Assert.Equal(new[] { "070522", "70522" }, BarraBalanza.CodigosPosibles("070522"));
        Assert.Equal(new[] { "123456" }, BarraBalanza.CodigosPosibles("123456"));
    }

    [Fact]
    public void CalculaElVerificadorEan13()
    {
        Assert.Equal(6, BarraBalanza.VerificadorEan13("207052203920"));
    }
}
