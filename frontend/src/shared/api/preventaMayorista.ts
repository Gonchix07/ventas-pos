import { api, unwrap } from "./client";

export interface PreventaLinea {
  reparto: string;
  pVentista: string;
  codigoArticulo: string;
  descripcionArticulo: string | null;
  unidadXBulto: number;
  cantidad: number;
  cantidadOriginal: number;
  importe: number;
  descuento: number;
  precarga: string;
  fechaPedido: string | null;
  estado: string;
  autorizado: boolean | null;
  logis: string;
}

export interface PreventaCliente {
  codigoCliente: string;
  nombreCliente: string | null;
  condicionIva: string | null;
  admitePresupuesto: boolean | null;
  cantidadReserva: number;
  cantidadOriginal: number;
  importeFinal: number;
  recargoLogistica: number;
  cantidadPrecargas: number;
  lineas: PreventaLinea[];
}

export interface Vendedor {
  codigo: string;
  nombre: string;
}

export const preventaMayorista = {
  pedidos: (forzarRefresco?: boolean) =>
    unwrap<PreventaCliente[]>(api.get("/preventa-mayorista/pedidos", { params: { forzarRefresco } })),
  vendedores: () => unwrap<Vendedor[]>(api.get("/preventa-mayorista/vendedores")),
};
