import { useEffect, useState } from "react";
import { clusters, clientes, type Cluster, type ClusterMiembro, type Cliente } from "../../shared/api/admin";

/**
 * ABM de clusters. El editor de miembros trabaja sobre una selección local (Set de ids) y se
 * guarda en una sola llamada (`setMiembros`), en vez de pegarle al backend por cada cliente: así
 * se pueden marcar/desmarcar varios y recién confirmar, y "quitar" es simétrico a "agregar".
 */
export function ClustersPage() {
  const [items, setItems] = useState<Cluster[]>([]);
  const [sel, setSel] = useState<Cluster | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  // Alta / renombre
  const [nuevoDesc, setNuevoDesc] = useState("");
  const [renombrando, setRenombrando] = useState<number | null>(null);
  const [renombreDesc, setRenombreDesc] = useState("");

  // Editor de miembros: `originales` = lo que hay en la BD, `seleccion` = lo que va a quedar.
  const [originales, setOriginales] = useState<ClusterMiembro[]>([]);
  const [seleccion, setSeleccion] = useState<Set<number>>(new Set());
  const [nombres, setNombres] = useState<Map<number, ClusterMiembro>>(new Map());
  const [guardando, setGuardando] = useState(false);

  // Búsqueda de clientes (independiente del alta de cluster: antes compartían estado y se pisaban).
  const [q, setQ] = useState("");
  const [resultados, setResultados] = useState<Cliente[]>([]);
  const [buscando, setBuscando] = useState(false);

  const cargar = async () => {
    setError(null);
    try { setItems(await clusters.list()); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(); }, []);

  const abrir = async (c: Cluster) => {
    setSel(c); setError(null); setAviso(null); setQ(""); setResultados([]);
    try {
      const ms = await clusters.miembros(c.idCluster);
      setOriginales(ms);
      setSeleccion(new Set(ms.map((m) => m.idCliente)));
      setNombres(new Map(ms.map((m) => [m.idCliente, m])));
    } catch (e) {
      setOriginales([]); setSeleccion(new Set());
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  // Debounce de la búsqueda: el operador escribe, no aprieta un botón "Buscar".
  useEffect(() => {
    if (!q.trim()) { setResultados([]); return; }
    const t = setTimeout(async () => {
      setBuscando(true);
      try { setResultados(await clientes.list(q.trim())); }
      catch { setResultados([]); }
      finally { setBuscando(false); }
    }, 300);
    return () => clearTimeout(t);
  }, [q]);

  const toggle = (c: Cliente | ClusterMiembro, idCliente: number) => {
    setNombres((prev) => {
      if (prev.has(idCliente)) return prev;
      const next = new Map(prev);
      const desc = "clienteDescripcion" in c ? c.clienteDescripcion : c.descripcion;
      const cod = "codigoInt" in c ? c.codigoInt : "";
      next.set(idCliente, { idCliente, clienteDescripcion: desc, codigoInt: cod });
      return next;
    });
    setSeleccion((prev) => {
      const next = new Set(prev);
      if (next.has(idCliente)) next.delete(idCliente); else next.add(idCliente);
      return next;
    });
    setAviso(null);
  };

  const idsOriginales = new Set(originales.map((m) => m.idCliente));
  const aAgregar = [...seleccion].filter((id) => !idsOriginales.has(id));
  const aQuitar = [...idsOriginales].filter((id) => !seleccion.has(id));
  const hayCambios = aAgregar.length > 0 || aQuitar.length > 0;

  const guardarMiembros = async () => {
    if (!sel) return;
    setError(null); setAviso(null); setGuardando(true);
    try {
      const r = await clusters.setMiembros(sel.idCluster, [...seleccion]);
      setAviso(`Guardado: ${r.agregados} agregado(s), ${r.quitados} quitado(s). Total ${r.total}.`);
      await cargar();
      const ms = await clusters.miembros(sel.idCluster);
      setOriginales(ms);
      setSeleccion(new Set(ms.map((m) => m.idCliente)));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setGuardando(false); }
  };

  const descartar = () => {
    setSeleccion(new Set(originales.map((m) => m.idCliente)));
    setAviso(null);
  };

  const crear = async () => {
    const d = nuevoDesc.trim();
    if (!d) return;
    setError(null); setAviso(null);
    try {
      const id = await clusters.create(d);
      setNuevoDesc("");
      await cargar();
      await abrir({ idCluster: id, descripcion: d, cantidadClientes: 0 });
      setAviso(`Cluster «${d}» creado. Ahora podés asignarle clientes.`);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const renombrar = async (id: number) => {
    const d = renombreDesc.trim();
    if (!d) return;
    setError(null);
    try {
      await clusters.rename(id, d);
      setRenombrando(null); setRenombreDesc("");
      await cargar();
      if (sel?.idCluster === id) setSel({ ...sel, descripcion: d });
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const eliminar = async (c: Cluster) => {
    if (!confirm(`¿Eliminar el cluster «${c.descripcion}»?${c.cantidadClientes > 0 ? ` Tiene ${c.cantidadClientes} cliente(s) asignado(s).` : ""}`)) return;
    setError(null);
    try {
      await clusters.remove(c.idCluster);
      if (sel?.idCluster === c.idCluster) { setSel(null); setOriginales([]); setSeleccion(new Set()); }
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // Lo que se muestra en el panel derecho: miembros actuales + los recién marcados en la búsqueda.
  const filasSeleccion = [...seleccion].map((id) => nombres.get(id)
    ?? { idCliente: id, clienteDescripcion: `Cliente ${id}`, codigoInt: "" });

  return (
    <div>
      <h1>Clusters de clientes</h1>
      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      <div className="two-col">
        <div>
          <div className="card form">
            <h3>Nuevo cluster</h3>
            <p className="muted" style={{ margin: "0 0 8px" }}>
              Solo el nombre — después le asignás los clientes que quieras.
            </p>
            <div className="toolbar">
              <input placeholder="Nombre del cluster" value={nuevoDesc}
                onChange={(e) => setNuevoDesc(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && crear()} />
              <button className="primary" disabled={!nuevoDesc.trim()} onClick={crear}>Crear</button>
            </div>
          </div>

          <table className="grid">
            <thead><tr><th>ID</th><th>Nombre</th><th>Clientes</th><th></th></tr></thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.idCluster} className={sel?.idCluster === c.idCluster ? "sel" : ""}>
                  <td className="mono">{c.idCluster}</td>
                  <td>
                    {renombrando === c.idCluster ? (
                      <div className="toolbar">
                        <input autoFocus value={renombreDesc} onChange={(e) => setRenombreDesc(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === "Enter") renombrar(c.idCluster);
                            if (e.key === "Escape") { setRenombrando(null); setRenombreDesc(""); }
                          }} />
                        <button className="primary" onClick={() => renombrar(c.idCluster)}>OK</button>
                        <button onClick={() => { setRenombrando(null); setRenombreDesc(""); }}>×</button>
                      </div>
                    ) : c.descripcion}
                  </td>
                  <td className="mono">{c.cantidadClientes}</td>
                  <td className="row-actions">
                    <button className="primary" onClick={() => abrir(c)}>Editar miembros</button>
                    <button onClick={() => { setRenombrando(c.idCluster); setRenombreDesc(c.descripcion); }}>Renombrar</button>
                    <button className="danger" onClick={() => eliminar(c)}>Eliminar</button>
                  </td>
                </tr>
              ))}
              {items.length === 0 && <tr><td colSpan={4} className="muted">Sin clusters. Creá el primero arriba.</td></tr>}
            </tbody>
          </table>
        </div>

        <div>
          {sel ? (
            <>
              <h3>Miembros de «{sel.descripcion}»</h3>

              <label className="inline-label" style={{ display: "flex", flexDirection: "column", alignItems: "stretch", gap: 4 }}>
                Buscar clientes para agregar o quitar (nombre, código, CUIT o documento)
                <input placeholder="Escribí para buscar…" value={q} onChange={(e) => setQ(e.target.value)} />
              </label>

              {q.trim() && (
                <div className="picker">
                  {buscando && <p className="muted">Buscando…</p>}
                  {!buscando && resultados.length === 0 && <p className="muted">Ningún cliente coincide.</p>}
                  {resultados.map((c) => {
                    const marcado = seleccion.has(c.idCliente);
                    const eraMiembro = idsOriginales.has(c.idCliente);
                    return (
                      <label key={c.idCliente} className={`picker-row${marcado ? " marcado" : ""}`}>
                        <input type="checkbox" checked={marcado} onChange={() => toggle(c, c.idCliente)} />
                        <span className="mono">{c.codigoInt}</span>
                        <span className="grow">{c.descripcion}</span>
                        {eraMiembro
                          ? <span className="badge on">miembro</span>
                          : marcado ? <span className="badge on">se agrega</span> : null}
                      </label>
                    );
                  })}
                </div>
              )}

              {hayCambios && (
                <div className="pending-bar">
                  <span>
                    {aAgregar.length > 0 && <b>+{aAgregar.length}</b>}
                    {aAgregar.length > 0 && aQuitar.length > 0 && " / "}
                    {aQuitar.length > 0 && <b>−{aQuitar.length}</b>}
                    {" "}cambio(s) sin guardar
                  </span>
                  <button className="primary" disabled={guardando} onClick={guardarMiembros}>
                    {guardando ? "Guardando…" : "Guardar cambios"}
                  </button>
                  <button disabled={guardando} onClick={descartar}>Descartar</button>
                </div>
              )}

              <table className="grid">
                <thead>
                  <tr><th style={{ width: 36 }}></th><th>Código</th><th>Cliente</th><th>Estado</th></tr>
                </thead>
                <tbody>
                  {filasSeleccion.map((m) => (
                    <tr key={m.idCliente}>
                      <td>
                        <input type="checkbox" checked onChange={() => toggle(m, m.idCliente)} title="Quitar del cluster" />
                      </td>
                      <td className="mono">{m.codigoInt}</td>
                      <td>{m.clienteDescripcion}</td>
                      <td>{idsOriginales.has(m.idCliente)
                        ? <span className="badge on">miembro</span>
                        : <span className="badge on">se agrega</span>}</td>
                    </tr>
                  ))}
                  {aQuitar.map((id) => {
                    const m = nombres.get(id);
                    return (
                      <tr key={`q-${id}`} className="inactive">
                        <td><input type="checkbox" checked={false} onChange={() => toggle(m ?? { idCliente: id, clienteDescripcion: "", codigoInt: "" }, id)} title="Volver a incluir" /></td>
                        <td className="mono">{m?.codigoInt}</td>
                        <td>{m?.clienteDescripcion}</td>
                        <td><span className="badge off">se quita</span></td>
                      </tr>
                    );
                  })}
                  {filasSeleccion.length === 0 && aQuitar.length === 0 && (
                    <tr><td colSpan={4} className="muted">
                      Sin miembros. Buscá clientes arriba para agregarlos.
                    </td></tr>
                  )}
                </tbody>
              </table>
            </>
          ) : <p className="muted">Elegí un cluster («Editar miembros») o creá uno nuevo.</p>}
        </div>
      </div>
    </div>
  );
}
