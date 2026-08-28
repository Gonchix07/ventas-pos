namespace Pos.Application.Abm;

// ---- Tipos y Medios de Pago ----
public record TipoPagoDto(int IdTipoPago, string Descripcion, int Fuente, string FuenteDescripcion,
    int Canal, string CanalDescripcion, int CantidadMedios);
public record TipoPagoInput(string Descripcion, int Fuente, int Canal);

public record MedioPagoDto(int IdMedioPago, string Descripcion, int IdTipoPago, string? TipoPagoDescripcion,
    int Canal, string? CanalDescripcion, bool EsPredeterminado, bool Activo, bool ImprimeComprobante,
    int? IdCluster, string? ClusterDescripcion, string? CodigoTarjetaInterfase);
/// <summary>IdCluster null = el medio lo puede usar cualquier cliente.</summary>
public record MedioPagoInput(string Descripcion, int IdTipoPago, bool EsPredeterminado, bool Activo,
    bool ImprimeComprobante, int? IdCluster, string? CodigoTarjetaInterfase = null);

// Plan de cuotas de un medio de pago (solo Tarjeta — se valida en CreatePlanAsync, no hay
// restricción de esquema porque el Fuente vive en TipoPago, no en MedioPago).
public record PlanCuotaDto(int IdPlan, int IdMedioPago, string Denominacion, int CantidadCuotas);
public record PlanCuotaInput(string Denominacion, int CantidadCuotas);

public interface IPagoAdminService
{
    Task<IReadOnlyList<TipoPagoDto>> GetTiposAsync(CancellationToken ct = default);
    Task<int> CreateTipoAsync(TipoPagoInput input, CancellationToken ct = default);
    Task<bool> UpdateTipoAsync(int id, TipoPagoInput input, CancellationToken ct = default);
    Task<bool> DeleteTipoAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<MedioPagoDto>> GetMediosAsync(CancellationToken ct = default);
    Task<int> CreateMedioAsync(MedioPagoInput input, CancellationToken ct = default);
    Task<bool> UpdateMedioAsync(int id, MedioPagoInput input, CancellationToken ct = default);
    Task<bool> DeleteMedioAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<PlanCuotaDto>> GetPlanesAsync(int idMedioPago, CancellationToken ct = default);
    Task<int> CreatePlanAsync(int idMedioPago, PlanCuotaInput input, CancellationToken ct = default);
    Task<bool> UpdatePlanAsync(int idPlan, PlanCuotaInput input, CancellationToken ct = default);
    Task<bool> DeletePlanAsync(int idPlan, CancellationToken ct = default);
}

// ---- Empresas y Sucursales ----
// Los datos fiscales (condición IVA, Ing. Brutos, inicio de actividad) y el domicilio son los que
// encabezan la factura A/B, por eso viven en la empresa y no en una configuración suelta.
public record EmpresaDto(int IdEmpresa, string CodigoInterno, string Descripcion, string? Cuit, string? CertificadoAlias,
    string? CondicionIva, string? IngresosBrutos, DateTime? InicioActividad,
    string? Domicilio, string? Localidad, string? Provincia, string? CodigoPostal);
public record EmpresaInput(string CodigoInterno, string Descripcion, string? Cuit, string? CertificadoAlias,
    string? CondicionIva, string? IngresosBrutos, DateTime? InicioActividad,
    string? Domicilio, string? Localidad, string? Provincia, string? CodigoPostal);

// El domicilio de la sucursal, si está cargado, es el que sale impreso en la factura.
public record SucursalDto(int IdSucursal, int IdEmpresa, string? EmpresaDescripcion, string Descripcion,
    string? Domicilio, string? Localidad, string? Provincia, string? CodigoPostal);
public record SucursalInput(int IdEmpresa, string Descripcion,
    string? Domicilio, string? Localidad, string? Provincia, string? CodigoPostal);

// El certificado (.pfx/.p12) se guarda en disco del servidor, no en la base; acá solo viaja la
// metadata para mostrar el estado en el ABM. La contraseña nunca se expone de vuelta al front.
public record CertificadoCaeDto(bool Presente, string? NombreArchivo, DateTime? Vencimiento, DateTime? SubidoUtc);

/// <summary>
/// Resultado de probar la conexión contra ARCA/AFIP con el certificado ya cargado — NO emite ni
/// autoriza nada (ni CAE ni CAEA): solo confirma que el certificado es válido para WSAA, que el
/// servicio WSFEv1 responde, y de paso informa el último comprobante autorizado en ese punto de
/// venta (útil para chequear que el numerador local esté sincronizado antes de facturar de verdad).
/// </summary>
public record ProbarConexionAfipDto(
    bool WsaaOk, string? WsaaError,
    bool DummyOk, string? DummyError,
    long? UltimoAutorizado, string? UltimoAutorizadoError,
    string? CertificadoSubject, string? CertificadoIssuer, string? CertificadoThumbprint);

/// <summary>
/// Resultado de pedir un CAE real de prueba contra ARCA (pensado para homologación — en producción
/// esto autoriza un comprobante de verdad, no es un simulacro). Un solo ítem al 21% por el importe
/// total, consumidor final sin identificar — alcanza para validar que el circuito completo
/// (WSAA + FECompUltimoAutorizado + FECAESolicitar) funciona de punta a punta.
/// </summary>
public record ProbarCaeDto(bool Ok, string? Error, long? Numero, string? Cae, DateTime? CaeVencimiento,
    IReadOnlyList<string> Observaciones);

