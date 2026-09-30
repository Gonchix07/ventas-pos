using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Application.Clientes;
using Pos.Application.Common;
using Pos.Domain.Entities;
using Pos.Infrastructure.Adapters.Dbf;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Compara clientes.dbf (app legacy VFP "Mayorista", misma carpeta que pedidos.dbf/operator.dbf —
/// ver PreventaMayorista:CarpetaDbf en Configuraciones) contra la tabla Clientes por CodigoInt, e
/// importa SOLO los que todavía no existen (nunca actualiza ni borra un cliente ya cargado). La
/// tarjeta vigente de cada cliente nuevo sale de codtarje.dbf (TIPOTARJE '03'=Roja/'04'=Azul,
/// confirmado por el usuario; INHABILITA=0 = vigente).
/// </summary>
public class ClienteDbfImportService : IClienteDbfImportService
{
    private const string ClaveCarpetaDbf = "PreventaMayorista:CarpetaDbf";
    private const string CarpetaDbfPorDefecto = @"S:\appvfp\Mayorista\Mayorista_Release\Datos";

    // CodigoErp de CondicionIva == código CONDIVA de clientes.dbf: confirmado (2026-09-30) contra
    // CONDIVAS.DBF de la misma carpeta legacy — 1=Consumidor Final, 2=Responsable Inscripto,
    // 3=Responsable No Inscripto, 4=Exento, 5=Monotributo, 6=Sujeto No Categorizado. El único valor
    // sin contraparte hoy es 7 ("Resp. Inscrip. M"), y cualquier código en blanco/desconocido cae en
    // el default (Consumidor Final).
    private const string CondIvaPorDefectoCodigoErp = "1";

    private static readonly IReadOnlySet<string> CamposClientesDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "CODIGO", "NOMBRE", "NOMFANT", "CUIT", "NUMDOC", "CONDIVA", "EMITECARGA", "INACTIVO", "BAJA",
          "SUSPENDIDO", "DOMICILIO", "COPOSTAL", "LOCALIDAD", "PROVINCIA_", "EMAIL" };

    private static readonly IReadOnlySet<string> CamposCodTarjeDbf = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "CLIENTE", "CODIGO", "TIPOTARJE", "INHABILITA", "FECHA" };

    private readonly PosDbContext _db;
    private readonly ILogger<ClienteDbfImportService> _log;

    public ClienteDbfImportService(PosDbContext db, ILogger<ClienteDbfImportService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<ClienteNuevoDto>> ObtenerNuevosAsync(CancellationToken ct = default)
    {
        var candidatos = await LeerCandidatosNuevosAsync(ct);
        return candidatos.Select(c => new ClienteNuevoDto(
            c.CodigoInt, c.Descripcion, c.NombreFantasia, c.Cuit, c.Documento,
            c.CondIvaDescripcion, c.PermitePresupuesto, c.Localidad, c.Provincia,
            c.NroTarjeta, c.TipoTarjetaDescripcion)).ToList();
    }

    public async Task<int> ImportarNuevosAsync(CancellationToken ct = default)
    {
        var candidatos = await LeerCandidatosNuevosAsync(ct);
        // A pedido del usuario: el comparador muestra TODOS los detectados, pero solo se importan
        // los que tienen tarjeta vigente asignada (los demás quedan en el comparador, sin crear).
        var conTarjeta = candidatos.Where(c => c.IdTipoTarjeta is not null).ToList();
        if (conTarjeta.Count == 0) return 0;

        var ahora = DateTime.UtcNow;
        var nuevos = conTarjeta.Select(c =>
        {
            var cliente = new Cliente
            {
                CodigoInt = c.CodigoInt,
                Cuit = c.Cuit,
                Documento = c.Documento,
                Descripcion = c.Descripcion,
                NombreFantasia = c.NombreFantasia,
                IdCondIva = c.IdCondIva,
                PermitePresupuesto = c.PermitePresupuesto,
                AdmiteCuentaCorriente = false,
                Activo = c.Activo,
                Domicilio = c.Domicilio,
                CodigoPostal = c.CodigoPostal,
                Localidad = c.Localidad,
                Provincia = c.Provincia,
                Email = c.Email,
                UltimaSincronizacionErpUtc = ahora
            };
            if (c.IdTipoTarjeta is int idTipoTarjeta)
                cliente.Tarjetas.Add(new TarjetaCliente
                {
                    IdTipoTarjeta = idTipoTarjeta, NroTarjeta = c.NroTarjeta!, Activa = true
                });
            return cliente;
        }).ToList();

        await _db.Clientes.AddRangeAsync(nuevos, ct);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Clientes: importados {Cantidad} nuevos desde clientes.dbf.", nuevos.Count);
        return nuevos.Count;
    }

    private sealed record CandidatoCliente(
        string CodigoInt, string Descripcion, string? NombreFantasia, string? Cuit, string? Documento,
        int IdCondIva, string CondIvaDescripcion, bool PermitePresupuesto, bool Activo,
        string? Domicilio, string? CodigoPostal, string? Localidad, string? Provincia, string? Email,
        string? NroTarjeta, int? IdTipoTarjeta, string? TipoTarjetaDescripcion);

    private async Task<List<CandidatoCliente>> LeerCandidatosNuevosAsync(CancellationToken ct)
    {
        var carpeta = await ObtenerCarpetaDbfAsync(ct);

        var existentes = await _db.Clientes.AsNoTracking().Select(c => c.CodigoInt).ToListAsync(ct);
        var existentesSet = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);

        var filas = LeerClientesDbf(Path.Combine(carpeta, "clientes.dbf"), existentesSet);
        if (filas.Count == 0) return new List<CandidatoCliente>();

        var condicionesPorErp = await _db.CondicionesIva.AsNoTracking()
            .Where(c => c.CodigoErp != null)
            .ToDictionaryAsync(c => c.CodigoErp!, c => c.IdCondIva, ct);
        var idCondIvaPorDefecto = condicionesPorErp.GetValueOrDefault(CondIvaPorDefectoCodigoErp);
        var descripcionPorId = await _db.CondicionesIva.AsNoTracking()
            .ToDictionaryAsync(c => c.IdCondIva, c => c.Descripcion, ct);

        var tiposTarjeta = await _db.TiposTarjeta.AsNoTracking().ToListAsync(ct);
        var idRoja = tiposTarjeta.FirstOrDefault(t => t.Descripcion.Contains("ROJA", StringComparison.OrdinalIgnoreCase))?.IdTipoTarjeta;
        var idAzul = tiposTarjeta.FirstOrDefault(t => t.Descripcion.Contains("AZUL", StringComparison.OrdinalIgnoreCase))?.IdTipoTarjeta;

        var codigosNuevos = filas.Select(f => f.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tarjetas = LeerTarjetasVigentes(Path.Combine(carpeta, "codtarje.dbf"), codigosNuevos);

        var resultado = new List<CandidatoCliente>();
        foreach (var f in filas)
        {
            var idCondIva = f.CondIva is not null && condicionesPorErp.TryGetValue(f.CondIva, out var id)
                ? id : idCondIvaPorDefecto;
            if (idCondIva == 0) continue; // no debería pasar (CF siempre está seedeada), pero sin lista válida no se crea el cliente.

            tarjetas.TryGetValue(f.Codigo, out var tarjeta);
            var idTipoTarjeta = tarjeta?.Tipo switch { "03" => idRoja, "04" => idAzul, _ => null };

            resultado.Add(new CandidatoCliente(
                f.Codigo, f.Nombre, f.NombreFantasia, f.Cuit, f.NumDoc,
                idCondIva, descripcionPorId.GetValueOrDefault(idCondIva, "Consumidor Final"),
                f.EmiteCarga == 1, !(f.Inactivo == 1 || f.Baja == 1 || f.Suspendido == 1),
                f.Domicilio, f.CodigoPostal, f.Localidad, f.Provincia, f.Email,
                tarjeta?.Codigo, idTipoTarjeta,
                idTipoTarjeta == idRoja ? "Roja" : idTipoTarjeta == idAzul ? "Azul" : null));
        }
        return resultado;
    }

    private sealed record FilaClienteDbf(
        string Codigo, string Nombre, string? NombreFantasia, string? Cuit, string? NumDoc, string? CondIva,
        int? EmiteCarga, int? Inactivo, int? Baja, int? Suspendido,
        string? Domicilio, string? CodigoPostal, string? Localidad, string? Provincia, string? Email);

    private List<FilaClienteDbf> LeerClientesDbf(string rutaDbf, HashSet<string> existentes)
    {
        try
        {
            using var reader = new DbfReader(rutaDbf);
            var filas = new List<FilaClienteDbf>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in reader.ReadRecords(CamposClientesDbf))
            {
                var codigo = row["CODIGO"];
                if (codigo.Length == 0) continue;
                if (existentes.Contains(codigo)) continue; // ya está en SQL: no se toca.
                if (!vistos.Add(codigo)) continue; // duplicado dentro del propio DBF: se queda con el primero.

                var nombre = row["NOMBRE"];
                if (nombre.Length == 0) continue; // sin razón social no hay nada que crear.

                filas.Add(new FilaClienteDbf(
                    codigo, nombre, Limpiar(row["NOMFANT"]), LimpiarCuit(row["CUIT"]), Limpiar(row["NUMDOC"]),
                    Limpiar(row["CONDIVA"]), ParseInt(row["EMITECARGA"]), ParseInt(row["INACTIVO"]),
                    ParseInt(row["BAJA"]), ParseInt(row["SUSPENDIDO"]),
                    Limpiar(row["DOMICILIO"]), Limpiar(row["COPOSTAL"]), Limpiar(row["LOCALIDAD"]),
                    Limpiar(row["PROVINCIA_"]), LimpiarEmail(row["EMAIL"])));
            }
            return filas;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer clientes.dbf en {Ruta} (¿red S:\\ no disponible?).", rutaDbf);
            throw new DomainException("CLIENTES_DBF_INACCESIBLE",
                "No se pudo leer el archivo de clientes (verificar acceso a S:\\).");
        }
    }

    private sealed record TarjetaVigente(string Codigo, string Tipo, DateOnly? Fecha);

    /// <summary>Por cliente, la tarjeta habilitada (INHABILITA=0) más reciente entre Roja/Azul — si
    /// tiene varias vigentes a la vez (no debería, pero el DBF no lo garantiza) gana la de FECHA más
    /// nueva. Solo resuelve para <paramref name="codigosDeInteres"/> (los clientes nuevos): leer
    /// las 170k filas de codtarje.dbf para descartar el resto en el momento es más simple y no pesa
    /// nada distinto a filtrar después.</summary>
    private Dictionary<string, TarjetaVigente> LeerTarjetasVigentes(string rutaDbf, HashSet<string> codigosDeInteres)
    {
        var resultado = new Dictionary<string, TarjetaVigente>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = new DbfReader(rutaDbf);
            foreach (var row in reader.ReadRecords(CamposCodTarjeDbf))
            {
                var cliente = row["CLIENTE"];
                if (!codigosDeInteres.Contains(cliente)) continue;
                if (ParseInt(row["INHABILITA"]) == 1) continue;

                var tipo = row["TIPOTARJE"];
                if (tipo != "03" && tipo != "04") continue;

                var fecha = ParseFechaYmd(row["FECHA"]);
                if (resultado.TryGetValue(cliente, out var actual) && (actual.Fecha ?? DateOnly.MinValue) >= (fecha ?? DateOnly.MinValue))
                    continue; // ya hay una igual o más reciente para este cliente.

                resultado[cliente] = new TarjetaVigente(row["CODIGO"], tipo, fecha);
            }
            return resultado;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "No se pudo leer codtarje.dbf en {Ruta} (¿red S:\\ no disponible?).", rutaDbf);
            throw new DomainException("CODTARJE_DBF_INACCESIBLE",
                "No se pudo leer el archivo de tarjetas de clientes (verificar acceso a S:\\).");
        }
    }

    private async Task<string> ObtenerCarpetaDbfAsync(CancellationToken ct)
    {
        var valor = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == ClaveCarpetaDbf).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(valor) ? CarpetaDbfPorDefecto : valor;
    }

    private static string? Limpiar(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // El DBF trae basura tipo "@" suelto en bastantes filas — solo se guarda si parece un email real.
    private static string? LimpiarEmail(string valor)
    {
        var v = valor.Trim();
        var pos = v.IndexOf('@');
        return pos > 0 && pos < v.Length - 1 ? v : null;
    }

    // El DBF trae el CUIT con guiones ("30-66615871-3") y placeholders tipo "-        -"; en SQL
    // Cliente.Cuit es solo dígitos y tiene HasMaxLength(11) (ver PosDbContext) — sin este parseo,
    // guardar el valor con guiones tal cual rompía el insert por "String or binary data would be
    // truncated". Se guarda null si no quedan al menos 8 dígitos (placeholder, no un CUIT real).
    private static string? LimpiarCuit(string valor)
    {
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        if (digitos.Length < 8) return null;
        return digitos.Length > 11 ? digitos[..11] : digitos;
    }

    private static int? ParseInt(string valor) =>
        int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    // FECHA viene en formato "YYYYMMDD" (tipo D de dBase); vacío o corrupto en registros viejos.
    private static DateOnly? ParseFechaYmd(string valor) =>
        valor.Length == 8 && DateOnly.TryParseExact(valor, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fecha) ? fecha : null;
}
