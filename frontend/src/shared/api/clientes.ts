import { api, unwrap } from "./client";
import type { Cliente } from "./admin";

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

/**
 * Módulo "Clientes" del menú principal (búsqueda manual): mismo buscador/columnas que el ABM de
 * Administración, pero de solo lectura — ver ClientesFichaController en el backend. `get` trae el
 * detalle completo (para el panel de solo lectura) y `ticket` arma el mismo `ClienteTicket` que
 * imprime el módulo de autoservicio, reusando `TicketCliente`.
 */
export const clientesFicha = {
  buscar: (q: string) => unwrap<Cliente[]>(api.get("/clientes-ficha/buscar", { params: { q } })),
  get: (id: number) => unwrap<Cliente>(api.get(`/clientes-ficha/${id}`)),
  ticket: (id: number) => unwrap<ClienteTicket>(api.get(`/clientes-ficha/${id}/ticket`)),
};
