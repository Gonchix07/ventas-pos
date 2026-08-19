import { api, unwrap } from "./client";

/** Debe coincidir con TipoAnulacion del backend (Pos.Domain.Services). */
export const TipoAnulacion = {
  Total: 1,
  PorArticulos: 2,
  PorMonto: 3,
} as const;
export type TipoAnulacion = (typeof TipoAnulacion)[keyof typeof TipoAnulacion];

export interface ComprobanteAnulable {
  idSucursal: number;
  idComprobante: number;
  numeroCompleto: string;
  letra?: string | null;
  fecha: string;
  idCliente?: number | null;
  clienteDescripcion?: string | null;
  total: number;
  /** Lo ya acreditado por notas de crédito anteriores. */
  yaAcreditado: number;
  saldoAnulable: number;
  anulable: boolean;
}

export interface LineaAnulable {
  idDetalleComprobante: number;
  idPresentacion: number;
  descripcionTicket: string;
  cantidad: number;
  precioUnit: number;
  descuento: number;
  alicuotaIva: number;
  importe: number;
  yaAnulada: boolean;
}

export interface ComprobanteAnulableDetalle {
  comprobante: ComprobanteAnulable;
  lineas: LineaAnulable[];
}

export interface NotaCreditoResultado {
  idSucursal: number;
  idComprobante: number;
  numeroCompleto: string;
  letra: string;
  cae?: string | null;
  caeVencimiento?: string | null;
  esCaea: boolean;
  estado: string;
  neto: number;
  iva: number;
  total: number;
  devueltoEnEfectivo: number;
  impreso: boolean;
  errorImpresion?: string | null;
}

export interface EmitirNotaCreditoRequest {
  idSucursal: number;
  idComprobanteOrigen: number;
  idCaja: number;
  tipo: TipoAnulacion;
  idsDetalle?: number[] | null;
  monto?: number | null;
  motivo?: string | null;
  // Null si quien emite ya es Supervisor/Administrador — ver shared/ui/SupervisorGate.tsx.
  codigoSupervisor?: string | null;
}

export const notasCredito = {
  buscar: (idSucursal: number, texto: string, desde?: string, hasta?: string) => {
    const p = new URLSearchParams({ idSucursal: String(idSucursal), texto });
    if (desde) p.set("desde", desde);
    if (hasta) p.set("hasta", hasta);
    return unwrap<ComprobanteAnulable[]>(api.get(`/notas-credito/comprobantes?${p}`));
  },

  obtener: (idSucursal: number, idComprobante: number) =>
    unwrap<ComprobanteAnulableDetalle>(
      api.get(`/notas-credito/comprobantes/${idComprobante}?idSucursal=${idSucursal}`),
    ),

  emitir: (req: EmitirNotaCreditoRequest) =>
    unwrap<NotaCreditoResultado>(api.post("/notas-credito/emitir", req)),
};
