import { api, unwrap } from "./client";

/** `admitePresupuesto`: si ESTA caja habilita el modo Presupuesto (el cliente necesita además su
    propio permiso, ver ClienteResumen.permitePresupuesto — las dos condiciones se exigen juntas). */
export interface Lote {
  idSucursal: number; idLote: number; idCaja: number; descripcionCaja: string; idPuntoVenta: number;
  fechaApertura: string; estado: string; admitePresupuesto: boolean;
  /** "FISCAL" o "ELECTRONICA" (nunca "PRESUPUESTO" — ver ModoFacturacion en el DTO backend). */
  modoFacturacion: string;
}
/** `fuente`: 2 = Tarjetas (pide cupón y lote), 5 = Cuenta corriente, 7 = Gift Card (pide código y
    permite "Validar" antes de cobrar). `esPredeterminado`: el que la caja propone al abrir el cobro
    (se configura en Medios de pago). `imprimeComprobante`: si al cobrar con este medio hay que
    imprimir además un comprobante propio (ej. VALE) para la firma. */
export interface MedioPagoResumen {
  idMedioPago: number; descripcion: string; fuente: number; esPredeterminado: boolean;
  imprimeComprobante: boolean;
}

/** Consulta de saldo/cliente de una gift card (giftcards-app) SIN cobrar — ver GET
 *  /caja/giftcard/validar. Se usa para mostrarle al cajero qué está por aplicar antes de confirmar. */
export interface GiftcardConsulta {
  codigo: string; cliente?: string | null; comercio?: string | null; saldo?: number | null;
  montoMax?: number | null; usoParcial?: boolean | null; estado?: string | null;
  fechaVencimiento?: string | null;
}
/** Plan de cuotas de un medio Tarjeta, para elegir junto con el medio al cobrar. */
export interface PlanCuotaResumen { idPlan: number; denominacion: string; cantidadCuotas: number; }
/** Banco emisor, para el combo del pago con Cheque. */
export interface BancoResumen { idBanco: number; descripcion: string; }
/** Oferta por medio de pago vigente. idPlanCuota null = aplica en cualquier cantidad de cuotas. */
export interface OfertaMedioPagoVigente { idMedioPago: number; idPlanCuota?: number | null; porcentaje: number; topeMaximo: number; }

/** Persona autorizada a comprar en nombre del cliente (solo llegan las activas). */
export interface AutorizadoResumen { dni: string; descripcion: string; }

export interface ClienteResumen {
  idCliente: number; codigoInt: string; descripcion: string; nombreFantasia?: string | null; cuit?: string | null;
  documento?: string | null; permitePresupuesto: boolean; condIvaDescripcion?: string | null;
  idConvenio?: number | null; descuentoConvenio?: number | null;
  domicilio?: string | null; localidad?: string | null;
  nroTarjeta?: string | null; tipoTarjeta?: string | null; cantidadTarjetas: number;
  listaPrecioDescripcion?: string | null; listaPrecioOrigen?: string | null;
  autorizados?: AutorizadoResumen[] | null;
}

export interface ArticuloEncontrado {
  idArticulo: number; idPresentacion: number; codigoInterno: string; descripcion: string;
  descripcionTicket?: string | null; unidadXBulto: number; imagenUrl: string;
  precioVigente: number; precioConvenio: number; tieneConvenio: boolean;
  /** Kilos leídos del propio código de barra (etiqueta de balanza). Manda sobre la cantidad tipeada. */
  cantidadDetectada?: number | null;
}

export interface OperacionLinea {
  idDetalle: number; idPresentacion: number; codigoInterno: string; descripcion: string;
  cantidad: number; precioUnit: number; bruto: number; descuento: number; neto: number;
  ofertasAplicadas: string[];
  listaPrecio?: string | null; esPrecioFolder: boolean;
}

