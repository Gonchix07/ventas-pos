using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Abm;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Domain.Enums;
using Pos.Infrastructure.Persistence;
using Pos.Infrastructure.Storage;

namespace Pos.Infrastructure.Services;

public class PagoAdminService : IPagoAdminService
{
    private readonly PosDbContext _db;
    public PagoAdminService(PosDbContext db) => _db = db;

    private static string FuenteDesc(FuentePago f) => f switch
    {
        FuentePago.Efectivo => "Efectivo",
        FuentePago.Tarjeta => "Tarjeta",
        FuentePago.BilleteraVirtual => "Billetera virtual",
        FuentePago.Transferencia => "Transferencia",
        FuentePago.CuentaCorriente => "Cuenta corriente",
        _ => f.ToString()
    };

    private static string CanalDesc(CanalCobro c) => c switch
    {
        CanalCobro.Manual => "Manual",
        CanalCobro.ICard => "iCARD",
        _ => c.ToString()
    };

    public async Task<IReadOnlyList<TipoPagoDto>> GetTiposAsync(CancellationToken ct = default)
    {
        var tipos = await _db.TiposPago.AsNoTracking()
            .Select(t => new { t.IdTipoPago, t.Descripcion, t.Fuente, t.Canal, Medios = t.Medios.Count })
            .OrderBy(t => t.Descripcion).ToListAsync(ct);
        return tipos.Select(t => new TipoPagoDto(t.IdTipoPago, t.Descripcion, (int)t.Fuente, FuenteDesc(t.Fuente),
            (int)t.Canal, CanalDesc(t.Canal), t.Medios)).ToList();
    }

    public async Task<int> CreateTipoAsync(TipoPagoInput input, CancellationToken ct = default)
    {
        var desc = (input.Descripcion ?? "").Trim();
        if (desc.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "El tipo de pago necesita un nombre.");
        if (await _db.TiposPago.AnyAsync(t => t.Descripcion == desc, ct))
            throw new DomainException("TIPO_DUPLICADO", $"Ya existe un tipo de pago llamado «{desc}».");

        var t = new TipoPago
        {
            Descripcion = desc,
            Fuente = Enum.IsDefined(typeof(FuentePago), input.Fuente) ? (FuentePago)input.Fuente : FuentePago.Efectivo,
            Canal = Enum.IsDefined(typeof(CanalCobro), input.Canal) ? (CanalCobro)input.Canal : CanalCobro.Manual
        };
        _db.TiposPago.Add(t);
        await _db.SaveChangesAsync(ct);
        return t.IdTipoPago;
    }

