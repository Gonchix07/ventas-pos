using Microsoft.EntityFrameworkCore;
using Pos.Domain.Common;
using Pos.Domain.Entities;

namespace Pos.Infrastructure.Persistence;

public class PosDbContext : DbContext
{
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

    /// <summary>
    /// Columna calculada (persistida) con el día de <see cref="LoteCaja.FechaApertura"/>. Existe solo
    /// para que el día pueda formar parte del índice único de lotes abiertos; no se mapea a una
    /// propiedad de la entidad porque es un dato derivado que nadie escribe ni consulta.
    /// </summary>
    internal const string DiaAperturaShadow = "DiaApertura";

    // Seguridad
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Modulo> Modulos => Set<Modulo>();
    public DbSet<Permiso> Permisos => Set<Permiso>();
    public DbSet<MovimientoAuditoria> MovimientosAuditoria => Set<MovimientoAuditoria>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Catálogo
    public DbSet<Articulo> Articulos => Set<Articulo>();
    public DbSet<Presentacion> Presentaciones => Set<Presentacion>();
    public DbSet<Barra> Barras => Set<Barra>();
    public DbSet<Sector> Sectores => Set<Sector>();
    public DbSet<Linea> Lineas => Set<Linea>();
    public DbSet<Familia> Familias => Set<Familia>();
    public DbSet<ModoIva> ModosIva => Set<ModoIva>();

    // Clientes
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ClienteEnCuenta> ClientesEnCuenta => Set<ClienteEnCuenta>();
    public DbSet<Cluster> Clusters => Set<Cluster>();
    public DbSet<ClusterCliente> ClusterClientes => Set<ClusterCliente>();
    public DbSet<Autorizado> Autorizados => Set<Autorizado>();
    public DbSet<TarjetaCliente> TarjetasClientes => Set<TarjetaCliente>();
    public DbSet<TipoTarjeta> TiposTarjeta => Set<TipoTarjeta>();
    public DbSet<CondicionIva> CondicionesIva => Set<CondicionIva>();

    // Estructura comercial
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Sucursal> Sucursales => Set<Sucursal>();
    public DbSet<PuntoVenta> PuntosVenta => Set<PuntoVenta>();
    public DbSet<TipoPuntoVenta> TiposPuntoVenta => Set<TipoPuntoVenta>();
    public DbSet<PuestoCaja> PuestosCaja => Set<PuestoCaja>();
    public DbSet<Caja> Cajas => Set<Caja>();
    public DbSet<TerminalTarjeta> TerminalesTarjeta => Set<TerminalTarjeta>();

    // Precios / ofertas
    public DbSet<ListaPrecio> ListasPrecios => Set<ListaPrecio>();
    public DbSet<Precio> Precios => Set<Precio>();
    public DbSet<Convenio> Convenios => Set<Convenio>();
    public DbSet<CabeceraOferta> CabecerasOfertas => Set<CabeceraOferta>();
    public DbSet<AlcanceOferta> AlcancesOfertas => Set<AlcanceOferta>();
    public DbSet<AccionOferta> AccionesOfertas => Set<AccionOferta>();
    public DbSet<ItemOferta> ItemsOfertas => Set<ItemOferta>();
    public DbSet<TipoOferta> TiposOferta => Set<TipoOferta>();
    public DbSet<OfertaMedioPago> OfertasMedioPago => Set<OfertaMedioPago>();

    // Pagos
    public DbSet<MedioPago> MediosPago => Set<MedioPago>();
    public DbSet<TipoPago> TiposPago => Set<TipoPago>();
    public DbSet<PlanCuota> PlanesCuota => Set<PlanCuota>();
    public DbSet<CuentaCorriente> CuentasCorrientes => Set<CuentaCorriente>();
    public DbSet<CorreccionCupon> CorreccionesCupon => Set<CorreccionCupon>();
    public DbSet<Banco> Bancos => Set<Banco>();

