import { useEffect, useState } from "react";
import {
  clientes, referencias,
  type Cliente, type ClienteInput, type AutorizadoInput, type Lookup,
} from "../../shared/api/admin";

// Debe coincidir con ClienteService.MaxResultados (backend).
const MAX_RESULTADOS = 50;

const VACIO: ClienteInput = {
  codigoInt: "", cuit: "", documento: "", descripcion: "", nombreFantasia: "",
  idCondIva: 0, permitePresupuesto: false, activo: true,
  domicilio: "", codigoPostal: "", localidad: "", provincia: "", email: "", admiteCuentaCorriente: false,
  autorizados: [],
};

// Fecha local (no UTC): un autorizado dado de alta a la noche no tiene que quedar con la de mañana.
const hoyLocal = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
};
const soloFecha = (v?: string | null) => (v ? v.slice(0, 10) : hoyLocal());

// La condición de IVA decide la letra del comprobante. El lookup de condiciones solo devuelve
// {id, descripcion}, así que acá se reconoce por nombre (el backend usa CondicionIva.Letra, que es
// la fuente de verdad — esto es solo el aviso en pantalla).
function esCondicionA(descripcion: string): boolean {
  const d = descripcion.toUpperCase();
  return d.includes("INSCRIPTO") || d.includes("MONOTRIBUT");
}

