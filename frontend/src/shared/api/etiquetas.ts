import { api, unwrap } from "./client";

export interface ArticuloParaEtiqueta {
  idArticulo: number; idPresentacion: number; codigoInterno: string;
  descripcion: string; descripcionTicket?: string | null;
}

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
  porClasificacion: (idSector?: number, idLinea?: number, idFamilia?: number) =>
    unwrap<ArticuloParaEtiqueta[]>(api.get(`/etiquetas/por-clasificacion`, { params: { idSector, idLinea, idFamilia } })),
  generar: (idSucursal: number, idsPresentacion: number[]) =>
    unwrap<Etiqueta[]>(api.post(`/etiquetas/generar`, { idSucursal, idsPresentacion })),
};
