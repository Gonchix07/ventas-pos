import { api, unwrap } from "./client";

// ---- Lookups (catálogo simple) ----
export interface Lookup {
  id: number;
  descripcion: string;
}

export const lookups = {
  list: (resource: string) => unwrap<Lookup[]>(api.get(`/admin/${resource}`)),
  create: (resource: string, descripcion: string) =>
    unwrap<Lookup>(api.post(`/admin/${resource}`, { descripcion })),
  update: (resource: string, id: number, descripcion: string) =>
    unwrap<Lookup>(api.put(`/admin/${resource}/${id}`, { descripcion })),
  remove: (resource: string, id: number) =>
    unwrap<boolean>(api.delete(`/admin/${resource}/${id}`)),
};

// ---- Familias (lookup con sector) ----
// La familia cuelga de un sector y el nombre solo es único dentro del sector, así que tiene su
// propio ABM en vez del CRUD genérico de lookups.
export interface Familia extends Lookup {
  idSector?: number | null;
  sectorDescripcion?: string | null;
}

export const familias = {
  list: (idSector?: number) =>
    unwrap<Familia[]>(api.get(`/admin/familias`, { params: { idSector } })),
  create: (descripcion: string, idSector: number) =>
    unwrap<Familia>(api.post(`/admin/familias`, { descripcion, idSector })),
  update: (id: number, descripcion: string, idSector: number | null) =>
    unwrap<Familia>(api.put(`/admin/familias/${id}`, { descripcion, idSector })),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/familias/${id}`)),
};

export const referencias = {
  modosIva: () => unwrap<Lookup[]>(api.get(`/admin/referencias/modos-iva`)),
  condicionesIva: () => unwrap<Lookup[]>(api.get(`/admin/referencias/condiciones-iva`)),
  sucursales: () => unwrap<Lookup[]>(api.get(`/admin/referencias/sucursales`)),
  listasPrecios: () => unwrap<Lookup[]>(api.get(`/admin/referencias/listas-precios`)),
};

// ---- Artículos ----
export interface Barra {
  idBarra?: number;
  codigoBarra: string;
  tipo: number; // 1=EAN13, 2=DUN14
}
export interface Presentacion {
  idPresentacion?: number;
  unidadXBulto: number;
  descripcionTicket?: string | null;
  barras: Barra[];
}
export interface ArticuloListItem {
  idArticulo: number;
  codigoInterno: string;
  descripcion: string;
  idSector: number;
  idLinea: number;
  idFamilia: number;
  idModoIva: number;
  sectorDescripcion?: string | null;
  lineaDescripcion?: string | null;
  familiaDescripcion?: string | null;
  modoIvaDescripcion?: string | null;
  unidadMedida: number; // 0=Ninguna, 1=Kilogramo, 2=Litro
  contenidoNetoUnitario?: number | null;
  cantidadPresentaciones: number;
  cantidadBarras: number;
  primeraBarra?: string | null;
  activo: boolean;
  imagenUrl: string;
}
export interface ArticuloFiltro {
  texto?: string;
  idSector?: number;
  idLinea?: number;
  idFamilia?: number;
  activo?: boolean;
  /** Cuántas filas traer como máximo. El servidor lo recorta a su tope duro (500). */
  max?: number;
}
export interface ArticuloInput {
  codigoInterno: string;
  descripcion: string;
  idSector: number;
  idLinea: number;
  idFamilia: number;
  idModoIva: number;
  activo: boolean;
  unidadMedida: number; // 0=Ninguna, 1=Kilogramo, 2=Litro
  contenidoNetoUnitario?: number | null;
  /** Unidades por bulto del artículo (1 = no viene en bulto) — dato propio de la ficha, distinto
   *  del unidadXBulto de cada Presentacion/código de barras. Lo usa la codificación de cantidades
   *  de la interfase contable. */
  unidadXBulto: number;
  /** Se vende suelto por peso (etiqueta de balanza) — la interfase contable codifica la cantidad
   *  distinto en ese caso (siempre 3 decimales: kilo entero + gramos). */
  ventaPorPeso: boolean;
  presentaciones: Presentacion[];
}

export const articulos = {
  list: (filtro?: ArticuloFiltro) =>
    unwrap<ArticuloListItem[]>(api.get(`/admin/articulos`, { params: filtro })),
  get: (id: number) => unwrap<ArticuloInput & { idArticulo: number }>(api.get(`/admin/articulos/${id}`)),
  create: (input: ArticuloInput) => unwrap<number>(api.post(`/admin/articulos`, input)),
  update: (id: number, input: ArticuloInput) => unwrap<boolean>(api.put(`/admin/articulos/${id}`, input)),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/articulos/${id}`)),
};

// ---- Clientes ----
/** Persona habilitada a comprar en nombre del cliente. */
export interface Autorizado {
  idAutorizado: number;
  dni: string;
  descripcion: string;
  /** Fecha de alta (ISO yyyy-MM-dd o fecha completa del backend). */
  fechaAlta: string;
  activo: boolean;
}
/** Al guardar: sin `idAutorizado` = nuevo; los que no se manden se borran. */
export interface AutorizadoInput {
  idAutorizado?: number | null;
  dni: string;
  descripcion: string;
  fechaAlta?: string | null;
  activo: boolean;
}

