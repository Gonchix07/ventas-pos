import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import {
  caja, type ArqueoX, type ArticuloEncontrado, type BancoResumen, type CierreTurnoResultado, type CierreZFiscalResultado,
  type ClienteResumen, type DeclaracionPago, type CajaDisponible, type GiftcardConsulta, type Lote, type MedioPagoResumen, type Motivo,
  type Operacion, type OperacionLinea, type OperacionPendiente, type OfertaMedioPagoVigente,
  type PlanCuotaResumen, type TurnoAbierto,
} from "../../shared/api/caja";
import { GiftcardValidacionModal } from "./GiftcardValidacionModal";
import {
  facturacion, type ComprobanteImpresion, type EmitirComprobanteResponse, type PagoInput,
} from "../../shared/api/facturacion";
import { ComprobanteImpresionView } from "./ComprobanteImpresion";
import { NotaCreditoModal } from "./NotaCreditoModal";
import { RetiroEfectivoModal } from "./RetiroEfectivoModal";
import { IngresoInicialModal } from "./IngresoInicialModal";
import { TicketIngresoInicial } from "./TicketIngresoInicial";
import { ArqueoXTicket } from "./ArqueoXTicket";
import { ReporteCierreTurno } from "./ReporteCierreTurno";
import { VoucherComprobantePago, type ItemVoucherPago } from "./VoucherComprobantePago";
import { PuntosCargadosPopup } from "./PuntosCargadosPopup";
import { useLectorCodigo } from "../../shared/ui/useLectorCodigo";
import { useSupervisorGate } from "../../shared/ui/SupervisorGate";
import { MonedaInput, formatearMoneda } from "../../shared/ui/moneda";

// Hora en 24 h: es-AR resuelve a 12 h en Chrome ("01:15 p. m."), que en una caja se lee mal.
// El valor llega en UTC (con "Z"), así que el navegador ya lo pasa a la hora local del puesto.
function formatearHora(iso: string): string {
  return new Date(iso).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });
}

// Cantidad de una línea: entera para unidades, con 3 decimales para lo pesado (3,920 kg).
function formatearCantidad(c: number): string {
  return Number.isInteger(c) ? String(c) : c.toFixed(3);
}

// Color del globo de la lista de precios del cliente: las dos listas del negocio (AZUL y ROJA) se
// reconocen por nombre, cualquier otra usa el globo neutro.
function claseLista(lista: string): string {
  const l = lista.toUpperCase();
  if (l.includes("AZUL")) return "badge lista-azul";
  if (l.includes("ROJA") || l.includes("ROJO")) return "badge lista-roja";
  return "badge";
}

interface ColaItem {
  id: number;
  codigo: string;
  cantidad: number;
}

interface PagoForm {
  idMedioPago: number;
  /** Importe del pago; null = campo vacío (lo maneja MonedaInput, que formatea al escribir). */
  monto: number | null;
  // Solo se completan si el medio es de tipo Tarjeta (quedan para la rendición de cupones).
  numeroCupon: string;
  numeroLote: string;
  /** Plan de cuotas elegido junto con el medio (solo Tarjeta); null = sin elegir ninguno. */
  idPlan: number | null;
  // Solo se completan si el medio es de tipo Cheque (banco y número obligatorios, observaciones libre).
  idBanco: number | null;
  numeroCheque: string;
  observacionesCheque: string;
  // Solo se completa si el medio es de tipo Gift Card (código de 8 caracteres de giftcards-app).
  codigoGiftcard: string;
  /** Vacío = todavía no se confirmó el canje en el popup; con valor = ya se descontó saldo de
   *  verdad en giftcards-app (ver GiftcardValidacionModal) — el código/monto quedan fijos. */
  transaccionIdGiftcard: string;
}

/** Ver enum FuentePago en el backend. */
const FUENTE_TARJETA = 2;
const FUENTE_EFECTIVO = 1;
const FUENTE_CHEQUE = 6;
const FUENTE_GIFTCARD = 7;

// Mismo cálculo que OfertaMedioPagoReglas en el backend (Pos.Domain.Services): esto es solo para
// mostrarle al cajero en vivo cuánto se le informa al cliente que abona — el monto real que se
// factura y se cobra lo recalcula el servidor al emitir, esto nunca es la fuente de verdad.
function resolverOfertaMp(ofertas: OfertaMedioPagoVigente[], idMedioPago: number, idPlan: number | null) {
  const delMedio = ofertas.filter((o) => o.idMedioPago === idMedioPago);
  return delMedio.find((o) => (o.idPlanCuota ?? null) === idPlan)
    ?? (idPlan != null ? delMedio.find((o) => o.idPlanCuota == null) : undefined);
}
function calcularDescuentoMp(monto: number, oferta?: OfertaMedioPagoVigente) {
  if (!oferta || monto <= 0) return 0;
  const descuento = Math.round((monto * oferta.porcentaje / 100) * 100) / 100;
  return oferta.topeMaximo > 0 ? Math.min(descuento, oferta.topeMaximo) : descuento;
}

// Tapa toda la pantalla con blur + spinner mientras se espera una respuesta lenta (arqueo/cierre:
// varias consultas seguidas + la impresora fiscal). Sin esto el cajero ve la pantalla "congelada"
// unos segundos y tiende a hacer doble clic. CSS puro (ruedita animada), no un .gif — evita depender
// de un asset externo y funciona igual de bien.
function PantallaBloqueada({ mensaje }: { mensaje: string }) {
  return (
    <div className="pantalla-bloqueada" role="alert" aria-busy="true">
      <div className="pantalla-bloqueada-caja">
        <div className="spinner" aria-hidden="true" />
        <p>{mensaje}</p>
      </div>
    </div>
  );
}