    public async Task<bool> UpdateTipoAsync(int id, TipoPagoInput input, CancellationToken ct = default)
    {
        var t = await _db.TiposPago.FirstOrDefaultAsync(x => x.IdTipoPago == id, ct);
        if (t is null) return false;
        var desc = (input.Descripcion ?? "").Trim();
        if (desc.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "El tipo de pago necesita un nombre.");
        if (await _db.TiposPago.AnyAsync(x => x.Descripcion == desc && x.IdTipoPago != id, ct))
            throw new DomainException("TIPO_DUPLICADO", $"Ya existe otro tipo de pago llamado «{desc}».");

        t.Descripcion = desc;
        t.Fuente = Enum.IsDefined(typeof(FuentePago), input.Fuente) ? (FuentePago)input.Fuente : t.Fuente;
        t.Canal = Enum.IsDefined(typeof(CanalCobro), input.Canal) ? (CanalCobro)input.Canal : t.Canal;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteTipoAsync(int id, CancellationToken ct = default)
    {
        if (await _db.MediosPago.AnyAsync(m => m.IdTipoPago == id, ct))
            throw new DomainException("EN_USO", "No se puede eliminar: tiene medios de pago asociados.");
        var t = await _db.TiposPago.FirstOrDefaultAsync(x => x.IdTipoPago == id, ct);
        if (t is null) return false;
        _db.TiposPago.Remove(t);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MedioPagoDto>> GetMediosAsync(CancellationToken ct = default)
    {
        // El canal se muestra heredado del tipo: es donde se configura, pero el operador necesita
        // verlo acá para saber por dónde va a salir el cobro de cada medio.
        var filas = await (
            from m in _db.MediosPago.AsNoTracking()
            join t in _db.TiposPago.AsNoTracking() on m.IdTipoPago equals t.IdTipoPago into tj
            from t in tj.DefaultIfEmpty()
            orderby m.Descripcion
            join c in _db.Clusters.AsNoTracking() on m.IdCluster equals c.IdCluster into cj
            from c in cj.DefaultIfEmpty()
            select new { m.IdMedioPago, m.Descripcion, m.IdTipoPago, Tipo = t, m.EsPredeterminado, m.Activo,
                m.ImprimeComprobante, m.IdCluster, ClusterDescripcion = c != null ? c.Descripcion : null }
        ).ToListAsync(ct);

        return filas.Select(x => new MedioPagoDto(x.IdMedioPago, x.Descripcion, x.IdTipoPago,
            x.Tipo?.Descripcion, x.Tipo is null ? 0 : (int)x.Tipo.Canal,
            x.Tipo is null ? null : CanalDesc(x.Tipo.Canal), x.EsPredeterminado, x.Activo,
            x.ImprimeComprobante, x.IdCluster, x.ClusterDescripcion)).ToList();
    }

    public async Task<int> CreateMedioAsync(MedioPagoInput input, CancellationToken ct = default)
    {
        var desc = (input.Descripcion ?? "").Trim();
        if (desc.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "El medio de pago necesita un nombre.");
        if (!await _db.TiposPago.AnyAsync(t => t.IdTipoPago == input.IdTipoPago, ct))
            throw new DomainException("TIPO_INEXISTENTE", "Hay que elegir un tipo de pago válido.");

        await ValidarClusterAsync(input.IdCluster, ct);

        var m = new MedioPago
        {
            Descripcion = desc, IdTipoPago = input.IdTipoPago,
            EsPredeterminado = input.EsPredeterminado, Activo = input.Activo,
            ImprimeComprobante = input.ImprimeComprobante, IdCluster = input.IdCluster
        };
        _db.MediosPago.Add(m);
        if (input.EsPredeterminado) await DestildarPredeterminadosAsync(null, ct);
        await _db.SaveChangesAsync(ct);
        await AsegurarPlanPorDefectoAsync(m.IdMedioPago, ct);
        return m.IdMedioPago;
    }

    public async Task<bool> UpdateMedioAsync(int id, MedioPagoInput input, CancellationToken ct = default)
    {
        var m = await _db.MediosPago.FirstOrDefaultAsync(x => x.IdMedioPago == id, ct);
        if (m is null) return false;
        var desc = (input.Descripcion ?? "").Trim();
        if (desc.Length == 0)
            throw new DomainException("DESCRIPCION_REQUERIDA", "El medio de pago necesita un nombre.");
        if (!await _db.TiposPago.AnyAsync(t => t.IdTipoPago == input.IdTipoPago, ct))
            throw new DomainException("TIPO_INEXISTENTE", "Hay que elegir un tipo de pago válido.");

        await ValidarClusterAsync(input.IdCluster, ct);

        m.Descripcion = desc;
        m.IdTipoPago = input.IdTipoPago;
        m.Activo = input.Activo;
        m.ImprimeComprobante = input.ImprimeComprobante;
        m.IdCluster = input.IdCluster;
        if (input.EsPredeterminado && !m.EsPredeterminado) await DestildarPredeterminadosAsync(id, ct);
        m.EsPredeterminado = input.EsPredeterminado;
        await _db.SaveChangesAsync(ct);
        // Puede haber pasado a ser Tarjeta recién ahora (cambio de tipo): se cubre acá también, no
        // solo en el alta.
        await AsegurarPlanPorDefectoAsync(id, ct);
        return true;
    }

    private async Task ValidarClusterAsync(int? idCluster, CancellationToken ct)
    {
        if (idCluster is not int id) return;
        if (!await _db.Clusters.AnyAsync(c => c.IdCluster == id, ct))
            throw new DomainException("CLUSTER_INEXISTENTE", "El cluster indicado no existe.");
    }

    /// <summary>
    /// El predeterminado es UNO solo: al marcar uno nuevo se destilda el anterior en vez de
    /// devolver un error (elegir el default es una acción, no una validación que el usuario tenga
    /// que resolver a mano).
    /// </summary>
    private async Task DestildarPredeterminadosAsync(int? excepto, CancellationToken ct)
    {
        var otros = await _db.MediosPago
            .Where(x => x.EsPredeterminado && (excepto == null || x.IdMedioPago != excepto))
            .ToListAsync(ct);
        foreach (var o in otros) o.EsPredeterminado = false;
    }

    public async Task<bool> DeleteMedioAsync(int id, CancellationToken ct = default)
    {
        var m = await _db.MediosPago.FirstOrDefaultAsync(x => x.IdMedioPago == id, ct);
        if (m is null) return false;
        // Un medio ya usado en una venta no se puede borrar: los movimientos de caja lo referencian
        // y el cierre Z acumula por medio. Se da de baja en su lugar.
        if (await _db.MovimientosPagos.AnyAsync(mp => mp.IdMedioPago == id, ct))
            throw new DomainException("EN_USO",
                "No se puede eliminar: ya se registraron pagos con este medio. Desactivalo en su lugar.");
        // FK Restrict contra PlanesCuota (ver migración PlanesCuotaMedioPago): sin este chequeo, un
        // medio con planes tira una violación de FK cruda en vez de un mensaje de negocio.
        if (await _db.PlanesCuota.AnyAsync(p => p.IdMedioPago == id, ct))
            throw new DomainException("EN_USO",
                "No se puede eliminar: tiene planes de cuotas cargados. Borralos primero.");
        _db.MediosPago.Remove(m);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Planes de cuotas (solo medios de tipo Tarjeta) ----

    public async Task<IReadOnlyList<PlanCuotaDto>> GetPlanesAsync(int idMedioPago, CancellationToken ct = default) =>
        await _db.PlanesCuota.AsNoTracking().Where(p => p.IdMedioPago == idMedioPago)
            .OrderBy(p => p.CantidadCuotas)
            .Select(p => new PlanCuotaDto(p.IdPlan, p.IdMedioPago, p.Denominacion, p.CantidadCuotas))
            .ToListAsync(ct);

    private async Task<MedioPago> ExigirMedioTarjetaAsync(int idMedioPago, CancellationToken ct)
    {
        var medio = await _db.MediosPago.Include(m => m.TipoPago)
            .FirstOrDefaultAsync(m => m.IdMedioPago == idMedioPago, ct)
            ?? throw new DomainException("MEDIO_INEXISTENTE", "El medio de pago indicado no existe.");
        if (medio.TipoPago?.Fuente != FuentePago.Tarjeta)
            throw new DomainException("MEDIO_NO_ES_TARJETA", "Los planes de cuotas son solo para medios de tipo Tarjeta.");
        return medio;
    }

    public async Task<int> CreatePlanAsync(int idMedioPago, PlanCuotaInput input, CancellationToken ct = default)
    {
        await ExigirMedioTarjetaAsync(idMedioPago, ct);
        var denominacion = (input.Denominacion ?? "").Trim();
        if (denominacion.Length == 0)
            throw new DomainException("DENOMINACION_REQUERIDA", "El plan necesita una denominación.");
        if (input.CantidadCuotas <= 0)
            throw new DomainException("CUOTAS_INVALIDAS", "La cantidad de cuotas debe ser mayor a cero.");
        if (await _db.PlanesCuota.AnyAsync(p => p.IdMedioPago == idMedioPago && p.Denominacion == denominacion, ct))
            throw new DomainException("PLAN_DUPLICADO", $"Ya existe un plan «{denominacion}» para este medio.");

        var p2 = new PlanCuota { IdMedioPago = idMedioPago, Denominacion = denominacion, CantidadCuotas = input.CantidadCuotas };
        _db.PlanesCuota.Add(p2);
        await _db.SaveChangesAsync(ct);
        return p2.IdPlan;
    }

    public async Task<bool> UpdatePlanAsync(int idPlan, PlanCuotaInput input, CancellationToken ct = default)
    {
        var p = await _db.PlanesCuota.FirstOrDefaultAsync(x => x.IdPlan == idPlan, ct);
        if (p is null) return false;
        var denominacion = (input.Denominacion ?? "").Trim();
        if (denominacion.Length == 0)
            throw new DomainException("DENOMINACION_REQUERIDA", "El plan necesita una denominación.");
        if (input.CantidadCuotas <= 0)
            throw new DomainException("CUOTAS_INVALIDAS", "La cantidad de cuotas debe ser mayor a cero.");
        if (await _db.PlanesCuota.AnyAsync(x => x.IdMedioPago == p.IdMedioPago && x.Denominacion == denominacion && x.IdPlan != idPlan, ct))
            throw new DomainException("PLAN_DUPLICADO", $"Ya existe otro plan «{denominacion}» para este medio.");

        p.Denominacion = denominacion;
        p.CantidadCuotas = input.CantidadCuotas;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeletePlanAsync(int idPlan, CancellationToken ct = default)
    {
        var p = await _db.PlanesCuota.FirstOrDefaultAsync(x => x.IdPlan == idPlan, ct);
        if (p is null) return false;
        // Elegir un plan es obligatorio al cobrar con Tarjeta (ver FacturacionService): un medio
        // Tarjeta nunca puede quedar en cero planes, o el cajero se queda sin poder cobrar con él.
        if (await _db.PlanesCuota.CountAsync(x => x.IdMedioPago == p.IdMedioPago, ct) <= 1)
            throw new DomainException("ULTIMO_PLAN",
                "No se puede eliminar: es el único plan del medio y elegir uno es obligatorio al cobrar.");
        // El plan queda copiado (denominación + cantidad de cuotas) en cada MovimientoPago que lo
        // usó — ver MovimientoPago.CantidadCuotas —, así que borrarlo no rompe el historial. Por
        // eso IdPlanCuota en MovimientoPago no tiene FK real: es solo una referencia, no una
        // dependencia que deba bloquear el borrado.
        _db.PlanesCuota.Remove(p);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Cada medio Tarjeta necesita al menos un plan de cuotas (elegir uno es obligatorio al cobrar,
    /// ver FacturacionService). Se llama tras crear/editar un medio: si es Tarjeta y todavía no
    /// tiene ninguno, se le da de alta el default "1 cuota" — así el cajero nunca se encuentra un
    /// medio Tarjeta sin nada para elegir.
    /// </summary>
    private async Task AsegurarPlanPorDefectoAsync(int idMedioPago, CancellationToken ct)
    {
        var medio = await _db.MediosPago.Include(m => m.TipoPago)
            .FirstOrDefaultAsync(m => m.IdMedioPago == idMedioPago, ct);
        if (medio?.TipoPago?.Fuente != FuentePago.Tarjeta) return;
        if (await _db.PlanesCuota.AnyAsync(p => p.IdMedioPago == idMedioPago, ct)) return;

        _db.PlanesCuota.Add(new PlanCuota { IdMedioPago = idMedioPago, Denominacion = "1 cuota", CantidadCuotas = 1 });
        await _db.SaveChangesAsync(ct);
    }
}

public class EstructuraService : IEstructuraService
{
    // Purpose fijo de Data Protection: si cambia entre versiones, todo lo cifrado con el purpose
    // viejo deja de poder descifrarse. No tocar sin migrar los certificados ya subidos.
    private const string DataProtectionPurpose = "Pos.CertificadoCae";

    private readonly PosDbContext _db;
    private readonly StorageOptions _storage;
    private readonly IDataProtector _protector;
    public EstructuraService(PosDbContext db, StorageOptions storage, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _storage = storage;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
    }

    private string RutaCertificado(int idEmpresa) => Path.Combine(_storage.CertificadosPath, $"empresa-{idEmpresa}.pfx");

    public async Task<IReadOnlyList<EmpresaDto>> GetEmpresasAsync(CancellationToken ct = default) =>
        (await _db.Empresas.AsNoTracking().OrderBy(e => e.Descripcion).ToListAsync(ct))
            .Select(e => new EmpresaDto(e.IdEmpresa, e.CodigoInterno, e.Descripcion, e.Cuit, e.CertificadoAlias,
                e.CondicionIva, e.IngresosBrutos, e.InicioActividad,
                e.Domicilio, e.Localidad, e.Provincia, e.CodigoPostal)).ToList();

    public async Task<int> CreateEmpresaAsync(EmpresaInput input, CancellationToken ct = default)
    {
        if (await _db.Empresas.AnyAsync(e => e.CodigoInterno == input.CodigoInterno, ct))
            throw new DomainException("CODIGO_DUPLICADO", $"Ya existe una empresa con código {input.CodigoInterno}.");
        var e = new Empresa
        {
            CodigoInterno = input.CodigoInterno.Trim(),
            Descripcion = input.Descripcion.Trim(),
            Cuit = string.IsNullOrWhiteSpace(input.Cuit) ? null : input.Cuit.Trim(),
            CertificadoAlias = string.IsNullOrWhiteSpace(input.CertificadoAlias) ? null : input.CertificadoAlias.Trim()
        };
        AplicarDatosFiscales(e, input);
        _db.Empresas.Add(e);
        await _db.SaveChangesAsync(ct);
        return e.IdEmpresa;
    }

    public async Task<bool> UpdateEmpresaAsync(int id, EmpresaInput input, CancellationToken ct = default)
    {
        var e = await _db.Empresas.FirstOrDefaultAsync(x => x.IdEmpresa == id, ct);
        if (e is null) return false;
        e.CodigoInterno = input.CodigoInterno.Trim();
        e.Descripcion = input.Descripcion.Trim();
        e.Cuit = string.IsNullOrWhiteSpace(input.Cuit) ? null : input.Cuit.Trim();
        e.CertificadoAlias = string.IsNullOrWhiteSpace(input.CertificadoAlias) ? null : input.CertificadoAlias.Trim();
        AplicarDatosFiscales(e, input);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteEmpresaAsync(int id, CancellationToken ct = default)
    {
        if (await _db.Sucursales.AnyAsync(s => s.IdEmpresa == id, ct))
            throw new DomainException("EN_USO", "No se puede eliminar: tiene sucursales asociadas.");
        var e = await _db.Empresas.FirstOrDefaultAsync(x => x.IdEmpresa == id, ct);
        if (e is null) return false;
        _db.Empresas.Remove(e);
        await _db.SaveChangesAsync(ct);
        if (File.Exists(RutaCertificado(id))) File.Delete(RutaCertificado(id));
        return true;
    }

    public async Task<CertificadoCaeDto> GetCertificadoAsync(int idEmpresa, CancellationToken ct = default)
    {
        var e = await _db.Empresas.AsNoTracking().FirstOrDefaultAsync(x => x.IdEmpresa == idEmpresa, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "No existe la empresa.");
        return new CertificadoCaeDto(e.CertificadoSubidoUtc.HasValue, e.CertificadoNombreArchivo,
            e.CertificadoVencimiento, e.CertificadoSubidoUtc);
    }

    public async Task<CertificadoCaeDto> SubirCertificadoAsync(int idEmpresa, byte[] contenidoPfx, string nombreArchivo,
        string clave, CancellationToken ct = default)
    {
        var e = await _db.Empresas.FirstOrDefaultAsync(x => x.IdEmpresa == idEmpresa, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "No existe la empresa.");

        // Se valida abriendo el .pfx con la contraseña dada: si no es un certificado válido o la
        // clave es incorrecta, falla acá y no llega a pisar nada en disco ni en la base.
        // EphemeralKeySet: la clave privada no se persiste en el store de usuario del proceso,
        // solo se usa en memoria para leer el vencimiento.
        DateTime vencimiento;
        try
        {
            using var cert = new X509Certificate2(contenidoPfx, clave, X509KeyStorageFlags.EphemeralKeySet);
            vencimiento = cert.NotAfter.ToUniversalTime();
        }
        catch (CryptographicException)
        {
            throw new DomainException("CERTIFICADO_INVALIDO",
                "El archivo no es un certificado .pfx/.p12 válido o la contraseña es incorrecta.");
        }

        return await GuardarCertificadoAsync(e, contenidoPfx, nombreArchivo, clave, vencimiento, ct);
    }

    public async Task<CertificadoCaeDto> SubirCertificadoDesdeClaveYCertAsync(int idEmpresa, byte[] clavePrivadaPem,
        byte[] certificadoBytes, string? passphraseClavePrivada, CancellationToken ct = default)
    {
        var e = await _db.Empresas.FirstOrDefaultAsync(x => x.IdEmpresa == idEmpresa, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "No existe la empresa.");

        // Se re-empaqueta como .pfx con una contraseña generada acá mismo (no la elige quien sube el
        // archivo): de acá en más se guarda y se abre exactamente igual que el flujo de subir un
        // .pfx ya armado, sin duplicar ese camino.
        byte[] pfxBytes;
        string claveGenerada;
        DateTime vencimiento;
        try
        {
            // El .crt/.cer que entrega ARCA es solo el certificado público (acepta PEM o DER); la
            // clave privada la generó quien tramitó el certificado y viaja aparte, en PEM, a veces
            // con passphrase propia (no relacionada con la contraseña del .pfx que se termina
            // generando acá). CopyWithPrivateKey ya valida que la clave corresponda al certificado.
            using var cert = new X509Certificate2(certificadoBytes);
            var keyPem = Encoding.UTF8.GetString(clavePrivadaPem);
            using var rsa = RSA.Create();
            if (string.IsNullOrEmpty(passphraseClavePrivada)) rsa.ImportFromPem(keyPem);
            else rsa.ImportFromEncryptedPem(keyPem, passphraseClavePrivada);
            using var certConClave = cert.CopyWithPrivateKey(rsa);
            claveGenerada = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            pfxBytes = certConClave.Export(X509ContentType.Pfx, claveGenerada);
            vencimiento = certConClave.NotAfter.ToUniversalTime();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            throw new DomainException("CERTIFICADO_INVALIDO",
                "El certificado y la clave privada no son válidos, no coinciden entre sí, o falta/es incorrecta la passphrase de la clave.");
        }

        return await GuardarCertificadoAsync(e, pfxBytes, "certificado.pfx (armado desde clave + certificado)",
            claveGenerada, vencimiento, ct);
    }

    private async Task<CertificadoCaeDto> GuardarCertificadoAsync(Empresa e, byte[] pfxBytes, string nombreArchivo,
        string clave, DateTime vencimientoUtc, CancellationToken ct)
    {
        Directory.CreateDirectory(_storage.CertificadosPath);
        await File.WriteAllBytesAsync(RutaCertificado(e.IdEmpresa), pfxBytes, ct);

        e.CertificadoNombreArchivo = nombreArchivo;
        e.CertificadoPasswordProtegida = _protector.Protect(clave);
        e.CertificadoVencimiento = vencimientoUtc;
        e.CertificadoSubidoUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new CertificadoCaeDto(true, e.CertificadoNombreArchivo, e.CertificadoVencimiento, e.CertificadoSubidoUtc);
    }

    public async Task<bool> EliminarCertificadoAsync(int idEmpresa, CancellationToken ct = default)
    {
        var e = await _db.Empresas.FirstOrDefaultAsync(x => x.IdEmpresa == idEmpresa, ct);
        if (e is null) return false;
        if (File.Exists(RutaCertificado(idEmpresa))) File.Delete(RutaCertificado(idEmpresa));
        e.CertificadoNombreArchivo = null;
        e.CertificadoPasswordProtegida = null;
        e.CertificadoVencimiento = null;
        e.CertificadoSubidoUtc = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<SucursalDto>> GetSucursalesAsync(CancellationToken ct = default)
    {
        var query =
            from s in _db.Sucursales.AsNoTracking()
            join e in _db.Empresas.AsNoTracking() on s.IdEmpresa equals e.IdEmpresa into ej
            from e in ej.DefaultIfEmpty()
            orderby s.Descripcion
            select new SucursalDto(s.IdSucursal, s.IdEmpresa, e != null ? e.Descripcion : null, s.Descripcion,
                s.Domicilio, s.Localidad, s.Provincia, s.CodigoPostal);
        return await query.ToListAsync(ct);
    }

    public async Task<int> CreateSucursalAsync(SucursalInput input, CancellationToken ct = default)
    {
        var s = new Sucursal { IdEmpresa = input.IdEmpresa, Descripcion = input.Descripcion.Trim() };
        AplicarDomicilio(s, input);
        _db.Sucursales.Add(s);
        await _db.SaveChangesAsync(ct);
        return s.IdSucursal;
    }

    public async Task<bool> UpdateSucursalAsync(int id, SucursalInput input, CancellationToken ct = default)
    {
        var s = await _db.Sucursales.FirstOrDefaultAsync(x => x.IdSucursal == id, ct);
        if (s is null) return false;
        s.IdEmpresa = input.IdEmpresa;
        s.Descripcion = input.Descripcion.Trim();
        AplicarDomicilio(s, input);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static void AplicarDatosFiscales(Empresa e, EmpresaInput input)
    {
        e.CondicionIva = Limpiar(input.CondicionIva);
        e.IngresosBrutos = Limpiar(input.IngresosBrutos);
        e.InicioActividad = input.InicioActividad;
        e.Domicilio = Limpiar(input.Domicilio);
        e.Localidad = Limpiar(input.Localidad);
        e.Provincia = Limpiar(input.Provincia);
        e.CodigoPostal = Limpiar(input.CodigoPostal);
    }

    private static void AplicarDomicilio(Sucursal s, SucursalInput input)
    {
        s.Domicilio = Limpiar(input.Domicilio);
        s.Localidad = Limpiar(input.Localidad);
        s.Provincia = Limpiar(input.Provincia);
        s.CodigoPostal = Limpiar(input.CodigoPostal);
    }

    public async Task<bool> DeleteSucursalAsync(int id, CancellationToken ct = default)
    {
        var s = await _db.Sucursales.FirstOrDefaultAsync(x => x.IdSucursal == id, ct);
        if (s is null) return false;
        _db.Sucursales.Remove(s);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class ConfiguracionAdminService : IConfiguracionAdminService
{
    private readonly PosDbContext _db;
    public ConfiguracionAdminService(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<ConfiguracionDto>> GetAllAsync(CancellationToken ct = default) =>
        (await _db.Configuraciones.AsNoTracking().OrderBy(c => c.Clave).ToListAsync(ct))
            .Select(c => new ConfiguracionDto(c.IdConfiguracion, c.Clave, c.Descripcion, c.Valor)).ToList();

    public async Task<int> CreateAsync(ConfiguracionInput input, CancellationToken ct = default)
    {
        if (await _db.Configuraciones.AnyAsync(c => c.Clave == input.Clave, ct))
            throw new DomainException("CLAVE_DUPLICADA", $"Ya existe la configuración {input.Clave}.");
        var c = new Configuracion { Clave = input.Clave.Trim(), Descripcion = input.Descripcion.Trim(), Valor = input.Valor };
        _db.Configuraciones.Add(c);
        await _db.SaveChangesAsync(ct);
        return c.IdConfiguracion;
    }

    public async Task<bool> UpdateAsync(int id, ConfiguracionInput input, CancellationToken ct = default)
    {
        var c = await _db.Configuraciones.FirstOrDefaultAsync(x => x.IdConfiguracion == id, ct);
        if (c is null) return false;
        c.Clave = input.Clave.Trim();
        c.Descripcion = input.Descripcion.Trim();
        c.Valor = input.Valor;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.Configuraciones.FirstOrDefaultAsync(x => x.IdConfiguracion == id, ct);
        if (c is null) return false;
        _db.Configuraciones.Remove(c);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// ABM de la conexión MySQL externa. Es singleton (una sola fila, sin alta/baja): GetAsync/UpdateAsync
/// leen/escriben siempre la primera (y única) fila, creándola si todavía no existe.
/// </summary>
public class ConexionExternaAdminService : IConexionExternaAdminService
{
    // Purpose propio y fijo, igual criterio que DataProtectionPurpose en EstructuraService: cambiarlo
    // invalida cualquier contraseña ya cifrada con el valor anterior.
    private const string DataProtectionPurpose = "Pos.ConexionExternaMySql";

    private readonly PosDbContext _db;
    private readonly IDataProtector _protector;
    public ConexionExternaAdminService(PosDbContext db, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
    }

    public async Task<ConexionExternaMySqlDto> GetAsync(CancellationToken ct = default)
    {
        var c = await _db.ConexionesExternasMySql.AsNoTracking().FirstOrDefaultAsync(ct);
        if (c is null) return new ConexionExternaMySqlDto("", 3306, "", "", false, false);
        return new ConexionExternaMySqlDto(c.Host, c.Puerto, c.BaseDatos, c.Usuario,
            !string.IsNullOrEmpty(c.PasswordProtegida), c.Habilitada);
    }

    public async Task UpdateAsync(ConexionExternaMySqlInput input, CancellationToken ct = default)
    {
        var c = await _db.ConexionesExternasMySql.FirstOrDefaultAsync(ct);
        if (c is null)
        {
            c = new ConexionExternaMySql();
            _db.ConexionesExternasMySql.Add(c);
        }
        c.Host = input.Host.Trim();
        c.Puerto = input.Puerto;
        c.BaseDatos = input.BaseDatos.Trim();
        c.Usuario = input.Usuario.Trim();
        // Vacío/null = conservar la contraseña ya guardada, para no obligar a retipearla cada vez
        // que se edita el host/usuario/base.
        if (!string.IsNullOrEmpty(input.Password))
            c.PasswordProtegida = _protector.Protect(input.Password);
        c.Habilitada = input.Habilitada;
        await _db.SaveChangesAsync(ct);
    }
}
