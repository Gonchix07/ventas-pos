import { api, unwrap } from "./client";
import type { Acumulado, DeclaracionPago, CierreZResultado } from "./caja";

export interface CajaResumen {
  idSucursal: number; sucursalDescripcion?: string | null; idCaja: number; cajaDescripcion: string;
  estado: string; idLote?: number | null; cajero?: string | null;
  fechaApertura?: string | null; fechaCierre?: string | null;
  totalLote?: number | null;
}
export interface Dashboard { cajas: CajaResumen[]; acumuladoGeneral: number; acumuladoPorMedio: Acumulado[]; }

export interface CierreListItem {
  idSucursal: number; idLote: number; idCaja: number; cajero?: string | null;
  idMedioPago: number; medioDescripcion: string; total: number; diferenciaTotal: number;
  idMotivoDiferencia?: number | null; observacionesCajero?: string | null;
  verificaTesoreria: boolean; fechaCierre?: string | null;
}

export interface MotivoCierre { id: number; descripcion: string; }

/** Lote que quedó abierto un día anterior: su cajero ya no puede cerrarlo desde Caja. */
export interface LotePendiente {
  idSucursal: number; sucursalDescripcion?: string | null; idLote: number; idCaja: number;
  cajaDescripcion: string; cajero?: string | null; fechaApertura: string; diasPendiente: number;
  acumulados: Acumulado[]; totalEsperado: number;
}

export const tesoreria = {
  dashboard: (idSucursal?: number) => unwrap<Dashboard>(api.get(`/tesoreria/dashboard`, { params: { idSucursal } })),
  motivosCierre: () => unwrap<MotivoCierre[]>(api.get(`/tesoreria/motivos-cierre`)),
  cierres: (idSucursal?: number, cajero?: string) =>
    unwrap<CierreListItem[]>(api.get(`/tesoreria/cierres`, { params: { idSucursal, cajero } })),
  validar: (idSucursal: number, idLote: number, idMotivoCierre: number | null, observacionTesoreria: string | null) =>
    unwrap<boolean>(api.post(`/tesoreria/cierres/${idLote}/validar`, { idMotivoCierre, observacionTesoreria }, { params: { idSucursal } })),

  motivosDiferencia: () => unwrap<MotivoCierre[]>(api.get(`/tesoreria/motivos-diferencia`)),
  lotesPendientes: (idSucursal?: number) =>
    unwrap<LotePendiente[]>(api.get(`/tesoreria/lotes-pendientes`, { params: { idSucursal } })),
  cerrarLotePendiente: (idSucursal: number, idLote: number, declaraciones: DeclaracionPago[],
    idMotivoDiferencia: number | null, idMotivoCierre: number, observacionTesoreria: string | null) =>
    unwrap<CierreZResultado>(api.post(`/tesoreria/lotes-pendientes/${idLote}/cerrar`,
      { declaraciones, idMotivoDiferencia, idMotivoCierre, observacionTesoreria }, { params: { idSucursal } })),
};
