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

/// <summary>
/// Lookup que además trae un código de otro sistema (ERP Central u otro) — se muestra de solo
/// lectura en el ABM genérico de lookups, nunca se edita ahí: lo carga el sync correspondiente.
/// </summary>
public interface IEntidadConCodigoErp : IEntidadLookup
{
    string? CodigoErp { get; }
}
