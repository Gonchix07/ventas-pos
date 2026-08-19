namespace Pos.Domain.Services;

/// <summary>
/// Control de crédito de cuenta corriente: el saldo adeudado (Debe acumulado - Haber acumulado)
/// más el nuevo cargo no puede superar el límite de crédito habilitado para ese cliente en esa
/// sucursal. Puro dominio: la orquestación (leer saldo/límite de BD, postear el asiento) vive en
/// FacturacionService.
/// </summary>
public static class CuentaCorrienteReglas
{
    public static bool PuedeAprobar(decimal saldoActual, decimal monto, decimal limiteCredito) =>
        saldoActual + monto <= limiteCredito;

    public static decimal CalcularSaldo(decimal totalDebe, decimal totalHaber) => totalDebe - totalHaber;
}
