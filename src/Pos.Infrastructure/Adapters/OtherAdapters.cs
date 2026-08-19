using Pos.Application.Abstractions;

namespace Pos.Infrastructure.Adapters;

public class BancoImagenesAdapter : IImageBank
{
    private readonly string _base;
    public BancoImagenesAdapter(string baseUrl) => _base = baseUrl.TrimEnd('/');

    public Uri BuildImageUrl(string codigoInterno) => new($"{_base}/{codigoInterno}_0.JPG");

    // Fase 1: no se consulta la existencia real contra el portal.
    public Task<bool> ExistsAsync(string codigoInterno, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>Envío de mail simulado: registra en consola en vez de enviar. Reemplazar por SMTP.</summary>
public class MockMailSender : IMailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        Console.WriteLine($"[MOCK MAIL] to={to} subject={subject} ({htmlBody.Length} chars)");
        return Task.CompletedTask;
    }
}

/// <summary>ERP deshabilitado en fase 1: los datos maestros son propios.</summary>
public class DisabledErpGateway : IErpGateway
{
    public bool Enabled => false;
}
