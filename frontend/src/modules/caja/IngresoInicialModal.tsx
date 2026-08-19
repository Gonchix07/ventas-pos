import { useState } from "react";
import { caja, type Lote } from "../../shared/api/caja";
import { MonedaInput } from "../../shared/ui/moneda";

interface Props {
  idSucursal: number;
  idCaja: number;
  /** Ya resuelto de antes (ver SupervisorGate en CajaPage) si se está abriendo la caja de otro puesto. */
  codigoSupervisor: string | null;
  /** Se llama apenas la caja queda abierta, con el monto confirmado — CajaPage decide ahí si
   *  corresponde imprimir el ticket (ver TicketIngresoInicial), este popup ya no lo hace: si lo
   *  hiciera acá, el cambio de pantalla (apertura → caja abierta) lo desmontaría a mitad de camino,
   *  antes de que el print pudiera disparar. */
  onAbierta: (lote: Lote, montoInicial: number) => void;
  onCerrar: () => void;
}

/**
 * Fondo inicial al abrir el turno: mismo mecanismo que un retiro de efectivo pero al revés (suma en
 * vez de restar) y sin concepto — es solo el monto con el que arranca la caja, se usa como saldo
 * inicial del lote en la rendición de Tesorería. La caja no se abre sin pasar por este popup, ni
 * siquiera con $0: "confirmar apertura" es el único botón que llama a caja.abrir.
 */
export function IngresoInicialModal({ idSucursal, idCaja, codigoSupervisor, onAbierta, onCerrar }: Props) {
  const [monto, setMonto] = useState<number | null>(0);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  const confirmar = async () => {
    setError(null);
    setCargando(true);
    try {
      const montoFinal = monto ?? 0;
      const lote = await caja.abrir(idSucursal, idCaja, codigoSupervisor, montoFinal);
      onAbierta(lote, montoFinal);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo abrir la caja.");
    } finally {
      setCargando(false);
    }
  };

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(460px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        <h2>Fondo inicial de caja</h2>
        <p className="muted">
          Monto en efectivo con el que arranca el turno. Dejalo en $0 si no corresponde ningún fondo.
        </p>
        {error && <p className="error">{error}</p>}
        <div className="form-grid" style={{ marginTop: 10 }}>
          <label>Monto inicial
            <MonedaInput value={monto} onChange={setMonto} autoFocus />
          </label>
        </div>
        <div className="row-actions" style={{ marginTop: 16 }}>
          <button onClick={onCerrar} disabled={cargando}>Cancelar</button>
          <button className="primary" onClick={confirmar} disabled={cargando}>
            {cargando ? "Abriendo…" : "Confirmar apertura"}
          </button>
        </div>
      </div>
    </div>
  );
}
