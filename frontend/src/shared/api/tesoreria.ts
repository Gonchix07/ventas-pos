import { api, unwrap } from "./client";
import type { Acumulado, Anulacion, Retiro, Vuelto, DeclaracionPago, CierreTurnoResultado, PlanCuotaResumen } from "./caja";

export type { Acumulado, Anulacion, Retiro, Vuelto };

export interface MotivoCierre { id: number; descripcion: string; }

/**
 * Una fila de la vista principal: un lote (turno de cajero), abierto o cerrado, dentro de la
 * vigencia consultada. `estadoLote` sale del esquema (Abierto/Cerrado); `estadoCierre` es un
 * estado calculado en el backend — Abierto | CierreCajero | CierreTesoreria — que mira si TODAS
 * las filas de cierre por medio de pago ya fueron validadas por Tesorería.
 */
export interface LoteResumen {
  idSucursal: number; sucursalDescripcion?: string | null; idLote: number; idCaja: number; cajaDescripcion: string;
  usuario?: string | null; fechaApertura: string; fechaCierre?: string | null;
  estadoLote: string; estadoCierre: string;
  /** Fondo con el que abrió el turno (0 si no cargó ninguno). */
  saldoInicial: number;
  /** Rendición NETA (ventas + retiros + vueltos + correcciones), sin el saldo inicial. */
  rendicionTotal: number;
  /** Vuelto entregado en efectivo durante el turno. */
  cambioAcumulado: number;
  /** saldoInicial + rendicionTotal: lo que el sistema espera que haya en caja a este momento. */
  saldoEsperado: number;
  /** Lo declarado por el cajero al cerrar (todos los medios). Null si el lote sigue abierto. */
  saldo?: number | null;
}

/** Fondo inicial cargado al abrir el turno (a lo sumo uno por lote). */
export interface Ingreso { idMovCaja: number; fecha: string; idMedioPago: number; monto: number; concepto?: string | null; }

/** Corrección +/- de Tesorería sobre un lote, incluso ya cerrado. */
export interface Correccion {
  idMovCaja: number; fecha: string; idMedioPago: number; monto: number;
  concepto?: string | null; usuario?: string | null;
}

/**
 * Detalle de rendición de un lote (la subfila al expandir una fila de la tabla principal). Si el
 * lote sigue Abierto, `declarado` viene vacío. Si está Cerrado, es una foto de lo que declaró el
 * cajero al cerrar (no cambia aunque después se cargue una corrección: esa ya se ve en `acumulados`
 * y en `correcciones`, pero `declarado` queda como constancia de lo que se dijo en su momento).
 */
export interface LoteDetalle {
  idSucursal: number; idLote: number;
  acumulados: Acumulado[];
  declarado: CierreTurnoDetalle[];
  ingresoInicial?: Ingreso | null;
  retiros: Retiro[];
  vueltos: Vuelto[];
  correcciones: Correccion[];
  anulaciones: Anulacion[];
  /** Solo si el lote está cerrado. En el cierre Z normal del cajero suele ser null (solo se exige
   *  en un cierre administrativo de Tesorería sobre un lote pendiente). */
  motivoCierreDescripcion?: string | null;
  /** Texto libre que dejó el cajero al cerrar. */
  observacionesCajero?: string | null;
}
export interface CierreTurnoDetalle {
  idMedioPago: number; descripcion: string; esperado: number; declarado: number;
  diferencia: number; requiereMotivo: boolean;
}

/** Cabecera de un comprobante del lote (popup al hacer click en un valor por medio de pago). */
export interface ComprobanteLote {
  idComprobante: number; numeroCompleto?: string | null; letra?: string | null; tipoDescripcion: string;
  fecha: string; total: number; montoEnMedio: number;
  clienteCodigo?: string | null; clienteDescripcion?: string | null;
}

export interface CorreccionManualInput { idMedioPago: number; monto: number; concepto: string; }

/** Lookup liviano para el select de "Entrega de valores": Tesorería no tiene acceso al ABM de
 *  medios (Administrador) ni a /caja/medios-pago (Cajero). */
export interface MedioPagoLookup { id: number; descripcion: string; }

