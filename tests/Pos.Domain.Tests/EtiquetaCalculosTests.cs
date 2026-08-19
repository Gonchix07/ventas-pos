using Pos.Domain.Services;

namespace Pos.Domain.Tests;

/// <summary>Verifica los cálculos contra las 3 etiquetas reales de muestra (Hergo).</summary>
public class EtiquetaCalculosTests
{
    [Fact]
    public void PlayaditoAzul_PrecioPorKg_CoincideConLaMuestraReal()
    {
        // YBA Playadito Suave 5x1 Kg — precio unitario (1 Kg) $3899.90 → precio por Kg = mismo valor.
        var r = EtiquetaCalculos.PrecioPorUnidadMedida(3899.90m, 1m);
        Assert.Equal(3899.90m, r);
    }

    [Fact]
    public void PlayaditoAzul_SinImpuestos_CoincideConLaMuestraReal()
    {
        var r = EtiquetaCalculos.PrecioSinImpuestosNacionales(3899.90m, 0m, 0.21m);
        Assert.Equal(3223.06m, r);
    }

    [Fact]
    public void PlayaditoRoja_SinImpuestos_CoincideConLaMuestraReal()
    {
        var r = EtiquetaCalculos.PrecioSinImpuestosNacionales(3900.29m, 0m, 0.21m);
        Assert.Equal(3223.38m, r);
    }

    [Fact]
    public void FernetBranca_PrecioPorLitro_CoincideConLaMuestraReal()
    {
        // Precio de la caja de 12x750cc = $14899.90; contenido neto de 1 unidad = 0.75 Lt.
        var r = EtiquetaCalculos.PrecioPorUnidadMedida(14899.90m, 0.75m);
        Assert.Equal(19866.53m, r);
    }

    [Fact]
    public void FernetBranca_SinImpuestos_CoincideConLaMuestraReal()
    {
        var r = EtiquetaCalculos.PrecioSinImpuestosNacionales(14899.90m, 0m, 0.21m);
        Assert.Equal(12313.97m, r);
    }

    [Fact]
    public void SinContenidoNeto_NoAplicaPrecioPorUnidadMedida()
    {
        Assert.Null(EtiquetaCalculos.PrecioPorUnidadMedida(1000m, null));
        Assert.Null(EtiquetaCalculos.PrecioPorUnidadMedida(1000m, 0m));
    }

    [Fact]
    public void ConImpuestoInterno_SeRestaAntesDeDesglosarIva()
    {
        // Si hubiera impuesto interno, se resta del precio antes de calcular el neto de IVA.
        var r = EtiquetaCalculos.PrecioSinImpuestosNacionales(1210m, 100m, 0.21m);
        // (1210 - 100) / 1.21 = 917.36...
        Assert.Equal(917.36m, r);
    }
}
