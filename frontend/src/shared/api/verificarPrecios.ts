import { api, unwrap } from "./client";

/** Precio del artículo en una lista puntual (AZUL/ROJA) — precio null = esa lista no tiene precio
 * cargado para este artículo. */
export interface PrecioLista {
  codigoLista: string;
  precio: number | null;
}

export interface OfertaResumen {
  idOferta: number;
  descripcion: string;
}

/** Resultado del kiosco de autoconsulta (módulo "VerificarPrecios") — ver ConsultaPrecioResult en
 * el backend. A diferencia de Caja, no resuelve UN precio ganador: muestra los precios de lista en
 * paralelo, más las señales del sticker (esListaFolder / ofertas). */
export interface ConsultaPrecio {
  codigoInterno: string;
  descripcion: string;
  imagenUrl: string;
  precios: PrecioLista[];
  esListaFolder: boolean;
  ofertas: OfertaResumen[];
}

export const verificarPrecios = {
  consultar: (idSucursal: number, codigo: string) =>
    unwrap<ConsultaPrecio>(api.get("/verificar-precios", { params: { idSucursal, codigo } })),
};
