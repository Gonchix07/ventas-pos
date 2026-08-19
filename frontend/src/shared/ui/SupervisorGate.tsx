import { useState } from "react";
import { useAuth } from "../auth/auth";

const ROLES_SIN_CONTROL = new Set(["Supervisor", "Administrador"]);

interface Pendiente {
  accion: (codigo: string | null) => Promise<void>;
}

/**
 * Control de supervisor: nota de crédito, anular un artículo del carrito, y abrir una caja en un
 * puesto distinto al propio piden autorización de un supervisor. Si quien está logueado YA es
 * Supervisor/Administrador, `ejecutarConSupervisor` llama la acción directo (codigo=null); si no,
 * abre un popup pidiendo el código de 8 dígitos y reintenta la acción con ese código.
 *
 * El código se valida en el backend en cada llamada (ver ISupervisorAuthService) — no queda
 * "recordado" para la próxima acción, y el chequeo de rol acá es solo para no hacerle mostrar el
 * popup a quien no lo necesita; la autoridad real es siempre del servidor.
 */
export function useSupervisorGate() {
  const { rol } = useAuth();
  const [pendiente, setPendiente] = useState<Pendiente | null>(null);
  const [codigo, setCodigo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  const yaAutorizado = !!rol && ROLES_SIN_CONTROL.has(rol);

  /**
   * Envolvé la acción gateada con esto en vez de llamarla directo. La acción es responsable de
   * mostrar su propio error (ej. `setError` + `throw`) — acá solo se evita que ese throw quede
   * como una promesa rechazada sin manejar cuando no hay popup de por medio (bypass).
   */
  const ejecutarConSupervisor = (accion: (codigo: string | null) => Promise<void>) => {
    if (yaAutorizado) { void accion(null).catch(() => {}); return; }
    setCodigo("");
    setError(null);
    setPendiente({ accion });
  };

  const confirmar = async () => {
    if (!pendiente) return;
    setError(null);
    setCargando(true);
    try {
      await pendiente.accion(codigo.trim());
      setPendiente(null);
    } catch (e) {
      // El código incorrecto/faltante no cierra el popup: se reintenta ahí mismo.
      setError(e instanceof Error ? e.message : "No se pudo validar el código.");
    } finally {
      setCargando(false);
    }
  };

  const cancelar = () => {
    if (cargando) return;
    setPendiente(null);
  };

  const modal = pendiente && (
    <div className="modal-fondo" onClick={cancelar}>
      <div className="modal-caja modal-pin" onClick={(e) => e.stopPropagation()}>
        <h2>Autorización de supervisor</h2>
        <p className="muted">Pedile a un supervisor que ingrese su código de 8 dígitos.</p>
        <input
          autoFocus
          type="password"
          inputMode="numeric"
          maxLength={8}
          className="pin-input"
          placeholder="········"
          value={codigo}
          onChange={(e) => setCodigo(e.target.value.replace(/\D/g, ""))}
          onKeyDown={(e) => e.key === "Enter" && codigo.length === 8 && !cargando && confirmar()}
        />
        {error && <p className="error">{error}</p>}
        <div className="row-actions" style={{ marginTop: 16 }}>
          <button onClick={cancelar} disabled={cargando}>Cancelar</button>
          <button className="primary" onClick={confirmar} disabled={cargando || codigo.length !== 8}>
            {cargando ? "Verificando…" : "Autorizar"}
          </button>
        </div>
      </div>
    </div>
  );

  return { ejecutarConSupervisor, modal };
}
