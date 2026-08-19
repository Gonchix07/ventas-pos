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
