import { api, unwrap } from "./client";

/** Un lote de comprobantes emitidos bajo el mismo CAEA (contingencia), en el mismo punto de venta
 * y del mismo tipo — ARCA exige informar cada combinación por separado (FECAEARegInformativo).
 * Todavía no se subió a ARCA: por eso aparece acá. */
export interface LoteCaeaPendiente {
  idSucursal: number; sucursalDescripcion: string; idPuntoVenta: number; numeroPuntoVenta: number;
  idTipoComprobante: number; tipoDescripcion: string; letra?: string | null;
  caea: string; cantidad: number; total: number; fechaDesde: string; fechaHasta: string;
}

export interface ComprobanteCaea {
  idSucursal: number; idComprobante: number; numeroCompleto?: string | null; letra?: string | null;
  fecha: string; total: number; clienteDescripcion?: string | null;
}

export interface InformarLoteCaeaResultado { ok: boolean; error?: string | null; cantidadInformada: number; }

export const caeaLote = {
  pendientes: () => unwrap<LoteCaeaPendiente[]>(api.get("/caea-lote/pendientes")),
  comprobantes: (idSucursal: number, idPuntoVenta: number, idTipoComprobante: number, caea: string) =>
    unwrap<ComprobanteCaea[]>(api.get("/caea-lote/pendientes/comprobantes", {
      params: { idSucursal, idPuntoVenta, idTipoComprobante, caea },
    })),
  informar: (idSucursal: number, idPuntoVenta: number, idTipoComprobante: number, caea: string) =>
    unwrap<InformarLoteCaeaResultado>(api.post("/caea-lote/informar", {
      idSucursal, idPuntoVenta, idTipoComprobante, caea,
    })),
};
