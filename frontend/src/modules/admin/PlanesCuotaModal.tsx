import { useEffect, useState } from "react";
import { pagos, type MedioPago, type PlanCuota } from "../../shared/api/admin";

interface Props {
  medio: MedioPago;
  onCerrar: () => void;
}

/**
 * CRUD de planes de cuotas de un medio de pago Tarjeta (ej. "3 cuotas sin interés"). El cajero
 * elige uno de estos planes junto con el medio al cobrar — ver PagoForm en CajaPage.tsx.
 */
export function PlanesCuotaModal({ medio, onCerrar }: Props) {
  const [planes, setPlanes] = useState<PlanCuota[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [denominacion, setDenominacion] = useState("");
  const [cuotas, setCuotas] = useState<number | "">("");
  const [editId, setEditId] = useState<number | null>(null);

  const cargar = async () => {
    setError(null);
    try { setPlanes(await pagos.planes(medio.idMedioPago)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const limpiar = () => { setDenominacion(""); setCuotas(""); setEditId(null); };

  const guardar = () => run(async () => {
    if (!denominacion.trim() || !cuotas) return;
    const input = { denominacion: denominacion.trim(), cantidadCuotas: Number(cuotas) };
    if (editId) await pagos.updatePlan(editId, input);
    else await pagos.createPlan(medio.idMedioPago, input);
    limpiar();
  });

  const editar = (p: PlanCuota) => {
    setEditId(p.idPlan);
    setDenominacion(p.denominacion);
    setCuotas(p.cantidadCuotas);
  };

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(480px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        <h2>Planes de {medio.descripcion}</h2>
        <p className="muted">El cajero elige uno de estos planes junto con el medio al cobrar (es opcional).</p>
        {error && <p className="error">{error}</p>}

        <div className="field-row">
          <label>Denominación
            <input placeholder="Ej. 3 cuotas sin interés" value={denominacion}
              onChange={(e) => setDenominacion(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && denominacion.trim() && cuotas && guardar()}
              maxLength={60} />
          </label>
          <label>Cuotas
            <input type="number" min={1} value={cuotas} style={{ width: 80 }}
              onChange={(e) => setCuotas(e.target.value ? Number(e.target.value) : "")} />
          </label>
          <button className="primary" disabled={!denominacion.trim() || !cuotas} onClick={guardar}>
            {editId ? "Guardar" : "Agregar"}
          </button>
          {editId && <button onClick={limpiar}>Cancelar</button>}
        </div>

        <table className="grid">
          <thead><tr><th>Denominación</th><th>Cuotas</th><th></th></tr></thead>
          <tbody>
            {planes.map((p) => (
              <tr key={p.idPlan} className={editId === p.idPlan ? "sel" : ""}>
                <td>{p.denominacion}</td>
                <td className="mono">{p.cantidadCuotas}</td>
                <td className="row-actions">
                  <button onClick={() => editar(p)}>Editar</button>
                  <button className="danger" onClick={() => run(() => pagos.removePlan(p.idPlan))}>Eliminar</button>
                </td>
              </tr>
            ))}
            {planes.length === 0 && <tr><td colSpan={3} className="muted">Sin planes cargados.</td></tr>}
          </tbody>
        </table>

        <div className="row-actions" style={{ marginTop: 16 }}>
          <button className="primary" onClick={onCerrar}>Cerrar</button>
        </div>
      </div>
    </div>
  );
}
