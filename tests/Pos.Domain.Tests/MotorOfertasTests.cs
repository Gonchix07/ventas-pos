using Pos.Domain.Enums;
using Pos.Domain.Services;

namespace Pos.Domain.Tests;

public class MotorOfertasTests
{
    // Artículo 10: sector 1, linea 1, familia 1, presentación 100.
    private static LineaPedido Linea(decimal cant, decimal precio = 100m) =>
        new(0, IdArticulo: 10, IdSector: 1, IdLinea: 1, IdFamilia: 1, IdPresentacion: 100, cant, precio);

    private static OfertaDef Descuento(decimal porc, bool acumula, params AlcanceDef[] alcances) =>
        new(1, $"Desc {porc}%", acumula, TipoOfertaEnum.Descuento, null, porc, null, null, null, alcances);

    [Fact]
    public void Descuento_Porcentual_SobreAlcanceLinea()
    {
        var of = Descuento(10m, false, new AlcanceDef(null, 1, null, null, null, false));
        var r = MotorOfertas.Aplicar(new[] { Linea(2) }, new[] { of }, new HashSet<int>());
        Assert.Equal(200m, r[0].Bruto);
        Assert.Equal(20m, r[0].Descuento);
        Assert.Equal(180m, r[0].Neto);
    }

    [Fact]
    public void NoAplica_SiNoMatcheaAlcance()
    {
        var of = Descuento(10m, false, new AlcanceDef(null, 99, null, null, null, false));
        var r = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { of }, new HashSet<int>());
        Assert.Equal(0m, r[0].Descuento);
    }

    [Fact]
    public void Excepcion_ExcluyeArticulo()
    {
        var of = Descuento(10m, false,
            new AlcanceDef(null, 1, null, null, null, false),            // incluye la línea
            new AlcanceDef(null, null, null, null, 10, true));           // excepción artículo 10
        var r = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { of }, new HashSet<int>());
        Assert.Equal(0m, r[0].Descuento);
    }

    [Fact]
    public void Cluster_SoloAplicaSiClienteEnCluster()
    {
        var of = Descuento(10m, false, new AlcanceDef(IdCluster: 7, null, null, null, null, false));
        var sin = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { of }, new HashSet<int>());
        Assert.Equal(0m, sin[0].Descuento);
        var con = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { of }, new HashSet<int> { 7 });
        Assert.Equal(10m, con[0].Descuento);
    }

    [Fact]
    public void NoAcumulables_TomaElMejor()
    {
        var a = Descuento(10m, false, new AlcanceDef(null, 1, null, null, null, false));
        var b = Descuento(25m, false, new AlcanceDef(null, 1, null, null, null, false));
        var r = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { a, b }, new HashSet<int>());
        Assert.Equal(25m, r[0].Descuento); // no se suman; gana el mayor
    }

    [Fact]
    public void Acumulables_SeSuman()
    {
        var a = Descuento(10m, true, new AlcanceDef(null, 1, null, null, null, false));
        var b = Descuento(5m, true, new AlcanceDef(null, 1, null, null, null, false));
        var r = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { a, b }, new HashSet<int>());
        Assert.Equal(15m, r[0].Descuento);
    }

    [Fact]
    public void Descuento_NuncaSuperaElBruto()
    {
        var a = Descuento(80m, true, new AlcanceDef(null, 1, null, null, null, false));
        var b = Descuento(80m, true, new AlcanceDef(null, 1, null, null, null, false));
        var r = MotorOfertas.Aplicar(new[] { Linea(1, 100m) }, new[] { a, b }, new HashSet<int>());
        Assert.Equal(100m, r[0].Descuento);
        Assert.Equal(0m, r[0].Neto);
    }

    [Fact]
    public void Bonificacion_LlevaTresPagaDos()
    {
        // min=2, bonif=1 → cada 3 unidades, 1 gratis.
        var of = new OfertaDef(2, "3x2", false, TipoOfertaEnum.Bonificacion, null,
            null, null, CantidadMin: 2, CantidadBonif: 1,
            new[] { new AlcanceDef(null, 1, null, null, null, false) });
        var r = MotorOfertas.Aplicar(new[] { Linea(6, 100m) }, new[] { of }, new HashSet<int>());
        // 6 unidades → 2 bloques de 3 → 2 gratis → descuento 200
        Assert.Equal(200m, r[0].Descuento);
        Assert.Equal(400m, r[0].Neto);
    }

    [Fact]
    public void SinInclusiones_AplicaAToda_LaSucursal()
    {
        var of = Descuento(10m, false); // sin alcances
        var r = MotorOfertas.Aplicar(new[] { Linea(1) }, new[] { of }, new HashSet<int>());
        Assert.Equal(10m, r[0].Descuento);
    }

    // ---------------- 2x1 y segunda unidad ----------------

    private static OfertaDef PorLinea(TipoOfertaEnum tipo, decimal? porc = null) =>
        new(3, tipo.ToString(), false, tipo, null, porc, null, null, null, Array.Empty<AlcanceDef>());

    [Theory]
    [InlineData(1, 0)]      // una sola unidad: no hay 2ª
    [InlineData(2, 100)]    // un par → 1 gratis
    [InlineData(3, 100)]    // la impar no suma
    [InlineData(4, 200)]    // dos pares
    public void DosPorUno_RegalaUnaCadaDosUnidades(int cantidad, decimal esperado)
    {
        var r = MotorOfertas.Aplicar(new[] { Linea(cantidad) },
            new[] { PorLinea(TipoOfertaEnum.DosPorUno) }, new HashSet<int>());
        Assert.Equal(esperado, r[0].Descuento);
    }

    [Fact]
    public void SegundaUnidad_BonificaElPorcentajeIndicado()
    {
        var r = MotorOfertas.Aplicar(new[] { Linea(2) },
            new[] { PorLinea(TipoOfertaEnum.SegundaUnidad, 70m) }, new HashSet<int>());
        Assert.Equal(70m, r[0].Descuento);   // 70% de la 2ª unidad de $100
    }

    [Fact]
    public void SegundaUnidad_SinPorcentaje_UsaElDefaultDe70()
    {
        var r = MotorOfertas.Aplicar(new[] { Linea(2) },
            new[] { PorLinea(TipoOfertaEnum.SegundaUnidad) }, new HashSet<int>());
        Assert.Equal(MotorOfertas.PorcentajeSegundaUnidadPorDefecto, r[0].Descuento);
    }

    // ---------------- Mix Canasta ----------------

    // Carrito de 3 artículos distintos, uno por línea, todos a $100.
    private static LineaPedido LineaArt(int indice, int idArticulo, decimal cant, decimal precio = 100m) =>
        new(indice, idArticulo, IdSector: 1, IdLinea: 1, IdFamilia: 1,
            IdPresentacion: 100 + idArticulo, cant, precio);

    private static OfertaDef Canasta(params ItemCanastaDef[] items) =>
        new(4, "Canasta", false, TipoOfertaEnum.MixCanasta, null, null, null, null, null,
            Array.Empty<AlcanceDef>(), items);

    private static readonly ItemCanastaDef[] DosAyUnB =
    {
        new(10, 2, RolItemCanasta.Condicion),
        new(11, 1, RolItemCanasta.Condicion),
        new(12, 1, RolItemCanasta.Bonificado),
    };

    [Fact]
    public void Canasta_CompletaBonificaElPremio()
    {
        var carrito = new[] { LineaArt(0, 10, 2), LineaArt(1, 11, 1), LineaArt(2, 12, 1) };
        var r = MotorOfertas.Aplicar(carrito, new[] { Canasta(DosAyUnB) }, new HashSet<int>());
        Assert.Equal(0m, r[0].Descuento);      // los de la canasta que activa no se bonifican
        Assert.Equal(0m, r[1].Descuento);
        Assert.Equal(100m, r[2].Descuento);    // el premio, gratis
    }

    [Fact]
    public void Canasta_Incompleta_NoAplica()
    {
        var carrito = new[] { LineaArt(0, 10, 1), LineaArt(1, 11, 1), LineaArt(2, 12, 1) };
        var r = MotorOfertas.Aplicar(carrito, new[] { Canasta(DosAyUnB) }, new HashSet<int>());
        Assert.All(r, l => Assert.Equal(0m, l.Descuento));
    }

    [Fact]
    public void Canasta_SeRepiteTantasVecesComoEntre()
    {
        var carrito = new[] { LineaArt(0, 10, 4), LineaArt(1, 11, 2), LineaArt(2, 12, 2) };
        var r = MotorOfertas.Aplicar(carrito, new[] { Canasta(DosAyUnB) }, new HashSet<int>());
        Assert.Equal(200m, r[2].Descuento);    // 2 canastas → 2 premios
    }

    [Fact]
    public void Canasta_SoloBonificaLasUnidadesQueElClienteLleva()
    {
        // Entra 2 veces, pero del artículo premiado hay una sola unidad en el carrito.
        var carrito = new[] { LineaArt(0, 10, 4), LineaArt(1, 11, 2), LineaArt(2, 12, 1) };
        var r = MotorOfertas.Aplicar(carrito, new[] { Canasta(DosAyUnB) }, new HashSet<int>());
        Assert.Equal(100m, r[2].Descuento);
    }

    [Fact]
    public void Canasta_SinElArticuloPremiado_NoDescuentaNada()
    {
        var carrito = new[] { LineaArt(0, 10, 2), LineaArt(1, 11, 1) };
        var r = MotorOfertas.Aplicar(carrito, new[] { Canasta(DosAyUnB) }, new HashSet<int>());
        Assert.All(r, l => Assert.Equal(0m, l.Descuento));
    }

    [Fact]
    public void Canasta_MismoArticuloEnLosDosLados_LlevaTresYUnoGratis()
    {
        var of = Canasta(new ItemCanastaDef(10, 3, RolItemCanasta.Condicion),
                         new ItemCanastaDef(10, 1, RolItemCanasta.Bonificado));
        var r = MotorOfertas.Aplicar(new[] { LineaArt(0, 10, 3) }, new[] { of }, new HashSet<int>());
        Assert.Equal(100m, r[0].Descuento);

        var r2 = MotorOfertas.Aplicar(new[] { LineaArt(0, 10, 2) }, new[] { of }, new HashSet<int>());
        Assert.Equal(0m, r2[0].Descuento);
    }

    [Fact]
    public void Canasta_SinLadoBonificado_NoHaceNada()
    {
        var of = Canasta(new ItemCanastaDef(10, 1, RolItemCanasta.Condicion));
        var r = MotorOfertas.Aplicar(new[] { LineaArt(0, 10, 5) }, new[] { of }, new HashSet<int>());
        Assert.Equal(0m, r[0].Descuento);
    }
}
