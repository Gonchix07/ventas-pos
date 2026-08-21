using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Pos.Application.Abstractions.Interfase;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Escribe en la base MySQL "interfase" del sistema contable externo (config en Administración →
/// Conexión externa, tabla ConexionesExternasMySql). Best-effort A PROPÓSITO — ver
/// <see cref="IInterfaseContableService"/>: la integración deshabilitada, sin contraseña, o
/// inalcanzable nunca debe impedir emitir una venta. Cualquier error se loguea y se traga acá.
/// </summary>
public class InterfaseContableService : IInterfaseContableService
{
    // Mismo purpose que ConexionExternaAdminService (ver AbmServices.cs) — tiene que ser idéntico
    // o Unprotect falla siempre, aunque la contraseña esté bien guardada.
    private const string DataProtectionPurpose = "Pos.ConexionExternaMySql";

    private readonly PosDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<InterfaseContableService> _log;

    public InterfaseContableService(PosDbContext db, IDataProtectionProvider dataProtection,
        ILogger<InterfaseContableService> log)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(DataProtectionPurpose);
        _log = log;
    }

    /// <summary>Resuelve la config, descifra la contraseña y abre la conexión — null si la
    /// integración no está configurada/habilitada (no es un error, no hay nada que hacer). El
    /// llamador es responsable de loguear cualquier excepción que tire esto (best-effort).</summary>
    private async Task<MySqlConnection?> AbrirConexionAsync(long idVentaSalonParaLog, CancellationToken ct)
    {
        var config = await _db.ConexionesExternasMySql.AsNoTracking().FirstOrDefaultAsync(ct);
        if (config is null || !config.Habilitada) return null;

        if (string.IsNullOrEmpty(config.PasswordProtegida))
        {
            _log.LogWarning(
                "Conexión externa MySQL habilitada sin contraseña configurada; no se registró la venta {IdVentaSalon} en interfase.",
                idVentaSalonParaLog);
            return null;
        }
        var password = _protector.Unprotect(config.PasswordProtegida);
        var csb = new MySqlConnectionStringBuilder
        {
            Server = config.Host, Port = (uint)config.Puerto, Database = config.BaseDatos,
            UserID = config.Usuario, Password = password, ConnectionTimeout = 5,
        };
        var conn = new MySqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task RegistrarVentaAsync(IvaVtaInterfase fila, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await AbrirConexionAsync(fila.IdVentaSalon, ct);
            if (conn is null) return;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ivavtas
                    (fecha, cliente, nombre, condiva, cuit, tipo, prenum, numero, neto, iva, iva_adic,
                     exento, periva, final, baseimp, impperc, porciibb1, prov, empresa, idventa_salon, importado)
                VALUES
                    (@fecha, @cliente, @nombre, @condiva, @cuit, @tipo, @prenum, @numero, @neto, @iva,
                     @iva_adic, @exento, @periva, @final, @baseimp, @impperc, @porciibb1, @prov, @empresa,
                     @idventa_salon, NULL)
                """;
            cmd.Parameters.AddWithValue("@fecha", fila.Fecha.Date);
            cmd.Parameters.AddWithValue("@cliente", (object?)fila.Cliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nombre", (object?)fila.Nombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@condiva", (object?)fila.CondIva ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cuit", (object?)fila.Cuit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tipo", fila.Tipo);
            cmd.Parameters.AddWithValue("@prenum", fila.Prenum);
            cmd.Parameters.AddWithValue("@numero", fila.Numero);
            cmd.Parameters.AddWithValue("@neto", fila.Neto);
            cmd.Parameters.AddWithValue("@iva", fila.Iva);
            cmd.Parameters.AddWithValue("@iva_adic", fila.IvaAdic);
            cmd.Parameters.AddWithValue("@exento", fila.Exento);
            cmd.Parameters.AddWithValue("@periva", fila.Periva);
            cmd.Parameters.AddWithValue("@final", fila.Final);
            cmd.Parameters.AddWithValue("@baseimp", fila.BaseImp);
            cmd.Parameters.AddWithValue("@impperc", fila.ImpPerc);
            cmd.Parameters.AddWithValue("@porciibb1", fila.PorcIibb1);
            cmd.Parameters.AddWithValue("@prov", fila.Prov);
            cmd.Parameters.AddWithValue("@empresa", fila.Empresa);
            cmd.Parameters.AddWithValue("@idventa_salon", fila.IdVentaSalon);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            // Best-effort: nunca debe romper la venta que ya se facturó/imprimió/cobró. Se loguea
            // para poder detectarlo y reintentar/corregir a mano (decisión del usuario: no hay cola
            // de reintento automático todavía).
            _log.LogError(ex,
                "No se pudo registrar la venta {IdVentaSalon} en la interfase contable (ivavtas).",
                fila.IdVentaSalon);
        }
    }

    public async Task RegistrarMovStockAsync(IReadOnlyList<MovStockInterfase> filas, CancellationToken ct = default)
    {
        if (filas.Count == 0) return;
        try
        {
            await using var conn = await AbrirConexionAsync(filas[0].IdVentaSalon, ct);
            if (conn is null) return;

            // Una sola conexión, una fila por línea del comprobante — no es una transacción propia
            // (movstock no tiene ninguna restricción de integridad entre filas), así que un error a
            // mitad de camino deja las líneas anteriores insertadas; se loguea igual como fallo de
            // la venta completa para que se revise a mano.
            foreach (var fila in filas)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO movstock
                        (fecha, articulo, salida, descto, desct2, desct3, desct4, unitario, pesos,
                         dedeposito, cliente, nombre, tipo, numero, vendedor, lista, impint, reparto,
                         modofact, codconv, prov, empresa, idventa_salon, importado, desctoante, iva, periva)
                    VALUES
                        (@fecha, @articulo, @salida, @descto, 0, 0, 0, @unitario, @pesos,
                         @dedeposito, @cliente, @nombre, @tipo, @numero, @vendedor, @lista, @impint, @reparto,
                         @modofact, @codconv, @prov, @empresa, @idventa_salon, NULL, 0, @iva, @periva)
                    """;
                cmd.Parameters.AddWithValue("@fecha", fila.Fecha.Date);
                cmd.Parameters.AddWithValue("@articulo", fila.Articulo);
                cmd.Parameters.AddWithValue("@salida", fila.Salida);
                cmd.Parameters.AddWithValue("@descto", fila.Descto);
                cmd.Parameters.AddWithValue("@unitario", fila.Unitario);
                cmd.Parameters.AddWithValue("@pesos", fila.Pesos);
                cmd.Parameters.AddWithValue("@dedeposito", fila.DeDeposito);
                cmd.Parameters.AddWithValue("@cliente", (object?)fila.Cliente ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@nombre", (object?)fila.Nombre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@tipo", fila.Tipo);
                cmd.Parameters.AddWithValue("@numero", fila.Numero);
                cmd.Parameters.AddWithValue("@vendedor", (object?)fila.Vendedor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@lista", fila.Lista);
                cmd.Parameters.AddWithValue("@impint", fila.ImpInt);
                cmd.Parameters.AddWithValue("@reparto", fila.Reparto);
                cmd.Parameters.AddWithValue("@modofact", fila.ModoFact);
                cmd.Parameters.AddWithValue("@codconv", (object?)fila.CodConv ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@prov", fila.Prov);
                cmd.Parameters.AddWithValue("@empresa", fila.Empresa);
                cmd.Parameters.AddWithValue("@idventa_salon", fila.IdVentaSalon);
                cmd.Parameters.AddWithValue("@iva", fila.Iva);
                cmd.Parameters.AddWithValue("@periva", fila.Periva);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "No se pudo registrar el stock de la venta {IdVentaSalon} en la interfase contable (movstock).",
                filas[0].IdVentaSalon);
        }
    }

    public async Task RegistrarCtaCteAsync(CtaCteInterfase fila, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await AbrirConexionAsync(fila.IdVentaSalon, ct);
            if (conn is null) return;

            await using var cmd = conn.CreateCommand();
            // "estado" queda sin completar (NULL, columna nullable) — confirmado con el usuario que
            // no lo llena pos-mayorista.
            cmd.CommandText = """
                INSERT INTO ctacte
                    (fecha, tipo, prenum, numero, debe, haber, estado, prov, empresa, idventa_salon, importado, cliente)
                VALUES
                    (@fecha, @tipo, @prenum, @numero, @debe, @haber, NULL, @prov, @empresa, @idventa_salon, NULL, @cliente)
                """;
            cmd.Parameters.AddWithValue("@fecha", fila.Fecha.Date);
            cmd.Parameters.AddWithValue("@tipo", fila.Tipo);
            cmd.Parameters.AddWithValue("@prenum", fila.Prenum);
            cmd.Parameters.AddWithValue("@numero", fila.Numero);
            cmd.Parameters.AddWithValue("@debe", fila.Debe);
            cmd.Parameters.AddWithValue("@haber", fila.Haber);
            cmd.Parameters.AddWithValue("@prov", fila.Prov);
            cmd.Parameters.AddWithValue("@empresa", fila.Empresa);
            cmd.Parameters.AddWithValue("@idventa_salon", fila.IdVentaSalon);
            cmd.Parameters.AddWithValue("@cliente", (object?)fila.Cliente ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "No se pudo registrar el movimiento de cuenta corriente de la venta {IdVentaSalon} en la interfase contable (ctacte).",
                fila.IdVentaSalon);
        }
    }

    public async Task RegistrarComisionAsync(ComisionInterfase fila, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await AbrirConexionAsync(fila.IdVentaSalon, ct);
            if (conn is null) return;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO comision
                    (fecha, cliente, tipo, prenum, numero, neto, final, vendedor, condvta, reparto,
                     prov, empresa, idventa_salon, importado, hora)
                VALUES
                    (@fecha, @cliente, @tipo, @prenum, @numero, @neto, @final, @vendedor, @condvta, @reparto,
                     @prov, @empresa, @idventa_salon, NULL, @hora)
                """;
            cmd.Parameters.AddWithValue("@fecha", fila.Fecha.Date);
            cmd.Parameters.AddWithValue("@cliente", (object?)fila.Cliente ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tipo", fila.Tipo);
            cmd.Parameters.AddWithValue("@prenum", fila.Prenum);
            cmd.Parameters.AddWithValue("@numero", fila.Numero);
            cmd.Parameters.AddWithValue("@neto", fila.Neto);
            cmd.Parameters.AddWithValue("@final", fila.Final);
            cmd.Parameters.AddWithValue("@vendedor", (object?)fila.Vendedor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@condvta", fila.CondVta);
            cmd.Parameters.AddWithValue("@reparto", fila.Reparto);
            cmd.Parameters.AddWithValue("@prov", fila.Prov);
            cmd.Parameters.AddWithValue("@empresa", fila.Empresa);
            cmd.Parameters.AddWithValue("@idventa_salon", fila.IdVentaSalon);
            cmd.Parameters.AddWithValue("@hora", fila.Hora);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "No se pudo registrar la comisión de la venta {IdVentaSalon} en la interfase contable (comision).",
                fila.IdVentaSalon);
        }
    }

    public async Task RegistrarCuponAsync(CuponInterfase fila, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await AbrirConexionAsync(fila.IdVentaSalon, ct);
            if (conn is null) return;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO cupones
                    (numero, tarjeta, plan, importe, fecha_rec, codcli, nomcli, caja, cajero, operacion, idventa_salon, importado)
                VALUES
                    (@numero, @tarjeta, @plan, @importe, @fecha_rec, @codcli, @nomcli, @caja, @cajero, @operacion, @idventa_salon, NULL)
                """;
            cmd.Parameters.AddWithValue("@numero", (object?)fila.Numero ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tarjeta", (object?)fila.Tarjeta ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@plan", (object?)fila.Plan ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@importe", fila.Importe);
            cmd.Parameters.AddWithValue("@fecha_rec", fila.FechaRec.Date);
            cmd.Parameters.AddWithValue("@codcli", (object?)fila.CodCli ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@nomcli", (object?)fila.NomCli ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@caja", fila.Caja);
            cmd.Parameters.AddWithValue("@cajero", fila.Cajero);
            cmd.Parameters.AddWithValue("@operacion", fila.Operacion);
            cmd.Parameters.AddWithValue("@idventa_salon", fila.IdVentaSalon);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "No se pudo registrar el cupón de la venta {IdVentaSalon} en la interfase contable (cupones).",
                fila.IdVentaSalon);
        }
    }

    public async Task RegistrarCierreCajaAsync(CjaMoviInterfase fila, CancellationToken ct = default)
    {
        try
        {
            // No hay idventa_salon en cja_movi (no es un comprobante) — 0 es solo la etiqueta de log
            // si la integración está deshabilitada/sin contraseña.
            await using var conn = await AbrirConexionAsync(0, ct);
            if (conn is null) return;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO cja_movi
                    (fecha, tipo, rubro, detalle, efectivo, cheques, dolares, documentos, tarjetas,
                     otros, estado, nro_caja, empresa, ididcja_movi, importado)
                VALUES
                    (@fecha, @tipo, @rubro, @detalle, @efectivo, @cheques, 0, 0, @tarjetas,
                     @otros, NULL, @nro_caja, @empresa, NULL, NULL)
                """;
            cmd.Parameters.AddWithValue("@fecha", fila.Fecha.Date);
            cmd.Parameters.AddWithValue("@tipo", InterfaseContableReglas.CjaMoviTipoFijo);
            cmd.Parameters.AddWithValue("@rubro", InterfaseContableReglas.CjaMoviRubroFijo);
            cmd.Parameters.AddWithValue("@detalle", fila.Detalle);
            cmd.Parameters.AddWithValue("@efectivo", fila.Efectivo);
            cmd.Parameters.AddWithValue("@cheques", fila.Cheques);
            cmd.Parameters.AddWithValue("@tarjetas", fila.Tarjetas);
            cmd.Parameters.AddWithValue("@otros", fila.Otros);
            cmd.Parameters.AddWithValue("@nro_caja", fila.NroCaja);
            cmd.Parameters.AddWithValue("@empresa", fila.Empresa);
            await cmd.ExecuteNonQueryAsync(ct);

            // ididcja_movi: confirmado con el usuario que es "el autonumérico de la tabla, un id de
            // movimiento" — es decir, se referencia a sí misma. No se puede saber el id en el mismo
            // INSERT (lo asigna MySQL), así que se completa con un UPDATE aparte usando el
            // LAST_INSERT_ID() de ESTA conexión (no hay condición de carrera: es el valor que
            // generó el propio InsertAsync recién ejecutado en esta misma conexión).
            await using var cmdUpdate = conn.CreateCommand();
            cmdUpdate.CommandText = "UPDATE cja_movi SET ididcja_movi = LAST_INSERT_ID() WHERE idcja_movi = LAST_INSERT_ID()";
            await cmdUpdate.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo registrar el cierre de caja en la interfase contable (cja_movi).");
        }

        // tmp_cja: mismo evento, tabla distinta (try/catch propio — que falle una no debe impedir
        // el intento de la otra). "caja"/"cajero" son columnas propias acá, a diferencia de
        // cja_movi donde van embebidos en el texto de "detalle".
        try
        {
            await using var conn = await AbrirConexionAsync(0, ct);
            if (conn is null) return;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO tmp_cja
                    (fecha, tipo, caja, cajero, detalle, efectivo, cheques, tarjetas, otros,
                     empresa, idventa_salon, importado)
                VALUES
                    (@fecha, @tipo, @caja, @cajero, @detalle, @efectivo, @cheques, @tarjetas, @otros,
                     @empresa, @idventa_salon, NULL)
                """;
            cmd.Parameters.AddWithValue("@fecha", fila.Fecha.Date);
            cmd.Parameters.AddWithValue("@tipo", InterfaseContableReglas.CjaMoviTipoFijo);
            cmd.Parameters.AddWithValue("@caja", fila.NroCaja);
            cmd.Parameters.AddWithValue("@cajero", fila.Cajero);
            cmd.Parameters.AddWithValue("@detalle", fila.Detalle);
            cmd.Parameters.AddWithValue("@efectivo", fila.Efectivo);
            cmd.Parameters.AddWithValue("@cheques", fila.Cheques);
            cmd.Parameters.AddWithValue("@tarjetas", fila.Tarjetas);
            cmd.Parameters.AddWithValue("@otros", fila.Otros);
            cmd.Parameters.AddWithValue("@empresa", fila.Empresa);
            cmd.Parameters.AddWithValue("@idventa_salon", (object?)fila.IdVentaSalon ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "No se pudo registrar el cierre de caja en la interfase contable (tmp_cja).");
        }
    }
}