    // Comprobantes / operaciones
    public DbSet<CabeceraComprobante> CabecerasComprobantes => Set<CabeceraComprobante>();
    public DbSet<DetalleComprobante> DetallesComprobantes => Set<DetalleComprobante>();
    public DbSet<TipoComprobante> TiposComprobante => Set<TipoComprobante>();
    public DbSet<ComprobanteAsociado> ComprobantesAsociados => Set<ComprobanteAsociado>();
    public DbSet<Operacion> Operaciones => Set<Operacion>();
    public DbSet<DetalleOperacion> DetallesOperaciones => Set<DetalleOperacion>();
    public DbSet<Numero> Numeros => Set<Numero>();

    // Caja
    public DbSet<LoteCaja> LotesCaja => Set<LoteCaja>();
    public DbSet<MovimientoCaja> MovimientosCaja => Set<MovimientoCaja>();
    public DbSet<MovimientoPago> MovimientosPagos => Set<MovimientoPago>();
    public DbSet<CierreLoteCaja> CierresLotesCaja => Set<CierreLoteCaja>();
    public DbSet<CierreZFiscal> CierresZFiscal => Set<CierreZFiscal>();
    public DbSet<MotivoDiferencia> MotivosDiferencia => Set<MotivoDiferencia>();
    public DbSet<MotivoCierre> MotivosCierre => Set<MotivoCierre>();

    // Fiscal / config
    public DbSet<PadronIngresosBrutos> PadronIngresosBrutos => Set<PadronIngresosBrutos>();
    public DbSet<PadronExcepcionPercepcionIva> PadronExcepcionPercepcionesIva => Set<PadronExcepcionPercepcionIva>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<ConexionExternaMySql> ConexionesExternasMySql => Set<ConexionExternaMySql>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        ConfigureKeys(b);
        ConfigureRelations(b);
        ConfigureIndexes(b);

        // Precisión decimal por defecto para todo el modelo (montos y cantidades).
        foreach (var property in b.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }

        // Concurrencia optimista: RowVersion (rowversion) en toda entidad auditable.
        foreach (var et in b.Model.GetEntityTypes())
        {
            var rv = et.FindProperty(nameof(AuditableEntity.RowVersion));
            if (rv is not null)
            {
                rv.IsConcurrencyToken = true;
                rv.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
                rv.SetColumnType("rowversion");
            }
        }

