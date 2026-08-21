import { api, unwrap } from "./client";
import type { ComprobanteImpresion } from "./facturacion";

/** Resultado de la búsqueda de comprobantes para reimprimir — mismo criterio que la búsqueda de
 * Nota de Crédito (número, cliente o CUIT + fecha), pero incluye tanto facturas como notas de
 * crédito y no calcula saldo anulable (no aplica acá). */
export interface ComprobanteReimpresion {
  idSucursal: number; idComprobante: number; numeroCompleto: string; letra?: string | null;
  tipoComprobante: string; fecha: string; idCliente?: number | null; clienteDescripcion?: string | null;
  total: number; estado: string;
}

export const reimpresion = {
  buscar: (idSucursal: number, texto: string, desde?: string, hasta?: string) => {
    const p = new URLSearchParams({ idSucursal: String(idSucursal), texto });
    if (desde) p.set("desde", desde);
    if (hasta) p.set("hasta", hasta);
    return unwrap<ComprobanteReimpresion[]>(api.get(`/reimpresion/comprobantes?${p}`));
  },
  /** Mismo armado que la vista inmediata post-emisión (ComprobanteImpresionView + window.print()). */
  impresion: (idSucursal: number, idComprobante: number) =>
    unwrap<ComprobanteImpresion>(api.get(`/reimpresion/${idComprobante}/impresion`, { params: { idSucursal } })),
};