export interface Cliente {
  idCliente: number;
  codigoInt: string;
  cuit?: string | null;
  documento?: string | null;
  descripcion: string;
  /** Con qué se lo conoce en el mostrador ("LA VACA LOCA"); casi nunca coincide con la razón social. */
  nombreFantasia?: string | null;
  idCondIva: number;
  condIvaDescripcion?: string | null;
  permitePresupuesto: boolean;
  /** Habilitación previa al límite de crédito: sin esto no se le puede cargar cuenta corriente. */
  admiteCuentaCorriente: boolean;
  activo: boolean;
  domicilio?: string | null;
  codigoPostal?: string | null;
  localidad?: string | null;
  provincia?: string | null;
  email?: string | null;
  /** Solo viene en el detalle (`clientes.get`), no en el listado. */
  autorizados?: Autorizado[] | null;
}
export interface ClienteInput {
  codigoInt: string;
  cuit?: string | null;
  documento?: string | null;
  descripcion: string;
  nombreFantasia?: string | null;
  idCondIva: number;
  permitePresupuesto: boolean;
  admiteCuentaCorriente: boolean;
  activo: boolean;
  domicilio?: string | null;
  codigoPostal?: string | null;
  localidad?: string | null;
  provincia?: string | null;
  email?: string | null;
  /** La lista completa: lo que no se manda se borra. */
  autorizados?: AutorizadoInput[];
}

