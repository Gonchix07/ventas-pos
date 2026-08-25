import { api, unwrap } from "./client";

/** Resultado de búsqueda del módulo "Clientes" (ficha + ticket) — no depende de sucursal, ver
 * ClienteTicketDto en el backend. Solo trae la tarjeta VIGENTE del cliente, si tiene.
 * `origen`: "Titular" (el DNI escaneado es el documento propio de esa cuenta) o "Autorizado" (el
 * DNI figura como autorizado a comprar en esa cuenta, que es de otra persona). */
export interface ClienteTicket {
  idCliente: number;
  codigoInt: string;
  descripcion: string;
  documento?: string | null;
  nroTarjeta?: string | null;
  tipoTarjeta?: string | null;
  origen: "Titular" | "Autorizado";
}

export const clientesModulo = {
  buscarPorDni: (dni: string) => unwrap<ClienteTicket[]>(api.get("/clientes/buscar-por-dni", { params: { dni } })),
};
