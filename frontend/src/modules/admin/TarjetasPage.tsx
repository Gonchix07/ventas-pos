import { useEffect, useRef, useState } from "react";
import {
  tarjetas, referencias, clientes,
  type TipoTarjeta, type TarjetaCliente, type Lookup, type Cliente,
} from "../../shared/api/admin";

const formatearFecha = (iso?: string | null) => {
  if (!iso) return "";
  const d = new Date(iso);
  return `${d.toLocaleDateString("es-AR")} ${d.toLocaleTimeString("es-AR", { hour12: false, hour: "2-digit", minute: "2-digit" })}`;
};

export function TarjetasPage() {
  const [tipos, setTipos] = useState<TipoTarjeta[]>([]);
  const [listas, setListas] = useState<Lookup[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const [tDesc, setTDesc] = useState(""); const [tLista, setTLista] = useState<number | 0>(0);

  // tarjetas por cliente
  const [q, setQ] = useState(""); const [cli, setCli] = useState<Cliente[]>([]);
  const [buscando, setBuscando] = useState(false);
  const [cliSel, setCliSel] = useState<Cliente | null>(null);
  const [tjs, setTjs] = useState<TarjetaCliente[]>([]);
  const [nroNueva, setNroNueva] = useState(""); const [tipoNueva, setTipoNueva] = useState(0);
  const nroRef = useRef<HTMLInputElement>(null);

  const cargarTipos = async () => {
    setError(null);
    try { const t = await tarjetas.tipos(); setTipos(t); if (t.length) setTipoNueva((v) => v || t[0].idTipoTarjeta); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargarTipos(); referencias.listasPrecios().then(setListas).catch(() => {}); }, []);

  const run = async (fn: () => Promise<unknown>, refreshCli = false) => {
    setError(null);
    try { await fn(); await cargarTipos(); if (refreshCli && cliSel) setTjs(await tarjetas.deCliente(cliSel.idCliente)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // Búsqueda con debounce, igual que en Clientes/Clusters: el padrón es grande y el endpoint topea
  // en 50 resultados.
  useEffect(() => {
    if (!q.trim()) { setCli([]); return; }
    const t = setTimeout(async () => {
      setBuscando(true);
      try { setCli(await clientes.list(q.trim())); }
      catch (e) { setError(e instanceof Error ? e.message : "Error"); }
      finally { setBuscando(false); }
    }, 300);
    return () => clearTimeout(t);
  }, [q]);

  const elegirCliente = async (c: Cliente) => {
    setError(null); setAviso(null);
    setCliSel(c);
    setNroNueva("");
    try { setTjs(await tarjetas.deCliente(c.idCliente)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    setTimeout(() => nroRef.current?.focus(), 0);
  };

  const vigente = tjs.find((t) => t.activa) ?? null;

  const agregarTarjeta = async () => {
    if (!cliSel || !nroNueva.trim() || !tipoNueva) return;
    setError(null); setAviso(null);
    try {
      const r = await tarjetas.add(cliSel.idCliente, tipoNueva, nroNueva.trim());
      setNroNueva("");
      setTjs(await tarjetas.deCliente(cliSel.idCliente));
      setAviso(r.anuladas > 0
        ? `Tarjeta asignada. Se anuló la anterior (${r.tipoAnulada ?? "tarjeta"} ${r.nroAnulada})`
          + (r.anuladas > 1 ? ` y ${r.anuladas - 1} más.` : ".")
        : "Tarjeta asignada.");
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      <h1>Tarjetas</h1>
      {error && <p className="error">{error}</p>}

      <h3>Tipos de tarjeta</h3>
      <p className="muted" style={{ margin: "0 0 4px" }}>
        La lista de precios es opcional: si se indica, las compras con esa tarjeta se valorizan
        con esa lista (ej. tarjeta AZUL / ROJA).
      </p>
      <div className="field-row">
        <label>Descripción
          <input placeholder="Ej. Tarjeta Socio" value={tDesc} onChange={(e) => setTDesc(e.target.value)} />
        </label>
        <label>Lista de precios
          <select className={tLista ? undefined : "sin-valor"} value={tLista}
            onChange={(e) => setTLista(Number(e.target.value))}>
            <option value={0}>(sin lista de precios)</option>
            {listas.map((l) => <option key={l.id} value={l.id}>{l.descripcion}</option>)}
          </select>
        </label>
        <button className="primary" disabled={!tDesc.trim()}
          onClick={() => run(async () => { await tarjetas.createTipo(tDesc.trim(), tLista || null); setTDesc(""); setTLista(0); })}>Agregar</button>
      </div>
      <table className="grid">
        <thead><tr><th style={{ width: 80 }}>ID</th><th>Descripción</th><th>Lista de precios</th><th style={{ width: 60 }}></th></tr></thead>
        <tbody>
          {tipos.map((t) => (
            <tr key={t.idTipoTarjeta}>
              <td className="mono">{t.idTipoTarjeta}</td><td>{t.descripcion}</td>
              <td>{t.listaCodigo ?? <span className="muted">(sin lista de precios)</span>}</td>
              <td><button className="danger" onClick={() => run(() => tarjetas.removeTipo(t.idTipoTarjeta))}>×</button></td>
            </tr>
          ))}
          {tipos.length === 0 && <tr><td colSpan={4} className="muted">Sin tipos.</td></tr>}
        </tbody>
      </table>

      <h3 style={{ marginTop: 28 }}>Tarjetas por cliente</h3>
      <p className="muted" style={{ margin: "0 0 4px" }}>
        Cada cliente tiene <strong>una sola tarjeta vigente</strong>: al asignarle una nueva, la que
        tenía queda anulada (se conserva en el historial, no se borra).
      </p>

      <div className="filter-bar">
        <label className="grow">Buscar cliente (nombre, código, CUIT o documento)
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Escribí para buscar…" />
        </label>
        <span className="filter-count">
          {buscando ? "Buscando…" : q.trim() ? `${cli.length} cliente${cli.length === 1 ? "" : "s"}${cli.length === 50 ? " (máx.) — refiná la búsqueda" : ""}` : ""}
        </span>
      </div>

      {q.trim() !== "" && (
        <table className="grid picker-table">
          <thead>
            <tr>
              <th style={{ width: 110 }}>Código</th>
              <th>Cliente</th>
              <th style={{ width: 150 }}>CUIT / Doc.</th>
              <th style={{ width: 180 }}>Localidad</th>
              <th style={{ width: 110 }}>Estado</th>
            </tr>
          </thead>
          <tbody>
            {cli.map((c) => (
              <tr key={c.idCliente}
                className={cliSel?.idCliente === c.idCliente ? "sel" : ""}
                style={{ cursor: "pointer" }}
                onClick={() => elegirCliente(c)}>
                <td className="mono">{c.codigoInt}</td>
                <td className="stack">
                  {c.descripcion}
                  {c.nombreFantasia && <small>{c.nombreFantasia}</small>}
                </td>
                <td className="mono">{c.cuit || c.documento || "—"}</td>
                <td>{c.localidad ?? "—"}</td>
                <td>{c.activo ? <span className="badge on">Activo</span> : <span className="badge off">Baja</span>}</td>
              </tr>
            ))}
            {cli.length === 0 && !buscando && (
              <tr><td colSpan={5} className="muted">Ningún cliente coincide con la búsqueda.</td></tr>
            )}
          </tbody>
        </table>
      )}

      {cliSel && (
        <div className="card form" style={{ marginTop: 14 }}>
          <div className="page-head">
            <h4 style={{ margin: 0 }}>
              {cliSel.descripcion} <span className="muted mono">({cliSel.codigoInt})</span>
            </h4>
            <button onClick={() => { setCliSel(null); setTjs([]); setAviso(null); }}>Elegir otro cliente</button>
          </div>

          <p className="muted" style={{ margin: "4px 0 0" }}>
            Tarjeta vigente:{" "}
            {vigente
              ? <strong>{vigente.tipoDescripcion ?? "—"} · <span className="mono">{vigente.nroTarjeta}</span></strong>
              : <em>sin tarjeta</em>}
          </p>

          <div className="field-row">
            <label>Tipo de tarjeta
              <select value={tipoNueva} onChange={(e) => setTipoNueva(Number(e.target.value))}>
                {tipos.map((t) => <option key={t.idTipoTarjeta} value={t.idTipoTarjeta}>{t.descripcion}</option>)}
              </select>
            </label>
            <label>Nº de tarjeta
              <input ref={nroRef} placeholder="Nº tarjeta" value={nroNueva}
                onChange={(e) => setNroNueva(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && agregarTarjeta()} />
            </label>
            <button className="primary" disabled={!nroNueva.trim() || !tipoNueva} onClick={agregarTarjeta}>
              {vigente ? "Asignar y anular la vigente" : "Asignar tarjeta"}
            </button>
          </div>
          {aviso && <p className="ok-msg">{aviso}</p>}

          <table className="grid">
            <thead>
              <tr>
                <th style={{ width: 200 }}>Tipo</th>
                <th>Nº tarjeta</th>
                <th style={{ width: 200 }}>Estado</th>
                <th style={{ width: 100 }}></th>
              </tr>
            </thead>
            <tbody>
              {tjs.map((t) => (
                <tr key={`${t.idTipoTarjeta}-${t.nroTarjeta}`} className={t.activa ? "" : "inactive"}>
                  <td>{t.tipoDescripcion ?? "—"}</td>
                  <td className="mono">{t.nroTarjeta}</td>
                  <td className="stack">
                    {t.activa ? <span className="badge on">Vigente</span> : <span className="badge off">Anulada</span>}
                    {!t.activa && t.fechaBajaUtc && <small>{formatearFecha(t.fechaBajaUtc)}</small>}
                  </td>
                  <td>
                    <button className="danger"
                      onClick={() => run(() => tarjetas.remove(cliSel.idCliente, t.idTipoTarjeta, t.nroTarjeta), true)}>
                      Quitar
                    </button>
                  </td>
                </tr>
              ))}
              {tjs.length === 0 && <tr><td colSpan={4} className="muted">El cliente no tiene tarjetas.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
