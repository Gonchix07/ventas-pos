import { api, unwrap } from "./client";

// Mismos valores que PeriodoEstadisticas en el backend (Pos.Application.Estadisticas).
export const PERIODOS = [
  { valor: 0, label: "Hoy" },
  { valor: 1, label: "Últimos 7 días" },
  { valor: 2, label: "Últimos 30 días" },
  { valor: 3, label: "Último año" },
] as const;
export type Periodo = (typeof PERIODOS)[number]["valor"];

export interface ResumenVentas {
  totalVentas: number; cantidadTickets: number; cantidadClientes: number;
  ticketPromedio: number; totalDescuentos: number; cantidadNotasCredito: number; totalNotasCredito: number;
}
export interface FamiliaVendida { idFamilia: number; descripcion: string; total: number; cantidad: number; participacion: number; }
export interface VentaPorPeriodo { etiqueta: string; total: number; }
export interface SectorConsumido { idSector: number; descripcion: string; total: number; cantidad: number; participacion: number; }
export interface ProductoVendido { idArticulo: number; codigoInterno: string; descripcion: string; cantidad: number; total: number; }
export interface TopCliente { idCliente: number | null; descripcion: string; total: number; cantidadTickets: number; }
export interface OfertaEfectividad { descripcion: string; vecesAplicada: number; descuentoOtorgado: number; importeAfectado: number; }

export interface EstadisticasVentas {
  periodo: Periodo; desde: string; hasta: string;
  resumen: ResumenVentas;
  familiasMasVendidas: FamiliaVendida[];
  evolucion: VentaPorPeriodo[];
  sectoresMasConsumidos: SectorConsumido[];
  productosMasVendidos: ProductoVendido[];
  topClientes: TopCliente[];
  ofertas: OfertaEfectividad[];
}

export const estadisticas = {
  ventas: (periodo: Periodo, idSucursal?: number) =>
    unwrap<EstadisticasVentas>(api.get(`/admin/estadisticas/ventas`, { params: { periodo, idSucursal } })),
};