export const tesoreria = {
  motivosCierre: () => unwrap<MotivoCierre[]>(api.get(`/tesoreria/motivos-cierre`)),
  motivosDiferencia: () => unwrap<MotivoCierre[]>(api.get(`/tesoreria/motivos-diferencia`)),
  mediosPago: () => unwrap<MedioPagoLookup[]>(api.get(`/tesoreria/medios-pago`)),

  /** Sin fechas, el backend por default trae "ayer" en las dos puntas. */
  lotes: (idSucursal?: number, desde?: string, hasta?: string) =>
    unwrap<LoteResumen[]>(api.get(`/tesoreria/lotes`, { params: { idSucursal, desde, hasta } })),
  detalleLote: (idSucursal: number, idLote: number) =>
    unwrap<LoteDetalle>(api.get(`/tesoreria/lotes/${idLote}/detalle`, { params: { idSucursal } })),
  comprobantesLote: (idSucursal: number, idLote: number, idMedioPago?: number) =>
    unwrap<ComprobanteLote[]>(api.get(`/tesoreria/lotes/${idLote}/comprobantes`, { params: { idSucursal, idMedioPago } })),
  corregir: (idSucursal: number, idLote: number, input: CorreccionManualInput) =>
    unwrap<Correccion>(api.post(`/tesoreria/lotes/${idLote}/correccion`, input, { params: { idSucursal } })),

  validar: (idSucursal: number, idLote: number, idMotivoCierre: number | null, observacionTesoreria: string | null) =>
    unwrap<boolean>(api.post(`/tesoreria/cierres/${idLote}/validar`, { idMotivoCierre, observacionTesoreria }, { params: { idSucursal } })),
  cerrarLotePendiente: (idSucursal: number, idLote: number, declaraciones: DeclaracionPago[],
    idMotivoDiferencia: number | null, idMotivoCierre: number, observacionTesoreria: string | null) =>
    unwrap<CierreTurnoResultado>(api.post(`/tesoreria/lotes-pendientes/${idLote}/cerrar`,
      { declaraciones, idMotivoDiferencia, idMotivoCierre, observacionTesoreria }, { params: { idSucursal } })),
};

// ---- Cupones de tarjeta ----

export interface Cupon {
  idMovPagos: number; idSucursal: number; idLote: number; idCaja: number; fecha: string;
  idMedioPago: number; medioDescripcion: string; monto: number;
  numeroCupon?: string | null; numeroLote?: string | null;
  idPlanCuota?: number | null; planDescripcion?: string | null; cantidadCuotas?: number | null;
  cajero?: string | null; idComprobante?: number | null; numeroComprobante?: string | null;
  corregido: boolean;
  /** true si una nota de crédito de reversión completa anuló este pago (ya no se rinde). */
  anulado: boolean;
  fechaAnulacion?: string | null;
}

export interface CorregirCuponInput {
  numeroCupon: string | null; numeroLote: string | null; idPlanCuota: number | null; motivo: string;
}

export interface CorreccionCupon {
  idCorreccionCupon: number; fecha: string; usuario?: string | null; motivo: string;
  numeroCuponAnterior?: string | null; numeroLoteAnterior?: string | null; idPlanCuotaAnterior?: number | null;
  numeroCuponNuevo?: string | null; numeroLoteNuevo?: string | null; idPlanCuotaNuevo?: number | null;
}

export const cupones = {
  /** Sin fechas, el backend por default trae "ayer" en las dos puntas. */
  listar: (idSucursal?: number, desde?: string, hasta?: string, cajero?: string) =>
    unwrap<Cupon[]>(api.get(`/tesoreria/cupones`, { params: { idSucursal, desde, hasta, cajero } })),
  corregir: (idSucursal: number, idMovPagos: number, input: CorregirCuponInput) =>
    unwrap<Cupon>(api.put(`/tesoreria/cupones/${idMovPagos}`, input, { params: { idSucursal } })),
  historial: (idSucursal: number, idMovPagos: number) =>
    unwrap<CorreccionCupon[]>(api.get(`/tesoreria/cupones/${idMovPagos}/historial`, { params: { idSucursal } })),
  /** Planes de cuotas del medio de pago de ESE cupón puntual, para elegir al corregir. */
  planes: (idSucursal: number, idMovPagos: number) =>
    unwrap<PlanCuotaResumen[]>(api.get(`/tesoreria/cupones/${idMovPagos}/planes`, { params: { idSucursal } })),
};
