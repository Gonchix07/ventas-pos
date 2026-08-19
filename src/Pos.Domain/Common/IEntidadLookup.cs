namespace Pos.Domain.Common;

/// <summary>
/// Marca las tablas de catálogo simples {Id, Descripcion} para poder exponerlas
/// con un CRUD genérico. Id es de solo lectura (mapea a la PK identity de cada tabla).
/// </summary>
public interface IEntidadLookup
{
    int Id { get; }
    string Descripcion { get; set; }
}
