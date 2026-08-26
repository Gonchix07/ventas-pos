namespace Pos.Domain.Services;

/// <summary>
/// Reglas puras de traducción entre pos-mayorista y los códigos que espera la tabla <c>ivavtas</c>
/// de la base MySQL "interfase" del sistema contable externo (ver
/// <see cref="Pos.Infrastructure.Services.InterfaseContableService"/>). Confirmados con el usuario
/// el 2026-08-21 contra la pantalla "Condiva" del sistema viejo y la convención de tipos de
/// comprobante (FA/FB/CA/CB).
/// </summary>
public static class InterfaseContableReglas
{
    /// <summary>
    /// Código numérico de condición de IVA que espera <c>ivavtas.condiva</c>, por <c>IdCondIva</c>
    /// de pos-mayorista (seed: 1 Responsable Inscripto, 2 Monotributista, 3 Exento, 4 Consumidor
    /// Final, 9 Responsable No Inscripto, 10 Sujeto No Categorizado). "Régimen Simplificado" en la
    /// pantalla del sistema viejo es el nombre histórico de Monotributo.
    /// </summary>
    private static readonly Dictionary<int, int> CondIvaPorIdCondIva = new()
    {
        [4] = 1,  // Consumidor Final
        [1] = 2,  // Responsable Inscripto
        [9] = 3,  // Responsable No Inscripto
        [3] = 4,  // Exento/No Alcanzado
        [2] = 5,  // Monotributista (= "Régimen Simplificado")
        [10] = 6, // Sujeto No Categorizado
    };

    /// <summary>Null si el <paramref name="idCondIva"/> no tiene mapeo conocido (no debería pasar
    /// con los valores del seed, pero no se quiere tirar una excepción por esto).</summary>
    public static int? CondIva(int? idCondIva) =>
        idCondIva is int id && CondIvaPorIdCondIva.TryGetValue(id, out var codigo) ? codigo : null;

    /// <summary>"FA"/"FB" para facturas, "CA"/"CB" para notas de crédito (Signo -1), según la letra
    /// del comprobante.</summary>
    public static string TipoComprobante(int signo, string letra) => (signo == 1 ? "F" : "C") + letra;

    /// <summary>Punto de venta a 4 dígitos, completando ceros a la izquierda (ej. 3 → "0003").</summary>
    public static string Prenum(int numeroPuntoVenta) => numeroPuntoVenta.ToString().PadLeft(4, '0');

    /// <summary>Número de comprobante a 8 dígitos, completando ceros a la izquierda.</summary>
    public static string Numero(long numero) => numero.ToString().PadLeft(8, '0');

    /// <summary>Código fijo de "proveedor" para todas las ventas de pos-mayorista (confirmado con
    /// el usuario, no varía por sucursal/empresa).</summary>
    public const string ProvFijo = "00MAY";

    /// <summary>Código fijo de depósito para <c>movstock.dedeposito</c> (confirmado con el usuario:
    /// pos-mayorista no maneja múltiples depósitos hoy).</summary>
    public const string DepositoFijo = "100";

    /// <summary>Código fijo de lista de precios para <c>movstock.lista</c> (confirmado con el
    /// usuario: nuestros códigos reales de lista —ej. "FOLDER AGO"— no entran en los 4 caracteres
    /// de esa columna, así que se manda este código fijo en vez de intentar mapear/truncar).</summary>
    public const string ListaFija = "2068";

    /// <summary>Código de artículo para <c>movstock.articulo</c> (char 13): el <c>CodigoInterno</c>
    /// del artículo, completando ceros a la izquierda.</summary>
    public static string Articulo(string codigoInterno) => codigoInterno.PadLeft(13, '0');

    /// <summary>Código de reparto para <c>movstock.reparto</c> (char 8): el número de operación de
    /// pos-mayorista, completando ceros a la izquierda (confirmado con el usuario — no hay reparto
    /// real todavía, se usa como referencia).</summary>
    public static string Reparto(int idOperacion) => idOperacion.ToString().PadLeft(8, '0');

    /// <summary>Código de convenio/oferta para <c>movstock.codconv</c> (char 8): el <c>IdOferta</c>
    /// de la primera/principal oferta aplicada a la línea, completando ceros a la izquierda — null
    /// si la línea no tuvo ninguna oferta (confirmado con el usuario).</summary>
    public static string? Codconv(int? idOfertaPrincipal) =>
        idOfertaPrincipal?.ToString().PadLeft(8, '0');

    /// <summary>
    /// "1" venta normal, "2" venta en cuenta corriente (confirmado con el usuario) — según si algún
    /// pago de la venta usó cuenta corriente como fuente.
    /// </summary>
    public static int ModoFact(bool tieneCuentaCorriente) => tieneCuentaCorriente ? 2 : 1;

    /// <summary>"01" para todas las operaciones, "02" para las de cuenta corriente (confirmado con
    /// el usuario) — <c>comision.condvta</c>. Misma condición que <see cref="ModoFact"/>, pero con
    /// otra codificación porque son columnas de tablas distintas.</summary>
    public static string CondVta(bool tieneCuentaCorriente) => tieneCuentaCorriente ? "02" : "01";

    /// <summary>Hora del comprobante en formato HH:mm, para <c>comision.hora</c> (char 5).</summary>
    public static string Hora(DateTime fecha) => fecha.ToString("HH:mm");

