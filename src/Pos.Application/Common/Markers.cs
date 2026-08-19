namespace Pos.Application.Common;

/// <summary>Marca un request que debe ejecutarse dentro de una transacción de BD.</summary>
public interface ITransactionalRequest { }

/// <summary>Marca un request que debe registrarse en la auditoría de negocio.</summary>
public interface IAuditableRequest
{
    string Modulo { get; }
    string Accion { get; }
    string? Entidad => null;
    string? EntidadId => null;
}