export interface Operacion {
  idSucursal: number; idOperacion: number; idCliente?: number | null; clienteDescripcion?: string | null;
  estado: string; lineas: OperacionLinea[]; bruto: number; descuento: number; neto: number;
  /** Percepción de IVA sobre el neto gravado al 21% (0 si no corresponde). */
  percepcionIva21: number;
  /** Percepción de IVA sobre el neto gravado al 10,5% (0 si no corresponde). */
  percepcionIva105: number;
  /** Percepción de Ingresos Brutos según el padrón del cliente (0 si no corresponde). */
  percepcionIibb: number;
  /** Alícuota (%) con la que se calculó percepcionIibb — la del padrón, o la general por defecto
      si el cliente tiene CUIT pero no está en el padrón (0 si no corresponde). */
  alicuotaIibb: number;
  /** neto + las 3 percepciones — el monto real a cobrar. */
  totalACobrar: number;
}

export interface TurnoAbierto {
  idSucursal: number; idLote: number; idCaja: number; descripcionCaja: string;
  idPuntoVenta: number; fechaAperturaUtc: string; ventasSinCobrar: number; esLaCajaDeEstaPc: boolean;
}

export interface CajaDisponible {
  idSucursal: number; idCaja: number; descripcion: string; idPuntoVenta: number;
}

export interface OperacionPendiente {
  idOperacion: number; fechaUtc: string; estado: string; cantidadLineas: number; total: number;
}

export interface Acumulado { idMedioPago: number; descripcion: string; total: number; redondeo: number; }
/**
 * Nota de crédito emitida en el turno. El importe YA viene restado del acumulado del medio de
 * pago (la devolución salió del cajón): esto es el detalle para justificarlo.
 */
export interface Anulacion {
  idComprobante: number; numeroCompleto: string; letra?: string | null; fecha: string;
  total: number; motivo?: string | null; comprobanteOrigen?: string | null;
}
/**
 * Retiro de efectivo del turno. El importe YA viene restado del acumulado de Efectivo (la plata
 * salió del cajón para enviarse a otro lado): esto es el detalle para justificarlo.
 */
export interface Retiro {
  idMovCaja: number; fecha: string; monto: number; concepto?: string | null; usuario?: string | null;
}
/**
 * Vuelto entregado en una venta con sobrante en Efectivo. El importe YA viene restado del
 * acumulado de Efectivo (mismo mecanismo que un retiro): esto es el detalle para justificarlo.
 */
export interface Vuelto {
  idMovCaja: number; fecha: string; monto: number; concepto?: string | null; usuario?: string | null;
}
/** Fondo inicial cargado al abrir el turno (ver TicketIngresoInicial) — a diferencia de
 * retiro/vuelto, suma al esperado en vez de restar. Como mucho hay uno por lote. */
export interface IngresoInicial {
  idMovCaja: number; fecha: string; idMedioPago: number; monto: number; concepto?: string | null;
}
export interface ArqueoX {
  idSucursal: number; idLote: number; idCaja: number; descripcionCaja: string; fechaApertura: string;
  acumulados: Acumulado[]; totalGeneral: number; referencia?: string | null;
  anulaciones: Anulacion[]; totalAnulaciones: number;
  retiros: Retiro[]; totalRetiros: number;
  vueltos: Vuelto[]; totalVueltos: number;
  ingresoInicial?: IngresoInicial | null;
  /** Efectivo acumulado en el lote (ya está adentro de acumulados/totalGeneral) y el tope
      configurado (Configuracion.LimiteEfectivoCaja) — 0 significa "sin límite cargado". */
  efectivoAcumulado: number;
  limiteEfectivoCaja: number;
  modoFacturacion: string;
}
export interface DeclaracionPago { idMedioPago: number; montoDeclarado: number; }
export interface CierreTurnoDetalle {
  idMedioPago: number; descripcion: string; esperado: number; declarado: number;
  diferencia: number; requiereMotivo: boolean;
}
// Sin referencia fiscal: el cierre de turno es negocio puro, no toca el controlador — ver
// CierreZFiscalResultado más abajo para el Z real.
export interface CierreTurnoResultado {
  idSucursal: number; idLote: number; numeroCierre: number; fechaCierre: string;
  detalle: CierreTurnoDetalle[]; diferenciaTotal: number;
  anulaciones: Anulacion[]; totalAnulaciones: number;
}

