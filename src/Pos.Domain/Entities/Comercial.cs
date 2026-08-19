using Pos.Domain.Common;

namespace Pos.Domain.Entities;

public class Empresa : AuditableEntity
{
    public int IdEmpresa { get; set; }
    public string CodigoInterno { get; set; } = "";
    /// <summary>Razón social: es lo que encabeza la factura.</summary>
    public string Descripcion { get; set; } = "";
    public string? Cuit { get; set; }
    /// <summary>Alias/etiqueta libre del certificado (ej. "Producción" vs "Homologación").</summary>
    public string? CertificadoAlias { get; set; }
    /// <summary>Nombre del archivo .pfx/.p12 tal como se subió (solo para mostrarlo en el ABM;
    /// el contenido se guarda en disco, no en la base).</summary>
    public string? CertificadoNombreArchivo { get; set; }
    /// <summary>Contraseña del certificado, cifrada en reposo con Data Protection — nunca en
    /// texto plano. Se descifra solo al momento de abrir el certificado para firmar ante ARCA.</summary>
    public string? CertificadoPasswordProtegida { get; set; }
    /// <summary>Vencimiento leído del propio certificado al subirlo (NotAfter del X509), para poder
    /// avisar antes de que expire.</summary>
    public DateTime? CertificadoVencimiento { get; set; }
    public DateTime? CertificadoSubidoUtc { get; set; }

    // --- Datos del emisor que exige el encabezado de la factura (A y B) ---
    /// <summary>Condición del emisor frente al IVA, en texto ("Resp. Inscripto").</summary>
    public string? CondicionIva { get; set; }
    /// <summary>Número de Ingresos Brutos (o "Exento" / "Convenio Multilateral NNN").</summary>
    public string? IngresosBrutos { get; set; }
    public DateTime? InicioActividad { get; set; }
    public string? Domicilio { get; set; }
    public string? Localidad { get; set; }
    public string? Provincia { get; set; }
    public string? CodigoPostal { get; set; }

    public ICollection<Sucursal> Sucursales { get; set; } = new List<Sucursal>();
}

public class Sucursal : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdEmpresa { get; set; }
    public Empresa? Empresa { get; set; }
    public string Descripcion { get; set; } = "";

    /// <summary>
    /// Domicilio comercial de la sucursal. Si está cargado es el que sale en la factura; si no,
    /// se cae al de la empresa (una empresa de una sola boca no necesita repetirlo).
    /// </summary>
    public string? Domicilio { get; set; }
    public string? Localidad { get; set; }
    public string? Provincia { get; set; }
    public string? CodigoPostal { get; set; }
}

public class TipoPuntoVenta : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdTipoPuntoVenta { get; set; }
    public string Descripcion { get; set; } = "";
    /// <summary>Tipo ARCA (CAE electrónico, fiscal, etc.).</summary>
    public string? TipoArca { get; set; }
}

public class PuntoVenta : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdPuntoVenta { get; set; }
    public int IdTipoPuntoVenta { get; set; }
    /// <summary>Número de punto de venta habilitado en ARCA.</summary>
    public int NumeroPuntoVenta { get; set; }
    /// <summary>
    /// IP del controlador fiscal (Hasar) con el que habla este punto de venta. Solo aplica al tipo
    /// FISCAL —los otros dos imprimen en la comandera local— y ahí es obligatoria.
    /// </summary>
    public string? IpControlador { get; set; }
}

public class PuestoCaja : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdPuestoAsignado { get; set; }
    /// <summary>Etiqueta libre para identificar el puesto en el ABM (ej. "Caja 1 - Mostrador").
    /// Ya NO se usa para resolver la caja al loguear — un navegador no puede leer el nombre real
    /// de la PC del sistema operativo; ver <see cref="Ip"/>.</summary>
    public string NombrePc { get; set; } = "";
    /// <summary>IP de LAN de la PC de ese puesto. Todas las cajas acceden a la MISMA URL central,
    /// así que la única forma confiable de identificar de qué PC física viene un login es la IP
    /// de origen del request (la ve el servidor, no depende de lo que reporte el navegador).</summary>
    public string? Ip { get; set; }
}

public class Caja : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdCaja { get; set; }
    public int IdPuntoVenta { get; set; }
    public string Descripcion { get; set; } = "";
    public int? IdPuestoAsignado { get; set; }
    /// <summary>
    /// Si esta caja puede vender con modo Presupuesto (comprobante X). El cliente necesita además
    /// su propia <see cref="Cliente.PermitePresupuesto"/> — las dos condiciones se exigen juntas.
    /// Default true: preserva el comportamiento anterior (Presupuesto ya estaba disponible en toda
    /// caja de forma automática) hasta que un administrador decida desactivarlo en alguna.
    /// </summary>
    public bool AdmitePresupuesto { get; set; } = true;
}

/// <summary>
/// Terminal física de tarjeta (posnet) dada de alta en una sucursal — mismo criterio que
/// <see cref="PuestoCaja"/>: ID local autoasignado (max+1) por sucursal. Hoy es solo el catálogo,
/// no hay ningún adaptador de cobro que la use.
/// </summary>
public class TerminalTarjeta : AuditableEntity
{
    public int IdSucursal { get; set; }
    public int IdTerminal { get; set; }
    /// <summary>Nro de terminal del proveedor (alfanumérico: FiServ/PayWay/PinPad no siempre usan solo dígitos).</summary>
    public string NumeroTerminal { get; set; } = "";
    public Pos.Domain.Enums.TipoTerminalTarjeta Tipo { get; set; }
    /// <summary>
    /// Caja a la que está asignada esta terminal (una caja puede tener varias terminales, pero cada
    /// terminal cuelga de UNA sola caja — la relación 1-a-N vive en esta columna, del lado "N", así
    /// que "no repetirse en otras cajas" queda garantizado por el modelo sin necesidad de un índice
    /// único aparte). Null = terminal dada de alta pero sin asignar todavía.
    /// </summary>
    public int? IdCajaAsignada { get; set; }
}
