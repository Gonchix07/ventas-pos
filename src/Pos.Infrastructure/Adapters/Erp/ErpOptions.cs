namespace Pos.Infrastructure.Adapters.Erp;

/// <summary>Cadena de conexión de solo lectura al ERP Central (DATA_PREV). El usuario configurado en
/// SQL Server no tiene permisos de escritura — este código, además, nunca arma un INSERT/UPDATE/DELETE
/// contra esa base.</summary>
public class ErpOptions
{
    public string ConnectionString { get; set; } = "";
}