export function ClientesPage() {
  const [items, setItems] = useState<Cliente[]>([]);
  const [condiciones, setCondiciones] = useState<Lookup[]>([]);
  const [q, setQ] = useState("");
  const [form, setForm] = useState<ClienteInput | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = async () => {
    setError(null);
    try {
      setItems(await clientes.list(q));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  useEffect(() => {
    void cargar();
    referencias.condicionesIva().then(setCondiciones).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const nuevo = () => {
    setEditId(null);
    setForm({ ...VACIO, idCondIva: condiciones[0]?.id ?? 0 });
  };

  const editar = async (c: Cliente) => {
    setError(null);
    setEditId(c.idCliente);
    const armar = (d: Cliente): ClienteInput => ({
      codigoInt: d.codigoInt, cuit: d.cuit ?? "", documento: d.documento ?? "",
      descripcion: d.descripcion, nombreFantasia: d.nombreFantasia ?? "", idCondIva: d.idCondIva,
      permitePresupuesto: d.permitePresupuesto, activo: d.activo,
      domicilio: d.domicilio ?? "", codigoPostal: d.codigoPostal ?? "",
      localidad: d.localidad ?? "", provincia: d.provincia ?? "", email: d.email ?? "",
      admiteCuentaCorriente: d.admiteCuentaCorriente,
      autorizados: (d.autorizados ?? []).map((a) => ({
        idAutorizado: a.idAutorizado, dni: a.dni, descripcion: a.descripcion,
        fechaAlta: soloFecha(a.fechaAlta), activo: a.activo,
      })),
    });
    setForm(armar(c));
    // El listado no trae los autorizados (por peso): el detalle sí.
    try { setForm(armar(await clientes.get(c.idCliente))); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const guardar = async () => {
    if (!form) return;
    setError(null);
    try {
      if (editId) await clientes.update(editId, form);
      else await clientes.create(form);
      setForm(null); setEditId(null);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const eliminar = async (id: number) => {
    if (!confirm("¿Dar de baja al cliente?")) return;
    try { await clientes.remove(id); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const set = (patch: Partial<ClienteInput>) => setForm((f) => (f ? { ...f, ...patch } : f));

  const autorizados = form?.autorizados ?? [];
  const setAutorizados = (lista: AutorizadoInput[]) => set({ autorizados: lista });
  const setAutorizado = (i: number, patch: Partial<AutorizadoInput>) =>
    setForm((f) => f ? {
      ...f, autorizados: (f.autorizados ?? []).map((a, idx) => idx === i ? { ...a, ...patch } : a),
    } : f);

  return (
    <div>
      <div className="page-head">
        <h1>Clientes</h1>
        <button className="primary" onClick={nuevo}>Nuevo cliente</button>
      </div>

      <div className="toolbar">
        <input placeholder="Buscar por nombre, fantasía, código, CUIT o documento"
          value={q} onChange={(e) => setQ(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && cargar()} style={{ flex: "1 1 320px", minWidth: 200 }} />
        <button onClick={cargar}>Buscar</button>
        <span className="filter-count">
          {`${items.length} cliente${items.length === 1 ? "" : "s"}${items.length === MAX_RESULTADOS ? " (máx.) — refiná la búsqueda" : ""}`}
        </span>
      </div>

      {error && <p className="error">{error}</p>}

      {form && (
        <div className="card form">
          <h3>{editId ? "Editar cliente" : "Nuevo cliente"}</h3>
          <div className="form-grid">
            <label>Código<input value={form.codigoInt} onChange={(e) => set({ codigoInt: e.target.value })} /></label>
            <label>Razón social / Nombre<input value={form.descripcion} onChange={(e) => set({ descripcion: e.target.value })} /></label>
            <label>Nombre de fantasía
              <input value={form.nombreFantasia ?? ""} onChange={(e) => set({ nombreFantasia: e.target.value })}
                maxLength={60} placeholder="Con qué se lo conoce en el mostrador" />
            </label>
            <label>CUIT<input value={form.cuit ?? ""} onChange={(e) => set({ cuit: e.target.value })} /></label>
            <label>Documento<input value={form.documento ?? ""} onChange={(e) => set({ documento: e.target.value })} /></label>
            <label>Condición IVA
              <select value={form.idCondIva} onChange={(e) => set({ idCondIva: Number(e.target.value) })}>
                {condiciones.map((c) => <option key={c.id} value={c.id}>{c.descripcion}</option>)}
              </select>
            </label>
            <label>Domicilio<input value={form.domicilio ?? ""} onChange={(e) => set({ domicilio: e.target.value })} maxLength={120} /></label>
            <label>Localidad<input value={form.localidad ?? ""} onChange={(e) => set({ localidad: e.target.value })} maxLength={60} /></label>
            <label>Provincia<input value={form.provincia ?? ""} onChange={(e) => set({ provincia: e.target.value })} maxLength={60} /></label>
            <label>Código postal<input value={form.codigoPostal ?? ""} onChange={(e) => set({ codigoPostal: e.target.value })} maxLength={8} /></label>
            <label>Email<input type="email" value={form.email ?? ""} onChange={(e) => set({ email: e.target.value })} maxLength={120} /></label>
            <label className="check"><input type="checkbox" checked={form.permitePresupuesto} onChange={(e) => set({ permitePresupuesto: e.target.checked })} /> Permite presupuesto</label>
            <label className="check" title="Habilita cargarle un límite de crédito por sucursal en Cuenta corriente">
              <input type="checkbox" checked={form.admiteCuentaCorriente}
                onChange={(e) => set({ admiteCuentaCorriente: e.target.checked })} /> Admite cuenta corriente
            </label>
            <label className="check"><input type="checkbox" checked={form.activo} onChange={(e) => set({ activo: e.target.checked })} /> Activo</label>
          </div>
          {/* La condición de IVA define la letra del comprobante: Responsable Inscripto y
              Monotributista facturan A, y la A no se puede emitir sin CUIT ni domicilio. */}
          {esCondicionA(condiciones.find((c) => c.id === form.idCondIva)?.descripcion ?? "") && (
            <p className={form.cuit?.trim() && form.domicilio?.trim() ? "muted" : "error"}>
              Con esta condición de IVA se emite <b>FACTURA A</b>: el CUIT y el domicilio son obligatorios.
            </p>
          )}
          <div className="presentaciones">
            <div className="page-head"><h4>Autorizados</h4>
              <button onClick={() => setAutorizados([...autorizados,
                { idAutorizado: null, dni: "", descripcion: "", fechaAlta: hoyLocal(), activo: true }])}>
                + Autorizado
              </button>
            </div>
            <p className="muted">Personas habilitadas a comprar en nombre del cliente. Se pueden inactivar
              sin borrarlas, para que quede el registro de que estuvieron autorizadas.</p>
            {autorizados.length === 0 && <p className="muted">Sin autorizados.</p>}
            {autorizados.map((a, i) => (
              <div key={a.idAutorizado ?? `nuevo-${i}`} className="pres-card">
                <div className="form-grid">
                  <label>DNI
                    <input value={a.dni} onChange={(e) => setAutorizado(i, { dni: e.target.value })}
                      maxLength={15} inputMode="numeric" />
                  </label>
                  <label>Nombre completo
                    <input value={a.descripcion} onChange={(e) => setAutorizado(i, { descripcion: e.target.value })}
                      maxLength={80} />
                  </label>
                  <label>Fecha de alta
                    <input type="date" value={soloFecha(a.fechaAlta)}
                      onChange={(e) => setAutorizado(i, { fechaAlta: e.target.value })} />
                  </label>
                  <label className="check">
                    <input type="checkbox" checked={a.activo}
                      onChange={(e) => setAutorizado(i, { activo: e.target.checked })} /> Activo
                  </label>
                  <button className="danger"
                    onClick={() => setAutorizados(autorizados.filter((_, idx) => idx !== i))}>Quitar</button>
                </div>
              </div>
            ))}
          </div>

          <div className="row-actions">
            <button className="primary" onClick={guardar}>Guardar</button>
            <button onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </div>
      )}

      <div className="table-scroll">
        <table className="grid">
          <thead>
            <tr><th>Código</th><th>Descripción</th><th>Fantasía</th><th>CUIT</th><th>Localidad</th><th>Cond. IVA</th><th>Presup.</th><th>Cta. cte.</th><th>Estado</th><th></th></tr>
          </thead>
          <tbody>
            {items.map((c) => (
              <tr key={c.idCliente} className={c.activo ? "" : "inactive"}>
                <td className="mono">{c.codigoInt}</td>
                <td>{c.descripcion}</td>
                <td>{c.nombreFantasia ?? <span className="muted">—</span>}</td>
                <td className="mono">{c.cuit}</td>
                <td>{c.localidad}</td>
                <td>{c.condIvaDescripcion}</td>
                <td>{c.permitePresupuesto ? "Sí" : "No"}</td>
                <td>{c.admiteCuentaCorriente ? <span className="badge on">Sí</span> : <span className="muted">No</span>}</td>
                <td>{c.activo ? <span className="badge on">Activo</span> : <span className="badge off">Baja</span>}</td>
                <td className="row-actions">
                  <button className="primary" onClick={() => editar(c)}>Editar</button>
                  <button className="danger-solid" onClick={() => eliminar(c.idCliente)}>Baja</button>
                </td>
              </tr>
            ))}
            {items.length === 0 && <tr><td colSpan={10} className="muted">Sin clientes.</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  );
}