public interface IEstructuraService
{
    Task<IReadOnlyList<EmpresaDto>> GetEmpresasAsync(CancellationToken ct = default);
    Task<int> CreateEmpresaAsync(EmpresaInput input, CancellationToken ct = default);
    Task<bool> UpdateEmpresaAsync(int id, EmpresaInput input, CancellationToken ct = default);
    Task<bool> DeleteEmpresaAsync(int id, CancellationToken ct = default);

    Task<CertificadoCaeDto> GetCertificadoAsync(int idEmpresa, CancellationToken ct = default);
    Task<CertificadoCaeDto> SubirCertificadoAsync(int idEmpresa, byte[] contenidoPfx, string nombreArchivo, string clave, CancellationToken ct = default);
    // Alternativa cuando no se tiene un .pfx ya armado: ARCA solo entrega el certificado (.crt/.cer)
    // firmado a partir de un CSR — la clave privada (.key) la genera quien tramita el certificado y
    // nunca pasa por ARCA. Acá se combinan ambos en el mismo almacenamiento que usa el flujo .pfx.
    Task<CertificadoCaeDto> SubirCertificadoDesdeClaveYCertAsync(int idEmpresa, byte[] clavePrivadaPem, byte[] certificadoBytes,
        string? passphraseClavePrivada, CancellationToken ct = default);
    Task<bool> EliminarCertificadoAsync(int idEmpresa, CancellationToken ct = default);
    Task<ProbarConexionAfipDto> ProbarConexionAfipAsync(int idEmpresa, int ptoVta, int cbteTipo, CancellationToken ct = default);
    Task<ProbarCaeDto> ProbarCaeAsync(int idEmpresa, int ptoVta, int cbteTipo, decimal importeTotal, CancellationToken ct = default);

    Task<IReadOnlyList<SucursalDto>> GetSucursalesAsync(CancellationToken ct = default);
    Task<int> CreateSucursalAsync(SucursalInput input, CancellationToken ct = default);
    Task<bool> UpdateSucursalAsync(int id, SucursalInput input, CancellationToken ct = default);
    Task<bool> DeleteSucursalAsync(int id, CancellationToken ct = default);
}

// ---- Configuraciones ----
public record ConfiguracionDto(int IdConfiguracion, string Clave, string Descripcion, string? Valor);
public record ConfiguracionInput(string Clave, string Descripcion, string? Valor);

public interface IConfiguracionAdminService
{
    Task<IReadOnlyList<ConfiguracionDto>> GetAllAsync(CancellationToken ct = default);
    Task<int> CreateAsync(ConfiguracionInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, ConfiguracionInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

// ---- Conexión a datos externa (MySQL) ----
// Fila única (singleton): a futuro, la aplicación deposita en esta base datos para que los consuma
// otro sistema. TieneContrasena reemplaza al valor real en la lectura (nunca se devuelve
// descifrada); en el Input, Password null/vacío = conservar la contraseña ya guardada.
public record ConexionExternaMySqlDto(string Host, int Puerto, string BaseDatos, string Usuario,
    bool TieneContrasena, bool Habilitada);
public record ConexionExternaMySqlInput(string Host, int Puerto, string BaseDatos, string Usuario,
    string? Password, bool Habilitada);
/// <summary>Resultado de un intento real de conexión — nunca expone la contraseña, solo si
/// funcionó o el motivo del error (tal como lo devuelve el driver de MySQL).</summary>
public record ProbarConexionResultado(bool Ok, string? Error);

public interface IConexionExternaAdminService
{
    Task<ConexionExternaMySqlDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(ConexionExternaMySqlInput input, CancellationToken ct = default);
    /// <summary>Prueba una conexión real (TCP + login) con los datos del formulario — si
    /// <paramref name="input"/>.Password viene vacío, usa la contraseña ya guardada (mismo criterio
    /// que UpdateAsync: vacío = conservar la actual). Nunca lanza por un fallo de conexión: eso es
    /// un resultado (Ok=false), no una excepción.</summary>
    Task<ProbarConexionResultado> ProbarConexionAsync(ConexionExternaMySqlInput input, CancellationToken ct = default);
}

// ---- Conexión al API de puntos-app (fidelización) ----
// Fila única (singleton), mismo criterio que ConexionExternaMySql. TieneToken reemplaza al valor
// real en la lectura (nunca se devuelve descifrado); en el Input, Token null/vacío = conservar el
// ya guardado. Comercio es el nombre tal como está dado de alta en el ABM de Comercios de puntos-app.
// "Token" es en realidad una API key fija (X-Api-Key = API_INTEGRATION_KEY en puntos-app), no un
// access_token de sesión de Supabase Auth (esos expiran a la hora y no sirven acá) — se mantiene el
// nombre "Token" en el DTO/Input por compatibilidad con el resto de la nomenclatura del ABM.
public record ConexionPuntosAppDto(string UrlBase, string Comercio, bool TieneToken, bool Habilitada);
public record ConexionPuntosAppInput(string UrlBase, string Comercio, string? Token, bool Habilitada);

public interface IConexionPuntosAppAdminService
{
    Task<ConexionPuntosAppDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(ConexionPuntosAppInput input, CancellationToken ct = default);
    /// <summary>Prueba real: pega contra <c>{UrlBase}/api/cargar-puntos</c> con un cuerpo inválido a
    /// propósito (sin dni/numero) — una API key válida responde 400 "Indicá..."; una inválida
    /// responde 403. Nunca lanza por un fallo de red: eso es un resultado (Ok=false), no una
    /// excepción. Si <paramref name="input"/>.Token viene vacío, usa la ya guardada (mismo criterio
    /// que UpdateAsync).</summary>
    Task<ProbarConexionResultado> ProbarConexionAsync(ConexionPuntosAppInput input, CancellationToken ct = default);
}
