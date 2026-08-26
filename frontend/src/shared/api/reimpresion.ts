import { api, unwrap } from "./client";
import type { ComprobanteImpresion } from "./facturacion";
import type { ArqueoX, CierreTurnoResultado } from "./caja";

/** Resultado de la búsqueda de comprobantes para reimprimir — mismo criterio que la búsqueda de
 * Nota de Crédito (número, cliente o CUIT + fecha), pero incluye facturas, notas de crédito y
 * presupuestos, y no calcula saldo anulable (no aplica acá). */
export interface ComprobanteReimpresion {
  idSucursal: number; idComprobante: number; numeroCompleto: string; letra?: string | null;
  tipoComprobante: string; fecha: string; idCliente?: number | null; clienteDescripcion?: string | null;
  total: number; estado: string;
}

/** Filtro de tipo del combo de Reimpresión — "" = todos. */
export type TipoReimpresion = "" | "Factura" | "NotaCredito" | "Presupuesto" | "Rendicion";

/** Resultado de búsqueda de rendiciones (cierres de turno) para reimprimir. */
export interface RendicionReimpresion {
  idSucursal: number; idLote: number; idCaja: number; descripcionCaja: string;
  cajero?: string | null; fechaCierre: string; numeroCierre?: number | null; total: number;
}

/** Misma forma que arma Caja al cerrar el turno — se reusa RendicionPdf.tsx sin cambios. */
export interface RendicionImpresion {
  arqueo: ArqueoX; cierre: CierreTurnoResultado; usuario: string;
  motivoDescripcion?: string | null; observaciones?: string | null;
}

export const reimpresion = {
  buscar: (idSucursal: number, texto: string, desde?: string, hasta?: string, tipo?: TipoReimpresion) => {
    const p = new URLSearchParams({ idSucursal: String(idSucursal), texto });
    if (desde) p.set("desde", desde);
    if (hasta) p.set("hasta", hasta);
    if (tipo) p.set("tipo", tipo);
    return unwrap<ComprobanteReimpresion[]>(api.get(`/reimpresion/comprobantes?${p}`));
  },
  /** Mismo armado que la vista inmediata post-emisión (ComprobanteImpresionView + window.print()). */
  impresion: (idSucursal: number, idComprobante: number) =>
    unwrap<ComprobanteImpresion>(api.get(`/reimpresion/${idComprobante}/impresion`, { params: { idSucursal } })),

  buscarRendiciones: (idSucursal: number, texto: string, desde?: string, hasta?: string) => {
    const p = new URLSearchParams({ idSucursal: String(idSucursal), texto });
    if (desde) p.set("desde", desde);
    if (hasta) p.set("hasta", hasta);
    return unwrap<RendicionReimpresion[]>(api.get(`/reimpresion/rendiciones?${p}`));
  },
  rendicion: (idSucursal: number, idLote: number) =>
    unwrap<RendicionImpresion>(api.get(`/reimpresion/rendiciones/${idLote}`, { params: { idSucursal } })),
};