// ---- Cierre Z del controlador fiscal: operación de máquina, aparte del cierre de turno. No exige
// lote/turno abierto — se puede disparar desde la pantalla de apertura. Ver SupervisorGate.tsx. ----
export interface CierreZFiscalResultado {
  idSucursal: number; idCaja: number; fechaHoraUtc: string; numeroFiscal?: string | null;
}

// ---- Retiro de efectivo del turno (ver Retiro más arriba para cómo aparece en la rendición) ----
export interface RetiroEfectivoResultado {
  idSucursal: number; idMovCaja: number; monto: number; concepto?: string | null; fecha: string;
}

export interface Motivo { id: number; descripcion: string; }

export const caja = {
  // codigoSupervisor: solo hace falta si idCaja no es la que le corresponde a este puesto (PC
  // caída) y quien abre no es ya Supervisor/Administrador — ver shared/ui/SupervisorGate.tsx.
  // montoInicial: fondo de caja con el que arranca el turno (0 = sin fondo, default). Se usa en la
  // rendición de Tesorería como saldo inicial del lote.
  abrir: (idSucursal: number, idCaja: number, codigoSupervisor?: string | null, montoInicial?: number) =>
    unwrap<Lote>(api.post(`/caja/apertura`, { idSucursal, idCaja, codigoSupervisor, montoInicial })),
  descripcion: (idSucursal: number, idCaja: number) =>
    unwrap<string | null>(api.get(`/caja/descripcion`, { params: { idSucursal, idCaja } })),
  motivosDiferencia: () => unwrap<Motivo[]>(api.get(`/caja/motivos-diferencia`)),
  loteActual: (idSucursal: number, idCaja: number) =>
    unwrap<Lote>(api.get(`/caja/lote-actual`, { params: { idSucursal, idCaja } })),
  misTurnos: (idSucursal: number) =>
    unwrap<TurnoAbierto[]>(api.get(`/caja/mis-turnos`, { params: { idSucursal } })),
  cajas: (idSucursal: number) =>
    unwrap<CajaDisponible[]>(api.get(`/caja/cajas`, { params: { idSucursal } })),
  /** Con `idCliente` se excluyen los medios restringidos a un cluster al que no pertenece. */
  mediosPago: (idCliente?: number) =>
    unwrap<MedioPagoResumen[]>(api.get(`/caja/medios-pago`, { params: { idCliente } })),
  /** Vacío si el medio no tiene planes cargados (lo normal para medios que no son Tarjeta). */
  planesMedio: (idMedioPago: number) =>
    unwrap<PlanCuotaResumen[]>(api.get(`/caja/medios-pago/${idMedioPago}/planes`)),
  /** Para calcular en vivo, mientras se arma el cobro, cuánto se le informa al cliente que abona por medio. */
  ofertasMedioPagoVigentes: (idSucursal: number) =>
    unwrap<OfertaMedioPagoVigente[]>(api.get(`/caja/ofertas-medio-pago`, { params: { idSucursal } })),
  bancos: () => unwrap<BancoResumen[]>(api.get(`/caja/bancos`)),
  /** Consulta una gift card SIN cobrar (botón "Validar" del medio Gift Card en el cobro). */
  giftcardValidar: (codigo: string) =>
    unwrap<GiftcardConsulta>(api.get(`/caja/giftcard/validar`, { params: { codigo } })),
  // imprimir=false: solo trae los acumulados (ej. el preview de "Cerrar turno"), sin disparar la
  // impresión del reporte X en el controlador fiscal. El botón "Arqueo X" no manda el parámetro
  // (default true en el backend): ahí sí corresponde imprimir.
  arqueoX: (idSucursal: number, idCaja: number, imprimir?: boolean) =>
    unwrap<ArqueoX>(api.get(`/caja/arqueo-x`, { params: { idSucursal, idCaja, imprimir } })),
  cerrarTurno: (idSucursal: number, idCaja: number, declaraciones: DeclaracionPago[], idMotivoDiferencia: number | null, observacionesCajero: string | null) =>
    unwrap<CierreTurnoResultado>(api.post(`/caja/cerrar-turno`, { declaraciones, idMotivoDiferencia, observacionesCajero }, { params: { idSucursal, idCaja } })),
  // codigoSupervisor: null si quien ejecuta ya es Supervisor/Administrador — ver SupervisorGate.tsx.
  cierreZFiscal: (idSucursal: number, idCaja: number, codigoSupervisor: string | null) =>
    unwrap<CierreZFiscalResultado>(api.post(`/caja/cierre-z-fiscal`, { codigoSupervisor }, { params: { idSucursal, idCaja } })),
  // Resta del efectivo esperado en la rendición del turno; concepto queda como "Retiro" o
  // "Retiro: <lo que escriba el cajero>".
  retiroEfectivo: (idSucursal: number, idCaja: number, monto: number, concepto: string | null) =>
    unwrap<RetiroEfectivoResultado>(api.post(`/caja/retiro-efectivo`, { monto, concepto }, { params: { idSucursal, idCaja } })),

  buscarCliente: (idSucursal: number, q: string) =>
    unwrap<ClienteResumen[]>(api.get(`/caja/clientes/buscar`, { params: { idSucursal, q } })),
  buscarArticulo: (idSucursal: number, codigo: string, idCliente: number | null) =>
    unwrap<ArticuloEncontrado>(api.get(`/caja/articulos/buscar`, { params: { idSucursal, codigo, idCliente } })),
  /** Búsqueda manual (lupa): varios resultados por código, descripción o barra. */
  buscarArticulos: (idSucursal: number, texto: string, idCliente: number | null) =>
    unwrap<ArticuloEncontrado[]>(api.get(`/caja/articulos/buscar-lista`, { params: { idSucursal, texto, idCliente } })),

  operacionesPendientes: (idSucursal: number, idCaja: number, idCliente: number) =>
    unwrap<OperacionPendiente[]>(api.get(`/caja/operaciones/pendientes`, { params: { idSucursal, idCaja, idCliente } })),
  crearOperacion: (idSucursal: number, idCaja: number, idCliente: number | null) =>
    unwrap<Operacion>(api.post(`/caja/operaciones`, { idSucursal, idCaja, idCliente })),
  obtenerOperacion: (idSucursal: number, idOperacion: number) =>
    unwrap<Operacion>(api.get(`/caja/operaciones/${idOperacion}`, { params: { idSucursal } })),
  agregarLinea: (idSucursal: number, idOperacion: number, idPresentacion: number, cantidad: number) =>
    unwrap<Operacion>(api.post(`/caja/operaciones/${idOperacion}/lineas`, { idPresentacion, cantidad }, { params: { idSucursal } })),
  anularLinea: (idSucursal: number, idOperacion: number, idDetalle: number, codigoSupervisor?: string | null) =>
    unwrap<Operacion>(api.post(`/caja/operaciones/${idOperacion}/lineas/${idDetalle}/anular`, null,
      { params: { idSucursal, codigoSupervisor } })),
  /** Fija la cantidad de una línea (botones +/− de la tabla). Cantidad 0 = anular. */
  cambiarCantidad: (idSucursal: number, idOperacion: number, idDetalle: number, cantidad: number) =>
    unwrap<Operacion>(api.put(`/caja/operaciones/${idOperacion}/lineas/${idDetalle}/cantidad`, { cantidad }, { params: { idSucursal } })),
  finalizar: (idSucursal: number, idOperacion: number) =>
    unwrap<Operacion>(api.post(`/caja/operaciones/${idOperacion}/finalizar`, null, { params: { idSucursal } })),
  /** Vuelve una operación Finalizada a EnCurso (botón "Volver" desde la pantalla de cobro). */
  reabrir: (idSucursal: number, idOperacion: number) =>
    unwrap<Operacion>(api.post(`/caja/operaciones/${idOperacion}/reabrir`, null, { params: { idSucursal } })),
  redondeo: (total: number) => unwrap<{ ajuste: number; totalConRedondeo: number }>(api.get(`/caja/redondeo`, { params: { total } })),
};
