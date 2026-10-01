import { api, unwrap } from "./client";

export interface ArticuloParaEtiqueta {
  idArticulo: number; idPresentacion: number; codigoInterno: string;
  descripcion: string; descripcionTicket?: string | null;
}

/** Artículo con cambio de precio programado para mañana (lista 2068). esPrecioUnico=false: viene de
 *  LISTAS_PROG.DBF y precioNuevo es el Azul nuevo (PFINAL; el Rojo se simula). esPrecioUnico=true: viene de
 *  PREC_PROG.DBF y precioNuevo es un único precio para Azul y Rojo. */
export interface ArticuloCambioPrecio extends ArticuloParaEtiqueta {
  precioNuevo: number; esPrecioUnico: boolean; impuestoInterno: number;
}
export interface PrecioUnicoNuevo { precio: number; impuestoInterno: number; }
/** Solo para "Cambio de Precios" (idPresentacion → valor); las etiquetas comunes no los usan. */
export interface PreciosSimulados {
  preciosAzulNuevos?: Record<number, number>;
  preciosUnicosNuevos?: Record<number, PrecioUnicoNuevo>;
}
export interface CambioPrecios { items: ArticuloCambioPrecio[]; detectados: number; sinMatch: string[]; }

export interface LookupSimple { id: number; descripcion: string; }
/** La familia trae su sector para poder filtrar el combo por el sector elegido. */
export interface FamiliaLookup extends LookupSimple { idSector?: number | null; }
export interface Clasificaciones { sectores: LookupSimple[]; lineas: LookupSimple[]; familias: FamiliaLookup[]; }

export interface TipoTarjetaPrecio {
  nombreTarjeta: string; precio: number; precioPorUnidadMedida?: number | null; precioSinImpuestos: number;
}

export interface Etiqueta {
  idPresentacion: number; codigoInterno: string; descripcion: string; descripcionTicket?: string | null;
  codigoBarra?: string | null; precioBase: number; precioBasePorUnidadMedida?: number | null;
  precioBaseSinImpuestos: number; preciosTarjeta: TipoTarjetaPrecio[]; compraMinima: number; unidadMedidaTexto: string;
  /** "Precio Único" cuando colapsó por folder vigente o porque Rojo/Azul coincidieron; si no, null. */
  aclaracionPrecio?: string | null;
}

export const etiquetas = {
  clasificaciones: () => unwrap<Clasificaciones>(api.get(`/etiquetas/clasificaciones`)),
  sucursales: () => unwrap<LookupSimple[]>(api.get(`/etiquetas/sucursales`)),
  buscar: (q: string) => unwrap<ArticuloParaEtiqueta[]>(api.get(`/etiquetas/buscar`, { params: { q } })),
  /** Coincidencia EXACTA por código de barras o código interno (sin texto parcial sobre
   *  descripción) — usado en la pantalla angosta de celular, ver EtiquetasPage.tsx. */
  buscarExacto: (codigo: string) =>
    unwrap<ArticuloParaEtiqueta | null>(api.get(`/etiquetas/buscar-exacto`, { params: { codigo } })),
  porClasificacion: (idSector?: number, idLinea?: number, idFamilia?: number) =>
    unwrap<ArticuloParaEtiqueta[]>(api.get(`/etiquetas/por-clasificacion`, { params: { idSector, idLinea, idFamilia } })),
  /** Lee LISTAS_PROG.DBF (archivo grande: tarda varios segundos) → sin timeout. */
  cambioDePrecios: () => unwrap<CambioPrecios>(api.get(`/etiquetas/cambio-de-precios`, { timeout: 0 })),
  /** `simulados` solo para "Cambio de Precios": esas etiquetas salen con el Azul nuevo y el Rojo
   *  simulado, o con el precio único; las demás no se tocan. */
  generar: (idSucursal: number, idsPresentacion: number[], simulados?: PreciosSimulados) =>
    unwrap<Etiqueta[]>(api.post(`/etiquetas/generar`, { idSucursal, idsPresentacion, ...simulados })),
};