export const clientes = {
  /** `admiteCuentaCorriente: true` trae solo los habilitados para cuenta corriente. */
  list: (q?: string, admiteCuentaCorriente?: boolean) =>
    unwrap<Cliente[]>(api.get(`/admin/clientes`, { params: { q, admiteCuentaCorriente } })),
  /** Detalle: es el único que trae los autorizados. */
  get: (id: number) => unwrap<Cliente>(api.get(`/admin/clientes/${id}`)),
  create: (input: ClienteInput) => unwrap<number>(api.post(`/admin/clientes`, input)),
  update: (id: number, input: ClienteInput) => unwrap<boolean>(api.put(`/admin/clientes/${id}`, input)),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/clientes/${id}`)),
};

// ---- Listas de precios + precios ----
export interface ListaPrecio {
  idListaPrecio: number;
  idSucursal: number;
  sucursalDescripcion?: string | null;
  codigoInterno: string;
  tipo: number; // 1=Base, 2=Temporal, 3=Folder
  tipoDescripcion: string;
  prioridad: number;
  fechaInicio?: string | null;
  fechaFin?: string | null;
  cantidadPrecios: number;
}
export interface ListaPrecioInput {
  idSucursal: number;
  codigoInterno: string;
  tipo: number;
  prioridad: number;
  fechaInicio?: string | null;
  fechaFin?: string | null;
}
export interface PrecioRow {
  idPresentacion: number;
  idArticulo: number;
  codigoInterno: string;
  articuloDescripcion: string;
  descripcionTicket?: string | null;
  unidadXBulto: number;
  precioFinal: number;
  impuestoInterno: number;
}

/** Resultado de aplicar un precio unitario a todas las presentaciones de un artículo. */
export interface PrecioAplicado {
  idPresentacion: number;
  descripcionTicket?: string | null;
  unidadXBulto: number;
  precioFinal: number;
  impuestoInterno: number;
}

// ---- Tipos / Medios de pago ----
export interface TipoPago {
  idTipoPago: number; descripcion: string;
  fuente: number; fuenteDescripcion: string;
  canal: number; canalDescripcion: string;
  cantidadMedios: number;
}
export interface TipoPagoInput { descripcion: string; fuente: number; canal: number; }
export interface MedioPago {
  idMedioPago: number; descripcion: string; idTipoPago: number; tipoPagoDescripcion?: string | null;
  canal: number; canalDescripcion?: string | null;
  /** El que la caja propone al abrir el cobro. Hay uno solo en todo el sistema. */
  esPredeterminado: boolean;
  activo: boolean;
  /** Si al cobrar con este medio corresponde imprimir un comprobante adicional. Todavía no se usa
   *  en Caja — se define más adelante qué hacer con esto. */
  imprimeComprobante: boolean;
  /** Si está seteado, el medio solo se ofrece a los clientes de ese cluster. Null = todos. */
  idCluster?: number | null;
  clusterDescripcion?: string | null;
  /** Código de tarjeta del sistema contable externo (interfase MySQL, cupones.tarjeta) — solo
   *  tiene sentido para medios de Tarjeta. Null si no está cargado. */
  codigoTarjetaInterfase?: string | null;
}
export interface MedioPagoInput {
  descripcion: string; idTipoPago: number; esPredeterminado: boolean; activo: boolean;
  imprimeComprobante: boolean; idCluster: number | null; codigoTarjetaInterfase?: string | null;
}

/** Plan de cuotas de un medio Tarjeta (ej. "3 cuotas sin interés"). Se elige junto con el medio al cobrar. */
export interface PlanCuota { idPlan: number; idMedioPago: number; denominacion: string; cantidadCuotas: number; }
export interface PlanCuotaInput { denominacion: string; cantidadCuotas: number; }

/** Familia genérica del tipo de pago (clasifica; no decide por dónde se cobra). */
export const FUENTES_PAGO = [
  { v: 1, l: "Efectivo" }, { v: 2, l: "Tarjetas" }, { v: 3, l: "Billetera virtual" },
  { v: 4, l: "Transferencia" }, { v: 5, l: "Cuenta corriente" }, { v: 6, l: "Cheque" },
];

/** Por dónde se efectúa el cobro. Se configura en el tipo y lo heredan todos sus medios. */
export const CANALES_COBRO = [
  { v: 1, l: "Manual" },
  { v: 2, l: "iCARD" },
];

export const pagos = {
  tipos: () => unwrap<TipoPago[]>(api.get(`/admin/tipos-pago`)),
  createTipo: (i: TipoPagoInput) => unwrap<number>(api.post(`/admin/tipos-pago`, i)),
  updateTipo: (id: number, i: TipoPagoInput) => unwrap<boolean>(api.put(`/admin/tipos-pago/${id}`, i)),
  removeTipo: (id: number) => unwrap<boolean>(api.delete(`/admin/tipos-pago/${id}`)),
  medios: () => unwrap<MedioPago[]>(api.get(`/admin/medios-pago`)),
  createMedio: (i: MedioPagoInput) => unwrap<number>(api.post(`/admin/medios-pago`, i)),
  updateMedio: (id: number, i: MedioPagoInput) => unwrap<boolean>(api.put(`/admin/medios-pago/${id}`, i)),
  removeMedio: (id: number) => unwrap<boolean>(api.delete(`/admin/medios-pago/${id}`)),
  planes: (idMedioPago: number) => unwrap<PlanCuota[]>(api.get(`/admin/medios-pago/${idMedioPago}/planes`)),
  createPlan: (idMedioPago: number, i: PlanCuotaInput) =>
    unwrap<number>(api.post(`/admin/medios-pago/${idMedioPago}/planes`, i)),
  updatePlan: (id: number, i: PlanCuotaInput) => unwrap<boolean>(api.put(`/admin/planes/${id}`, i)),
  removePlan: (id: number) => unwrap<boolean>(api.delete(`/admin/planes/${id}`)),
};

// ---- Empresas / Sucursales ----
// Los datos fiscales de la empresa y el domicilio de la sucursal son los que encabezan la factura
// (A y B): razón social, CUIT, condición frente al IVA, Ing. Brutos e inicio de actividad.
export interface DatosFiscales {
  condicionIva?: string | null;
  ingresosBrutos?: string | null;
  inicioActividad?: string | null; // ISO (yyyy-MM-dd)
  domicilio?: string | null;
  localidad?: string | null;
  provincia?: string | null;
  codigoPostal?: string | null;
}
export interface Domicilio {
  domicilio?: string | null;
  localidad?: string | null;
  provincia?: string | null;
  codigoPostal?: string | null;
}
export interface Empresa extends DatosFiscales {
  idEmpresa: number; codigoInterno: string; descripcion: string;
  cuit?: string | null; certificadoAlias?: string | null;
}
export interface EmpresaInput extends DatosFiscales {
  codigoInterno: string; descripcion: string; cuit?: string | null; certificadoAlias?: string | null;
}
export interface Sucursal extends Domicilio {
  idSucursal: number; idEmpresa: number; empresaDescripcion?: string | null; descripcion: string;
}
export interface SucursalInput extends Domicilio { idEmpresa: number; descripcion: string; }

// El certificado (.pfx/.p12) se guarda en el servidor; acá solo viaja la metadata para mostrar
// el estado en el ABM. La contraseña nunca vuelve del backend.
export interface CertificadoCae {
  presente: boolean;
  nombreArchivo?: string | null;
  vencimiento?: string | null; // ISO
  subidoUtc?: string | null; // ISO
}

export const estructura = {
  empresas: () => unwrap<Empresa[]>(api.get(`/admin/empresas`)),
  createEmpresa: (i: EmpresaInput) => unwrap<number>(api.post(`/admin/empresas`, i)),
  updateEmpresa: (id: number, i: EmpresaInput) => unwrap<boolean>(api.put(`/admin/empresas/${id}`, i)),
  removeEmpresa: (id: number) => unwrap<boolean>(api.delete(`/admin/empresas/${id}`)),
  certificado: (idEmpresa: number) => unwrap<CertificadoCae>(api.get(`/admin/empresas/${idEmpresa}/certificado`)),
  subirCertificado: (idEmpresa: number, archivo: File, clave: string) => {
    const form = new FormData();
    form.append("archivo", archivo);
    form.append("clave", clave);
    return unwrap<CertificadoCae>(api.post(`/admin/empresas/${idEmpresa}/certificado`, form));
  },
  // Alternativa cuando no se tiene un .pfx ya armado: ARCA solo entrega el certificado (.crt/.cer);
  // la clave privada (.key) la generó quien tramitó el certificado. El backend combina ambos.
  subirCertificadoDesdeClaveYCert: (idEmpresa: number, clavePrivada: File, certificado: File, passphrase: string) => {
    const form = new FormData();
    form.append("clavePrivada", clavePrivada);
    form.append("certificado", certificado);
    if (passphrase) form.append("passphrase", passphrase);
    return unwrap<CertificadoCae>(api.post(`/admin/empresas/${idEmpresa}/certificado/clave-cert`, form));
  },
  removeCertificado: (idEmpresa: number) => unwrap<boolean>(api.delete(`/admin/empresas/${idEmpresa}/certificado`)),
  // Solo lectura contra ARCA (login WSAA + FEDummy + FECompUltimoAutorizado) — nunca emite ni
  // autoriza un comprobante real.
  probarConexionAfip: (idEmpresa: number, ptoVta: number, cbteTipo: number) =>
    unwrap<ProbarConexionAfip>(api.get(`/admin/empresas/${idEmpresa}/certificado/probar-conexion`, { params: { ptoVta, cbteTipo } })),
  sucursales: () => unwrap<Sucursal[]>(api.get(`/admin/sucursales`)),
  createSucursal: (i: SucursalInput) => unwrap<number>(api.post(`/admin/sucursales`, i)),
  updateSucursal: (id: number, i: SucursalInput) => unwrap<boolean>(api.put(`/admin/sucursales/${id}`, i)),
  removeSucursal: (id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${id}`)),
};

