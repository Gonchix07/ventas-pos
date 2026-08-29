using Pos.Domain.Common;

namespace Pos.Domain.Entities;

public class PadronIngresosBrutos : AuditableEntity
{
    public string Cuit { get; set; } = "";
    public decimal Percepcion { get; set; }
}

public class PadronExcepcionPercepcionIva : AuditableEntity
{
    public string Cuit { get; set; } = "";
}

/// <summary>
/// CAEA cargado a mano por un administrador — contingencia para cuando WSFEv1 (CAE) no responde al
/// momento de facturar. El valor real se consigue CON conexión (FECAEASolicitar, se pide con
/// antelación por quincena) y se guarda acá para poder seguir facturando aunque ARCA esté
/// inaccesible justo en el momento de la venta. Uno por empresa+período+quincena — el CAEA de ARCA
/// no depende del punto de venta (ver AfipWsfeClient.SolicitarCaeaAsync).
/// </summary>
public class CaeaCargado : AuditableEntity
{
    public int IdCaea { get; set; }
    public int IdEmpresa { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    /// <summary>1 = del 1 al 15, 2 = del 16 a fin de mes.</summary>
    public int Orden { get; set; }
    public string Valor { get; set; } = "";
    public DateTime VigenciaDesde { get; set; }
    public DateTime VigenciaHasta { get; set; }
}

/// <summary>
/// Configuraciones clave-valor: límites de facturación a Consumidor Final, límite de efectivo
/// en caja, reintentos por CAE inaccesible, rango de redondeo, timeouts, etc.
/// </summary>
public class Configuracion : AuditableEntity
{
    public int IdConfiguracion { get; set; }
    public string Clave { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string? Valor { get; set; }
}

/// <summary>
/// Conexión a una base MySQL externa donde, a futuro, la aplicación deposita datos generados para
/// que los consuma otro sistema. Tabla singleton (una sola fila): <see cref="ConexionExternaAdminService"/>
/// hace upsert sobre la única fila en vez de un ABM de varias. <see cref="Habilitada"/> arranca en
/// false: hoy es solo la configuración, todavía no hay ningún proceso que la use para conectarse.
/// </summary>
public class ConexionExternaMySql : AuditableEntity
{
    public int IdConexionExterna { get; set; }
    public string Host { get; set; } = "";
    public int Puerto { get; set; } = 3306;
    public string BaseDatos { get; set; } = "";
    public string Usuario { get; set; } = "";
    /// <summary>Cifrada en reposo con Data Protection (purpose propio "Pos.ConexionExternaMySql"),
    /// nunca en texto plano — mismo patrón que <see cref="Empresa.CertificadoPasswordProtegida"/>.
    /// Nunca se expone descifrada de vuelta al frontend.</summary>
    public string? PasswordProtegida { get; set; }
    public bool Habilitada { get; set; }
}

/// <summary>
/// Conexión al API de puntos-app (programa de fidelización externo, otro proyecto): cada Factura de
/// venta con cliente identificado (DNI) suma puntos allá — ver <see cref="Pos.Application.Abstractions.Fidelizacion.IPuntosFidelizacionService"/>.
/// Tabla singleton (una sola fila), mismo criterio que <see cref="ConexionExternaMySql"/>.
/// <see cref="Comercio"/> es el nombre del comercio ya dado de alta en puntos-app (ABM de Comercios
/// de ese sistema) — se manda igual en todas las cargas, no varía por sucursal.
/// </summary>
public class ConexionPuntosApp : AuditableEntity
{
    public int IdConexionPuntosApp { get; set; }
    public string UrlBase { get; set; } = "";
    public string Comercio { get; set; } = "";
    /// <summary>API key fija de integración (X-Api-Key, = API_INTEGRATION_KEY en puntos-app), NO un
    /// access_token de sesión (esos expiran a la hora). Cifrada en reposo con Data Protection
    /// (purpose propio "Pos.ConexionPuntosApp") — nunca se expone descifrada al frontend.</summary>
    public string? TokenProtegido { get; set; }
    public bool Habilitada { get; set; }
}

/// <summary>
/// Conexión al API de giftcards-app (proyecto externo): permite usar una gift card como medio de
/// pago en Caja — ver <see cref="Pos.Application.Abstractions.Giftcards.IGiftcardsAppService"/>.
/// Tabla singleton (una sola fila), mismo criterio que <see cref="ConexionPuntosApp"/>.
/// <see cref="Comercio"/> es el nombre tal como está en <c>empresas.comercio</c> de giftcards-app —
/// <c>usar_giftcard_api</c> rechaza cualquier gift card de una campaña con otro comercio.
/// A diferencia de puntos-app, esto SÍ mueve plata real de la venta (no es best-effort).
/// </summary>
public class ConexionGiftcardsApp : AuditableEntity
{
    public int IdConexionGiftcardsApp { get; set; }
    public string UrlBase { get; set; } = "";
    public string Comercio { get; set; } = "";
    /// <summary>API key fija (X-Api-Key, = API_INTEGRATION_KEY en giftcards-app — proyecto propio,
    /// NO comparte valor con la de puntos-app). Cifrada en reposo con Data Protection (purpose
    /// propio "Pos.ConexionGiftcardsApp").</summary>
    public string? TokenProtegido { get; set; }
    public bool Habilitada { get; set; }
}
