using System.Globalization;
using Pos.Application.Abstractions.Fiscal;
using Pos.Domain.Enums;

namespace Pos.Infrastructure.Adapters.Hasar;

/// <summary>
/// Traducción entre los conceptos del POS y las constantes del protocolo fiscal 2G. En el XML las
/// constantes viajan por nombre simbólico, no por su valor numérico.
/// </summary>
public static class HasarMapeo
{
    /// <summary>
    /// La SMH/PT-250F imprime sobre rollo continuo, así que NO puede emitir Factura A/B/C (ésas
    /// son de formulario continuo u hoja suelta): el equivalente en rollo es el Tique Factura.
    /// </summary>
    public static string CodigoComprobante(string letra, bool notaCredito) => (letra.ToUpperInvariant(), notaCredito) switch
    {
        ("A", false) => "TiqueFacturaA",
        ("B", false) => "TiqueFacturaB",
        ("C", false) => "TiqueFacturaC",
        ("M", false) => "TiqueFacturaM",
        ("A", true) => "TiqueNotaCreditoA",
        ("B", true) => "TiqueNotaCreditoB",
        ("C", true) => "TiqueNotaCreditoC",
        ("M", true) => "TiqueNotaCreditoM",
        (_, true) => "TiqueNotaCredito",
        _ => "Tique"
    };

    public static string Responsabilidad(ResponsabilidadIvaFiscal r) => r switch
    {
        ResponsabilidadIvaFiscal.ResponsableInscripto => "ResponsableInscripto",
        ResponsabilidadIvaFiscal.Monotributo => "Monotributo",
        ResponsabilidadIvaFiscal.MonotributoSocial => "MonotributoSocial",
        ResponsabilidadIvaFiscal.Exento => "ResponsableExento",
        ResponsabilidadIvaFiscal.NoResponsable => "NoResponsable",
        ResponsabilidadIvaFiscal.NoCategorizado => "NoCategorizado",
        _ => "ConsumidorFInal" // sic: el protocolo declara la constante con esa grafía
    };

    public static string TipoDocumento(TipoDocumentoFiscal t) => t switch
    {
        TipoDocumentoFiscal.Cuit => "TipoCUIT",
        TipoDocumentoFiscal.Cuil => "TipoCUIL",
        TipoDocumentoFiscal.Dni => "TipoDNI",
        TipoDocumentoFiscal.Pasaporte => "TipoPasaporte",
        TipoDocumentoFiscal.Ci => "TipoCI",
        TipoDocumentoFiscal.Le => "TipoLE",
        TipoDocumentoFiscal.Lc => "TipoLC",
        _ => "TipoNinguno"
    };

    /// <summary>
    /// Forma de pago fiscal según la fuente configurada en el tipo de pago del POS. Billetera
    /// virtual y transferencia caen en TransferenciaNoBancaria/Bancaria; la distinción fina (MODO
    /// vs MercadoPago) no existe en la tabla del protocolo y va en la descripción libre.
    /// </summary>
    public static string FormaPago(FuentePago f) => f switch
    {
        FuentePago.Efectivo => "Efectivo",
        FuentePago.Tarjeta => "TarjetaDeCredito",
        FuentePago.BilleteraVirtual => "TransferenciaNoBancaria",
        FuentePago.Transferencia => "TransferenciaBancaria",
        FuentePago.CuentaCorriente => "CuentaCorriente",
        _ => "OtrosMediosPago"
    };

    /// <summary>Constante <c>TiposTributos</c> del comando ImprimirOtrosTributos.</summary>
    public static string Tributo(TipoTributoFiscal t) => t switch
    {
        TipoTributoFiscal.PercepcionIva => "PercepcionIVA",
        TipoTributoFiscal.PercepcionIibb => "PercepcionIIBB",
        _ => "OtrosTributos"
    };

    /// <summary>El equipo espera punto decimal, sin separador de miles ni signo de moneda.</summary>
    public static string Monto(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Cantidad(decimal v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// La descripción de ítem tiene ancho acotado en el rollo; se recorta acá y no en la UI para
    /// que un nombre largo no haga fallar el comprobante entero a mitad de impresión.
    /// </summary>
    public static string Texto(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        v = v.Trim();
        return v.Length <= max ? v : v[..max];
    }
}