    /// <summary>Código de plan de cuotas para <c>cupones.plan</c> (char 3): la cantidad de cuotas,
    /// completando ceros a la izquierda (confirmado con el usuario — sin cuotas/1 sola cuota →
    /// "001"). Null si el pago no tiene plan asociado (medios sin cuotas).</summary>
    public static string? Plan(int? cantidadCuotas) => cantidadCuotas?.ToString().PadLeft(3, '0');

    /// <summary>Código de caja para <c>cupones.caja</c> (char 4): el <c>IdCaja</c> interno de
    /// pos-mayorista a 2 dígitos (confirmado con el usuario, ej. IdCaja=1 → "01").</summary>
    public static string CajaCodigo(int idCaja) => idCaja.ToString().PadLeft(2, '0');

    /// <summary>Código de cajero para <c>cupones.cajero</c> (char 2): el <c>IdUsuario</c> interno de
    /// pos-mayorista a 2 dígitos (confirmado con el usuario, mismo criterio que <see cref="CajaCodigo"/>).</summary>
    public static string CajeroCodigo(int idUsuario) => idUsuario.ToString().PadLeft(2, '0');

    /// <summary>Valores fijos confirmados con el usuario para <c>cja_movi</c>: "I" siempre en
    /// <c>tipo</c>, "15418" siempre en <c>rubro</c> — una sola fila por cierre de turno del cajero,
    /// con los totales de efectivo/tarjetas/cheques rendidos.</summary>
    public const string CjaMoviTipoFijo = "I";
    public const string CjaMoviRubroFijo = "15418";

    /// <summary>
    /// Texto de <c>cja_movi.detalle</c> (varchar 40): "Planilla Nro {caja}-{cierre} {cajero}-{nombre}"
    /// (ej. "Planilla Nro 08-00027843 01-Cajero1"), confirmado con el usuario. Se corta a 40
    /// caracteres si el nombre de usuario lo hace exceder — el formato es más importante que
    /// perder los últimos caracteres del nombre en un caso límite.
    /// </summary>
    public static string DetalleCierre(int idCaja, int numeroCierre, int idUsuario, string nombreUsuario)
    {
        var texto = $"Planilla Nro {CajaCodigo(idCaja)}-{Numero(numeroCierre)} {CajeroCodigo(idUsuario)}-{nombreUsuario}";
        return texto.Length <= 40 ? texto : texto[..40];
    }

    /// <summary>
    /// Texto de <c>cja_movi.detalle</c> para un retiro de efectivo (entrega a tesorería) — otra fila
    /// de la misma tabla, distinta de <see cref="DetalleCierre"/>: "Entrega Tesorería {caja}-{nombre}"
    /// (ej. "Entrega Tesorería 01-Cajero1"), confirmado con el usuario. Mismos <c>tipo</c>/<c>rubro</c>
    /// fijos que el cierre de turno.
    /// </summary>
    public static string DetalleRetiro(int idCaja, string nombreUsuario)
    {
        var texto = $"Entrega Tesorería {CajaCodigo(idCaja)}-{nombreUsuario}";
        return texto.Length <= 40 ? texto : texto[..40];
    }

    /// <summary>
    /// Codifica la cantidad de <c>movstock.salida</c> según la regla confirmada con el usuario
    /// (2026-08-25): el número es decimal, pero NO es la cantidad real — la parte entera son
    /// bultos y la parte decimal son unidades sueltas.
    /// <list type="bullet">
    /// <item>Venta normal: se descompone <paramref name="totalUnidades"/> (cantidad × UnidadXBulto
    /// de la PRESENTACIÓN vendida, ver FacturacionService) usando el <c>UnidadXBulto</c> de la
    /// FICHA del artículo (no el de la presentación: ese puede no estar cargado). Ej. UnidadXBulto
    /// del artículo=12, se vendieron 15 unidades → 1 bulto + 3 sueltas → "1.03". Los decimales son
    /// 2 si UnidadXBulto ≤ 99, o 3 si lo supera (para que quepan las unidades sueltas, que van de 0
    /// a UnidadXBulto-1).</item>
    /// <item>Venta por peso (<see cref="Pos.Domain.Entities.Articulo.VentaPorPeso"/>): siempre 3
    /// decimales, con el kilo entero como parte entera y los gramos como parte decimal — que es
    /// exactamente cómo ya viene <paramref name="totalUnidades"/> en ese caso (el peso en Kg), así
    /// que solo se redondea a 3 decimales, sin aplicar la descomposición en bultos.</item>
    /// </list>
    /// </summary>
    public static decimal CodificarCantidadMovStock(decimal totalUnidades, decimal unidadXBultoArticulo, bool ventaPorPeso)
    {
        if (ventaPorPeso)
            return Math.Round(totalUnidades, 3, MidpointRounding.AwayFromZero);

        var bulto = unidadXBultoArticulo <= 0 ? 1m : unidadXBultoArticulo;
        var decimales = bulto > 99m ? 3 : 2;
        var factor = decimales == 2 ? 100m : 1000m;

        var bultos = Math.Floor(totalUnidades / bulto);
        // Sueltas siempre entera (no tiene sentido "media unidad suelta"): se redondea antes de
        // convertirla en la parte decimal, por si totalUnidades trajera algún resto no entero.
        var sueltas = Math.Round(totalUnidades - bultos * bulto, 0, MidpointRounding.AwayFromZero);
        return bultos + sueltas / factor;
    }
}