export interface ProbarConexionAfip {
  wsaaOk: boolean; wsaaError?: string | null;
  dummyOk: boolean; dummyError?: string | null;
  ultimoAutorizado?: number | null; ultimoAutorizadoError?: string | null;
  certificadoSubject?: string | null; certificadoIssuer?: string | null; certificadoThumbprint?: string | null;
}

// ---- CAEA precargado (contingencia cuando WSFEv1/CAE no responde) ----
export interface CaeaCargado {
  idCaea: number; idEmpresa: number; anio: number; mes: number; orden: number;
  valor: string; vigenciaDesde: string; vigenciaHasta: string; vigenteHoy: boolean;
}
export interface CaeaCargadoInput {
  idEmpresa: number; anio: number; mes: number; orden: number;
  valor: string; vigenciaDesde: string; vigenciaHasta: string;
}

export const caea = {
  list: (idEmpresa: number) => unwrap<CaeaCargado[]>(api.get(`/admin/empresas/${idEmpresa}/caea`)),
  create: (idEmpresa: number, i: CaeaCargadoInput) => unwrap<number>(api.post(`/admin/empresas/${idEmpresa}/caea`, i)),
  update: (idCaea: number, i: CaeaCargadoInput) => unwrap<boolean>(api.put(`/admin/caea/${idCaea}`, i)),
  remove: (idCaea: number) => unwrap<boolean>(api.delete(`/admin/caea/${idCaea}`)),
};

// ---- Configuraciones ----
export interface Configuracion { idConfiguracion: number; clave: string; descripcion: string; valor?: string | null; }
export interface ConfiguracionInput { clave: string; descripcion: string; valor?: string | null; }

