/**
 * Abreviatura de tipo de comprobante para columnas de tabla angostas — mismas descripciones fijas
 * del seed de TiposComprobante (ver DbSeeder.cs): "Factura A/B", "Nota de Crédito A/B", "Presupuesto".
 * Usado en Reimpresión y en el popup de comprobantes por medio de pago de Tesorería.
 */
const ABREV_TIPO: Record<string, string> = {
  "Factura A": "FA", "Factura B": "FB",
  "Nota de Crédito A": "NCA", "Nota de Crédito B": "NCB",
  "Presupuesto": "P",
};

export const abreviarTipoComprobante = (t: string) => ABREV_TIPO[t] ?? t;
