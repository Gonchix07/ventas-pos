using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pos.Application.Common;
using Pos.Application.Facturacion;
using Pos.Domain.Services;
using Pos.Infrastructure.Adapters;

namespace Pos.Domain.Tests;

public class EmailReglasTests
{
    [Theory]
    [InlineData("cliente@dominio.com")]
    [InlineData("  cliente@dominio.com  ")]
    [InlineData("juan.perez+facturas@mi-empresa.com.ar")]
    [InlineData("a@b.co")]
    public void Acepta_mails_con_formato_valido(string email) =>
        Assert.True(EmailReglas.EsValido(email));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba.com")]
    [InlineData("@dominio.com")]
    [InlineData("cliente@")]
    [InlineData("cliente@dominio")]            // sin punto en el dominio
    [InlineData("cliente@dominio.c")]          // TLD de 1 letra
    [InlineData("cli ente@dominio.com")]       // espacio
    [InlineData("a@b@dominio.com")]            // dos arrobas
    [InlineData("cliente@dominio..com")]       // puntos consecutivos
    [InlineData(".cliente@dominio.com")]       // punto al borde
    [InlineData("cliente.@dominio.com")]
    [InlineData("cliente@-dominio.com")]
    [InlineData("\"x\"@dominio.com")]          // comillas
    [InlineData("a@dominio.com\r\nBcc: x@y.com")] // inyección de cabeceras
    public void Rechaza_mails_con_formato_invalido(string? email) =>
        Assert.False(EmailReglas.EsValido(email));

    [Fact]
    public void Rechaza_mails_mas_largos_que_la_columna_de_la_ficha()
    {
        var largo = new string('a', EmailReglas.LargoMaximo) + "@dominio.com";
        Assert.False(EmailReglas.EsValido(largo));
    }
}

public class ComprobanteMailHtmlTests
{
    private static ComprobanteImpresionDto Armar(string letra, string descripcionLinea = "FERNET BRANCA 12X750 CC") =>
        new(1, 10, $"Factura {letra}", letra, "006", "0001-00001234", new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
            new EmisorComprobanteDto("HERGO SA", "30-11111111-1", "Responsable Inscripto", "Calle 1", "Mar del Plata", "Buenos Aires", "7600", "123", null),
            new ClienteComprobanteDto("MARTINEZ MARINA", "27-22222222-3", null, "Responsable Inscripto", "Calle 2", "MDP", "BA", "7600", 5, "m@x.com"),
            new List<LineaComprobanteDto> { new(descripcionLinea, 2m, 1000m, 0m, 2000m, 0.21m) },
            0m, 1652.89m, 347.11m, 2000m,
            letra == "A" ? new List<IvaDiscriminadoDto> { new(0.21m, 1652.89m, 347.11m) } : new List<IvaDiscriminadoDto>(),
            new List<PagoComprobanteDto> { new("Efectivo", 2000m) },
            "74123456789012", new DateTime(2026, 10, 15), false, "Impreso");

    [Fact]
    public void Dinero_usa_formato_argentino_con_punto_de_miles_y_coma_decimal() =>
        Assert.Equal("1.234.567,89", ComprobanteMailHtml.Dinero(1234567.89m));

    [Fact]
    public void Factura_A_discrimina_iva_y_muestra_el_cae()
    {
        var html = ComprobanteMailHtml.Render(Armar("A"));
        Assert.Contains("FACTURA A", html);
        Assert.Contains("CUIT:</strong> 27-22222222-3", html);
        Assert.Contains("IVA 21,0%", html);
        Assert.Contains("$347,11", html);
        Assert.Contains("74123456789012", html);
        Assert.Contains("15/10/2026", html);   // vencimiento del CAE
        Assert.Contains("$2.000,00", html);
    }

    [Fact]
    public void Factura_B_no_discrimina_iva_ni_pide_datos_del_comprador()
    {
        var html = ComprobanteMailHtml.Render(Armar("B"));
        Assert.Contains("FACTURA B", html);
        Assert.DoesNotContain("IVA 21,0%", html);
        Assert.DoesNotContain("Subtotal", html);
        Assert.DoesNotContain("CUIT:</strong> 27-22222222-3", html);
        Assert.Contains("2,00 x 1.000,00", html);
    }

    [Fact]
    public void Escapa_el_html_de_los_datos_cargados_a_mano()
    {
        var html = ComprobanteMailHtml.Render(Armar("B", "<script>alert(1)</script> & CIA"));
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp; CIA", html);
    }

    [Fact]
    public void Asunto_lleva_tipo_numero_y_empresa() =>
        Assert.Equal("Factura B 0001-00001234 - HERGO SA", ComprobanteMailHtml.Asunto(Armar("B")));
}

public class BrevoMailSenderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.Created;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"messageId\":\"x\"}") };
        }
    }

    private static BrevoMailSender Crear(FakeHandler h, string apiKey = "KEY-DE-PRUEBA") =>
        new(new HttpClient(h), new BrevoMailOptions { ApiKey = apiKey }, NullLogger<BrevoMailSender>.Instance);

    [Fact]
    public async Task Arma_el_request_de_Brevo_con_el_remitente_info_de_hergo()
    {
        var h = new FakeHandler();
        await Crear(h).SendAsync("cliente@dominio.com", "Factura B 0001-00000001", "<p>hola</p>", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, h.Request!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", h.Request.RequestUri!.ToString());
        Assert.Equal("KEY-DE-PRUEBA", h.Request.Headers.GetValues("api-key").Single());

        using var doc = JsonDocument.Parse(h.Body!);
        var root = doc.RootElement;
        Assert.Equal("info@hergo.com.ar", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("cliente@dominio.com", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("Factura B 0001-00000001", root.GetProperty("subject").GetString());
        Assert.Equal("<p>hola</p>", root.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task Sin_api_key_falla_con_error_claro_y_no_llama_a_la_red()
    {
        var h = new FakeHandler();
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Crear(h, apiKey: "").SendAsync("a@b.com", "s", "b", CancellationToken.None));
        Assert.Equal("MAIL_NO_CONFIGURADO", ex.Code);
        Assert.Null(h.Request);
    }

    [Fact]
    public async Task Si_Brevo_rechaza_el_envio_lanza_MAIL_ERROR()
    {
        var h = new FakeHandler { Status = HttpStatusCode.Unauthorized };
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            Crear(h).SendAsync("a@b.com", "s", "b", CancellationToken.None));
        Assert.Equal("MAIL_ERROR", ex.Code);
        Assert.Contains("401", ex.Message);
    }
}
