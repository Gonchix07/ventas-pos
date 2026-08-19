using Pos.Domain.Common;

namespace Pos.Domain.Entities;

public class CondicionIva : AuditableEntity
{
    public int IdCondIva { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Letra { get; set; }
    public string? CodigoInterno { get; set; }
}

public class Cliente : AuditableEntity
{
    public int IdCliente { get; set; }
    public string CodigoInt { get; set; } = "";
    public string? Cuit { get; set; }
    public string? Documento { get; set; }
    /// <summary>Razón social / nombre, tal como se factura.</summary>
    public string Descripcion { get; set; } = "";
    /// <summary>
    /// Nombre de fantasía: con qué se conoce al cliente en el mostrador ("LA VACA LOCA"), que casi
    /// nunca coincide con la razón social. Opcional: en el padrón real solo ~8% lo tiene cargado.
    /// </summary>
    public string? NombreFantasia { get; set; }
    public int IdCondIva { get; set; }
    public CondicionIva? CondicionIva { get; set; }
    public bool PermitePresupuesto { get; set; }
    /// <summary>
    /// Si el cliente puede operar en cuenta corriente. Es el filtro previo al límite de crédito:
    /// el límite se carga por sucursal (ClienteEnCuenta), pero solo tiene sentido para quien está
    /// habilitado acá.
    /// </summary>
    public bool AdmiteCuentaCorriente { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Domicilio del cliente, tal como venía del padrón del ERP anterior. Es texto plano en un solo
    /// campo (no calle/número separados) porque así está en el origen: "GARAY  3445". Todos opcionales:
    /// en el padrón importado el 3% no tiene domicilio y el 94% no tiene email.
    /// </summary>
    public string? Domicilio { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Localidad { get; set; }
    /// <summary>Provincia: la factura A la lleva impresa junto con la localidad.</summary>
    public string? Provincia { get; set; }
    public string? Email { get; set; }

    public ICollection<ClienteEnCuenta> Cuentas { get; set; } = new List<ClienteEnCuenta>();
    public ICollection<Autorizado> Autorizados { get; set; } = new List<Autorizado>();
    public ICollection<TarjetaCliente> Tarjetas { get; set; } = new List<TarjetaCliente>();
}

public class ClienteEnCuenta : AuditableEntity
{
    public int IdCliente { get; set; }
    public Cliente? Cliente { get; set; }
    public int IdSucursal { get; set; }
    public decimal LimiteCredito { get; set; }
}

/// <summary>
/// Agrupación de clientes usada como alcance de ofertas (ver MotorOfertas). Es una entidad propia:
/// existe independientemente de sus miembros, así que se puede crear vacía y renombrar sin tocar
/// la lista de clientes.
/// </summary>
public class Cluster : AuditableEntity
{
    public int IdCluster { get; set; }
    public string Descripcion { get; set; } = "";
    public ICollection<ClusterCliente> Miembros { get; set; } = new List<ClusterCliente>();
}

/// <summary>Pertenencia de un cliente a un cluster. Un cliente puede estar en varios clusters.</summary>
public class ClusterCliente : AuditableEntity
{
    public int IdCluster { get; set; }
    public Cluster? Cluster { get; set; }
    public int IdCliente { get; set; }
    public Cliente? Cliente { get; set; }
}

/// <summary>
/// Persona habilitada a comprar en nombre de un cliente. Se guardan datos mínimos: la identifica
/// el DNI, y se la puede inactivar sin borrarla (queda el registro de que estuvo autorizada).
/// </summary>
public class Autorizado : AuditableEntity
{
    public int IdAutorizado { get; set; }
    public int IdCliente { get; set; }
    public Cliente? Cliente { get; set; }
    public string Dni { get; set; } = "";
    /// <summary>Nombre completo.</summary>
    public string Descripcion { get; set; } = "";
    /// <summary>Desde cuándo está autorizada (fecha del alta).</summary>
    public DateTime FechaAlta { get; set; }
    public bool Activo { get; set; } = true;
}

public class TipoTarjeta : AuditableEntity
{
    public int IdTipoTarjeta { get; set; }
    public string Descripcion { get; set; } = "";
    public int? IdListaPrecio { get; set; }
}

public class TarjetaCliente : AuditableEntity
{
    public int IdCliente { get; set; }
    public Cliente? Cliente { get; set; }
    public int IdTipoTarjeta { get; set; }
    public TipoTarjeta? TipoTarjeta { get; set; }
    public string NroTarjeta { get; set; } = "";

    /// <summary>
    /// El cliente tiene UNA sola tarjeta vigente: al darle una nueva, la anterior se anula (no se
    /// borra, para no perder el rastro de las ventas viejas hechas con ese número).
    /// </summary>
    public bool Activa { get; set; } = true;
    public DateTime? FechaBajaUtc { get; set; }
}