export function CajaPage() {
  const { usuario, logout, idSucursal: idSucursalAuth, idCaja: idCajaAuth } = useAuth();
  const { ejecutarConSupervisor, modal: modalSupervisor } = useSupervisorGate();
  const navigate = useNavigate();

  // Resolución de sucursal/caja: normalmente viene del login (IP de la PC → puesto → caja). El
  // default 1/1 es solo para roles sin puesto asignado (Administrador/Tesorero) — pendiente un
  // selector manual para ese caso; no confundir con la resolución automática por IP.
  const [idSucursal] = useState<number>(idSucursalAuth ?? 1);
  // La caja puede cambiarse a mano DESPUÉS de identificado el puesto (ver el corte más abajo si
  // idCajaAuth es null): si la PC del cajero se rompe, se sienta en otra PC ya vinculada a un
  // puesto y retoma su turno (o abre uno) desde ahí.
  const [idCaja, setIdCaja] = useState<number>(idCajaAuth ?? 1);
  const [turnos, setTurnos] = useState<TurnoAbierto[]>([]);
  const [cajas, setCajas] = useState<CajaDisponible[]>([]);

  const [lote, setLote] = useState<Lote | null>(null);
  const [descripcionCaja, setDescripcionCaja] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loadingLote, setLoadingLote] = useState(true);

  const [busquedaCliente, setBusquedaCliente] = useState("");
  // Última búsqueda efectivamente ejecutada: sin esto, "Sin resultados" aparecía mientras se tipea.
  const [busquedaEjecutada, setBusquedaEjecutada] = useState("");
  const [resultadosCliente, setResultadosCliente] = useState<ClienteResumen[]>([]);
  const [clienteSel, setClienteSel] = useState<ClienteResumen | null>(null);
  const [clienteConfirmado, setClienteConfirmado] = useState(false);
  // Ventas del cliente que quedaron sin cobrar en este turno (recuperación ante caída del sistema).
  const [pendientes, setPendientes] = useState<OperacionPendiente[]>([]);

  const [operacion, setOperacion] = useState<Operacion | null>(null);

  // ---- Cola de lectura de artículos ----
  const [cantidadPendiente, setCantidadPendiente] = useState<number>(1);
  const [codigoInput, setCodigoInput] = useState("");
  const [cola, setCola] = useState<ColaItem[]>([]);
  // Fila que acaba de cambiar (nueva o con cantidad acumulada): se destella y se limpia sola.
  const [lineaResaltada, setLineaResaltada] = useState<number | null>(null);
  const [colaError, setColaError] = useState<{ codigo: string; mensaje: string } | null>(null);
  const procesando = useRef(false);
  const proxId = useRef(1);
  const inputCodigo = useRef<HTMLInputElement>(null);

  // ---- Búsqueda manual de artículo (la lupa del campo de escaneo) ----
  const [buscadorAbierto, setBuscadorAbierto] = useState(false);
  const [busquedaArt, setBusquedaArt] = useState("");
  const [resultadosArt, setResultadosArt] = useState<ArticuloEncontrado[]>([]);
  const [buscandoArt, setBuscandoArt] = useState(false);
  const [busquedaArtHecha, setBusquedaArtHecha] = useState<string | null>(null);
  const [agregandoPres, setAgregandoPres] = useState<number | null>(null);

  // ---- Cobro / facturación ----
  const [cobroActivo, setCobroActivo] = useState(false);
  const [mediosPago, setMediosPago] = useState<MedioPagoResumen[]>([]);
  // Ofertas por medio de pago vigentes de la sucursal: no dependen del cliente, se cargan una sola
  // vez al entrar (a diferencia de mediosPago, que sí varía según el cliente identificado).
  const [ofertasMedioPago, setOfertasMedioPago] = useState<OfertaMedioPagoVigente[]>([]);
  const [pagos, setPagos] = useState<PagoForm[]>([]);
  // Planes de cuotas por medio, cargados on-demand la primera vez que se elige ese medio en un
  // pago (la mayoría de los medios no son Tarjeta y nunca los necesita).
  const [planesPorMedio, setPlanesPorMedio] = useState<Record<number, PlanCuotaResumen[]>>({});
  // Bancos para el combo del pago con Cheque — no depende del cliente ni del medio, se carga una
  // sola vez al entrar (igual criterio que ofertasMedioPago).
  const [bancos, setBancos] = useState<BancoResumen[]>([]);
  const [emitiendo, setEmitiendo] = useState(false);
  const [volviendo, setVolviendo] = useState(false);
  const [comprobante, setComprobante] = useState<EmitirComprobanteResponse | null>(null);
  // true justo después de facturar cuando sumó puntos (comprobante.fidelizacion.ok) — el ticket para
  // imprimir queda tapado por el popup hasta que el cajero lo cierra (ver render de comprobante).
  const [puntosPopupVisible, setPuntosPopupVisible] = useState(false);
  // Comprobante ya armado en su formato de impresión (A o B). Se pide después de emitir.
  const [impresion, setImpresion] = useState<ComprobanteImpresion | null>(null);
  // Si se pagó con algún medio "Imprime comprobante" (ej. VALE), se imprime este ticket aparte para
  // que lo firme el empleado, ANTES de mostrar la pantalla del comprobante fiscal (no pueden convivir
  // los dos en el DOM al imprimir: window.print() imprimiría todo lo marcado .cbte a la vez).
  const [voucherPago, setVoucherPago] = useState<
    { fecha: Date; numeroComprobante: string; items: ItemVoucherPago[] } | null
  >(null);
  // Letra que le va a tocar a esta venta, para avisarla antes de cobrar.
  const [letraPrevista, setLetraPrevista] = useState<string | null>(null);
  // Presupuesto (comprobante X): solo si el cliente lo admite (clienteSel.permitePresupuesto).
  // No es fiscal ni electrónico, se cobra siempre en efectivo y no discrimina impuestos.
  const [modoPresupuesto, setModoPresupuesto] = useState(false);

  // ---- Notas de crédito ----
  const [notaCreditoAbierta, setNotaCreditoAbierta] = useState(false);

  // ---- Retiro de efectivo ----
  const [retiroAbierto, setRetiroAbierto] = useState(false);

  // ---- Bloqueo de pantalla mientras se espera una respuesta lenta (arqueo/cierre pegan varias
  // consultas + la impresora fiscal, y tardan unos segundos): se tapa la pantalla con blur para que
  // el cajero no piense que se colgó ni haga doble clic.
  const [bloqueando, setBloqueando] = useState<string | null>(null);

  // ---- Arqueo X ----
  const [arqueoActivo, setArqueoActivo] = useState(false);
  const [arqueo, setArqueo] = useState<ArqueoX | null>(null);

  // ---- Aviso de límite de efectivo en caja (Configuracion.LimiteEfectivoCaja): se refresca solo,
  // en silencio (sin bloquear pantalla ni imprimir nada), al abrir la caja y después de cada venta,
  // para sugerirle al cajero que haga un retiro sin que tenga que entrar a Arqueo X a mirarlo. */
  const [avisoEfectivo, setAvisoEfectivo] = useState<{ efectivo: number; limite: number } | null>(null);
  const revisarLimiteEfectivo = async () => {
    try {
      const x = await caja.arqueoX(idSucursal, idCaja, false);
      setAvisoEfectivo(x.limiteEfectivoCaja > 0 && x.efectivoAcumulado > x.limiteEfectivoCaja
        ? { efectivo: x.efectivoAcumulado, limite: x.limiteEfectivoCaja } : null);
    } catch {
      // Silencioso: sin lote abierto todavía, o cualquier otro motivo — no es un error para el
      // cajero, es solo un aviso opcional que no pudo calcularse esta vez.
    }
  };

  // ---- Cierre de turno (negocio, sobre el lote — separado del Cierre Z fiscal, ver más abajo) ----
  const [cierreActivo, setCierreActivo] = useState(false);
  const [declaraciones, setDeclaraciones] = useState<Record<number, number | null>>({});
  const [motivos, setMotivos] = useState<Motivo[]>([]);
  const [idMotivo, setIdMotivo] = useState<number | 0>(0);
  const [observacionCierre, setObservacionCierre] = useState("");
  const [cerrando, setCerrando] = useState(false);
  const [cierreResultado, setCierreResultado] = useState<CierreTurnoResultado | null>(null);

  // ---- Cierre Z del controlador fiscal: operación de máquina aparte, disponible desde la
  // pantalla de apertura (no exige turno abierto). Gateada por código de supervisor.
  const [cierreZFiscalResultado, setCierreZFiscalResultado] = useState<CierreZFiscalResultado | null>(null);
  const [ejecutandoZFiscal, setEjecutandoZFiscal] = useState(false);

  useEffect(() => {
    void cargarLote();
    void cargarMediosPago();
    caja.ofertasMedioPagoVigentes(idSucursal).then(setOfertasMedioPago).catch(() => {});
    caja.bancos().then(setBancos).catch(() => {});
    caja.descripcion(idSucursal, idCaja).then(setDescripcionCaja).catch(() => {});
    caja.misTurnos(idSucursal).then(setTurnos).catch(() => {});
    caja.cajas(idSucursal).then(setCajas).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Recibe la caja como parámetro: cuando se cambia de caja hay que consultar la NUEVA, y el estado
  // recién seteado todavía no se ve dentro de este closure.
  const cargarLote = async (cajaObjetivo = idCaja) => {
    setLoadingLote(true);
    try {
      const l = await caja.loteActual(idSucursal, cajaObjetivo);
      setLote(l);
      void revisarLimiteEfectivo();
    } catch {
      setLote(null);
    } finally {
      setLoadingLote(false);
    }
  };

  const cambiarCaja = async (nueva: number) => {
    setError(null);
    setIdCaja(nueva);
    setDescripcionCaja(cajas.find((c) => c.idCaja === nueva)?.descripcion ?? null);
    setCierreZFiscalResultado(null); // era de la caja anterior, no de esta
    await cargarLote(nueva);
  };

  const retomarTurno = async (t: TurnoAbierto) => {
    setError(null);
    setIdCaja(t.idCaja);
    setDescripcionCaja(t.descripcionCaja);
    await cargarLote(t.idCaja);
  };

  // Abrir una caja que no es la del propio puesto (PC caída, se sigue vendiendo desde otra) pide
  // autorización de supervisor; abrir la propia, no — ni siquiera pasa por el gate. Antes de abrir
  // de verdad, se pide el fondo inicial (IngresoInicialModal) — si hace falta código de supervisor,
  // se resuelve PRIMERO (vía el gate) para tenerlo ya validado cuando se confirme el monto.
  const [aperturaPendiente, setAperturaPendiente] = useState<{ codigoSupervisor: string | null } | null>(null);
  // Si el fondo inicial fue > 0, se imprime un ticket aparte (ver TicketIngresoInicial) apenas la
  // caja queda abierta.
  const [ingresoAImprimir, setIngresoAImprimir] = useState<{ fecha: Date; monto: number } | null>(null);
  const abrirCaja = () => {
    const enOtroPuesto = idCajaAuth !== null && idCaja !== idCajaAuth;
    if (enOtroPuesto) {
      return ejecutarConSupervisor(async (codigoSupervisor) => {
        setError(null);
        setAperturaPendiente({ codigoSupervisor });
      });
    }
    setError(null);
    setAperturaPendiente({ codigoSupervisor: null });
  };

  // ---- Identificación de cliente ----
  // Acepta un valor explícito: en Enter (lector de barra/teclado rápido) el estado controlado
  // puede no haber comprometido aún el último carácter tipeado, así que se lee del DOM.
  const buscarCliente = async (valor?: string) => {
    const q = (valor ?? busquedaCliente).trim();
    setError(null);
    if (!q) return;
    try {
      const r = await caja.buscarCliente(idSucursal, q);
      setResultadosCliente(r);
      setBusquedaEjecutada(q);
      if (r.length === 1) seleccionarCliente(r[0]);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // Los medios dependen del cliente: un medio puede estar limitado a un cluster y solo se ofrece a
  // los clientes que pertenecen a él. Sin cliente se traen los que no tienen restricción.
  const cargarMediosPago = async (idCliente?: number) => {
    try { setMediosPago(await caja.mediosPago(idCliente)); }
    catch { /* el cobro avisa si quedó sin medios */ }
  };

  // Siempre con cliente: no hay venta anónima desde caja (se quitó "Continuar sin cliente").
  const seleccionarCliente = async (c: ClienteResumen) => {
    setClienteSel(c);
    setResultadosCliente([]);
    setError(null);
    await cargarMediosPago(c.idCliente);
    try {
      // Si el cliente tiene ventas sin terminar en este turno (caída del sistema, F5, o el cajero
      // se cambió de cliente sin cobrar), se ofrece retomarlas antes de arrancar una nueva.
      const pend = await caja.operacionesPendientes(idSucursal, idCaja, c.idCliente);
      if (pend.length > 0) { setPendientes(pend); return; }
      await iniciarVenta(c);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const iniciarVenta = async (c: ClienteResumen) => {
    setError(null);
    try {
      setPendientes([]);
      setClienteConfirmado(true);
      setOperacion(await caja.crearOperacion(idSucursal, idCaja, c.idCliente));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const retomarOperacion = async (p: OperacionPendiente) => {
    setError(null);
    try {
      const op = await caja.obtenerOperacion(idSucursal, p.idOperacion);
      setOperacion(op);
      setPendientes([]);
      setClienteConfirmado(true);
      // Una operación Finalizada ya no admite más artículos: lo único pendiente es cobrarla.
      if (op.estado === "Finalizada") {
        setModoPresupuesto(false);
        setPagos(mediosPago.length ? [nuevoPago()] : []);
        void cargarLetra(op.idOperacion);
        setCobroActivo(true);
      }
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const volverAIdentificar = () => {
    setPendientes([]);
    setClienteSel(null);
  };

  // "Anular Operación": vuelve a la selección de clientes SIN borrar nada. La operación ya está
  // persistida desde que se creó en iniciarVenta (con o sin líneas escaneadas), así que abandonarla
  // acá simplemente la deja "sin terminar" — al volver a identificar a este mismo cliente aparece en
  // la lista de "Ventas sin terminar" (ver operacionesPendientes) para retomarla más adelante.
  const anularOperacion = () => {
    if (operacion && operacion.lineas.length > 0) {
      if (!window.confirm("¿Anular la operación actual? Los artículos ya escaneados quedan guardados y se puede retomar la venta más adelante desde este mismo cliente.")) {
        return;
      }
    }
    setOperacion(null);
    setClienteSel(null);
    setClienteConfirmado(false);
    setCola([]);
    setColaError(null);
    setCodigoInput("");
  };

  // ---- Cola de artículos ----
  // Idem búsqueda de cliente: acepta valor explícito para no perder el último carácter
  // cuando el Enter llega pegado al texto (lector de código de barras tipo wedge).
  const encolar = (valor?: string) => {
    const codigo = (valor ?? codigoInput).trim();
    if (!codigo) return;
    setCola((q) => [...q, { id: proxId.current++, codigo, cantidad: cantidadPendiente }]);
    setCodigoInput("");
    setCantidadPendiente(1); // se borra una vez procesado (SRS)
  };

  useEffect(() => {
    void procesarCola();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cola, colaError]);

  useEffect(() => {
    if (lineaResaltada === null) return;
    const t = setTimeout(() => setLineaResaltada(null), 1600);
    return () => clearTimeout(t);
  }, [lineaResaltada]);

  // ---- Lector de código de barras sin depender del foco ----
  // El lector escribe como un teclado: si el foco no está en el input de escaneo (el cajero apretó
  // un botón, hizo clic en la tabla, etc.) la lectura se perdía. Estos dos hooks la levantan igual,
  // cada uno en la pantalla que corresponde.
  const enPantallaDeVenta = !!lote && clienteConfirmado && !cobroActivo && !comprobante
    && !arqueoActivo && !cierreActivo;
  const enIdentificacion = !!lote && !clienteConfirmado && pendientes.length === 0
    && !arqueoActivo && !cierreActivo;

  useLectorCodigo({ activo: enPantallaDeVenta && !!operacion && !colaError && !buscadorAbierto, onCodigo: encolar });
  useLectorCodigo({ activo: enIdentificacion, onCodigo: (c) => void buscarCliente(c) });

  // El foco vuelve solo al campo de escaneo cuando la cola queda libre: así la próxima lectura entra
  // por el camino normal y el cajero puede además tipear el código a mano sin buscar el campo.
  useEffect(() => {
    if (enPantallaDeVenta && cola.length === 0 && !colaError) inputCodigo.current?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enPantallaDeVenta, cola.length, colaError, operacion?.lineas.length]);

  const procesarCola = async () => {
    if (procesando.current || colaError || cola.length === 0 || !operacion) return;
    procesando.current = true;
    const item = cola[0];
    try {
      const art = await caja.buscarArticulo(idSucursal, item.codigo, clienteSel?.idCliente ?? null);
      // Etiqueta de balanza: el peso viene DENTRO del código de barra y manda sobre la cantidad
      // tipeada (el cajero no tiene por qué saber cuánto pesa el paquete).
      const cantidad = art.cantidadDetectada ?? item.cantidad;
      const op = await caja.agregarLinea(idSucursal, operacion.idOperacion, art.idPresentacion, cantidad);
      // Un artículo repetido no crea fila nueva (el backend acumula en la existente), así que se
      // resalta la fila que cambió para que el cajero vea el efecto del último escaneo.
      const tocada = op.lineas.find((l) => {
        const antes = operacion.lineas.find((p) => p.idDetalle === l.idDetalle);
        return !antes || antes.cantidad !== l.cantidad;
      });
      setLineaResaltada(tocada?.idDetalle ?? null);
      setOperacion(op);
      setCola((q) => q.slice(1));
    } catch (e) {
      // Advertencia y STOP de la cola hasta resolución (SRS).
      setColaError({ codigo: item.codigo, mensaje: e instanceof Error ? e.message : "Artículo no encontrado" });
    } finally {
      procesando.current = false;
    }
  };

  const descartarError = () => {
    setColaError(null);
    setCola((q) => q.slice(1));
  };

  const anularLinea = (idDetalle: number) => ejecutarConSupervisor(async (codigoSupervisor) => {
    if (!operacion) return;
    setError(null);
    try {
      setOperacion(await caja.anularLinea(idSucursal, operacion.idOperacion, idDetalle, codigoSupervisor));
    } catch (e) {
      const mensaje = e instanceof Error ? e.message : "Error";
      setError(mensaje);
      throw e;
    }
  });

  // Deja la operación nueva y destella la fila que cambió (misma señal que al escanear).
  const aplicarOperacion = (op: Operacion) => {
    const tocada = op.lineas.find((l) => {
      const antes = operacion?.lineas.find((p) => p.idDetalle === l.idDetalle);
      return !antes || antes.cantidad !== l.cantidad;
    });
    setLineaResaltada(tocada?.idDetalle ?? null);
    setOperacion(op);
  };

  // +/- de la tabla: el backend recalcula las ofertas de TODA la operación, no solo de esta línea.
  const cambiarCantidad = async (l: OperacionLinea, delta: number) => {
    if (!operacion) return;
    const nueva = l.cantidad + delta;
    if (nueva < 1) return; // para sacar el artículo está Anular, así no se borra sin querer
    setError(null);
    try { aplicarOperacion(await caja.cambiarCantidad(idSucursal, operacion.idOperacion, l.idDetalle, nueva)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // ---- Buscador manual (lupa) ----
  const abrirBuscador = () => {
    setBuscadorAbierto(true);
    setBusquedaArt(codigoInput.trim());
    setResultadosArt([]);
    setBusquedaArtHecha(null);
  };

  const cerrarBuscador = () => {
    setBuscadorAbierto(false);
    setResultadosArt([]);
    setBusquedaArtHecha(null);
    inputCodigo.current?.focus();
  };

  const buscarArticulosManual = async (valor?: string) => {
    const t = (valor ?? busquedaArt).trim();
    if (t.length < 2) { setResultadosArt([]); setBusquedaArtHecha(null); return; }
    setError(null);
    setBuscandoArt(true);
    try {
      setResultadosArt(await caja.buscarArticulos(idSucursal, t, clienteSel?.idCliente ?? null));
      setBusquedaArtHecha(t);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setBuscandoArt(false); }
  };

  // Se agrega directo por presentación (ya se sabe cuál eligió el cajero), sin pasar por la cola
  // de escaneo. Agregado el artículo se cierra el buscador y el foco vuelve al campo de escaneo,
  // que es lo normal: la lupa es la excepción, escanear es el caso habitual.
  const agregarDesdeBuscador = async (art: ArticuloEncontrado) => {
    if (!operacion) return;
    setError(null);
    setAgregandoPres(art.idPresentacion);
    try {
      aplicarOperacion(await caja.agregarLinea(idSucursal, operacion.idOperacion, art.idPresentacion,
        cantidadPendiente || 1));
      setCantidadPendiente(1);
      cerrarBuscador();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setAgregandoPres(null); }
  };

  // ---- Pasar a cobro ----
  const irACobrar = async () => {
    if (!operacion) return;
    setError(null);
    try {
      const op = await caja.finalizar(idSucursal, operacion.idOperacion);
      setOperacion(op);
      setModoPresupuesto(false);
      setPagos(mediosPago.length ? [nuevoPago()] : []);
      void cargarLetra(op.idOperacion);
      setCobroActivo(true);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // ---- Volver de cobro al carrito ----
  // "finalizar" deja la operación en Finalizada en el servidor (para que no se le puedan seguir
  // agregando artículos mientras se cobra). Si el cajero se arrepiente y aprieta "Volver" hay que
  // revertir eso en el servidor con "reabrir" — antes esto solo hacía setCobroActivo(false) y la
  // operación quedaba Finalizada: al volver a tocar el carrito, agregar/anular línea fallaba con
  // "La operación ya fue finalizada o anulada" porque el backend seguía exigiendo EnCurso.
  const volverAlCarrito = async () => {
    if (!operacion) { setCobroActivo(false); return; }
    setError(null);
    setVolviendo(true);
    try {
      setOperacion(await caja.reabrir(idSucursal, operacion.idOperacion));
      setCobroActivo(false);
    } catch (e) { setError(e instanceof Error ? e.message : "Error al volver al carrito"); }
    finally { setVolviendo(false); }
  };

  // Medio que la caja propone: el marcado como predeterminado en el ABM (normalmente Efectivo).
  // Si nadie lo marcó, cae al primero de la lista para no dejar el cobro sin medio.
  const medioPorDefecto = () =>
    mediosPago.find((m) => m.esPredeterminado)?.idMedioPago ?? mediosPago[0]?.idMedioPago ?? 0;

  // Monto SIEMPRE arranca vacío: el cajero tiene que cargar a mano cuánto recibió en cada medio
  // (no se le sugiere ni el total ni el faltante) — así puede cargar de verdad la plata que tiene
  // en la mano, aunque sea más que lo que corresponde (vuelto, ver calcularVuelto más abajo).
  const nuevoPago = (): PagoForm =>
    ({ idMedioPago: medioPorDefecto(), monto: null, numeroCupon: "", numeroLote: "", idPlan: null,
      idBanco: null, numeroCheque: "", observacionesCheque: "", codigoGiftcard: "", transaccionIdGiftcard: "" });

  const esTarjeta = (idMedioPago: number) =>
    mediosPago.find((m) => m.idMedioPago === idMedioPago)?.fuente === FUENTE_TARJETA;

  const esEfectivo = (idMedioPago: number) =>
    mediosPago.find((m) => m.idMedioPago === idMedioPago)?.fuente === FUENTE_EFECTIVO;

  const esCheque = (idMedioPago: number) =>
    mediosPago.find((m) => m.idMedioPago === idMedioPago)?.fuente === FUENTE_CHEQUE;

  const esGiftcard = (idMedioPago: number) =>
    mediosPago.find((m) => m.idMedioPago === idMedioPago)?.fuente === FUENTE_GIFTCARD;

  // "Validar" trae los datos y recién ahí abre el popup "Confirmar uso" (GiftcardValidacionModal) —
  // ese popup es el que de verdad canjea, acá solo se consulta (sin descontar saldo). Un solo popup
  // a la vez (guardamos también el índice de fila, para saber dónde volcar el resultado al confirmar).
  const [giftcardModal, setGiftcardModal] = useState<{ i: number; info: GiftcardConsulta } | null>(null);
  const [giftcardValidando, setGiftcardValidando] = useState<number | null>(null);
  const [giftcardError, setGiftcardError] = useState<Record<number, string>>({});

  const abrirValidarGiftcard = async (i: number, codigo: string) => {
    const cod = codigo.trim().toUpperCase();
    setGiftcardError((prev) => { const n = { ...prev }; delete n[i]; return n; });
    if (cod.length !== 8) {
      setGiftcardError((prev) => ({ ...prev, [i]: "El código debe tener 8 caracteres." }));
      return;
    }
    setGiftcardValidando(i);
    try {
      const info = await caja.giftcardValidar(cod);
      setGiftcardModal({ i, info });
    } catch (e) {
      setGiftcardError((prev) => ({ ...prev, [i]: e instanceof Error ? e.message : "No se pudo validar." }));
    } finally {
      setGiftcardValidando(null);
    }
  };

  // Se piden una sola vez por medio y quedan en caché: la mayoría de los medios no son Tarjeta y
  // nunca los necesitan, así que no tiene sentido traer los planes de todos de entrada.
  const asegurarPlanesDe = (idMedioPago: number) => {
    if (idMedioPago in planesPorMedio) return;
    setPlanesPorMedio((ps) => ({ ...ps, [idMedioPago]: [] })); // corta pedidos duplicados mientras llega
    caja.planesMedio(idMedioPago).then((ps) => {
      setPlanesPorMedio((prev) => ({ ...prev, [idMedioPago]: ps }));
      // Elegir un plan es obligatorio: se preselecciona el primero (todo medio Tarjeta tiene al
      // menos el "1 cuota" por defecto) para no dejar el cobro trabado esperando que el cajero
      // note que falta elegir algo que no sabía que existía.
      if (ps.length > 0) {
        setPagos((prevPagos) => prevPagos.map((p) =>
          p.idMedioPago === idMedioPago && p.idPlan === null ? { ...p, idPlan: ps[0].idPlan } : p));
      }
    }).catch(() => {});
  };

  // El presupuesto se cobra siempre con un único pago en efectivo, sin combinar medios: al
  // activarlo se fuerza ese único pago a Efectivo (el medio queda bloqueado, no elegible) pero el
  // monto arranca vacío igual que en el cobro normal — el cajero carga a mano lo que recibió, con
  // el mismo vuelto si entrega de más.
  const toggleModoPresupuesto = (activo: boolean) => {
    setModoPresupuesto(activo);
    if (!operacion) return;
    if (activo) {
      const efectivo = mediosPago.find((m) => m.fuente === FUENTE_EFECTIVO);
      setPagos(efectivo
        ? [{ idMedioPago: efectivo.idMedioPago, monto: null, numeroCupon: "", numeroLote: "", idPlan: null,
            idBanco: null, numeroCheque: "", observacionesCheque: "", codigoGiftcard: "", transaccionIdGiftcard: "" }]
        : []);
    } else {
      setPagos(mediosPago.length ? [nuevoPago()] : []);
    }
  };

  const totalPagos = pagos.reduce((acc, p) => acc + (p.monto ?? 0), 0);

  const setPago = (i: number, patch: Partial<PagoForm>) =>
    setPagos((ps) => ps.map((p, idx) => (idx === i ? { ...p, ...patch } : p)));

  // Cambiar de medio invalida cualquier plan ya elegido (es de OTRO medio) y, si el nuevo es
  // Tarjeta, dispara la carga de sus planes.
  const elegirMedioPago = (i: number, idMedioPago: number) => {
    if (esTarjeta(idMedioPago)) {
      asegurarPlanesDe(idMedioPago);
      // Si ya estaban en caché de un pago anterior, se preselecciona de una; si no, lo hace
      // asegurarPlanesDe cuando termine de traerlos.
      const cache = planesPorMedio[idMedioPago];
      setPago(i, { idMedioPago, idPlan: cache && cache.length > 0 ? cache[0].idPlan : null });
    } else {
      // Cambiar de medio invalida cualquier canje de gift card ya confirmado en la fila (era de
      // OTRO medio) — si el cajero vuelve a elegir Gift Card, arranca de cero.
      setPago(i, { idMedioPago, idPlan: null, codigoGiftcard: "", transaccionIdGiftcard: "" });
    }
  };

  const agregarPago = () => {
    if (!mediosPago.length || !operacion) return;
    setPagos((ps) => [...ps, nuevoPago()]);
  };

  const quitarPago = (i: number) => {
    // La gift card ya canjeada (transaccionIdGiftcard) descontó saldo de verdad en giftcards-app —
    // no hay reversión automática, así que sacarla del cobro acá NO la devuelve. Se avisa antes de
    // dejar tirar el pago al vacío.
    const p = pagos[i];
    if (p && esGiftcard(p.idMedioPago) && p.transaccionIdGiftcard
      && !confirm("Esta gift card ya fue canjeada (se descontó el saldo en giftcards-app). Quitarla del cobro NO revierte el canje — hay que revertirlo a mano en giftcards-app si corresponde. ¿Quitar igual?")) {
      return;
    }
    setPagos((ps) => ps.filter((_, idx) => idx !== i));
  };

  // Cubre el caso en que un pago arranca siendo tarjeta sin haber pasado por elegirMedioPago (ej.
  // el medio predeterminado del sistema es una tarjeta). asegurarPlanesDe ya es idempotente.
  useEffect(() => {
    pagos.forEach((p) => { if (esTarjeta(p.idMedioPago)) asegurarPlanesDe(p.idMedioPago); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pagos]);

  // La letra sale de la condición de IVA del cliente (A para Responsable Inscripto/Monotributista).
  // Se anticipa en pantalla para que el cajero sepa qué comprobante va a salir antes de cobrar.
  const cargarLetra = async (idOperacion: number) => {
    try { setLetraPrevista(await facturacion.letra(idSucursal, idOperacion)); }
    catch { setLetraPrevista(null); }
  };

  const confirmarCobro = async () => {
    if (!operacion || !lote) return;
    setError(null);
    setEmitiendo(true);
    // Facturar ya no es instantáneo: Electrónica pide un CAE real contra ARCA (puede tardar unos
    // segundos) — el mismo popup con spinner que ya se usa para el arqueo/cierre, para que el
    // cajero no piense que la pantalla se colgó.
    setBloqueando(modoPresupuesto ? "Generando presupuesto…" : "Facturando…");
    try {
      const pagosInput: PagoInput[] = pagos
        .map((p) => ({
          idMedioPago: p.idMedioPago,
          monto: p.monto ?? 0,
          // El cupón, el lote y el plan solo viajan si el medio es tarjeta; en el resto el backend los ignora.
          numeroCupon: esTarjeta(p.idMedioPago) ? p.numeroCupon.trim() || null : null,
          numeroLote: esTarjeta(p.idMedioPago) ? p.numeroLote.trim() || null : null,
          idPlan: esTarjeta(p.idMedioPago) ? p.idPlan : null,
          // Banco, número y observaciones solo viajan si el medio es Cheque; en el resto el backend los ignora.
          idBanco: esCheque(p.idMedioPago) ? p.idBanco : null,
          numeroCheque: esCheque(p.idMedioPago) ? p.numeroCheque.trim() || null : null,
          observacionesCheque: esCheque(p.idMedioPago) ? p.observacionesCheque.trim() || null : null,
          codigoGiftcard: esGiftcard(p.idMedioPago) ? p.codigoGiftcard.trim().toUpperCase() || null : null,
          transaccionIdGiftcard: esGiftcard(p.idMedioPago) ? p.transaccionIdGiftcard || null : null,
        }))
        .filter((p) => p.monto > 0);
      // La letra (A, B o X) la resuelve el servidor. En Presupuesto, modo=0: sin CAE, sin
      // impresora fiscal, siempre efectivo — el servidor además ignora idPuntoVenta y usa el
      // punto de venta de tipo Presupuesto de la sucursal.
      const resp = await facturacion.emitir(idSucursal, operacion.idOperacion, lote.idPuntoVenta,
        modoPresupuesto ? 0 : 2, pagosInput);
      // El popup con spinner tiene que seguir tapando la pantalla hasta que el comprobante para
      // imprimir esté armado, no solo hasta tener el CAE: si se llamara a setComprobante(resp) acá
      // y recién después se pidiera la impresión, React ya cambiaría a la pantalla del comprobante
      // (más abajo) mostrando por un instante el resumen simple de respaldo en vez del ticket real
      // — se arman los dos datos primero y se setean juntos al final, ya con "bloqueando" activo.
      let impresionResp: ComprobanteImpresion | null = null;
      try {
        impresionResp = await facturacion.impresion(idSucursal, resp.idComprobante);
      } catch {
        // El comprobante YA se emitió: si falla el armado para imprimir se muestra el resumen
        // simple, nunca se pierde la venta.
        impresionResp = null;
      }
      setComprobante(resp);
      setImpresion(impresionResp);
      // El popup de puntos SOLO se muestra cuando realmente sumó (ok=true) — si la integración está
      // deshabilitada, el cliente no tiene tarjeta, etc., se salta directo al comprobante, igual que
      // hoy (ver FidelizacionResult en facturacion.ts).
      setPuntosPopupVisible(Boolean(resp.fidelizacion?.ok));

      // Medios marcados "Imprime comprobante" (ej. VALE): se junta un ticket aparte con lo pagado
      // en esos medios, para que el empleado lo firme.
      const itemsVoucher: ItemVoucherPago[] = pagosInput
        .filter((p) => mediosPago.find((m) => m.idMedioPago === p.idMedioPago)?.imprimeComprobante)
        .map((p) => ({
          descripcionMedio: mediosPago.find((m) => m.idMedioPago === p.idMedioPago)?.descripcion ?? "",
          monto: p.monto,
        }));
      if (itemsVoucher.length > 0) {
        setVoucherPago({ fecha: new Date(), numeroComprobante: resp.numeroCompleto, items: itemsVoucher });
      }
      // Recién cobrado puede haber cruzado el límite de efectivo en caja — se revisa en silencio
      // después de cada venta, no solo al abrir la caja.
      void revisarLimiteEfectivo();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error al emitir el comprobante");
    } finally {
      setEmitiendo(false);
      setBloqueando(null);
    }
  };

  const nuevaVenta = () => {
    setComprobante(null); setImpresion(null); setPuntosPopupVisible(false); setVoucherPago(null); setCobroActivo(false); setPagos([]);
    setOperacion(null); setClienteSel(null); setClienteConfirmado(false); setPendientes([]);
    setBusquedaCliente(""); setBusquedaEjecutada(""); setResultadosCliente([]);
    setCola([]); setColaError(null); setModoPresupuesto(false);
    void cargarMediosPago();
  };

  // ---- Arqueo X ----
  const abrirArqueo = async () => {
    setError(null);
    setBloqueando("Calculando arqueo…");
    try { setArqueo(await caja.arqueoX(idSucursal, idCaja)); setArqueoActivo(true); }
    catch (e) { setError(e instanceof Error ? e.message : "Error al obtener el arqueo"); }
    finally { setBloqueando(null); }
  };

  // ---- Cierre de turno (rendición del cajero; el Cierre Z fiscal es otra función, más abajo) ----
  const abrirCierre = async () => {
    setError(null);
    setBloqueando("Preparando cierre de turno…");
    try {
      // imprimir=false: es solo el preview para armar esta pantalla, no corresponde imprimir un
      // reporte X en el controlador fiscal — la rendición del cajero se imprime al confirmar el
      // cierre (ver ReporteCierreTurno), un X del controlador ahí es un papel de más.
      const [x, m] = await Promise.all([caja.arqueoX(idSucursal, idCaja, false), caja.motivosDiferencia()]);
      setArqueo(x);
      setMotivos(m);
      const init: Record<number, number | null> = {};
      x.acumulados.forEach((a) => { init[a.idMedioPago] = a.total; });
      setDeclaraciones(init);
      // Ya no hay una opción "(sin diferencia)" fija en el combo: ese motivo ahora es uno más de la
      // lista real (cargado desde Admin), así que se arranca en el primero de la lista.
      setIdMotivo(m[0]?.id ?? 0);
      setObservacionCierre("");
      setCierreResultado(null);
      setCierreActivo(true);
    } catch (e) { setError(e instanceof Error ? e.message : "Error al iniciar el cierre"); }
    finally { setBloqueando(null); }
  };

  const confirmarCierre = async () => {
    if (!arqueo) return;
    setError(null);
    setCerrando(true);
    try {
      const decl: DeclaracionPago[] = arqueo.acumulados.map((a) => ({
        idMedioPago: a.idMedioPago, montoDeclarado: declaraciones[a.idMedioPago] ?? 0,
      }));
      const r = await caja.cerrarTurno(idSucursal, idCaja, decl, idMotivo || null, observacionCierre || null);
      setCierreResultado(r);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error al cerrar el turno");
    } finally {
      setCerrando(false);
    }
  };

  const finCierre = () => {
    setCierreActivo(false); setCierreResultado(null);
    void cargarLote();
  };

  // ---- Cierre Z del controlador fiscal ----
  // No depende de ningún lote: se puede ejecutar aunque haya (o no) turnos de cajero abiertos en
  // esta caja. Se llama desde la pantalla de apertura, para que un supervisor no tenga que abrir
  // un turno solo para poder hacer el Z.
  const ejecutarCierreZFiscal = () => ejecutarConSupervisor(async (codigoSupervisor) => {
    setError(null);
    setEjecutandoZFiscal(true);
    try {
      setCierreZFiscalResultado(await caja.cierreZFiscal(idSucursal, idCaja, codigoSupervisor));
    } catch (e) {
      const mensaje = e instanceof Error ? e.message : "No se pudo ejecutar el cierre Z.";
      setError(mensaje);
      throw e;
    } finally {
      setEjecutandoZFiscal(false);
    }
  });

  // ---------- Render ----------

  if (loadingLote) return <div className="caja-shell"><p className="muted">Cargando caja…</p></div>;

  // Esta PC no está vinculada a ningún puesto (ver ABM Estructura de caja > Puestos): sin eso no
  // hay una caja física real que abrir, así que no se ofrece "elegir una a mano" — eso permitía
  // abrir turno y facturar sobre CUALQUIER caja de CUALQUIER sucursal desde una PC sin identificar
  // (ver AsegurarCaja: sin idCaja en la sesión, el backend no restringe nada). Se corta acá.
  if (idCajaAuth === null) {
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="user-box"><span className="usuario-badge">{usuario}</span><button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button></div>
        </header>
        <div className="caja-center">
          <div className="card form" style={{ maxWidth: 560 }}>
            <h3>Puesto no autorizado para operar</h3>
            <p className="muted">
              Esta PC todavía no está vinculada a ningún puesto de caja. Andá a Administración &gt;
              Asignación de cajas y usá "Vincular este equipo" parado frente a esta PC.
            </p>
          </div>
        </div>
      </div>
    );
  }

  if (!lote) {
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="user-box"><span className="usuario-badge">{usuario}</span><button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button></div>
        </header>
        <div className="caja-center">
          <div className="card form" style={{ maxWidth: 560 }}>
            <h3>Apertura de caja</h3>
            <p className="muted">
              Sucursal {idSucursal} · Caja {descripcionCaja ?? idCaja}. No hay un lote abierto hoy en esta caja.
            </p>
            {error && <p className="error">{error}</p>}

            {/* Turno abierto en OTRA caja: caída de la PC original. Se retoma el mismo lote desde acá,
                así aparecen sus ventas sin cobrar y se puede cerrar el turno donde se está trabajando. */}
            {turnos.filter((t) => t.idCaja !== idCaja).length > 0 && (
              <div className="note" style={{ display: "block" }}>
                <p><b>Tenés un turno abierto en otra caja.</b> Podés retomarlo acá sin perder lo que quedó sin cobrar.</p>
                <table className="grid">
                  <thead><tr><th>Turno</th><th>Caja</th><th>Abierto</th><th>Sin cobrar</th><th></th></tr></thead>
                  <tbody>
                    {turnos.filter((t) => t.idCaja !== idCaja).map((t) => (
                      <tr key={t.idLote}>
                        <td className="mono">#{t.idLote}</td>
                        <td>{t.descripcionCaja}</td>
                        <td className="mono">{formatearHora(t.fechaAperturaUtc)}</td>
                        <td className="mono">{t.ventasSinCobrar}</td>
                        <td className="row-actions">
                          <button className="primary" onClick={() => retomarTurno(t)}>Retomar acá</button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <label className="inline-label" style={{ marginTop: 4 }}>
              Caja
              <select value={idCaja} onChange={(e) => cambiarCaja(Number(e.target.value))}>
                {cajas.length === 0 && <option value={idCaja}>{descripcionCaja ?? `Caja ${idCaja}`}</option>}
                {cajas.map((c) => (
                  <option key={c.idCaja} value={c.idCaja}>
                    {c.descripcion}{c.idCaja === idCajaAuth ? " (esta PC)" : ""}
                  </option>
                ))}
              </select>
            </label>
            <button className="primary" onClick={abrirCaja}>Abrir caja</button>
          </div>

          {/* Cierre Z del controlador fiscal: operación de máquina, no de negocio — no depende de
              ningún turno (puede haber cero, uno o varios cajeros con lote abierto en esta caja a
              la vez) y por eso se ofrece acá, ANTES de abrir uno. Así un supervisor no tiene que
              abrir un turno de venta solo para poder ejecutar el Z. */}
          <div className="card form" style={{ maxWidth: 560, marginTop: 16 }}>
            <h3>Cierre Z (controlador fiscal)</h3>
            <p className="muted">
              Cierra la jornada fiscal del controlador de <b>Caja {descripcionCaja ?? idCaja}</b>.
              No requiere un turno abierto ni afecta los turnos de los cajeros que estén operando.
            </p>
            {cierreZFiscalResultado && (
              <p>
                Cierre Z ejecutado a las {new Date(cierreZFiscalResultado.fechaHoraUtc).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" })}
                {cierreZFiscalResultado.numeroFiscal && <> · Nº fiscal <b className="mono">{cierreZFiscalResultado.numeroFiscal}</b></>}
              </p>
            )}
            <button onClick={ejecutarCierreZFiscal} disabled={ejecutandoZFiscal}>
              {ejecutandoZFiscal ? "Ejecutando…" : "Ejecutar Cierre Z"}
            </button>
          </div>
        </div>
        {modalSupervisor}
        {aperturaPendiente && (
          <IngresoInicialModal
            idSucursal={idSucursal} idCaja={idCaja}
            codigoSupervisor={aperturaPendiente.codigoSupervisor}
            onAbierta={(l, monto) => {
              setLote(l);
              caja.misTurnos(idSucursal).then(setTurnos).catch(() => {});
              if (monto > 0) setIngresoAImprimir({ fecha: new Date(), monto });
              setAperturaPendiente(null);
            }}
            onCerrar={() => setAperturaPendiente(null)}
          />
        )}
      </div>
    );
  }

  if (ingresoAImprimir) {
    return (
      <TicketIngresoInicial
        fecha={ingresoAImprimir.fecha} monto={ingresoAImprimir.monto}
        descripcionCaja={descripcionCaja ?? `Caja ${idCaja}`} usuario={usuario ?? ""}
        onImpreso={() => setIngresoAImprimir(null)}
      />
    );
  }

  if (arqueoActivo && arqueo) {
    return (
      <div className="caja-shell">
        {/* Caja Electrónica: no hay controlador fiscal que imprima el Reporte X — lo imprime la
            comandera (ver ArqueoXTicket). En Fiscal, el backend ya lo mandó al Hasar. */}
        {arqueo.modoFacturacion === "ELECTRONICA" && <ArqueoXTicket arqueo={arqueo} usuario={usuario ?? ""} />}
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="lote-badge">Lote #{arqueo.idLote} · {arqueo.descripcionCaja}</div>
          <div className="modo-badge">{arqueo.modoFacturacion}</div>
          <div className="user-box"><span className="usuario-badge">{usuario}</span><button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button></div>
        </header>
        <div className="caja-body">
          <h1>Arqueo X</h1>
          <p className="muted">Vista del lote abierto — no cierra la caja. Abierto: {new Date(arqueo.fechaApertura).toLocaleString()}</p>
          <table className="grid">
            <thead><tr><th>Medio de pago</th><th>Total</th><th>Redondeo</th></tr></thead>
            <tbody>
              {arqueo.acumulados.map((a) => (
                <tr key={a.idMedioPago}>
                  <td>{a.descripcion}</td>
                  <td className="mono">{formatearMoneda(a.total)}</td>
                  <td className="mono">{formatearMoneda(a.redondeo)}</td>
                </tr>
              ))}
              {arqueo.acumulados.length === 0 && <tr><td colSpan={3} className="muted">Sin movimientos todavía.</td></tr>}
            </tbody>
          </table>
          {/* Las anulaciones ya están descontadas de los acumulados de arriba (la plata salió del
              cajón). Se listan aparte para que el cajero pueda justificar el faltante. */}
          {arqueo.anulaciones.length > 0 && (
            <>
              <h2 style={{ marginTop: 20 }}>Anulaciones (notas de crédito)</h2>
              <table className="grid">
                <thead><tr><th>Nota de crédito</th><th>Anula</th><th>Motivo</th><th>Importe</th></tr></thead>
                <tbody>
                  {arqueo.anulaciones.map((a) => (
                    <tr key={a.idComprobante}>
                      <td className="mono">{a.numeroCompleto} {a.letra}</td>
                      <td className="mono">{a.comprobanteOrigen ?? "—"}</td>
                      <td>{a.motivo ?? "—"}</td>
                      <td className="mono">−{formatearMoneda(a.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          {/* Los retiros también ya están descontados de los acumulados de arriba. */}
          {arqueo.retiros.length > 0 && (
            <>
              <h2 style={{ marginTop: 20 }}>Retiros de efectivo</h2>
              <table className="grid">
                <thead><tr><th>Hora</th><th>Concepto</th><th>Cajero</th><th>Importe</th></tr></thead>
                <tbody>
                  {arqueo.retiros.map((r) => (
                    <tr key={r.idMovCaja}>
                      <td className="mono">{formatearHora(r.fecha)}</td>
                      <td>{r.concepto ?? "—"}</td>
                      <td>{r.usuario ?? "—"}</td>
                      <td className="mono">−{formatearMoneda(r.monto)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          {/* El vuelto entregado también ya está descontado de los acumulados de arriba. */}
          {arqueo.vueltos.length > 0 && (
            <>
              <h2 style={{ marginTop: 20 }}>Vueltos dados</h2>
              <table className="grid">
                <thead><tr><th>Hora</th><th>Concepto</th><th>Cajero</th><th>Importe</th></tr></thead>
                <tbody>
                  {arqueo.vueltos.map((v) => (
                    <tr key={v.idMovCaja}>
                      <td className="mono">{formatearHora(v.fecha)}</td>
                      <td>{v.concepto ?? "—"}</td>
                      <td>{v.usuario ?? "—"}</td>
                      <td className="mono">−{formatearMoneda(v.monto)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          <div className="caja-totales">
            {arqueo.anulaciones.length > 0 && (
              <div className="total"><span>Total anulaciones</span><b>−{formatearMoneda(arqueo.totalAnulaciones)}</b></div>
            )}
            {arqueo.retiros.length > 0 && (
              <div className="total"><span>Total retiros</span><b>−{formatearMoneda(arqueo.totalRetiros)}</b></div>
            )}
            {arqueo.vueltos.length > 0 && (
              <div className="total"><span>Total vueltos</span><b>−{formatearMoneda(arqueo.totalVueltos)}</b></div>
            )}
            <div className="total"><span>Total general</span><b>{formatearMoneda(arqueo.totalGeneral)}</b></div>
          </div>
          {arqueo.limiteEfectivoCaja > 0 && arqueo.efectivoAcumulado > arqueo.limiteEfectivoCaja && (
            <div className="note note-aviso-limite" style={{ marginTop: 16 }}>
              <div className="note-aviso-limite__fila">
                <img src="/icons/aviso-limite.png" alt="" className="note-aviso-limite__icono" />
                <p>Limite de efectivo superado, realizar un RETIRO</p>
              </div>
            </div>
          )}
          <div className="row-actions" style={{ marginTop: 16 }}>
            {/* Fiscal ya imprimió el ticket desde el backend al calcular el arqueo; acá es para
                Electrónica, por si se cerró el diálogo de impresión automático o hace falta otra
                copia — reimprime el mismo ArqueoXTicket ya montado más arriba. */}
            {arqueo.modoFacturacion === "ELECTRONICA" && (
              <button onClick={() => window.print()}>Imprimir</button>
            )}
            <button className="primary" onClick={() => setArqueoActivo(false)}>Volver</button>
          </div>
        </div>
      </div>
    );
  }

  if (cierreActivo) {
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="lote-badge">Lote #{lote.idLote} · {lote.descripcionCaja}</div>
          <div className="modo-badge">{lote.modoFacturacion}</div>
          <div className="user-box"><span className="usuario-badge">{usuario}</span><button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button></div>
        </header>
        <div className="caja-body">
          {cierreResultado && arqueo ? (
            <ReporteCierreTurno
              arqueo={arqueo}
              cierre={cierreResultado}
              usuario={usuario ?? ""}
              motivoDescripcion={cierreResultado.detalle.some((d) => d.requiereMotivo)
                ? (motivos.find((m) => m.id === idMotivo)?.descripcion ?? null) : null}
              observaciones={observacionCierre || null}
              onCerrar={finCierre}
            />
          ) : (
            <>
              <h1>Cierre de turno (rendición final)</h1>
              <p className="muted">Confirmación de valores por medio de pago. Esta acción es <b>irreversible</b>.</p>
              {error && <p className="error">{error}</p>}
              <table className="grid">
                <thead><tr><th>Medio de pago</th><th>Esperado (sistema)</th><th>Declarado (contado)</th></tr></thead>
                <tbody>
                  {arqueo?.acumulados.map((a) => (
                    <tr key={a.idMedioPago}>
                      <td>{a.descripcion}</td>
                      <td className="mono">{formatearMoneda(a.total)}</td>
                      <td>
                        <MonedaInput value={declaraciones[a.idMedioPago] ?? null}
                          onChange={(v) => setDeclaraciones((d) => ({ ...d, [a.idMedioPago]: v }))} style={{ width: 140 }} />
                      </td>
                    </tr>
                  ))}
                  {(!arqueo || arqueo.acumulados.length === 0) && (
                    <tr><td colSpan={3} className="muted">Sin movimientos en este lote.</td></tr>
                  )}
                </tbody>
              </table>
              <div className="card form">
                <h3>Justificación (si hay diferencias)</h3>
                <div className="form-grid">
                  <label>Motivo de diferencia
                    <select value={idMotivo} onChange={(e) => setIdMotivo(Number(e.target.value))}>
                      {motivos.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                    </select>
                  </label>
                  <label>Observaciones<input value={observacionCierre} onChange={(e) => setObservacionCierre(e.target.value)} /></label>
                </div>
              </div>
              <div className="row-actions">
                <button className="primary" disabled={cerrando} onClick={confirmarCierre}>
                  {cerrando ? "Cerrando…" : "Confirmar cierre de turno"}
                </button>
                <button onClick={() => setCierreActivo(false)} disabled={cerrando}>Cancelar</button>
              </div>
            </>
          )}
        </div>
      </div>
    );
  }

  if (voucherPago) {
    return (
      <VoucherComprobantePago
        fecha={voucherPago.fecha}
        clienteCodigo={clienteSel?.codigoInt ?? "-"}
        clienteDescripcion={clienteSel?.descripcion ?? "Consumidor final"}
        numeroComprobante={voucherPago.numeroComprobante}
        items={voucherPago.items}
        onImpreso={() => setVoucherPago(null)}
      />
    );
  }

  if (comprobante) {
    // El popup tapa TODO hasta que el cajero lo cierra — recién ahí se arma la pantalla del
    // comprobante de abajo (ver confirmarCobro/nuevaVenta para cuándo se prende/apaga).
    if (puntosPopupVisible && comprobante.fidelizacion?.ok) {
      return (
        <PuntosCargadosPopup
          cliente={comprobante.fidelizacion.cliente ?? clienteSel?.descripcion ?? "Cliente"}
          puntosOtorgados={comprobante.fidelizacion.puntosOtorgados ?? 0}
          puntosTotales={comprobante.fidelizacion.puntosTotales ?? 0}
          onCerrar={() => setPuntosPopupVisible(false)}
        />
      );
    }
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="user-box"><span className="usuario-badge">{usuario}</span><button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button></div>
        </header>
        <div className="caja-center">
          {/* El comprobante se muestra en su formato real (A o B) y se imprime desde el navegador,
              igual que las etiquetas. Si no se pudo armar, queda el resumen simple de abajo. */}
          {impresion ? (
            <ComprobanteImpresionView c={impresion} onCerrar={nuevaVenta} />
          ) : (
          <div className="ticket-card">
            <p className="muted">Comprobante {comprobante.esCaea ? "(contingencia CAEA)" : "emitido"}</p>
            <div className="ticket-numero">{comprobante.numeroCompleto}</div>
            {comprobante.cae && (
              <p className="muted">
                {comprobante.esCaea ? "CAEA" : "CAE"}: <span className="mono">{comprobante.cae}</span>
              </p>
            )}
            <div className="ticket-totales">
              <div><span>Neto</span><b>{formatearMoneda(comprobante.neto)}</b></div>
              <div><span>IVA</span><b>{formatearMoneda(comprobante.iva)}</b></div>
              {comprobante.percepcionIva21 > 0 && (
                <div><span>Percepción IVA 21%</span><b>{formatearMoneda(comprobante.percepcionIva21)}</b></div>
              )}
              {comprobante.percepcionIva105 > 0 && (
                <div><span>Percepción IVA 10,5%</span><b>{formatearMoneda(comprobante.percepcionIva105)}</b></div>
              )}
              {comprobante.percepcionIibb > 0 && (
                <div><span>Percepción IIBB ({comprobante.alicuotaIibb.toFixed(2)}%)</span><b>{formatearMoneda(comprobante.percepcionIibb)}</b></div>
              )}
              <div className="total"><span>Total</span><b>{formatearMoneda(comprobante.total)}</b></div>
            </div>
            {comprobante.vuelto > 0 && <p className="vuelto">Vuelto: {formatearMoneda(comprobante.vuelto)}</p>}
            <p className={comprobante.impreso ? "muted" : "error"}>
              {comprobante.impreso ? "✓ Impreso" : `Sin imprimir: ${comprobante.errorImpresion ?? "error"}`}
            </p>
            <button className="primary" onClick={nuevaVenta}>Nueva venta</button>
          </div>
          )}
        </div>
      </div>
    );
  }

  if (!clienteConfirmado && pendientes.length > 0 && clienteSel) {
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="lote-badge">Lote #{lote.idLote} · {lote.descripcionCaja}</div>
          <div className="modo-badge">{lote.modoFacturacion}</div>
          <div className="user-box">
            <span className="usuario-badge">{usuario}</span>
            <button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button>
          </div>
        </header>
        <div className="caja-wide">
          <div className="ident-head">
            <h1>Ventas sin terminar</h1>
            <p className="muted">
              <b>{clienteSel.descripcion}</b> tiene {pendientes.length === 1 ? "una venta" : `${pendientes.length} ventas`} sin
              cobrar en este turno. Podés retomarla o empezar una nueva.
            </p>
          </div>
          {error && <p className="error">{error}</p>}
          <table className="grid ident-table">
            <thead>
              <tr><th>Operación</th><th>Hora</th><th>Artículos</th><th>Total</th><th>Estado</th><th></th></tr>
            </thead>
            <tbody>
              {pendientes.map((p) => (
                <tr key={p.idOperacion} onClick={() => retomarOperacion(p)}>
                  <td className="mono">#{p.idOperacion}</td>
                  <td className="mono">{formatearHora(p.fechaUtc)}</td>
                  <td className="mono">{p.cantidadLineas}</td>
                  <td className="mono">{formatearMoneda(p.total)}</td>
                  <td>
                    {p.estado === "Finalizada"
                      ? <span className="badge on">Lista para cobrar</span>
                      : <span className="badge warn">A medio escanear</span>}
                  </td>
                  <td className="row-actions">
                    <button className="primary" onClick={(e) => { e.stopPropagation(); retomarOperacion(p); }}>
                      Retomar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="row-actions" style={{ marginTop: 14 }}>
            <button onClick={() => iniciarVenta(clienteSel)}>Empezar una venta nueva</button>
            <button onClick={volverAIdentificar}>Elegir otro cliente</button>
          </div>
        </div>
      </div>
    );
  }

  if (!clienteConfirmado) {
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="lote-badge">Lote #{lote.idLote} · {lote.descripcionCaja}</div>
          <div className="modo-badge">{lote.modoFacturacion}</div>
          <div className="user-box">
            <button className="danger-solid" onClick={() => setNotaCreditoAbierta(true)}>Notas de Crédito</button>
          <button onClick={() => setRetiroAbierto(true)}>Retiro de efectivo</button>
            <button onClick={abrirArqueo}>Arqueo X</button>
            <button onClick={abrirCierre}>Cerrar turno</button>
            <span className="usuario-badge">{usuario}</span>
            <button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button>
          </div>
        </header>
        {notaCreditoAbierta && (
          <NotaCreditoModal idSucursal={idSucursal} idCaja={lote.idCaja}
            onCerrar={() => setNotaCreditoAbierta(false)} />
        )}
        {retiroAbierto && (
          <RetiroEfectivoModal idSucursal={idSucursal} idCaja={lote.idCaja}
            usuario={usuario ?? ""} descripcionCaja={lote.descripcionCaja}
            onCerrar={() => setRetiroAbierto(false)} />
        )}
        {bloqueando && <PantallaBloqueada mensaje={bloqueando} />}
        <div className="caja-wide">
          <div className="ident-head">
            <h1>Identificación de cliente</h1>
            <p className="muted">
              Tarjeta, DNI, CUIT, código, razón social o nombre de fantasía. La venta requiere un
              cliente identificado.
            </p>
          </div>
          <div className="ident-search">
            <input autoFocus placeholder="Buscar cliente por nombre, fantasía, CUIT, DNI o tarjeta…" value={busquedaCliente}
              onChange={(e) => setBusquedaCliente(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && buscarCliente(e.currentTarget.value)} />
            <button className="primary" onClick={() => buscarCliente()}>Buscar</button>
          </div>
          {error && <p className="error">{error}</p>}
          {resultadosCliente.length > 1 && (
            <>
              <p className="muted">{resultadosCliente.length} clientes — elegí uno para continuar.</p>
              <table className="grid ident-table">
                <thead>
                  <tr>
                    <th>Código</th><th>Cliente</th><th>CUIT</th><th>DNI</th><th>Cond. IVA</th>
                    <th>Domicilio</th><th>Tarjeta</th><th>Lista de precios</th><th></th>
                  </tr>
                </thead>
                <tbody>
                  {resultadosCliente.map((c) => (
                    <tr key={c.idCliente} onClick={() => seleccionarCliente(c)}>
                      <td className="mono">{c.codigoInt}</td>
                      <td className="stack">
                        {c.descripcion}
                        {c.nombreFantasia ? <small>{c.nombreFantasia}</small> : null}
                        {c.idConvenio ? <small>Convenio · {c.descuentoConvenio}% dto.</small> : null}
                      </td>
                      <td className="mono">{c.cuit || <span className="muted">—</span>}</td>
                      <td className="mono">{c.documento || <span className="muted">—</span>}</td>
                      <td>{c.condIvaDescripcion || <span className="muted">—</span>}</td>
                      <td className="stack">
                        {c.domicilio || <span className="muted">—</span>}
                        {c.localidad ? <small>{c.localidad}</small> : null}
                      </td>
                      <td className="stack">
                        {c.nroTarjeta
                          ? <><span className="mono">{c.nroTarjeta}</span>
                              <small>{c.tipoTarjeta}{c.cantidadTarjetas > 1 ? ` · +${c.cantidadTarjetas - 1} más` : ""}</small></>
                          : <span className="muted">—</span>}
                      </td>
                      <td className="stack">
                        {c.listaPrecioDescripcion
                          ? <>{c.listaPrecioDescripcion}<small>según {c.listaPrecioOrigen?.toLowerCase()}</small></>
                          : <span className="muted">(lista base)</span>}
                      </td>
                      <td className="row-actions">
                        <button className="primary" onClick={(e) => { e.stopPropagation(); seleccionarCliente(c); }}>
                          Seleccionar
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          {resultadosCliente.length === 0 && busquedaEjecutada && (
            <p className="muted">Sin resultados para «{busquedaEjecutada}».</p>
          )}
        </div>
      </div>
    );
  }

  if (cobroActivo && operacion) {
    // El Presupuesto nunca lleva percepciones (no tiene valor fiscal) aunque la vista previa del
    // carrito las haya calculado para una venta fiscal normal — por eso acá se usa "neto" a secas
    // en ese modo, y "totalACobrar" (neto + percepciones) en el resto.
    const totalEsperado = modoPresupuesto ? operacion.neto : operacion.totalACobrar;
    const diferencia = Math.round((totalEsperado - totalPagos) * 100) / 100;
    // Vuelto: el sobrante solo es válido si viene de Efectivo (no se puede "dar vuelto" en una
    // tarjeta) — mismo criterio que ValidacionPagos en el backend, que es quien lo valida de verdad.
    const sumaEfectivo = pagos.filter((p) => esEfectivo(p.idMedioPago)).reduce((acc, p) => acc + (p.monto ?? 0), 0);
    const sumaNoEfectivo = totalPagos - sumaEfectivo;
    const noEfectivoSuperaElTotal = Math.round((sumaNoEfectivo - totalEsperado) * 100) / 100 > 0.005;
    const cubreElTotal = diferencia <= 0.005 && !noEfectivoSuperaElTotal;
    // El backend rechaza una tarjeta sin cupón/lote/plan (CUPON_REQUERIDO / LOTE_REQUERIDO /
    // PLAN_REQUERIDO); se avisa antes de intentar emitir para no gastar el intento.
    const faltaCuponOLote = pagos.some((p) =>
      esTarjeta(p.idMedioPago) && (!p.numeroCupon.trim() || !p.numeroLote.trim() || !p.idPlan));
    // El backend rechaza un cheque sin banco/número (BANCO_REQUERIDO / NUMERO_CHEQUE_REQUERIDO);
    // mismo criterio que faltaCuponOLote, se avisa antes de intentar emitir.
    const faltaDatosCheque = pagos.some((p) =>
      esCheque(p.idMedioPago) && (!p.idBanco || !p.numeroCheque.trim()));
    // A diferencia de cupón/cheque (que el backend valida recién al facturar), una gift card tiene
    // que estar YA canjeada (transaccionIdGiftcard) antes de poder confirmar el cobro: el canje se
    // aplica en el popup "Confirmar uso", no acá — sin eso no hay plata real cubriendo ese pago.
    const faltaCodigoGiftcard = pagos.some((p) => esGiftcard(p.idMedioPago) && !p.transaccionIdGiftcard);
    // Descuento por medio de pago y vuelto: mismo algoritmo que el backend (FacturacionService) —
    // se calcula sobre lo que cada pago realmente CUBRE de la venta (tope al saldo que todavía
    // falta cubrir), no sobre el monto entregado: si en Efectivo se entrega de más para llevarse
    // vuelto, ese excedente nunca fue parte de la venta y no tiene que inflar el descuento.
    let restanteSaldo = totalEsperado;
    const filasPago = pagos.map((p) => {
      const monto = p.monto ?? 0;
      const efectivo = esEfectivo(p.idMedioPago);
      const cubierto = efectivo ? Math.min(monto, Math.max(restanteSaldo, 0)) : monto;
      const excedente = monto - cubierto;
      restanteSaldo -= cubierto;
      const oferta = resolverOfertaMp(ofertasMedioPago, p.idMedioPago, esTarjeta(p.idMedioPago) ? p.idPlan : null);
      const descuentoMp = calcularDescuentoMp(cubierto, oferta);
      return { oferta, cubierto, descuentoMp, vueltoFila: efectivo ? excedente + descuentoMp : 0 };
    });
    const descuentoMpTotal = filasPago.reduce((acc, f) => acc + f.descuentoMp, 0);
    const vuelto = filasPago.reduce((acc, f) => acc + f.vueltoFila, 0);
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
          <div className="lote-badge">Lote #{lote.idLote} · {lote.descripcionCaja}</div>
          <div className="modo-badge">{lote.modoFacturacion}</div>
          <div className="user-box"><span className="usuario-badge">{usuario}</span><button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button></div>
        </header>
        {bloqueando && <PantallaBloqueada mensaje={bloqueando} />}
        {giftcardModal && (
          <GiftcardValidacionModal
            idSucursal={idSucursal}
            info={giftcardModal.info}
            idempotencyKey={`${idSucursal}-${operacion.idOperacion}-gc-${giftcardModal.info.codigo}`}
            onCerrar={() => setGiftcardModal(null)}
            onConfirmado={(monto, transaccionId) => {
              setPago(giftcardModal.i, {
                codigoGiftcard: giftcardModal.info.codigo, monto,
                transaccionIdGiftcard: transaccionId ?? "",
              });
              setGiftcardModal(null);
            }}
          />
        )}
        <div className="caja-body">
          <h1>Cobro</h1>
          <p className="muted">
            Operación #{operacion.idOperacion} · Total a cobrar: <b>{formatearMoneda(totalEsperado)}</b>
            {(operacion.percepcionIva21 > 0 || operacion.percepcionIva105 > 0 || operacion.percepcionIibb > 0) && !modoPresupuesto && (
              <> (incluye {formatearMoneda(operacion.percepcionIva21 + operacion.percepcionIva105 + operacion.percepcionIibb)} de percepciones)</>
            )}
            {modoPresupuesto ? (
              <>
                {" · Se emite "}
                <b>PRESUPUESTO</b> (comprobante X, sin discriminar impuestos)
              </>
            ) : letraPrevista && (
              <>
                {" · Se emite "}
                <b>FACTURA {letraPrevista}</b>
                {letraPrevista === "A" ? " (IVA discriminado)" : ""}
              </>
            )}
          </p>
          {error && <p className="error">{error}</p>}

          {/* Presupuesto exige las dos condiciones: el cliente lo admite Y esta caja lo tiene
              habilitado (Caja.AdmitePresupuesto — un admin puede desactivarlo por caja). Cambia el
              comprobante a X (sin fiscal ni electrónico), siempre efectivo, sin discriminar
              impuestos — no importa la condición de IVA del cliente. Cuenta como venta en las
              estadísticas igual que cualquier otro comprobante. */}
          {clienteSel?.permitePresupuesto && lote.admitePresupuesto && (
            <label className="check-box">
              <input type="checkbox" checked={modoPresupuesto}
                onChange={(e) => toggleModoPresupuesto(e.target.checked)} />
              Vender como Presupuesto (comprobante X, sin factura — se cobra siempre en efectivo)
            </label>
          )}

          <div className="card form">
            <h3>Medios de pago</h3>
            {modoPresupuesto && (
              <p className="muted" style={{ marginTop: -8 }}>
                El presupuesto se cobra siempre en efectivo, sin combinar medios — el medio queda
                bloqueado, pero cargá igual la plata que recibiste (si es de más, se calcula el vuelto).
              </p>
            )}
            {pagos.map((p, i) => {
              const { oferta, cubierto, descuentoMp } = filasPago[i];
              return (
              <div key={i}>
              <div className="pago-row">
                <label className="campo-medio">Medio
                  <select value={p.idMedioPago} disabled={modoPresupuesto}
                    onChange={(e) => elegirMedioPago(i, Number(e.target.value))}>
                    {mediosPago.map((m) => <option key={m.idMedioPago} value={m.idMedioPago}>{m.descripcion}</option>)}
                  </select>
                </label>
                <label className="campo-monto">Monto
                  {/* Gift card ya canjeada: el monto quedó fijo en lo que se confirmó en el popup
                      (ver GiftcardValidacionModal) — no se puede "retocar" después sin volver a
                      canjear, así que el input queda deshabilitado. */}
                  <MonedaInput value={p.monto} onChange={(v) => setPago(i, { monto: v })}
                    disabled={esGiftcard(p.idMedioPago) && !!p.transaccionIdGiftcard} />
                </label>
                {/* Cupón, lote y plan: solo para tarjetas. Se piden en el cobro porque es cuando el
                    cajero tiene el ticket del posnet en la mano; cupón/lote sirven después para
                    rendir los cupones, el plan queda para la rendición de cuotas del operador.
                    En Presupuesto el medio queda fijo en Efectivo, nunca aplica. */}
                {!modoPresupuesto && esTarjeta(p.idMedioPago) && (
                  <>
                    <label className="campo-cupon">Nº de cupón
                      <input value={p.numeroCupon} maxLength={20} inputMode="numeric"
                        onChange={(e) => setPago(i, { numeroCupon: e.target.value })} />
                    </label>
                    <label className="campo-cupon">Nº de lote
                      <input value={p.numeroLote} maxLength={20} inputMode="numeric"
                        onChange={(e) => setPago(i, { numeroLote: e.target.value })} />
                    </label>
                    {/* Obligatorio: todo medio Tarjeta tiene al menos el plan "1 cuota" por
                        defecto (ver PagoAdminService.AsegurarPlanPorDefectoAsync), así que no hay
                        una opción válida de "sin plan". Mientras se cargan (recién elegido el
                        medio) el select queda deshabilitado en vez de dejar elegir a ciegas. */}
                    <label className="campo-medio">Plan
                      <select value={p.idPlan ?? ""} disabled={!planesPorMedio[p.idMedioPago]?.length}
                        onChange={(e) => setPago(i, { idPlan: Number(e.target.value) })}>
                        {!planesPorMedio[p.idMedioPago]?.length && <option value="">(cargando…)</option>}
                        {planesPorMedio[p.idMedioPago]?.map((pl) => (
                          <option key={pl.idPlan} value={pl.idPlan}>{pl.denominacion}</option>
                        ))}
                      </select>
                    </label>
                  </>
                )}
                {/* Banco, número de cheque y observaciones: solo para Cheque. Análogo a cupón/lote
                    de Tarjeta, pero identifican el cheque físico para presentarlo en Tesorería/banco
                    en vez de un cupón de posnet. Observaciones queda libre, no se exige. */}
                {!modoPresupuesto && esCheque(p.idMedioPago) && (
                  <>
                    <label className="campo-medio">Banco
                      <select value={p.idBanco ?? 0} onChange={(e) => setPago(i, { idBanco: Number(e.target.value) || null })}>
                        <option value={0}>(elegir)</option>
                        {bancos.map((b) => <option key={b.idBanco} value={b.idBanco}>{b.descripcion}</option>)}
                      </select>
                    </label>
                    <label className="campo-cupon">Nº de cheque
                      <input value={p.numeroCheque} maxLength={8}
                        onChange={(e) => setPago(i, { numeroCheque: e.target.value })} />
                    </label>
                    <label className="campo-cupon">Observaciones
                      <input value={p.observacionesCheque}
                        onChange={(e) => setPago(i, { observacionesCheque: e.target.value })} />
                    </label>
                  </>
                )}
                {/* Gift Card: código + "Validar" abre el popup "Confirmar uso" (calcado de
                    giftcards-app) — ahí, al confirmar, se descuenta saldo DE INMEDIATO (no al
                    facturar). Una vez canjeada, el código queda fijo (hay que "Quitar" el pago
                    entero para elegir otra). */}
                {!modoPresupuesto && esGiftcard(p.idMedioPago) && (
                  <>
                    {p.transaccionIdGiftcard ? (
                      <p className="muted">✔ {p.codigoGiftcard} canjeada</p>
                    ) : (
                      <>
                        <label className="campo-cupon">Código
                          <input value={p.codigoGiftcard} maxLength={8}
                            onChange={(e) => {
                              setPago(i, { codigoGiftcard: e.target.value });
                              setGiftcardError((prev) => { const n = { ...prev }; delete n[i]; return n; });
                            }} />
                        </label>
                        <button type="button" className="success-solid"
                          disabled={p.codigoGiftcard.trim().length !== 8 || giftcardValidando === i}
                          onClick={() => void abrirValidarGiftcard(i, p.codigoGiftcard)}>
                          {giftcardValidando === i ? "Validando…" : "Validar"}
                        </button>
                        {giftcardError[i] && <p className="error">{giftcardError[i]}</p>}
                      </>
                    )}
                  </>
                )}
                {/* Presupuesto: un único pago fijo en Efectivo, sin combinar medios — no hay
                    "Quitar" ni "+ Otro medio de pago". */}
                {!modoPresupuesto && pagos.length > 1 && <button className="danger" onClick={() => quitarPago(i)}>Quitar</button>}
                {/* "+ Otro medio" va en la última fila, no en un renglón aparte: así toda la línea
                    del pago (medio, monto, cupón, lote y el botón) se lee de corrido. */}
                {!modoPresupuesto && i === pagos.length - 1 && (
                  <button onClick={agregarPago}>+ Otro medio de pago</button>
                )}
              </div>
              {/* Hasta que los pagos no cubran el total, "cubierto" es un valor a medio cargar (ej.
                  recién tipeó el primer dígito) — mostrar el descuento sobre eso sería una cuenta
                  sin sentido; el backend tampoco lo calcula hasta que CubreElTotal da true. */}
              {descuentoMp > 0 && diferencia <= 0.005 && (
                <p className="muted" style={{ marginTop: -4 }}>
                  Con descuento por medio de pago ({oferta!.porcentaje}%, tope {formatearMoneda(oferta!.topeMaximo)}):
                  se le cobran <b>{formatearMoneda(cubierto - descuentoMp)}</b> (ahorra {formatearMoneda(descuentoMp)}).
                </p>
              )}
              </div>
              );
            })}
            {!modoPresupuesto && pagos.length === 0 && (
              <div className="row-actions">
                <button onClick={agregarPago}>+ Otro medio de pago</button>
              </div>
            )}
            {diferencia > 0.005 ? (
              <p className="error">Falta cubrir {formatearMoneda(diferencia)}</p>
            ) : noEfectivoSuperaElTotal ? (
              <p className="error">
                Lo cargado en medios distintos de Efectivo supera lo que corresponde: el vuelto solo se puede dar en efectivo.
              </p>
            ) : vuelto > 0.005 ? (
              <p className="vuelto">Vuelto: {formatearMoneda(vuelto)}</p>
            ) : (
              <p className="muted">Los pagos cubren el total.</p>
            )}
            {descuentoMpTotal > 0 && diferencia <= 0.005 && (
              <p className="muted">
                Descuento por medio de pago: −{formatearMoneda(descuentoMpTotal)}. Total a cobrar: <b>{formatearMoneda(totalEsperado - descuentoMpTotal)}</b>
              </p>
            )}
            {faltaCuponOLote && (
              <p className="error">Los pagos con tarjeta necesitan el número de cupón, el de lote y un plan de cuotas.</p>
            )}
            {faltaDatosCheque && (
              <p className="error">Los pagos con cheque necesitan el banco emisor y el número de cheque.</p>
            )}
            {faltaCodigoGiftcard && (
              <p className="error">Los pagos con Gift Card necesitan confirmar el canje ("Validar" → "Confirmar uso").</p>
            )}
            <div className="row-actions">
              <button className="primary" disabled={!cubreElTotal || emitiendo || faltaCuponOLote || faltaDatosCheque || faltaCodigoGiftcard}
                onClick={confirmarCobro}>
                {emitiendo ? "Emitiendo…" : modoPresupuesto ? "Confirmar presupuesto" : "Confirmar cobro y facturar"}
              </button>
              <button onClick={volverAlCarrito} disabled={emitiendo || volviendo}>
                {volviendo ? "Volviendo…" : "Volver"}
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="caja-shell">
      <header className="caja-header">
        <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Caja</span></span>
        <div className="lote-badge">Lote #{lote.idLote} · {lote.descripcionCaja}</div>
        <div className="modo-badge">{lote.modoFacturacion}</div>
        <div className="user-box">
          <button className="danger-solid" onClick={() => setNotaCreditoAbierta(true)}>Notas de Crédito</button>
          <button className="warning-solid" onClick={anularOperacion}>Anular Operación</button>
          <button onClick={() => setRetiroAbierto(true)}>Retiro de efectivo</button>
          <button onClick={abrirArqueo}>Arqueo X</button>
          <button onClick={abrirCierre}>Cerrar turno</button>
          <span className="usuario-badge">{usuario}</span>
          <button onClick={() => navigate("/")}>Módulos</button><button onClick={logout}>Salir</button>
        </div>
      </header>
      {notaCreditoAbierta && (
        <NotaCreditoModal idSucursal={idSucursal} idCaja={lote.idCaja}
          onCerrar={() => setNotaCreditoAbierta(false)} />
      )}
      {retiroAbierto && (
        <RetiroEfectivoModal idSucursal={idSucursal} idCaja={lote.idCaja}
          usuario={usuario ?? ""} descripcionCaja={lote.descripcionCaja}
          onCerrar={() => { setRetiroAbierto(false); void revisarLimiteEfectivo(); }} />
      )}
      {bloqueando && <PantallaBloqueada mensaje={bloqueando} />}

      <div className="caja-body">
        <div className="caja-cliente">
          <span>
            Cliente: <b>{clienteSel ? clienteSel.descripcion : "Consumidor final"}</b>
            {/* El nombre de fantasía es con lo que el cajero reconoce al cliente. */}
            {clienteSel?.nombreFantasia && <span className="muted"> ({clienteSel.nombreFantasia})</span>}
          </span>
          {clienteSel?.idConvenio && <span className="badge on">Convenio {clienteSel.descuentoConvenio}%</span>}
          {clienteSel?.listaPrecioDescripcion && (
            <span className={claseLista(clienteSel.listaPrecioDescripcion)}
              title={`Lista de precios según ${clienteSel.listaPrecioOrigen?.toLowerCase() ?? "cliente"}`}>
              {clienteSel.listaPrecioDescripcion}
            </span>
          )}
          {operacion && <span className="muted">· Operación #{operacion.idOperacion}</span>}
        </div>

        {/* Quién puede comprar en nombre de este cliente: el cajero lo controla contra el DNI que le
            presentan. Solo llegan los autorizados activos. */}
        {clienteSel?.autorizados && clienteSel.autorizados.length > 0 && (
          <div className="caja-autorizados">
            <span className="tit">Autorizados:</span>
            <ul>
              {clienteSel.autorizados.map((a) => (
                <li key={a.dni}>{a.descripcion} <span className="mono">· DNI {a.dni}</span></li>
              ))}
            </ul>
          </div>
        )}

        <div className="toolbar">
          {/* Enter en la cantidad pasa al campo de escaneo: el lector puede leer enseguida sin clic. */}
          <input type="number" min={1} value={cantidadPendiente}
            onChange={(e) => setCantidadPendiente(Number(e.target.value) || 1)}
            onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); inputCodigo.current?.focus(); } }}
            style={{ width: 80 }} title="Cantidad" />
          <div className="campo-lupa">
            <input ref={inputCodigo} autoFocus placeholder="Escanear o escribir código de artículo…" value={codigoInput}
              onChange={(e) => setCodigoInput(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && encolar(e.currentTarget.value)} />
            {/* Para cuando el código no se puede leer: búsqueda manual por código o descripción. */}
            <button type="button" className="lupa" title="Buscar artículo a mano" onClick={abrirBuscador}>
              <span aria-hidden="true">🔍</span><span className="sr-only">Buscar artículo</span>
            </button>
          </div>
          <button className="primary" onClick={() => encolar()}>Agregar</button>
        </div>
        {cola.length > 0 && <p className="muted">En cola: {cola.length}</p>}
        {colaError && (
          <div className="note note-aviso-limite">
            <div className="note-aviso-limite__fila">
              <img src="/icons/aviso-limite.png" alt="" className="note-aviso-limite__icono" />
              <p>ARTICULO NO ENCONTRADO - REVISAR ANTES DE CONTINUAR</p>
              <button className="danger" onClick={descartarError}>Descartar y continuar</button>
            </div>
          </div>
        )}
        {error && <p className="error">{error}</p>}
        {avisoEfectivo && (
          <div className="note note-aviso-limite">
            <div className="note-aviso-limite__fila">
              <img src="/icons/aviso-limite.png" alt="" className="note-aviso-limite__icono" />
              <p>Limite de efectivo superado, realizar un RETIRO</p>
              <button className="primary" onClick={() => setRetiroAbierto(true)}>Hacer retiro</button>
            </div>
          </div>
        )}

        {/* Totales y Cobrar quedan pegados arriba, entre el escaneo y la lista: con muchos artículos
            el cajero sigue viendo el total y el botón sin tener que bajar hasta el final. */}
        {operacion && operacion.lineas.length > 0 && (
          <div className="caja-totales caja-totales-fija">
            <div><span>Bruto</span><b>{formatearMoneda(operacion.bruto)}</b></div>
            <div><span>Descuento</span><b>-{formatearMoneda(operacion.descuento)}</b></div>
            {operacion.percepcionIva21 > 0 && (
              <div><span>Percepción IVA 21%</span><b>{formatearMoneda(operacion.percepcionIva21)}</b></div>
            )}
            {operacion.percepcionIva105 > 0 && (
              <div><span>Percepción IVA 10,5%</span><b>{formatearMoneda(operacion.percepcionIva105)}</b></div>
            )}
            {operacion.percepcionIibb > 0 && (
              <div><span>Percepción IIBB ({operacion.alicuotaIibb.toFixed(2)}%)</span><b>{formatearMoneda(operacion.percepcionIibb)}</b></div>
            )}
            <div className="total"><span>Total</span><b>{formatearMoneda(operacion.totalACobrar)}</b></div>
            <button className="primary" onClick={irACobrar}>Cobrar</button>
          </div>
        )}

        <table className="grid">
          <thead><tr><th>Código</th><th>Artículo</th><th>Cant.</th><th>Precio</th><th>Descuento</th><th>Final</th><th>Ofertas</th><th></th></tr></thead>
          <tbody>
            {/* El backend devuelve las líneas con la última escaneada primero. */}
            {operacion?.lineas.map((l) => (
              <tr key={l.idDetalle} className={l.idDetalle === lineaResaltada ? "linea-tocada" : ""}>
                <td className="mono">{l.codigoInterno}</td>
                <td>{l.descripcion}</td>
                {/* Los +/- son de a una unidad: en un artículo pesado (cantidad con decimales,
                    que sale del código de la balanza) no tienen sentido y no se muestran. */}
                {Number.isInteger(l.cantidad) ? (
                  <td className="cant-cell">
                    <button type="button" onClick={() => cambiarCantidad(l, -1)}
                      disabled={l.cantidad <= 1} title="Una unidad menos">−</button>
                    <span className="mono">{l.cantidad}</span>
                    <button type="button" onClick={() => cambiarCantidad(l, 1)} title="Una unidad más">+</button>
                  </td>
                ) : (
                  <td className="cant-cell"><span className="mono" title="Peso leído del código de barra">{formatearCantidad(l.cantidad)}</span></td>
                )}
                {/* Precio de folder: es un precio de promoción, no el habitual del artículo. */}
                <td className={l.esPrecioFolder ? "mono precio-folder" : "mono"}
                  title={l.listaPrecio ? `Lista ${l.listaPrecio}` : undefined}>
                  {formatearMoneda(l.precioUnit)}
                </td>
                <td className="mono">{formatearMoneda(l.descuento)}</td>
                <td className="mono">{formatearMoneda(l.neto)}</td>
                <td>{l.ofertasAplicadas.join(", ")}</td>
                <td><button className="danger" onClick={() => anularLinea(l.idDetalle)}>Anular</button></td>
              </tr>
            ))}
            {(!operacion || operacion.lineas.length === 0) && (
              <tr><td colSpan={8} className="muted">Sin artículos leídos.</td></tr>
            )}
          </tbody>
        </table>

        {buscadorAbierto && (
          <div className="modal-fondo" onClick={cerrarBuscador}>
            <div className="modal-caja" onClick={(e) => e.stopPropagation()}
              onKeyDown={(e) => { if (e.key === "Escape") cerrarBuscador(); }}>
              <div className="page-head">
                <h3>Buscar artículo</h3>
                <button onClick={cerrarBuscador}>Cerrar</button>
              </div>
              <div className="ident-search">
                <input autoFocus value={busquedaArt} onChange={(e) => setBusquedaArt(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && buscarArticulosManual(e.currentTarget.value)}
                  placeholder="Código, descripción o código de barra…" />
                <button className="primary" onClick={() => buscarArticulosManual()}>Buscar</button>
              </div>
              <p className="muted">
                Se agrega de a un artículo por vez, con la cantidad del campo de arriba
                ({cantidadPendiente}). Al agregarlo se cierra la búsqueda.
              </p>
              {buscandoArt && <p className="muted">Buscando…</p>}
              {!buscandoArt && resultadosArt.length > 0 && (
                <table className="grid">
                  <thead><tr><th>Código</th><th>Artículo</th><th>Presentación</th><th>Precio</th><th></th></tr></thead>
                  <tbody>
                    {resultadosArt.map((a) => (
                      <tr key={a.idPresentacion}>
                        <td className="mono">{a.codigoInterno}</td>
                        <td>{a.descripcion}</td>
                        <td>{a.descripcionTicket || `x${a.unidadXBulto}`}</td>
                        <td className="mono">
                          {formatearMoneda(a.tieneConvenio ? a.precioConvenio : a.precioVigente)}
                        </td>
                        <td className="row-actions">
                          <button className="primary" disabled={agregandoPres === a.idPresentacion}
                            onClick={() => agregarDesdeBuscador(a)}>
                            {agregandoPres === a.idPresentacion ? "Agregando…" : "Agregar"}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              {!buscandoArt && busquedaArtHecha && resultadosArt.length === 0 && (
                <p className="muted">Sin resultados para «{busquedaArtHecha}».</p>
              )}
            </div>
          </div>
        )}
      </div>
      {modalSupervisor}
    </div>
  );
}