        // Sin cascadas: evita múltiples caminos de borrado en SQL Server y borrados accidentales.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    private static void ConfigureKeys(ModelBuilder b)
    {
        // --- Claves simples (surrogate int/bigint identity) ---
        b.Entity<Usuario>().HasKey(x => x.IdUsuario);
        b.Entity<Rol>().HasKey(x => x.IdRol);
        b.Entity<Modulo>().HasKey(x => x.IdModulo);
        b.Entity<Permiso>().HasKey(x => x.IdPermiso);
        b.Entity<MovimientoAuditoria>().HasKey(x => x.IdMovimiento);
        b.Entity<RefreshToken>().HasKey(x => x.IdRefreshToken);
        b.Entity<Articulo>().HasKey(x => x.IdArticulo);
        b.Entity<Presentacion>().HasKey(x => x.IdPresentacion);
        b.Entity<Barra>().HasKey(x => x.IdBarra);
        b.Entity<Sector>().HasKey(x => x.IdSector);
        b.Entity<Linea>().HasKey(x => x.IdLinea);
        b.Entity<Banco>().HasKey(x => x.IdBanco);
        b.Entity<Familia>().HasKey(x => x.IdFamilia);
        b.Entity<ModoIva>().HasKey(x => x.IdModoIva);
        b.Entity<Cliente>().HasKey(x => x.IdCliente);
        b.Entity<Autorizado>().HasKey(x => x.IdAutorizado);
        b.Entity<TipoTarjeta>().HasKey(x => x.IdTipoTarjeta);
        b.Entity<CondicionIva>().HasKey(x => x.IdCondIva);
        b.Entity<Empresa>().HasKey(x => x.IdEmpresa);
        b.Entity<Sucursal>().HasKey(x => x.IdSucursal);
        b.Entity<ListaPrecio>().HasKey(x => x.IdListaPrecio);
        b.Entity<TipoOferta>().HasKey(x => x.IdTipoOferta);
        b.Entity<AlcanceOferta>().HasKey(x => x.IdAlcance);
        b.Entity<AccionOferta>().HasKey(x => x.IdAccion);
        b.Entity<ItemOferta>().HasKey(x => x.IdItem);
        b.Entity<MedioPago>().HasKey(x => x.IdMedioPago);
        b.Entity<TipoPago>().HasKey(x => x.IdTipoPago);
        b.Entity<PlanCuota>().HasKey(x => x.IdPlan);
        b.Entity<TipoComprobante>().HasKey(x => x.IdTipoComprobante);
        b.Entity<DetalleComprobante>().HasKey(x => x.IdDetalleComprobante);
        b.Entity<DetalleOperacion>().HasKey(x => x.IdDetalleOperacion);
        b.Entity<MovimientoPago>().HasKey(x => x.IdMovPagos);
        b.Entity<MotivoDiferencia>().HasKey(x => x.IdMotivoDiferencia);
        b.Entity<MotivoCierre>().HasKey(x => x.IdMotivoCierre);
        b.Entity<Cluster>().HasKey(x => x.IdCluster);
        b.Entity<Configuracion>().HasKey(x => x.IdConfiguracion);
        b.Entity<ConexionExternaMySql>().HasKey(x => x.IdConexionExterna);
        b.Entity<PadronIngresosBrutos>().HasKey(x => x.Cuit);
        b.Entity<PadronExcepcionPercepcionIva>().HasKey(x => x.Cuit);

        // --- Claves compuestas (negocio multi-sucursal) ---
        b.Entity<ClienteEnCuenta>().HasKey(x => new { x.IdCliente, x.IdSucursal });
        b.Entity<ClusterCliente>().HasKey(x => new { x.IdCluster, x.IdCliente });
        b.Entity<TarjetaCliente>().HasKey(x => new { x.IdCliente, x.IdTipoTarjeta, x.NroTarjeta });
        b.Entity<TipoPuntoVenta>().HasKey(x => new { x.IdSucursal, x.IdTipoPuntoVenta });
        b.Entity<PuntoVenta>().HasKey(x => new { x.IdSucursal, x.IdPuntoVenta });
        b.Entity<PuestoCaja>().HasKey(x => new { x.IdSucursal, x.IdPuestoAsignado });
        b.Entity<Caja>().HasKey(x => new { x.IdSucursal, x.IdCaja });
        b.Entity<TerminalTarjeta>().HasKey(x => new { x.IdSucursal, x.IdTerminal });
        b.Entity<Precio>().HasKey(x => new { x.IdListaPrecio, x.IdPresentacion });
        b.Entity<Convenio>().HasKey(x => new { x.IdSucursal, x.IdConvenio });
        b.Entity<CabeceraOferta>().HasKey(x => new { x.IdSucursal, x.IdOferta });
        b.Entity<OfertaMedioPago>().HasKey(x => new { x.IdSucursal, x.IdOfertaMedioPago });
        b.Entity<CuentaCorriente>().HasKey(x => new { x.IdSucursal, x.IdCliente, x.IdComprobante });
        b.Entity<CorreccionCupon>().HasKey(x => x.IdCorreccionCupon);
        b.Entity<CabeceraComprobante>().HasKey(x => new { x.IdSucursal, x.IdComprobante });
        b.Entity<ComprobanteAsociado>().HasKey(x => new { x.IdComprobanteOrigen, x.IdComprobanteAsociado });
        b.Entity<Operacion>().HasKey(x => new { x.IdSucursal, x.IdOperacion });
        b.Entity<Numero>().HasKey(x => new { x.IdSucursal, x.IdNumero });
        b.Entity<LoteCaja>().HasKey(x => new { x.IdSucursal, x.IdLote });
        b.Entity<MovimientoCaja>().HasKey(x => new { x.IdSucursal, x.IdMovCaja });
        b.Entity<CierreLoteCaja>().HasKey(x => new { x.IdSucursal, x.IdLote, x.IdMedioPago });
        b.Entity<CierreZFiscal>().HasKey(x => x.IdCierreZFiscal);

        // CUIT del padrón: longitud fija.
        b.Entity<PadronIngresosBrutos>().Property(x => x.Cuit).HasMaxLength(11);
        b.Entity<PadronExcepcionPercepcionIva>().Property(x => x.Cuit).HasMaxLength(11);
    }

