namespace Pos.Application.Common;

public record ApiError(string Code, string Message);

/// <summary>Envoltura de respuesta uniforme: { ok, data, error }.</summary>
public class ApiResult<T>
{
    public bool Ok { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }

    public static ApiResult<T> Success(T data) => new() { Ok = true, Data = data };
    public static ApiResult<T> Fail(string code, string message) =>
        new() { Ok = false, Error = new ApiError(code, message) };
}

/// <summary>Excepción de negocio que el middleware traduce a ApiResult.Fail.</summary>
public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// Excepción de autorización a nivel de recurso (ej. el usuario está atado a una sucursal/caja
/// específica y pidió operar sobre otra). El middleware la traduce a 403, no a 409 como
/// <see cref="DomainException"/> (que es para conflictos de negocio, no de permisos).
/// </summary>
public class AccesoDenegadoException : Exception
{
    public string Code { get; }
    public AccesoDenegadoException(string code, string message) : base(message) => Code = code;
}