export const configuraciones = {
  list: () => unwrap<Configuracion[]>(api.get(`/admin/configuraciones`)),
  create: (i: ConfiguracionInput) => unwrap<number>(api.post(`/admin/configuraciones`, i)),
  update: (id: number, i: ConfiguracionInput) => unwrap<boolean>(api.put(`/admin/configuraciones/${id}`, i)),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/configuraciones/${id}`)),
};

// ---- Conexión a datos externa (MySQL) ----
// Fila única: a futuro la app deposita acá datos para que los consuma otro sistema.
// tieneContrasena reemplaza al valor real (nunca viaja descifrado); en el Input, password
// null/vacío = no tocar la contraseña ya guardada.
export interface ConexionExternaMySql {
  host: string; puerto: number; baseDatos: string; usuario: string;
  tieneContrasena: boolean; habilitada: boolean;
}
export interface ConexionExternaMySqlInput {
  host: string; puerto: number; baseDatos: string; usuario: string;
  password?: string | null; habilitada: boolean;
}
/** error viene tal cual lo devuelve el driver de MySQL (host/usuario/clave incorrectos, etc.) —
 *  nunca se expone la contraseña. */
export interface ProbarConexionResultado { ok: boolean; error?: string | null; }

export const conexionExterna = {
  get: () => unwrap<ConexionExternaMySql>(api.get(`/admin/conexion-externa`)),
  update: (i: ConexionExternaMySqlInput) => unwrap<boolean>(api.put(`/admin/conexion-externa`, i)),
  probar: (i: ConexionExternaMySqlInput) =>
    unwrap<ProbarConexionResultado>(api.post(`/admin/conexion-externa/probar`, i)),
};

// ---- Estructura de caja (por sucursal) ----
/** Catálogo FIJO: ELECTRONICA / FISCAL / PRESUPUESTO. No se dan de alta ni se borran. */
export interface TipoPuntoVenta {
  idSucursal: number; idTipoPuntoVenta: number; descripcion: string; tipoArca?: string | null;
  detalle: string; requiereIpControlador: boolean;
}
export interface PuntoVenta {
  idSucursal: number; idPuntoVenta: number; idTipoPuntoVenta: number; tipoDescripcion?: string | null;
  numeroPuntoVenta: number; /** Solo en los FISCAL: IP del controlador Hasar. */ ipControlador?: string | null;
}
export interface Puesto {
  idSucursal: number; idPuestoAsignado: number; nombrePc: string;
  /** null = todavía no se vinculó ninguna PC a este puesto (ver cajaEstructura.vincularEquipo). */
  identificadorEquipo?: string | null;
  /** Ya no se usa para resolver la caja — solo dato informativo/auditoría. */
  ip?: string | null;
}
export interface TerminalTarjeta {
  idSucursal: number; idTerminal: number; numeroTerminal: string; tipo: number; tipoDescripcion: string;
  /** Caja a la que está asignada (null = sin asignar). Una terminal cuelga de UNA sola caja. */
  idCajaAsignada?: number | null; cajaDescripcion?: string | null;
}
/** FiServ / PayWay / PinPad — ver enum TipoTerminalTarjeta en el backend. */
export const TIPOS_TERMINAL = [
  { v: 1, l: "FiServ" }, { v: 2, l: "PayWay" }, { v: 3, l: "PinPad" },
];
export interface CajaFisica {
  idSucursal: number; idCaja: number; idPuntoVenta: number; descripcion: string;
  idPuestoAsignado?: number | null; nombrePc?: string | null; ip?: string | null;
  /** Si esta caja puede vender con modo Presupuesto (además el cliente necesita su propio permiso). */
  admitePresupuesto: boolean;
}

export const cajaEstructura = {
  // Los tipos son fijos: solo lectura (no hay alta ni baja en la API).
  tiposPv: (suc: number) => unwrap<TipoPuntoVenta[]>(api.get(`/admin/sucursales/${suc}/tipos-punto-venta`)),

  puntosVenta: (suc: number) => unwrap<PuntoVenta[]>(api.get(`/admin/sucursales/${suc}/puntos-venta`)),
  createPv: (suc: number, idTipoPuntoVenta: number, numeroPuntoVenta: number, ipControlador: string | null) =>
    unwrap<number>(api.post(`/admin/sucursales/${suc}/puntos-venta`, { idTipoPuntoVenta, numeroPuntoVenta, ipControlador })),
  updatePv: (suc: number, id: number, idTipoPuntoVenta: number, numeroPuntoVenta: number, ipControlador: string | null) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/puntos-venta/${id}`, { idTipoPuntoVenta, numeroPuntoVenta, ipControlador })),
  removePv: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/puntos-venta/${id}`)),

  puestos: (suc: number) => unwrap<Puesto[]>(api.get(`/admin/sucursales/${suc}/puestos`)),
  createPuesto: (suc: number, nombrePc: string, ip: string | null) =>
    unwrap<number>(api.post(`/admin/sucursales/${suc}/puestos`, { nombrePc, ip })),
  updatePuesto: (suc: number, id: number, nombrePc: string, ip: string | null) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/puestos/${id}`, { nombrePc, ip })),
  removePuesto: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/puestos/${id}`)),
  // Sin body: el identificador lo manda el interceptor de client.ts (header X-Puesto-Id) desde la
  // PC en la que está parado quien hace el click — por eso hay que vincular desde esa misma PC.
  vincularEquipo: (suc: number, id: number) =>
    unwrap<boolean>(api.post(`/admin/sucursales/${suc}/puestos/${id}/vincular-equipo`)),

  cajas: (suc: number) => unwrap<CajaFisica[]>(api.get(`/admin/sucursales/${suc}/cajas`)),
  createCaja: (suc: number, idPuntoVenta: number, descripcion: string, idPuestoAsignado: number | null, admitePresupuesto: boolean) =>
    unwrap<number>(api.post(`/admin/sucursales/${suc}/cajas`, { idPuntoVenta, descripcion, idPuestoAsignado, admitePresupuesto })),
  updateCaja: (suc: number, id: number, idPuntoVenta: number, descripcion: string, idPuestoAsignado: number | null, admitePresupuesto: boolean) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/cajas/${id}`, { idPuntoVenta, descripcion, idPuestoAsignado, admitePresupuesto })),
  removeCaja: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/cajas/${id}`)),

  terminales: (suc: number) => unwrap<TerminalTarjeta[]>(api.get(`/admin/sucursales/${suc}/terminales-tarjeta`)),
  createTerminal: (suc: number, numeroTerminal: string, tipo: number, idCajaAsignada: number | null = null) =>
    unwrap<number>(api.post(`/admin/sucursales/${suc}/terminales-tarjeta`, { numeroTerminal, tipo, idCajaAsignada })),
  updateTerminal: (suc: number, id: number, numeroTerminal: string, tipo: number, idCajaAsignada: number | null) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/terminales-tarjeta/${id}`, { numeroTerminal, tipo, idCajaAsignada })),
  removeTerminal: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/terminales-tarjeta/${id}`)),
};

// ---- Usuarios / Roles ----
export interface Rol { idRol: number; descripcion: string; }
// codigoSupervisor: el de 8 dígitos del control de supervisor (nota de crédito, anular artículo,
// abrir caja en otro puesto). Solo tiene sentido cargado en usuarios Supervisor/Administrador.
export interface Usuario {
  idUsuario: number; nombreUsuario: string; idRol: number; rol?: string | null; activo: boolean;
  codigoSupervisor?: string | null;
}
export interface UsuarioCreateInput {
  nombreUsuario: string; clave: string; idRol: number; activo: boolean; codigoSupervisor?: string | null;
}
export interface UsuarioUpdateInput {
  nombreUsuario: string; idRol: number; activo: boolean; codigoSupervisor?: string | null;
}

export const usuarios = {
  roles: () => unwrap<Rol[]>(api.get(`/admin/roles`)),
  list: () => unwrap<Usuario[]>(api.get(`/admin/usuarios`)),
  create: (i: UsuarioCreateInput) => unwrap<number>(api.post(`/admin/usuarios`, i)),
  update: (id: number, i: UsuarioUpdateInput) => unwrap<boolean>(api.put(`/admin/usuarios/${id}`, i)),
  resetClave: (id: number, nuevaClave: string) => unwrap<boolean>(api.post(`/admin/usuarios/${id}/reset-clave`, { nuevaClave })),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/usuarios/${id}`)),
};

