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
  /** Cuánto de esta línea ya se acreditó en notas de crédito anteriores. */
  cantidadYaAnulada: number;
  /** Lo que todavía se puede anular de esta línea (cantidad completa la primera vez, el resto si
   *  ya hubo una anulación parcial). Es el tope del campo "cantidad a anular". */
  cantidadDisponible: number;
  /** true solo cuando no queda nada disponible (cantidadDisponible <= 0). */
  yaAnulada: boolean;
}

export interface ComprobanteAnulableDetalle {
  comprobante: ComprobanteAnulable;
  lineas: LineaAnulable[];
}

/** Lo devuelto en UN medio de pago concreto (ver NotaCreditoResultado.devoluciones). */
export interface DevolucionMedio { idMedioPago: number; medioDescripcion: string; monto: number; }

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
  /** Desglose real de por dónde salió la plata (una fila por medio en una reversión completa). */
  devoluciones: DevolucionMedio[];
  /** true si esta NC revirtió TODOS los medios de pago originales (cupones incluidos), en vez de
   *  devolver un monto genérico en efectivo — ver NotaCreditoService.EmitirAsync. */
  reversionCompleta: boolean;
}

/** Una línea elegida en "Por artículos", con la cantidad puntual a acreditar (de 1 hasta
 *  LineaAnulable.cantidadDisponible de esa línea). */
export interface LineaSeleccionNc {
  idDetalle: number;
  cantidad: number;
}

export interface EmitirNotaCreditoRequest {
  idSucursal: number;
  idComprobanteOrigen: number;
  idCaja: number;
  tipo: TipoAnulacion;
  lineas?: LineaSeleccionNc[] | null;
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