    private static void ConfigureRelations(ModelBuilder b)
    {
        b.Entity<Usuario>().HasOne(x => x.Rol).WithMany(r => r.Usuarios).HasForeignKey(x => x.IdRol);
        b.Entity<Permiso>().HasOne(x => x.Rol).WithMany(r => r.Permisos).HasForeignKey(x => x.IdRol);
        b.Entity<Permiso>().HasOne(x => x.Modulo).WithMany(m => m.Permisos).HasForeignKey(x => x.IdModulo);
        b.Entity<RefreshToken>().HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario);

        b.Entity<Familia>().HasOne(x => x.Sector).WithMany().HasForeignKey(x => x.IdSector);

        b.Entity<Articulo>().HasOne(x => x.Sector).WithMany().HasForeignKey(x => x.IdSector);
        b.Entity<Articulo>().HasOne(x => x.Linea).WithMany().HasForeignKey(x => x.IdLinea);
        b.Entity<Articulo>().HasOne(x => x.Familia).WithMany().HasForeignKey(x => x.IdFamilia);
        b.Entity<Articulo>().HasOne(x => x.ModoIva).WithMany().HasForeignKey(x => x.IdModoIva);
        b.Entity<Presentacion>().HasOne(x => x.Articulo).WithMany(a => a.Presentaciones).HasForeignKey(x => x.IdArticulo);
        b.Entity<Barra>().HasOne(x => x.Presentacion).WithMany(p => p.Barras).HasForeignKey(x => x.IdPresentacion);

