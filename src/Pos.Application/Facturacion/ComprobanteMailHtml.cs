using System.Globalization;
using System.Net;
using System.Text;

namespace Pos.Application.Facturacion;

/// <summary>
/// Arma el cuerpo HTML del mail con la factura (formatos A y B), a partir del mismo
/// <see cref="ComprobanteImpresionDto"/> que usa la vista de impresión de Caja. Lógica pura (sin I/O).
///
/// Va con estilos inline y tablas (no CSS externo ni flex/grid): los clientes de mail ignoran
/// o rompen casi todo lo demás. Todo texto que viene de la base (razón social, descripciones de
/// artículos, domicilios) se escapa con HtmlEncode — son datos cargados a mano o importados del ERP,
/// no se asume que sean seguros para meter en HTML.
/// </summary>
public static class ComprobanteMailHtml
{
    // Formato es-AR fijo (1.234,56) sin depender de la cultura del servidor ni de la globalización
    // invariante: un mail con "1,234.56" en una factura argentina sería un error grave de lectura.
    private static readonly NumberFormatInfo Nf = new()
    {
        NumberDecimalSeparator = ",", NumberGroupSeparator = ".",
        NumberGroupSizes = new[] { 3 }, NumberDecimalDigits = 2
    };

    public static string Dinero(decimal n) => n.ToString("N2", Nf);
    private static string Cantidad(decimal n) => n.ToString("N2", Nf);
    private static string Pct(decimal alicuota) => (alicuota * 100m).ToString("N1", Nf);
    private static string Fecha(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>Asunto del mail: "Factura B 0001-00001234 - EMPRESA".</summary>
    public static string Asunto(ComprobanteImpresionDto c) =>
        $"{c.TipoComprobante} {c.NumeroCompleto}".Trim() +
        (string.IsNullOrWhiteSpace(c.Emisor.RazonSocial) ? "" : $" - {c.Emisor.RazonSocial}");

    public static string Render(ComprobanteImpresionDto c)
    {
        var esA = string.Equals(c.Letra, "A", StringComparison.OrdinalIgnoreCase);
        var cliente = c.Cliente;
        var emisor = c.Emisor;
        var sb = new StringBuilder();

        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:640px;margin:auto;color:#1e293b;font-size:14px\">");

        // Saludo
        sb.Append($"<p>Hola {E(cliente.Descripcion)},</p>");
        sb.Append($"<p>Te enviamos el comprobante de tu compra en <strong>{E(emisor.RazonSocial)}</strong>.</p>");

        // Caja del comprobante
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border:1px solid #cbd5e1;border-collapse:collapse\"><tr><td style=\"padding:16px\">");

        // Cabecera: tipo + letra / número y fecha
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>");
        sb.Append($"<td valign=\"top\"><div style=\"font-size:18px;font-weight:bold\">{E(c.TipoComprobante.ToUpperInvariant())}</div>");
        sb.Append($"<div style=\"font-size:12px;color:#64748b\">ORIGINAL{(string.IsNullOrWhiteSpace(c.CodigoArca) ? "" : " Cod.: " + E(c.CodigoArca))}</div></td>");
        sb.Append($"<td valign=\"top\" align=\"right\"><div><strong>Nro.:</strong> {E(c.NumeroCompleto)}</div><div><strong>Fecha:</strong> {Fecha(c.Fecha)}</div></td>");
        sb.Append("</tr></table>");

        // Emisor
        sb.Append("<div style=\"margin-top:12px;font-size:13px\">");
        sb.Append($"<strong>{E(emisor.RazonSocial)}</strong>");
        sb.Append($"<br>CUIT: {E(emisor.Cuit ?? "—")}{(string.IsNullOrWhiteSpace(emisor.CondicionIva) ? "" : " " + E(emisor.CondicionIva))}");
        if (!string.IsNullOrWhiteSpace(emisor.Domicilio)) sb.Append($"<br>{E(emisor.Domicilio)}");
        var locProv = string.Join(" - ", new[] { emisor.Localidad, emisor.Provincia }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (locProv.Length > 0) sb.Append($"<br>{E(locProv)}");
        if (!string.IsNullOrWhiteSpace(emisor.IngresosBrutos)) sb.Append($"<br>Ing. Brutos: {E(emisor.IngresosBrutos)}");
        if (emisor.InicioActividad is not null) sb.Append($"<br>Inicio Actividad: {Fecha(emisor.InicioActividad)}");
        sb.Append("</div>");

        // Cliente: la A identifica al comprador completo, la B solo cond. IVA (igual que la impresión).
        sb.Append("<div style=\"margin-top:12px;padding-top:8px;border-top:1px solid #e2e8f0;font-size:13px\">");
        sb.Append($"<strong>Cliente:</strong> {E(cliente.Descripcion)}");
        if (esA)
        {
            if (!string.IsNullOrWhiteSpace(cliente.Cuit)) sb.Append($"<br><strong>CUIT:</strong> {E(cliente.Cuit)}");
            else if (!string.IsNullOrWhiteSpace(cliente.Documento)) sb.Append($"<br><strong>Documento:</strong> {E(cliente.Documento)}");
            if (!string.IsNullOrWhiteSpace(cliente.Domicilio)) sb.Append($"<br><strong>Dirección:</strong> {E(cliente.Domicilio)}");
            sb.Append($"<br><strong>Cond. ante IVA:</strong> {E(cliente.CondicionIva ?? "—")}");
            if (!string.IsNullOrWhiteSpace(cliente.Localidad)) sb.Append($"<br><strong>Localidad:</strong> {E(cliente.Localidad)}");
            if (!string.IsNullOrWhiteSpace(cliente.Provincia)) sb.Append($"<br><strong>Provincia:</strong> {E(cliente.Provincia)}");
        }
        else
        {
            sb.Append($"<br><strong>Cond. ante IVA:</strong> {E(cliente.CondicionIva ?? "Consumidor final")}");
        }
        sb.Append("</div>");

        // Líneas
        const string th = "style=\"text-align:left;padding:6px 4px;border-bottom:2px solid #1e293b;font-size:12px\"";
        const string thR = "style=\"text-align:right;padding:6px 4px;border-bottom:2px solid #1e293b;font-size:12px\"";
        const string td = "style=\"padding:5px 4px;border-bottom:1px solid #e2e8f0;vertical-align:top\"";
        const string tdR = "style=\"padding:5px 4px;border-bottom:1px solid #e2e8f0;text-align:right;vertical-align:top;white-space:nowrap\"";
        sb.Append("<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:14px;border-collapse:collapse;font-size:13px\">");
        if (esA)
        {
            sb.Append($"<tr><th {th}>Descripción</th><th {thR}>Unid</th><th {thR}>$ Unid.</th><th {thR}>$ Neto</th></tr>");
            foreach (var l in c.Lineas)
                sb.Append($"<tr><td {td}>{E(l.Descripcion)}</td><td {tdR}>{Cantidad(l.Cantidad)}</td><td {tdR}>${Dinero(l.PrecioUnitario)}</td><td {tdR}>${Dinero(l.Importe)}</td></tr>");
        }
        else
        {
            sb.Append($"<tr><th {th}>Cantidad / Precio Unit.<br>Descripción</th><th {thR}>IMPORTE</th></tr>");
            foreach (var l in c.Lineas)
                sb.Append($"<tr><td {td}>{Cantidad(l.Cantidad)} x {Dinero(l.PrecioUnitario)}<br><span style=\"color:#475569;font-size:12px\">{E(l.Descripcion)}</span></td><td {tdR}>${Dinero(l.Importe)}</td></tr>");
        }
        sb.Append("</table>");

        // Totales (alineados a la derecha)
        sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" align=\"right\" style=\"margin-top:12px;font-size:13px;min-width:260px\">");
        void Fila(string rotulo, string valor, bool destacada = false)
        {
            var est = destacada ? "font-size:16px;font-weight:bold;border-top:2px solid #1e293b;padding-top:6px" : "";
            sb.Append($"<tr><td style=\"padding:2px 16px 2px 0;{est}\">{E(rotulo)}</td><td align=\"right\" style=\"padding:2px 0;white-space:nowrap;{est}\">{valor}</td></tr>");
        }
        Fila("Descuento", (c.Descuento > 0 ? "-" : "") + "$" + Dinero(c.Descuento));
        if (esA)
        {
            Fila("Subtotal", "$" + Dinero(c.Neto));
            foreach (var iva in c.IvaDiscriminado)
                Fila($"IVA {Pct(iva.Alicuota)}%", "$" + Dinero(iva.Importe));
            if (c.ImpuestoInterno > 0) Fila("Exento", "$" + Dinero(c.ImpuestoInterno));
        }
        if (c.PercepcionIva21 > 0) Fila("Percepción IVA 21%", "$" + Dinero(c.PercepcionIva21));
        if (c.PercepcionIva105 > 0) Fila("Percepción IVA 10,5%", "$" + Dinero(c.PercepcionIva105));
        if (c.PercepcionIibb > 0)
            Fila($"Percepción IIBB ({c.AlicuotaIibb.ToString("N2", Nf)}%)", "$" + Dinero(c.PercepcionIibb));
        Fila("Total", "$" + Dinero(c.Total), destacada: true);
        sb.Append("</table><div style=\"clear:both\"></div>");

        // Pagos
        if (c.Pagos.Count > 0)
        {
            sb.Append("<div style=\"margin-top:14px;padding-top:8px;border-top:1px solid #e2e8f0;font-size:13px\"><strong>Pagos</strong>");
            foreach (var p in c.Pagos)
                sb.Append($"<br>{E(p.Descripcion)}: ${Dinero(p.Monto)}");
            sb.Append("</div>");
        }

        // CAE: solo el camino Electrónica lo tiene; con controlador fiscal la autorización queda en el equipo.
        sb.Append("<div style=\"margin-top:14px;padding-top:8px;border-top:1px solid #e2e8f0;font-size:13px\">");
        if (!string.IsNullOrWhiteSpace(c.Cae))
        {
            sb.Append($"<strong>CAE N°:</strong> {E(c.Cae)}");
            if (c.CaeVencimiento is not null) sb.Append($"<br><strong>Fecha de Vto. de CAE:</strong> {Fecha(c.CaeVencimiento)}");
            sb.Append($"<br><em>{(c.EsCaea ? "Comprobante autorizado (CAEA)" : "Comprobante Autorizado")}</em>");
        }
        else
        {
            sb.Append("<em>Comprobante fiscal emitido por controlador fiscal homologado — vale como factura.</em>");
        }
        sb.Append("</div>");

        sb.Append("</td></tr></table>");
        sb.Append("</div>");
        return sb.ToString();
    }
}