// ---- Ofertas ----
/** Comportamiento de cada tipo de oferta (columna Codigo de TiposOferta = TipoOfertaEnum del backend). */
export const TipoOfertaCodigo = {
  Descuento: 1,
  MixCanasta: 2,
  /** Legacy "lleva N + M": ya no se ofrece en el alta, pero puede venir en ofertas viejas. */
  Bonificacion: 3,
  DosPorUno: 4,
  SegundaUnidad: 5,
} as const;

/** De qué canasta es un artículo en una Mix Canasta. */
export const RolItemCanasta = { Condicion: 1, Bonificado: 2 } as const;

export interface TipoOferta { id: number; descripcion: string; codigo: number; }
export interface ItemCanasta { idArticulo: number; cantidad: number; rol: number; articuloDescripcion?: string | null; }
export interface Alcance { idCluster?: number | null; idLinea?: number | null; idSector?: number | null; idFamilia?: number | null; idArticulo?: number | null; esExcepcion: boolean; articuloDescripcion?: string | null; }
export interface Accion { idTipoOferta: number; idPresentacion?: number | null; porcentaje?: number | null; montoFijo?: number | null; cantidadMin?: number | null; cantidadBonif?: number | null; items?: ItemCanasta[]; }
export interface OfertaListItem { idSucursal: number; idOferta: number; descripcion: string; fechaInicio: string; fechaFin: string; acumula: boolean; permiteConvenio: boolean; cantAlcances: number; cantAcciones: number; }
export interface OfertaInput { descripcion: string; fechaInicio: string; fechaFin: string; acumula: boolean; permiteConvenio: boolean; alcances: Alcance[]; acciones: Accion[]; }
/** Detalle completo (con alcances y acciones): es lo que devuelve el GET por id. */
export interface OfertaDetail extends OfertaInput { idSucursal: number; idOferta: number; }

export const ofertas = {
  list: (suc: number) => unwrap<OfertaListItem[]>(api.get(`/admin/sucursales/${suc}/ofertas`)),
  get: (suc: number, id: number) => unwrap<OfertaDetail>(api.get(`/admin/sucursales/${suc}/ofertas/${id}`)),
  create: (suc: number, input: OfertaInput) => unwrap<number>(api.post(`/admin/sucursales/${suc}/ofertas`, input)),
  update: (suc: number, id: number, input: OfertaInput) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/ofertas/${id}`, input)),
  remove: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/ofertas/${id}`)),
  /** Solo los tipos seleccionables: son fijos, no hay ABM. */
  tipos: () => unwrap<TipoOferta[]>(api.get(`/admin/tipos-oferta`)),
};

// ---- Ofertas por medio de pago (se aplican en el cobro, no en el carrito) ----
// idPlanCuota null = aplica en cualquier cantidad de cuotas de ese medio (o no es tarjeta).
export interface OfertaMedioPago {
  idSucursal: number; idOfertaMedioPago: number; descripcion: string;
  idMedioPago: number; medioPagoDescripcion?: string | null;
  idPlanCuota?: number | null; planCuotaDescripcion?: string | null;
  porcentaje: number; topeMaximo: number; activo: boolean;
  fechaInicio: string; fechaFin: string;
}
export interface OfertaMedioPagoInput {
  descripcion: string; idMedioPago: number; idPlanCuota?: number | null;
  porcentaje: number; topeMaximo: number; activo: boolean;
  fechaInicio: string; fechaFin: string;
}
export const ofertasMedioPago = {
  list: (suc: number) => unwrap<OfertaMedioPago[]>(api.get(`/admin/sucursales/${suc}/ofertas-medio-pago`)),
  create: (suc: number, input: OfertaMedioPagoInput) =>
    unwrap<number>(api.post(`/admin/sucursales/${suc}/ofertas-medio-pago`, input)),
  update: (suc: number, id: number, input: OfertaMedioPagoInput) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/ofertas-medio-pago/${id}`, input)),
  remove: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/ofertas-medio-pago/${id}`)),
};

