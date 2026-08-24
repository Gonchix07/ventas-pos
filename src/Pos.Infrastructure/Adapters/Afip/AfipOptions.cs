namespace Pos.Infrastructure.Adapters.Afip;

/// <summary>Ambiente contra el que habla el adaptador — cada uno tiene URLs y certificados propios,
/// nunca se mezclan (ver AfipUrls).</summary>
public enum AfipAmbiente
{
    Homologacion,
    Produccion
}

/// <summary>
/// Config del adaptador AFIP/ARCA (WSAA + WSFEv1). El CUIT y el certificado NO van acá: son datos
/// de la <see cref="Pos.Domain.Entities.Empresa"/> (uno por empresa, ya cargados desde el ABM). Acá
/// solo va lo que es igual para todas las empresas del sistema: contra qué ambiente se habla.
/// </summary>
public class AfipOptions
{
    public AfipAmbiente Ambiente { get; init; } = AfipAmbiente.Homologacion;
}

/// <summary>URLs fijas de AFIP/ARCA por ambiente — no son configurables, son las que publica AFIP.</summary>
public static class AfipUrls
{
    public static string Wsaa(AfipAmbiente a) => a == AfipAmbiente.Produccion
        ? "https://wsaa.afip.gov.ar/ws/services/LoginCms"
        : "https://wsaahomo.afip.gov.ar/ws/services/LoginCms";

    public static string Wsfe(AfipAmbiente a) => a == AfipAmbiente.Produccion
        ? "https://servicios1.afip.gov.ar/wsfev1/service.asmx"
        : "https://wswhomo.afip.gov.ar/wsfev1/service.asmx";
}
