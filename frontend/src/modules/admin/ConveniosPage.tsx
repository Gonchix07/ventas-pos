import { useEffect, useState } from "react";
import {
  convenios, referencias, clientes,
  type Convenio, type Lookup, type Cliente,
} from "../../shared/api/admin";

export function ConveniosPage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [listas, setListas] = useState<Lookup[]>([]);
  const [suc, setSuc] = useState(0);
  const [items, setItems] = useState<Convenio[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [q, setQ] = useState(""); const [cli, setCli] = useState<Cliente[]>([]);
  const [idCliente, setIdCliente] = useState(0);
  const [descuento, setDescuento] = useState(0);
  const [idLista, setIdLista] = useState<number | 0>(0);

  useEffect(() => {
    referencias.sucursales().then((s) => { setSucursales(s); if (s.length) setSuc(s[0].id); }).catch(() => {});
    referencias.listasPrecios().then(setListas).catch(() => {});
  }, []);

  const cargar = async (s: number) => {
    if (!s) return;
    setError(null);
    try { setItems(await convenios.list(s)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(suc); /* eslint-disable-next-line */ }, [suc]);

  const buscarCli = async () => {
    try { setCli(await clientes.list(q)); } catch { /* noop */ }
  };

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(suc); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      <div className="page-head">
        <h1>Convenios</h1>
        <label className="inline-label">Sucursal
          <select value={suc} onChange={(e) => setSuc(Number(e.target.value))}>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}

      <div className="card form">
        <h3>Nuevo convenio</h3>
        <div className="toolbar">
          <input placeholder="Buscar cliente" value={q} onChange={(e) => setQ(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && buscarCli()} />
          <button onClick={buscarCli}>Buscar</button>
        </div>
        {cli.length > 0 && (
          <select value={idCliente} onChange={(e) => setIdCliente(Number(e.target.value))} style={{ maxWidth: 420 }}>
            <option value={0}>— elegir cliente —</option>
            {cli.map((c) => <option key={c.idCliente} value={c.idCliente}>{c.codigoInt} · {c.descripcion}</option>)}
          </select>
        )}
        <div className="form-grid">
          <label>Descuento (%)<input type="number" step="0.01" value={descuento} onChange={(e) => setDescuento(Number(e.target.value))} /></label>
          <label>Lista de precios (opcional)
            <select value={idLista} onChange={(e) => setIdLista(Number(e.target.value))}>
              <option value={0}>(ninguna)</option>
              {listas.map((l) => <option key={l.id} value={l.id}>{l.descripcion}</option>)}
            </select>
          </label>
        </div>
        <div className="row-actions">
          <button className="primary" disabled={!idCliente}
            onClick={() => run(async () => { await convenios.create(suc, idCliente, descuento, idLista || null); setIdCliente(0); setDescuento(0); setIdLista(0); setCli([]); setQ(""); })}>
            Agregar convenio
          </button>
        </div>
      </div>

      <table className="grid">
        <thead><tr><th>ID</th><th>Cliente</th><th>Descuento</th><th>Lista</th><th></th></tr></thead>
        <tbody>
          {items.map((c) => (
            <tr key={c.idConvenio}>
              <td className="mono">{c.idConvenio}</td>
              <td>{c.clienteDescripcion}</td>
              <td className="mono">{c.descuento}%</td>
              <td>{c.listaCodigo ?? "—"}</td>
              <td><button className="danger" onClick={() => run(() => convenios.remove(suc, c.idConvenio))}>Eliminar</button></td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan={5} className="muted">Sin convenios.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