// ---- Convenios ----
export interface Convenio { idSucursal: number; idConvenio: number; idCliente: number; clienteDescripcion?: string | null; descuento: number; idListaPrecio?: number | null; listaCodigo?: string | null; }
export const convenios = {
  list: (suc: number) => unwrap<Convenio[]>(api.get(`/admin/sucursales/${suc}/convenios`)),
  create: (suc: number, idCliente: number, descuento: number, idListaPrecio: number | null) =>
    unwrap<number>(api.post(`/admin/sucursales/${suc}/convenios`, { idCliente, descuento, idListaPrecio })),
  remove: (suc: number, id: number) => unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/convenios/${id}`)),
};

// ---- Cuenta corriente (límite de crédito) ----
export interface CuentaCorrienteLimite {
  idSucursal: number; idCliente: number; clienteDescripcion: string;
  limiteCredito: number; saldoActual: number;
}
export const cuentaCorriente = {
  list: (suc: number) => unwrap<CuentaCorrienteLimite[]>(api.get(`/admin/sucursales/${suc}/cuenta-corriente`)),
  upsert: (suc: number, idCliente: number, limiteCredito: number) =>
    unwrap<boolean>(api.put(`/admin/sucursales/${suc}/cuenta-corriente/${idCliente}`, { limiteCredito })),
  remove: (suc: number, idCliente: number) =>
    unwrap<boolean>(api.delete(`/admin/sucursales/${suc}/cuenta-corriente/${idCliente}`)),
};

// ---- Clusters ----
export interface Cluster { idCluster: number; descripcion: string; cantidadClientes: number; }
export interface ClusterMiembro { idCliente: number; clienteDescripcion: string; codigoInt: string; }
export interface ClusterMiembrosResultado { agregados: number; quitados: number; total: number; }
export const clusters = {
  list: () => unwrap<Cluster[]>(api.get(`/admin/clusters`)),
  miembros: (id: number) => unwrap<ClusterMiembro[]>(api.get(`/admin/clusters/${id}/miembros`)),
  create: (descripcion: string) => unwrap<number>(api.post(`/admin/clusters`, { descripcion })),
  rename: (id: number, descripcion: string) => unwrap<boolean>(api.put(`/admin/clusters/${id}`, { descripcion })),
  addMiembro: (id: number, idCliente: number) => unwrap<boolean>(api.post(`/admin/clusters/${id}/miembros`, { idCliente })),
  removeMiembro: (id: number, idCliente: number) => unwrap<boolean>(api.delete(`/admin/clusters/${id}/miembros/${idCliente}`)),
  /** Guardado en lote: deja como miembros exactamente los ids indicados. */
  setMiembros: (id: number, idsClientes: number[]) =>
    unwrap<ClusterMiembrosResultado>(api.put(`/admin/clusters/${id}/miembros`, { idsClientes })),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/clusters/${id}`)),
};

// ---- Tarjetas ----
export interface TipoTarjeta { idTipoTarjeta: number; descripcion: string; idListaPrecio?: number | null; listaCodigo?: string | null; }
export interface TarjetaCliente {
  idCliente: number; idTipoTarjeta: number; tipoDescripcion?: string | null; nroTarjeta: string;
  /** El cliente tiene UNA vigente; las anteriores quedan anuladas (se guardan como historia). */
  activa: boolean;
  fechaBajaUtc?: string | null;
}
/** El alta anula la tarjeta que el cliente tuviera vigente; acá viene cuál se anuló. */
export interface AltaTarjetaResultado { ok: boolean; anuladas: number; nroAnulada?: string | null; tipoAnulada?: string | null; }
export const tarjetas = {
  tipos: () => unwrap<TipoTarjeta[]>(api.get(`/admin/tipos-tarjeta`)),
  createTipo: (descripcion: string, idListaPrecio: number | null) => unwrap<number>(api.post(`/admin/tipos-tarjeta`, { descripcion, idListaPrecio })),
  removeTipo: (id: number) => unwrap<boolean>(api.delete(`/admin/tipos-tarjeta/${id}`)),
  deCliente: (idCliente: number) => unwrap<TarjetaCliente[]>(api.get(`/admin/clientes/${idCliente}/tarjetas`)),
  add: (idCliente: number, idTipoTarjeta: number, nroTarjeta: string) =>
    unwrap<AltaTarjetaResultado>(api.post(`/admin/clientes/${idCliente}/tarjetas`, { idTipoTarjeta, nroTarjeta })),
  remove: (idCliente: number, idTipoTarjeta: number, nroTarjeta: string) => unwrap<boolean>(api.delete(`/admin/clientes/${idCliente}/tarjetas/${idTipoTarjeta}/${encodeURIComponent(nroTarjeta)}`)),
};

// ---- Padrones ----
export interface PadronIibb { cuit: string; percepcion: number; }
export interface PadronExIva { cuit: string; }
/** Resultado de reemplazar el padrón completo desde un archivo. */
export interface ImportacionPadron {
  filasLeidas: number;
  importadas: number;
  sinPercepcion: number;
  invalidas: number;
  borradasPrevias: number;
  milisegundosTotales: number;
}

