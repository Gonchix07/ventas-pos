using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pos.Application.Abstractions;
using Pos.Application.Common;
using Pos.Application.Facturacion;
using Pos.Domain.Enums;
using Pos.Domain.Services;
using Pos.Infrastructure.Persistence;

namespace Pos.Infrastructure.Services;

/// <summary>
/// Envía por mail la factura recién emitida (o una anterior) al cliente. Solo Factura A y B:
/// el presupuesto (letra X) no es un comprobante fiscal y las notas de crédito tienen su propio
/// circuito, así que se rechazan acá del lado del servidor aunque la pantalla ya no ofrezca el botón.
/// </summary>
public class ComprobanteMailService : IComprobanteMailService
{
    private readonly PosDbContext _db;
    private readonly IFacturacionService _facturacion;
    private readonly IMailSender _mail;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public ComprobanteMailService(PosDbContext db, IFacturacionService facturacion, IMailSender mail,
        ICurrentUser currentUser, IAuditLogger audit)
    {
        _db = db;
        _facturacion = facturacion;
        _mail = mail;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<EnviarComprobanteMailResponse> EnviarAsync(int idSucursal, int idComprobante,
        EnviarComprobanteMailRequest req, CancellationToken ct = default)
    {
        _currentUser.AsegurarSucursal(idSucursal);

        var email = EmailReglas.Normalizar(req.Email);
        if (!EmailReglas.EsValido(email))
            throw new DomainException("EMAIL_INVALIDO", "El mail no tiene un formato válido (ej.: cliente@dominio.com).");

        var cab = await _db.CabecerasComprobantes.AsNoTracking()
            .Where(c => c.IdSucursal == idSucursal && c.IdComprobante == idComprobante)
            .Select(c => new
            {
                c.Letra, c.Estado, c.IdCliente,
                Signo = c.TipoComprobante != null ? c.TipoComprobante.Signo : 1
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("NO_ENCONTRADO", "El comprobante no existe.");

        var esFacturaAB = cab.Signo == 1
            && (string.Equals(cab.Letra, LetraComprobante.A, StringComparison.OrdinalIgnoreCase)
                || string.Equals(cab.Letra, LetraComprobante.B, StringComparison.OrdinalIgnoreCase));
        if (!esFacturaAB)
            throw new DomainException("COMPROBANTE_NO_ENVIABLE",
                "Solo se pueden enviar por mail las Facturas A y B (no presupuestos ni notas de crédito).");
        if (cab.Estado is EstadoComprobante.Iniciado or EstadoComprobante.PagoOk or EstadoComprobante.Anulado)
            throw new DomainException("COMPROBANTE_NO_ENVIABLE", "El comprobante no está emitido o fue anulado.");

        var dto = await _facturacion.ObtenerParaImprimirAsync(idSucursal, idComprobante, ct)
            ?? throw new DomainException("NO_ENCONTRADO", "El comprobante no existe.");

        await _mail.SendAsync(email!, ComprobanteMailHtml.Asunto(dto), ComprobanteMailHtml.Render(dto), ct);

        // El mail se guarda en la ficha recién DESPUÉS de un envío exitoso, y solo si el cajero lo
        // pidió y es distinto del que ya estaba: un tipeo erróneo que el servicio de mail rechaza no
        // tiene que pisar un mail bueno. Sin cliente identificado no hay ficha donde guardarlo.
        var guardado = false;
        if (req.GuardarEnCliente && cab.IdCliente is int idCliente)
        {
            var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.IdCliente == idCliente, ct);
            if (cliente is not null && !string.Equals(cliente.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase))
            {
                cliente.Email = email;
                await _db.SaveChangesAsync(ct);
                guardado = true;
            }
        }

        await _audit.LogAsync("Facturacion", "EnviarMail", "Comprobante", idComprobante.ToString(),
            datosDespues: JsonSerializer.Serialize(new { email, guardadoEnCliente = guardado }), ct: ct);

        return new EnviarComprobanteMailResponse(email!, guardado);
    }
}