        b.Entity<Cliente>().HasOne(x => x.CondicionIva).WithMany().HasForeignKey(x => x.IdCondIva);
        b.Entity<ClienteEnCuenta>().HasOne(x => x.Cliente).WithMany(c => c.Cuentas).HasForeignKey(x => x.IdCliente);
        b.Entity<ClusterCliente>().HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.IdCliente);
        // Sin cascada, por la convención global de abajo: ClusterService borra las pertenencias
        // explícitamente antes de borrar el cluster.
        b.Entity<ClusterCliente>().HasOne(x => x.Cluster).WithMany(x => x.Miembros).HasForeignKey(x => x.IdCluster);
        b.Entity<Autorizado>().HasOne(x => x.Cliente).WithMany(c => c.Autorizados).HasForeignKey(x => x.IdCliente);
        b.Entity<TarjetaCliente>().HasOne(x => x.Cliente).WithMany(c => c.Tarjetas).HasForeignKey(x => x.IdCliente);
        b.Entity<TarjetaCliente>().HasOne(x => x.TipoTarjeta).WithMany().HasForeignKey(x => x.IdTipoTarjeta);

        b.Entity<Sucursal>().HasOne(x => x.Empresa).WithMany(e => e.Sucursales).HasForeignKey(x => x.IdEmpresa);

        // Caja → PuestoCaja: FK "manual" hasta ahora (join a mano en CajaEstructuraService, sin
        // constraint en la base). Se formaliza acá para que el motor impida cajas apuntando a un
        // puesto borrado/inexistente. IdPuestoAsignado es nullable (hay cajas sin puesto — roles
        // Administrador/Tesorero usan el fallback 1/1 sin puesto real), así que la FK compuesta
        // queda automáticamente "no exigida" cuando es null (comportamiento estándar de SQL Server
        // para FKs multi-columna con algún componente NULL).
        b.Entity<Caja>().HasOne<PuestoCaja>().WithMany()
            .HasForeignKey(x => new { x.IdSucursal, x.IdPuestoAsignado });

        // Terminal → Caja: 1 caja a N terminales. La FK vive del lado de Terminal (la "N"), así que
        // "una terminal no puede repetirse en otra caja" es una garantía estructural del modelo, no
        // una regla que haya que validar aparte con un índice único.
        b.Entity<TerminalTarjeta>().HasOne<Caja>().WithMany()
            .HasForeignKey(x => new { x.IdSucursal, x.IdCajaAsignada });

        b.Entity<Precio>().HasOne(x => x.ListaPrecio).WithMany(l => l.Precios).HasForeignKey(x => x.IdListaPrecio);
        b.Entity<Precio>().HasOne(x => x.Presentacion).WithMany().HasForeignKey(x => x.IdPresentacion);
        b.Entity<Convenio>().HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.IdCliente);

        b.Entity<AlcanceOferta>().HasOne<CabeceraOferta>().WithMany(o => o.Alcances)
            .HasForeignKey(x => new { x.IdSucursal, x.IdOferta });
        b.Entity<AccionOferta>().HasOne<CabeceraOferta>().WithMany(o => o.Acciones)
            .HasForeignKey(x => new { x.IdSucursal, x.IdOferta });
        b.Entity<AccionOferta>().HasOne(x => x.TipoOferta).WithMany().HasForeignKey(x => x.IdTipoOferta);
        // Los items de canasta cuelgan de la acción (la convención global Restrict de más abajo
        // pisa cualquier cascada, así que OfertaAdminService los borra explícitamente).
        b.Entity<ItemOferta>().HasOne<AccionOferta>().WithMany(a => a.Items).HasForeignKey(x => x.IdAccion);

        b.Entity<MedioPago>().HasOne(x => x.TipoPago).WithMany(x => x.Medios).HasForeignKey(x => x.IdTipoPago);
        b.Entity<OfertaMedioPago>().HasOne<MedioPago>().WithMany().HasForeignKey(x => x.IdMedioPago);
        b.Entity<OfertaMedioPago>().HasOne<PlanCuota>().WithMany().HasForeignKey(x => x.IdPlanCuota);
        b.Entity<PlanCuota>().HasOne(x => x.MedioPago).WithMany(x => x.Planes).HasForeignKey(x => x.IdMedioPago);

        b.Entity<CabeceraComprobante>().HasOne(x => x.TipoComprobante).WithMany().HasForeignKey(x => x.IdTipoComprobante);
        b.Entity<DetalleComprobante>().HasOne(x => x.Comprobante).WithMany(c => c.Detalles)
            .HasForeignKey(x => new { x.IdSucursal, x.IdComprobante });

        b.Entity<Operacion>().HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.IdCliente);
        b.Entity<DetalleOperacion>().HasOne(x => x.Operacion).WithMany(o => o.Detalles)
            .HasForeignKey(x => new { x.IdSucursal, x.IdOperacion });
    }

    private static void ConfigureIndexes(ModelBuilder b)
    {
        b.Entity<Usuario>().HasIndex(x => x.NombreUsuario).IsUnique();
        // SQL Server no cuenta NULLs como duplicados en un índice único: los usuarios sin código
        // (la mayoría) conviven sin problema, y solo se exige unicidad entre los que sí tienen uno.
        b.Entity<Usuario>().HasIndex(x => x.CodigoSupervisor).IsUnique();
        b.Entity<Articulo>().HasIndex(x => x.CodigoInterno).IsUnique();
        b.Entity<Barra>().HasIndex(x => x.CodigoBarra).IsUnique();
        b.Entity<Cliente>().HasIndex(x => x.CodigoInt).IsUnique();
        b.Entity<Cliente>().HasIndex(x => x.Cuit);
        b.Entity<Cliente>().HasIndex(x => x.Documento);
        b.Entity<Empresa>().HasIndex(x => x.CodigoInterno).IsUnique();
        b.Entity<CierreZFiscal>().HasIndex(x => new { x.IdSucursal, x.IdCaja, x.FechaHoraUtc });
        b.Entity<CabeceraComprobante>().HasIndex(x => x.Cae);
        b.Entity<CuentaCorriente>().HasIndex(x => new { x.IdSucursal, x.IdCliente });
        b.Entity<Configuracion>().HasIndex(x => x.Clave).IsUnique();
        // Se consulta siempre filtrando por sucursal+medio+Activo (FacturacionService, al cobrar).
        b.Entity<OfertaMedioPago>().HasIndex(x => new { x.IdSucursal, x.IdMedioPago, x.Activo });

        // Respaldo a nivel BD de invariantes que hoy dependen de lógica de aplicación (lock de
        // aplicación en FacturacionService/CajaService): si algún día se agrega otra vía de
        // inserción que no pase por ahí, el motor sigue impidiendo el dato corrupto en vez de
        // aceptarlo silenciosamente.
        // Incluye el TIPO de comprobante: ante ARCA la identidad de un comprobante es
        // tipo + punto de venta + número, y cada tipo lleva su propia serie. Sin el tipo, la nota
        // de crédito Nº 1 de un punto de venta chocaba contra la factura Nº 1 del mismo punto.
        b.Entity<CabeceraComprobante>()
            .HasIndex(x => new { x.IdSucursal, x.IdTipoComprobante, x.NumeroCompleto }).IsUnique();
        // Filtrado: solo puede haber UN lote en estado Abierto (=1) por caja+cajero **y día** — sin
        // esto, dos aperturas simultáneas del mismo cajero en la misma caja podían dejar dos lotes
        // "Abierto" el mismo día. Es por (caja, cajero) y NO solo por caja: varios cajeros pueden
        // compartir la misma caja física a la vez, cada uno con su propio lote.
        //
        // El día va en la clave porque la regla de la app (LoteCajaReglas.PuedeAbrirNuevoLote y
        // CajaService.ObtenerLoteAbiertoHoyAsync) es "un lote abierto por caja+cajero POR DÍA", y
        // cierre/arqueo solo operan sobre el lote de hoy (decisión de FASE-5: un lote de ayer no se
        // toca). Sin el día acá, el índice enforceaba "un solo lote abierto de por vida": un lote de
        // un día anterior que quedó sin cerrar hacía fallar toda apertura futura de ese cajero en esa
        // caja con un choque de clave duplicada (500), sin forma de destrabarlo desde la app.
        b.Entity<LoteCaja>().Property<DateOnly>(DiaAperturaShadow)
            .HasComputedColumnSql("CONVERT(date, [FechaApertura])", stored: true);
        b.Entity<LoteCaja>()
            .HasIndex(nameof(LoteCaja.IdSucursal), nameof(LoteCaja.IdCaja), nameof(LoteCaja.IdUsuarioApertura), DiaAperturaShadow)
            .IsUnique()
            .HasFilter("[Estado] = 1")
            .HasDatabaseName("IX_LotesCaja_UnAbiertoPorCajaCajeroYDia");

        // Tarjetas del cliente: la identificación en Caja busca por número y el ABM lista las del
        // cliente; ambas consultas filtran por Activa (el cliente tiene UNA tarjeta vigente, las
        // anteriores quedan anuladas como historia).
        //
        // No hay índice único "una activa por cliente": el padrón importado ya viola la regla
        // (2.310 clientes con 2 tarjetas y la misma fecha de alta, no hay forma de deducir cuál es
        // la vigente). La invariante la aplica TarjetaAdminService.AddTarjetaAsync; para blindarla
        // acá primero hay que normalizar esos casos.
        b.Entity<TarjetaCliente>().HasIndex(x => new { x.NroTarjeta, x.Activa });
        b.Entity<TarjetaCliente>().HasIndex(x => new { x.IdCliente, x.Activa });

        // Refresh tokens: se busca por hash (nunca por Id), y se listan los activos de un usuario
        // al detectar reuso/logout.
        b.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        b.Entity<RefreshToken>().HasIndex(x => new { x.IdUsuario, x.RevocadoUtc });

        // Longitudes de strings frecuentes.
        b.Entity<Articulo>().Property(x => x.CodigoInterno).HasMaxLength(30);
        b.Entity<Barra>().Property(x => x.CodigoBarra).HasMaxLength(20);
        b.Entity<Usuario>().Property(x => x.NombreUsuario).HasMaxLength(50);
        b.Entity<Usuario>().Property(x => x.CodigoSupervisor).HasMaxLength(8);
        b.Entity<Cliente>().Property(x => x.Cuit).HasMaxLength(11);
        // Largos con margen sobre lo que trae el padrón a importar (domicilio 50, localidad 25,
        // email 40, CP 4): el CP se dimensiona a 8 para admitir el CPA argentino completo (B7600ABC),
        // no solo los 4 dígitos del código viejo.
        b.Entity<Cliente>().Property(x => x.Domicilio).HasMaxLength(120);
        b.Entity<Cliente>().Property(x => x.CodigoPostal).HasMaxLength(8);
        b.Entity<Cliente>().Property(x => x.Localidad).HasMaxLength(60);
        b.Entity<Cliente>().Property(x => x.Provincia).HasMaxLength(60);
        b.Entity<Cliente>().Property(x => x.NombreFantasia).HasMaxLength(60);
        b.Entity<Cliente>().Property(x => x.Email).HasMaxLength(120);
        // SHA-256 en hex = 64 caracteres fijos. Necesario acotarlo: un índice único no puede ir
        // sobre nvarchar(max) en SQL Server.
        // 45 = largo máximo de una IPv6 en texto.
        b.Entity<PuntoVenta>().Property(x => x.IpControlador).HasMaxLength(45);
        // 45 = largo máximo de una IPv6 en texto (mismo criterio que IpControlador arriba); acepta
        // igual un hostname/DNS si el servidor MySQL se referencia por nombre en vez de IP.
        b.Entity<ConexionExternaMySql>().Property(x => x.Host).HasMaxLength(45);
        b.Entity<TerminalTarjeta>().Property(x => x.NumeroTerminal).HasMaxLength(30);
        b.Entity<MovimientoPago>().Property(x => x.NumeroCupon).HasMaxLength(20);
        b.Entity<MovimientoPago>().Property(x => x.NumeroLote).HasMaxLength(20);
        b.Entity<MovimientoPago>().Property(x => x.NumeroCheque).HasMaxLength(8);
        b.Entity<MovimientoPago>().Property(x => x.ObservacionesCheque).HasMaxLength(250);
        // Explícito: "IdBanco" no lo detecta la convención de EF como FK de la navegación Banco acá
        // (a diferencia de Familia.IdSector) — sin esto, EF crea una FK "fantasma" aparte
        // (BancoIdBanco) y deja IdBanco como una columna suelta sin relación.
        b.Entity<MovimientoPago>().HasOne(x => x.Banco).WithMany()
            .HasForeignKey(x => x.IdBanco).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CorreccionCupon>().Property(x => x.NumeroCuponAnterior).HasMaxLength(20);
        b.Entity<CorreccionCupon>().Property(x => x.NumeroCuponNuevo).HasMaxLength(20);
        b.Entity<CorreccionCupon>().Property(x => x.NumeroLoteAnterior).HasMaxLength(20);
        b.Entity<CorreccionCupon>().Property(x => x.NumeroLoteNuevo).HasMaxLength(20);
        b.Entity<CorreccionCupon>().Property(x => x.Motivo).HasMaxLength(200);
        b.Entity<MovimientoCaja>().Property(x => x.Concepto).HasMaxLength(200);
        b.Entity<PlanCuota>().Property(x => x.Denominacion).HasMaxLength(60);
        b.Entity<RefreshToken>().Property(x => x.TokenHash).HasMaxLength(64);
    }

    public override int SaveChanges()
    {
        StampAudit();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAudit()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAtUtc = now;
            else if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAtUtc = now;
        }
    }
}
