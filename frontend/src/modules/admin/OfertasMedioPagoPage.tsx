import { useEffect, useState } from "react";
import {
  ofertasMedioPago, referencias, pagos as pagosApi,
  type Lookup, type OfertaMedioPago, type OfertaMedioPagoInput, type MedioPago, type PlanCuota,
} from "../../shared/api/admin";

// Fecha en hora LOCAL: con toISOString() (UTC) después de las 21 hs la oferta nueva arrancaba
// mañana y no aplicaba en el día (mismo criterio que OfertasPage).
const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const hoy = () => iso(new Date());
const enUnMes = () => { const d = new Date(); d.setMonth(d.getMonth() + 1); return iso(d); };

const vacio = (): OfertaMedioPagoInput => ({
  descripcion: "", idMedioPago: 0, idPlanCuota: null, porcentaje: 0, topeMaximo: 0, activo: true,
  fechaInicio: hoy(), fechaFin: enUnMes(),
});

/**
 * Descuento por medio de pago (y, si es tarjeta, por una cantidad de cuotas puntual): se aplica en
 * la pantalla de cobro sobre el medio elegido, no en el carrito — ver el motor de ofertas por
 * artículo en "Ofertas". El resultado sale como una línea "Descuento x MP" en el comprobante.
 */
export function OfertasMedioPagoPage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [suc, setSuc] = useState(0);
  const [items, setItems] = useState<OfertaMedioPago[]>([]);
  const [medios, setMedios] = useState<MedioPago[]>([]);
  const [planesPorMedio, setPlanesPorMedio] = useState<Record<number, PlanCuota[]>>({});
  const [error, setError] = useState<string | null>(null);

  const [form, setForm] = useState<OfertaMedioPagoInput>(vacio);
  const [editId, setEditId] = useState<number | null>(null);

  useEffect(() => {
    referencias.sucursales().then((s) => { setSucursales(s); if (s.length) setSuc(s[0].id); }).catch(() => {});
    pagosApi.medios().then(setMedios).catch(() => {});
  }, []);

  const cargar = async (s: number) => {
    if (!s) return;
    setError(null);
    try { setItems(await ofertasMedioPago.list(s)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(suc); /* eslint-disable-next-line */ }, [suc]);

  const asegurarPlanesDe = (idMedioPago: number) => {
    if (!idMedioPago || idMedioPago in planesPorMedio) return;
    pagosApi.planes(idMedioPago).then((ps) => setPlanesPorMedio((prev) => ({ ...prev, [idMedioPago]: ps }))).catch(() => {});
  };
  useEffect(() => { if (form.idMedioPago) asegurarPlanesDe(form.idMedioPago); /* eslint-disable-next-line */ }, [form.idMedioPago]);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(suc); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const cancelar = () => { setForm(vacio); setEditId(null); };

  const editar = (o: OfertaMedioPago) => {
    setEditId(o.idOfertaMedioPago);
    setForm({
      descripcion: o.descripcion, idMedioPago: o.idMedioPago, idPlanCuota: o.idPlanCuota ?? null,
      porcentaje: o.porcentaje, topeMaximo: o.topeMaximo, activo: o.activo,
      // El backend devuelve fecha completa; los <input type="date"> quieren yyyy-MM-dd.
      fechaInicio: o.fechaInicio.slice(0, 10), fechaFin: o.fechaFin.slice(0, 10),
    });
    asegurarPlanesDe(o.idMedioPago);
  };

  const guardar = () => run(async () => {
    if (editId != null) await ofertasMedioPago.update(suc, editId, form);
    else await ofertasMedioPago.create(suc, form);
    cancelar();
  });

  const planesDelMedio = planesPorMedio[form.idMedioPago] ?? [];

  return (
    <div>
      <div className="page-head">
        <h1>Ofertas por medio de pago</h1>
        <label className="inline-label">Sucursal
          <select value={suc} onChange={(e) => setSuc(Number(e.target.value))}>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
      </div>
      <p className="muted" style={{ marginTop: -8 }}>
        Descuento % (con tope máximo en $) que se aplica al elegir el medio de pago en el cobro —
        no al armar el carrito. Sale impreso como una línea "Descuento x MP" en el comprobante.
      </p>
      {error && <p className="error">{error}</p>}

      <div className="card form">
        <h3>{editId != null ? `Editar oferta #${editId}` : "Nueva oferta"}</h3>
        <div className="form-grid">
          <label>Descripción
            <input value={form.descripcion} onChange={(e) => setForm((f) => ({ ...f, descripcion: e.target.value }))} />
          </label>
          <label>Medio de pago
            <select value={form.idMedioPago}
              onChange={(e) => setForm((f) => ({ ...f, idMedioPago: Number(e.target.value), idPlanCuota: null }))}>
              <option value={0}>(elegir)</option>
              {medios.map((m) => <option key={m.idMedioPago} value={m.idMedioPago}>{m.descripcion}</option>)}
            </select>
          </label>
          <label>Cuotas
            <select value={form.idPlanCuota ?? 0} disabled={!planesDelMedio.length}
              onChange={(e) => setForm((f) => ({ ...f, idPlanCuota: Number(e.target.value) || null }))}>
              <option value={0}>(cualquier cantidad de cuotas)</option>
              {planesDelMedio.map((p) => <option key={p.idPlan} value={p.idPlan}>{p.denominacion}</option>)}
            </select>
          </label>
          <label>Porcentaje %
            <input type="number" step="0.01" min={0} max={100} value={form.porcentaje}
              onChange={(e) => setForm((f) => ({ ...f, porcentaje: Number(e.target.value) }))} />
          </label>
          <label>Tope máximo $
            <input type="number" step="0.01" min={0} value={form.topeMaximo}
              onChange={(e) => setForm((f) => ({ ...f, topeMaximo: Number(e.target.value) }))} />
          </label>
          <label>Vigencia desde
            <input type="date" value={form.fechaInicio} onChange={(e) => setForm((f) => ({ ...f, fechaInicio: e.target.value }))} />
          </label>
          <label>Vigencia hasta
            <input type="date" value={form.fechaFin} onChange={(e) => setForm((f) => ({ ...f, fechaFin: e.target.value }))} />
          </label>
          <label className="check-box">
            <input type="checkbox" checked={form.activo} onChange={(e) => setForm((f) => ({ ...f, activo: e.target.checked }))} />
            Activa
          </label>
        </div>
        <div className="row-actions">
          <button className="primary"
            disabled={!form.descripcion.trim() || !form.idMedioPago || form.porcentaje <= 0 || form.topeMaximo <= 0
              || !form.fechaInicio || !form.fechaFin || form.fechaFin < form.fechaInicio}
            onClick={guardar}>
            {editId != null ? "Guardar" : "Agregar"}
          </button>
          {editId != null && <button onClick={cancelar}>Cancelar</button>}
        </div>
      </div>

      <table className="grid">
        <thead><tr><th>ID</th><th>Descripción</th><th>Medio</th><th>Cuotas</th><th>%</th><th>Tope $</th><th>Vigencia</th><th>Activa</th><th></th></tr></thead>
        <tbody>
          {items.map((o) => (
            <tr key={o.idOfertaMedioPago}>
              <td className="mono">{o.idOfertaMedioPago}</td>
              <td>{o.descripcion}</td>
              <td>{o.medioPagoDescripcion ?? o.idMedioPago}</td>
              <td className="muted">{o.planCuotaDescripcion ?? "cualquiera"}</td>
              <td className="mono">{o.porcentaje}%</td>
              <td className="mono">${o.topeMaximo.toFixed(2)}</td>
              <td className="mono">{o.fechaInicio.slice(0, 10)} → {o.fechaFin.slice(0, 10)}</td>
              <td>{o.activo ? <span className="badge on">Sí</span> : <span className="badge off">No</span>}</td>
              <td className="row-actions">
                <button onClick={() => editar(o)}>✎</button>
                <button className="danger" onClick={() => run(() => ofertasMedioPago.remove(suc, o.idOfertaMedioPago))}>×</button>
              </td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan={9} className="muted">Sin ofertas por medio de pago.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