/**
 * Avance del import. El servidor procesa el archivo A MEDIDA que lo recibe (lo lee en streaming),
 * así que el progreso de la subida es el progreso real del procesamiento, no solo de la red.
 */
export type ProgresoImport = (porcentaje: number, bytesEnviados: number) => void;

export const padrones = {
  iibb: (q?: string) => unwrap<PadronIibb[]>(api.get(`/admin/padrones/iibb`, { params: { q } })),
  /**
   * Sube el TXT del padrón RGS y reemplaza TODO el padrón de IIBB. El archivo real pesa cientos de
   * MB: sin `timeout: 0` axios cortaría la subida a mitad de camino.
   */
  importarIibb: (archivo: File, incluirSinPercepcion: boolean, onProgreso?: ProgresoImport) => {
    const fd = new FormData();
    fd.append("archivo", archivo);
    return unwrap<ImportacionPadron>(api.post(`/admin/padrones/iibb/importar`, fd, {
      params: { incluirSinPercepcion },
      timeout: 0,
      onUploadProgress: (e) => {
        if (onProgreso && e.total) onProgreso(Math.round((e.loaded * 100) / e.total), e.loaded);
      },
    }));
  },
  upsertIibb: (cuit: string, percepcion: number) => unwrap<boolean>(api.put(`/admin/padrones/iibb`, { cuit, percepcion })),
  removeIibb: (cuit: string) => unwrap<boolean>(api.delete(`/admin/padrones/iibb/${cuit}`)),
  exIva: (q?: string) => unwrap<PadronExIva[]>(api.get(`/admin/padrones/excepcion-iva`, { params: { q } })),
  /** Archivo de ancho fijo: el CUIT son los primeros 11 caracteres de cada línea. */
  importarExIva: (archivo: File, onProgreso?: ProgresoImport) => {
    const fd = new FormData();
    fd.append("archivo", archivo);
    return unwrap<ImportacionPadron>(api.post(`/admin/padrones/excepcion-iva/importar`, fd, {
      timeout: 0,
      onUploadProgress: (e) => {
        if (onProgreso && e.total) onProgreso(Math.round((e.loaded * 100) / e.total), e.loaded);
      },
    }));
  },
  addExIva: (cuit: string) => unwrap<boolean>(api.post(`/admin/padrones/excepcion-iva`, { cuit })),
  removeExIva: (cuit: string) => unwrap<boolean>(api.delete(`/admin/padrones/excepcion-iva/${cuit}`)),
};

export const listasPrecios = {
  list: () => unwrap<ListaPrecio[]>(api.get(`/admin/listas-precios`)),
  create: (input: ListaPrecioInput) => unwrap<number>(api.post(`/admin/listas-precios`, input)),
  update: (id: number, input: ListaPrecioInput) => unwrap<boolean>(api.put(`/admin/listas-precios/${id}`, input)),
  remove: (id: number) => unwrap<boolean>(api.delete(`/admin/listas-precios/${id}`)),
  // Máximo 50 filas (el backend topea): para encontrar un precio puntual se pasa `texto`.
  precios: (id: number, texto?: string) =>
    unwrap<PrecioRow[]>(api.get(`/admin/listas-precios/${id}/precios`, { params: { texto } })),
  // Precios de artículos concretos (los que muestra el buscador), sin tope.
  preciosDeArticulos: (id: number, idsArticulos: number[]) =>
    unwrap<PrecioRow[]>(api.get(`/admin/listas-precios/${id}/precios`,
      { params: { idsArticulos: idsArticulos.join(",") } })),
  setPrecio: (id: number, idPresentacion: number, precioFinal: number, impuestoInterno: number) =>
    unwrap<boolean>(api.put(`/admin/listas-precios/${id}/precios/${idPresentacion}`, { precioFinal, impuestoInterno })),
  /** Un único precio unitario → se aplica a todas las presentaciones × unidades por bulto. */
  setPrecioArticulo: (id: number, idArticulo: number, precioUnitario: number, impuestoInternoUnitario: number) =>
    unwrap<PrecioAplicado[]>(api.put(`/admin/listas-precios/${id}/articulos/${idArticulo}/precio`,
      { precioUnitario, impuestoInternoUnitario })),
  removePrecio: (id: number, idPresentacion: number) =>
    unwrap<boolean>(api.delete(`/admin/listas-precios/${id}/precios/${idPresentacion}`)),
};

// ---- Permisos por rol (acceso a los módulos del menú principal) ----
export interface ModuloPermiso { idModulo: number; descripcion: string; }
export interface CeldaPermiso { idModulo: number; puedeVer: boolean; }
export interface FilaPermisoRol { idRol: number; rolDescripcion: string; celdas: CeldaPermiso[]; }
export interface MatrizPermisos { modulos: ModuloPermiso[]; roles: FilaPermisoRol[]; }

export const permisos = {
  matriz: () => unwrap<MatrizPermisos>(api.get(`/admin/permisos`)),
  actualizar: (idRol: number, idModulo: number, puedeVer: boolean) =>
    unwrap<boolean>(api.put(`/admin/permisos`, { idRol, idModulo, puedeVer })),
};
