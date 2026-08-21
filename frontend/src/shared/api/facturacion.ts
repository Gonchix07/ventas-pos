import { api, unwrap } from "./client";

export interface PagoInput {
  idMedioPago: number;
  monto: number;
  /** Obligatorios cuando el medio es de tipo Tarjeta: quedan para la rendición de cupones. */
  numeroCupon?: string | null;
  numeroLote?: string | null;
  /** Plan de cuotas elegido junto con el medio (solo Tarjeta; opcional). */
  idPlan?: number | null;
  /** Obligatorios cuando el medio es de tipo Cheque: banco emisor y número (hasta 8 caracteres). */
  idBanco?: number | null;
  numeroCheque?: string | null;
  /** Libre, solo Cheque — no se exige. */
  observacionesCheque?: string | null;
}

export interface PagoResultado {
  idMedioPago: number; monto: number; aprobado: boolean; idTransaccion?: string | null; error?: string | null;
}

export interface EmitirComprobanteResponse {
  idSucursal: number; idComprobante: number; numeroCompleto: string; letra: string;
  cae?: string | null; caeVencimiento?: string | null; esCaea: boolean; estado: string;
  neto: number; iva: number; total: number; pagos: PagoResultado[]; impreso: boolean; errorImpresion?: string | null;
  percepcionIva21: number; percepcionIva105: number; percepcionIibb: number;
  /** Alícuota (%) con la que se calculó percepcionIibb (0 si no corresponde). */
  alicuotaIibb: number;
  /** Sobrante devuelto en efectivo (0 si no hubo). Ya quedó registrado aparte como salida de caja. */
  vuelto: number;
}

// ---- Comprobante para imprimir (formatos A y B) ----

export interface EmisorComprobante {
  razonSocial: string; cuit?: string | null; condicionIva?: string | null;
  domicilio?: string | null; localidad?: string | null; provincia?: string | null; codigoPostal?: string | null;
  ingresosBrutos?: string | null; inicioActividad?: string | null;
}

export interface ClienteComprobante {
  descripcion: string; cuit?: string | null; documento?: string | null; condicionIva?: string | null;
  domicilio?: string | null; localidad?: string | null; provincia?: string | null; codigoPostal?: string | null;
}

/** En la A los importes vienen NETOS (el IVA se discrimina al pie); en la B, con IVA incluido. */
export interface LineaComprobante {
  descripcion: string; cantidad: number; precioUnitario: number; descuento: number;
  importe: number; alicuota: number;
}

export interface IvaDiscriminado { alicuota: number; base: number; importe: number; }
export interface PagoComprobante { descripcion: string; monto: number; }

export interface ComprobanteImpresion {
  idSucursal: number; idComprobante: number;
  tipoComprobante: string; letra: string; codigoArca?: string | null;
  numeroCompleto: string; fecha: string;
  emisor: EmisorComprobante; cliente: ClienteComprobante; lineas: LineaComprobante[];
  descuento: number; neto: number; iva: number; total: number;
  ivaDiscriminado: IvaDiscriminado[]; pagos: PagoComprobante[];
  cae?: string | null; caeVencimiento?: string | null; esCaea: boolean; estado: string;
  percepcionIva21: number; percepcionIva105: number; percepcionIibb: number;
  /** Alícuota (%) con la que se calculó percepcionIibb (0 si no corresponde). */
  alicuotaIibb: number;
}

export const facturacion = {
  // `letra` va por compatibilidad: el servidor resuelve la letra real según la condición de IVA
  // del cliente (A para Responsable Inscripto/Monotributista, B para el resto).
  emitir: (idSucursal: number, idOperacion: number, idPuntoVenta: number, modo: number, pagos: PagoInput[]) =>
    unwrap<EmitirComprobanteResponse>(api.post(`/facturacion/emitir`, { idSucursal, idOperacion, idPuntoVenta, modo, pagos })),
  /** Letra que le va a corresponder a la operación, para anticiparla en la pantalla de cobro. */
  letra: (idSucursal: number, idOperacion: number) =>
    unwrap<string>(api.get(`/facturacion/letra`, { params: { idSucursal, idOperacion } })),
  impresion: (idSucursal: number, idComprobante: number) =>
    unwrap<ComprobanteImpresion>(api.get(`/facturacion/${idComprobante}/impresion`, { params: { idSucursal } })),
};
