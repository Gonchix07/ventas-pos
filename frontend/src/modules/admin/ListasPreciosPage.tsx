import { useEffect, useMemo, useState } from "react";
import {
  listasPrecios, referencias, articulos,
  type ListaPrecio, type ListaPrecioInput, type Lookup, type PrecioRow,
  type ArticuloListItem, type Presentacion,
} from "../../shared/api/admin";
import { MonedaInput, formatearMoneda } from "../../shared/ui/moneda";

const TIPOS = [
  { v: 1, l: "Base" },
  { v: 2, l: "Temporal" },
  { v: 3, l: "Folder" },
];

const VACIO: ListaPrecioInput = {
  idSucursal: 0, codigoInterno: "", tipo: 1, prioridad: 0, fechaInicio: null, fechaFin: null,
};

// Debe coincidir con ListaPrecioService.MaxResultados (backend).
const MAX_PRECIOS = 50;

// Cuántos artículos muestra el buscador de "asignar precio". El tope lo aplica el backend
// (ArticuloFiltro.Max): con 14 mil artículos no tiene sentido traer cientos para elegir uno.
const MAX_BUSQUEDA = 20;

export function ListasPreciosPage() {
  const [listas, setListas] = useState<ListaPrecio[]>([]);
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [form, setForm] = useState<ListaPrecioInput | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [selLista, setSelLista] = useState<ListaPrecio | null>(null);
  const [error, setError] = useState<string | null>(null);

  const cargar = async () => {
    setError(null);
    try { setListas(await listasPrecios.list()); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  useEffect(() => {
    void cargar();
    referencias.sucursales().then(setSucursales).catch(() => {});
  }, []);

  const nuevo = () => { setEditId(null); setForm({ ...VACIO, idSucursal: sucursales[0]?.id ?? 0 }); };
  const editar = (l: ListaPrecio) => {
    setEditId(l.idListaPrecio);
    setForm({
      idSucursal: l.idSucursal, codigoInterno: l.codigoInterno, tipo: l.tipo,
      prioridad: l.prioridad, fechaInicio: l.fechaInicio ?? null, fechaFin: l.fechaFin ?? null,
    });
  };

  const guardar = async () => {
    if (!form) return;
    setError(null);
    try {
      if (editId) await listasPrecios.update(editId, form);
      else await listasPrecios.create(form);
      setForm(null); setEditId(null);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const eliminar = async (id: number) => {
    if (!confirm("¿Eliminar la lista y todos sus precios?")) return;
    try { await listasPrecios.remove(id); if (selLista?.idListaPrecio === id) setSelLista(null); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const set = (patch: Partial<ListaPrecioInput>) => setForm((f) => (f ? { ...f, ...patch } : f));

  if (selLista) {
    return <PreciosEditor lista={selLista} onBack={() => { setSelLista(null); void cargar(); }} />;
  }

  return (
    <div>
      <div className="page-head">
        <h1>Listas de precios</h1>
        <button className="primary" onClick={nuevo}>Nueva lista</button>
      </div>
      {error && <p className="error">{error}</p>}

      {form && (
        <div className="card form">
          <h3>{editId ? "Editar lista" : "Nueva lista"}</h3>
          <div className="form-grid">
            <label>Código<input value={form.codigoInterno} onChange={(e) => set({ codigoInterno: e.target.value })} /></label>
            <label>Sucursal
              <select value={form.idSucursal} onChange={(e) => set({ idSucursal: Number(e.target.value) })}>
                {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Tipo
              <select value={form.tipo} onChange={(e) => set({ tipo: Number(e.target.value) })}>
                {TIPOS.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
              </select>
            </label>
            <label>Prioridad<input type="number" value={form.prioridad} onChange={(e) => set({ prioridad: Number(e.target.value) })} /></label>
            <label>Vigencia desde<input type="date" value={form.fechaInicio?.slice(0, 10) ?? ""} onChange={(e) => set({ fechaInicio: e.target.value || null })} /></label>
            <label>Vigencia hasta<input type="date" value={form.fechaFin?.slice(0, 10) ?? ""} onChange={(e) => set({ fechaFin: e.target.value || null })} /></label>
          </div>
          <p className="muted">Prioridad de resolución: Folder &gt; Temporal vigente &gt; Base (mayor prioridad gana).</p>
          <div className="row-actions">
            <button className="primary" onClick={guardar}>Guardar</button>
            <button onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </div>
      )}

      <table className="grid">
        <thead>
          <tr><th>Código</th><th>Sucursal</th><th>Tipo</th><th>Prioridad</th><th>Vigencia</th><th># Precios</th><th></th></tr>
        </thead>
        <tbody>
          {listas.map((l) => (
            <tr key={l.idListaPrecio}>
              <td className="mono">{l.codigoInterno}</td>
              <td>{l.sucursalDescripcion}</td>
              <td>{l.tipoDescripcion}</td>
              <td className="mono">{l.prioridad}</td>
              <td>{l.fechaInicio ? `${l.fechaInicio.slice(0, 10)} → ${l.fechaFin?.slice(0, 10) ?? "—"}` : "—"}</td>
              <td className="mono">{l.cantidadPrecios}</td>
              <td className="row-actions">
                <button className="primary" onClick={() => setSelLista(l)}>Precios</button>
                <button onClick={() => editar(l)}>Editar</button>
                <button className="danger" onClick={() => eliminar(l.idListaPrecio)}>Eliminar</button>
              </td>
            </tr>
          ))}
          {listas.length === 0 && <tr><td colSpan={7} className="muted">Sin listas.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}

// ---- Editor de precios de una lista ----
/**
 * El precio se carga UNA sola vez (el de la unidad suelta) y se propaga a todas las presentaciones
 * multiplicado por sus unidades por bulto. Antes había que cargar cada presentación por separado,
 * lo que era tedioso y permitía que quedaran precios incoherentes entre sí.
 */
function PreciosEditor({ lista, onBack }: { lista: ListaPrecio; onBack: () => void }) {
  const [precios, setPrecios] = useState<PrecioRow[]>([]);
  const [filtroPrecios, setFiltroPrecios] = useState("");
  const [preciosResultados, setPreciosResultados] = useState<PrecioRow[]>([]);
  const [resultados, setResultados] = useState<ArticuloListItem[]>([]);
  const [q, setQ] = useState("");
  const [buscando, setBuscando] = useState(false);
  const [artSel, setArtSel] = useState<ArticuloListItem | null>(null);
  const [presentaciones, setPresentaciones] = useState<Presentacion[]>([]);
  const [precioUnit, setPrecioUnit] = useState<number | null>(null);
  const [impUnit, setImpUnit] = useState<number | null>(0);
  const [guardando, setGuardando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const cargarPrecios = async (filtro = filtroPrecios) => {
    try { setPrecios(await listasPrecios.precios(lista.idListaPrecio, filtro.trim() || undefined)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // El listado de precios viene topeado a 50 (una lista real tiene miles), así que se filtra en el
  // backend con debounce en vez de traer todo y filtrar en memoria.
  useEffect(() => {
    const t = setTimeout(() => void cargarPrecios(filtroPrecios), filtroPrecios.trim() ? 300 : 0);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filtroPrecios]);

  // Búsqueda contra el backend (con debounce), igual que en el ABM de artículos. Sin término no se
  // busca nada: es un buscador, y traer el catálogo entero (tope 500) sólo para llenar la pantalla
  // hacía lenta la carga de la lista.
  useEffect(() => {
    if (!q.trim()) { setResultados([]); setBuscando(false); return; }
    const t = setTimeout(async () => {
      setBuscando(true);
      try {
        // El tope lo aplica el backend (`max`), pero se recorta igual acá: así la tabla y el
        // contador coinciden aunque responda una versión de la API que todavía ignore el parámetro.
        const r = await articulos.list({ texto: q.trim(), activo: true, max: MAX_BUSQUEDA });
        setResultados(r.slice(0, MAX_BUSQUEDA));
      }
      catch { setResultados([]); }
      finally { setBuscando(false); }
    }, 300);
    return () => clearTimeout(t);
  }, [q]);

  // Precios de los artículos que está mostrando el buscador: el listado de abajo está topeado, así
  // que no sirve para saber si un artículo ya tiene precio — se consultan estos puntualmente. Si la
  // búsqueda trajo demasiados, no se consulta (la URL de ids se iría de largo): con un término
  // razonable son unos pocos.
  const cargarPreciosResultados = async (arts = resultados) => {
    if (arts.length === 0 || arts.length > 200) { setPreciosResultados([]); return; }
    try { setPreciosResultados(await listasPrecios.preciosDeArticulos(lista.idListaPrecio, arts.map((a) => a.idArticulo))); }
    catch { setPreciosResultados([]); }
  };

  useEffect(() => {
    let vigente = true;
    void (async () => {
      if (resultados.length === 0 || resultados.length > 200) { setPreciosResultados([]); return; }
      try {
        const r = await listasPrecios.preciosDeArticulos(lista.idListaPrecio, resultados.map((a) => a.idArticulo));
        if (vigente) setPreciosResultados(r);
      } catch { if (vigente) setPreciosResultados([]); }
    })();
    return () => { vigente = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resultados, lista.idListaPrecio]);

  const elegirArticulo = async (a: ArticuloListItem) => {
    setArtSel(a); setError(null); setAviso(null);
    try {
      const det = await articulos.get(a.idArticulo);
      setPresentaciones(det.presentaciones);

      // Si el artículo ya tiene precio en esta lista, se deduce el unitario dividiendo por las
      // unidades por bulto — así el campo arranca con lo que ya está cargado y no en blanco.
      // Se consulta por artículo: el listado de abajo está topeado y puede no incluirlo.
      const cargados = await listasPrecios.preciosDeArticulos(lista.idListaPrecio, [a.idArticulo]);
      if (cargados.length > 0) {
        const base = cargados.reduce((m, x) => (x.unidadXBulto < m.unidadXBulto ? x : m), cargados[0]);
        setPrecioUnit(base.unidadXBulto > 0 ? Number((base.precioFinal / base.unidadXBulto).toFixed(2)) : null);
        setImpUnit(base.unidadXBulto > 0 ? Number((base.impuestoInterno / base.unidadXBulto).toFixed(2)) : 0);
      } else {
        setPrecioUnit(null); setImpUnit(0);
      }
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const cerrarEditor = () => { setArtSel(null); setPresentaciones([]); setPrecioUnit(null); setImpUnit(0); };

  const guardar = async () => {
    if (!artSel || precioUnit === null) return;
    setError(null); setAviso(null); setGuardando(true);
    try {
      const aplicados = await listasPrecios.setPrecioArticulo(
        lista.idListaPrecio, artSel.idArticulo, precioUnit, impUnit ?? 0);
      setAviso(`Precio aplicado a ${aplicados.length} presentación(es) de ${artSel.descripcion}.`);
      // Guardado OK: se cierra el editor y se refrescan LAS DOS tablas — la de precios cargados y
      // el badge "cargado / sin precio" del buscador, que si no seguía diciendo "sin precio".
      cerrarEditor();
      await Promise.all([cargarPrecios(), cargarPreciosResultados()]);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setGuardando(false); }
  };

  const eliminarPrecio = async (idPresentacion: number) => {
    try { await listasPrecios.removePrecio(lista.idListaPrecio, idPresentacion); await cargarPrecios(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // Vista previa: qué queda en cada presentación con el precio unitario tipeado.
  const previa = useMemo(() => presentaciones
    .slice()
    .sort((a, b) => a.unidadXBulto - b.unidadXBulto)
    .map((p) => ({
      id: p.idPresentacion!,
      nombre: p.descripcionTicket || `Presentación ${p.idPresentacion}`,
      unidadXBulto: p.unidadXBulto,
      precio: precioUnit === null ? null : redondear(precioUnit * p.unidadXBulto),
      imp: redondear((impUnit ?? 0) * p.unidadXBulto),
    })), [presentaciones, precioUnit, impUnit]);

  const yaTienePrecio = (idArticulo: number) => preciosResultados.some((p) => p.idArticulo === idArticulo);

  return (
    <div>
      <div className="page-head">
        <h1>Precios · {lista.codigoInterno} <span className="muted">({lista.tipoDescripcion} · {lista.sucursalDescripcion})</span></h1>
        <button onClick={onBack}>← Volver a listas</button>
      </div>
      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      <div className="card form">
        <h3>Asignar precio a un artículo</h3>
        <p className="muted" style={{ margin: "0 0 8px" }}>
          Se carga un solo precio (el de la unidad suelta) y cada presentación se valoriza
          multiplicándolo por sus unidades por bulto.
        </p>

        {/* Div y no label: adentro hay un botón (limpiar), y un control interactivo dentro de un
            label se lleva mal con el click-to-focus. El label envuelve solo al input. */}
        <div className="search-field">
          <label htmlFor="buscar-articulo-precio">Buscar artículo por código, descripción o código de barra</label>
          <span className="search-box">
            <input id="buscar-articulo-precio" placeholder="Escribí o escaneá un producto…"
              value={q} onChange={(e) => setQ(e.target.value)} />
            {q && (
              <button type="button" className="search-clear" title="Limpiar" onClick={() => setQ("")}>×</button>
            )}
          </span>
          <span className="search-hint">
            {buscando
              ? "Buscando…"
              : resultados.length === 0
                ? `Se muestran hasta ${MAX_BUSQUEDA} resultados.`
                : `${resultados.length} resultado${resultados.length === 1 ? "" : "s"}` +
                  (resultados.length === MAX_BUSQUEDA ? ` (máx. ${MAX_BUSQUEDA}) — refiná la búsqueda` : "")}
          </span>
        </div>

        <table className="grid picker-table">
          <thead>
            {/* Anchos explícitos: la tabla es table-layout:fixed (por el scroll con encabezado
                fijo), así que sin esto las 6 columnas se repartirían el ancho en partes iguales
                y la descripción —lo único que se lee de verdad— quedaría apretada. */}
            <tr>
              <th style={{ width: "12%" }}>Código</th>
              <th style={{ width: "34%" }}>Descripción</th>
              <th style={{ width: "20%" }}>Clasificación</th>
              <th style={{ width: "9%" }}>Present.</th>
              <th style={{ width: "13%" }}>Precio</th>
              <th style={{ width: "12%" }}></th>
            </tr>
          </thead>
          <tbody>
            {buscando && <tr><td colSpan={6} className="muted">Buscando…</td></tr>}
            {!buscando && resultados.map((a) => (
              <tr key={a.idArticulo} className={artSel?.idArticulo === a.idArticulo ? "sel" : ""}>
                <td className="mono">{a.codigoInterno}</td>
                <td>{a.descripcion}</td>
                <td className="muted">{[a.sectorDescripcion, a.lineaDescripcion].filter(Boolean).join(" · ") || "—"}</td>
                <td className="mono">{a.cantidadPresentaciones}</td>
                <td>{yaTienePrecio(a.idArticulo)
                  ? <span className="badge on">cargado</span>
                  : <span className="muted">sin precio</span>}</td>
                <td><button className={artSel?.idArticulo === a.idArticulo ? "primary" : ""}
                  onClick={() => elegirArticulo(a)}>
                  {yaTienePrecio(a.idArticulo) ? "Editar precio" : "Poner precio"}
                </button></td>
              </tr>
            ))}
            {!buscando && resultados.length === 0 && (
              <tr><td colSpan={6} className="muted">
                {q.trim()
                  ? "Ningún artículo coincide con la búsqueda."
                  : "Escribí o escaneá un producto para buscarlo."}
              </td></tr>
            )}
          </tbody>
        </table>

        {artSel && (
          <div className="precio-editor">
            <h4 style={{ margin: "16px 0 4px" }}>
              <span className="mono">{artSel.codigoInterno}</span> · {artSel.descripcion}
            </h4>
            <div className="field-row">
              <label>Precio unitario (unidad suelta)
                <MonedaInput value={precioUnit} onChange={setPrecioUnit} autoFocus onEnter={guardar} style={{ width: 170 }} />
              </label>
              <label>Impuesto interno (por unidad)
                <MonedaInput value={impUnit} onChange={setImpUnit} style={{ width: 150 }} />
              </label>
              <button className="primary" disabled={precioUnit === null || guardando} onClick={guardar}>
                {guardando ? "Guardando…" : "Guardar precios"}
              </button>
              <button disabled={guardando} onClick={cerrarEditor}>Cancelar</button>
            </div>

            <table className="grid" style={{ marginTop: 4 }}>
              <thead>
                <tr>
                  <th>Presentación</th><th>Un×Bulto</th>
                  <th className="money">Precio final</th><th className="money">Imp. interno</th>
                </tr>
              </thead>
              <tbody>
                {previa.map((p) => (
                  <tr key={p.id}>
                    <td>{p.nombre}</td>
                    <td className="mono">{p.unidadXBulto}</td>
                    <td className="money">{p.precio === null ? <span className="muted">—</span> : formatearMoneda(p.precio)}</td>
                    <td className="money">{formatearMoneda(p.imp)}</td>
                  </tr>
                ))}
                {previa.length === 0 && (
                  <tr><td colSpan={4} className="muted">El artículo no tiene presentaciones.</td></tr>
                )}
              </tbody>
            </table>
            {precioUnit !== null && previa.length > 0 && (
              <p className="muted" style={{ marginTop: 6 }}>
                Vista previa — se guarda al presionar «Guardar precios».
              </p>
            )}
          </div>
        )}
      </div>

      <h3>Precios cargados</h3>
      <div className="filter-bar">
        <label className="grow">
          Buscar en los precios cargados (código o descripción)
          <input placeholder="Filtrar…" value={filtroPrecios}
            onChange={(e) => setFiltroPrecios(e.target.value)} />
        </label>
        {filtroPrecios && <button onClick={() => setFiltroPrecios("")}>Limpiar</button>}
        <span className="filter-count">
          {`${precios.length} precio${precios.length === 1 ? "" : "s"}`}
          {precios.length === MAX_PRECIOS ? " (máx.) — refiná la búsqueda" : ""}
        </span>
      </div>
      <table className="grid">
        <thead>
          <tr>
            <th>Código</th><th>Artículo</th><th>Presentación</th><th>Un×Bulto</th>
            <th className="money">Precio final</th><th className="money">Imp. interno</th><th></th>
          </tr>
        </thead>
        <tbody>
          {precios.map((p) => (
            <tr key={p.idPresentacion}>
              <td className="mono">{p.codigoInterno}</td>
              <td>{p.articuloDescripcion}</td>
              <td>{p.descripcionTicket}</td>
              <td className="mono">{p.unidadXBulto}</td>
              <td className="money">{formatearMoneda(p.precioFinal)}</td>
              <td className="money">{formatearMoneda(p.impuestoInterno)}</td>
              <td><button className="danger" onClick={() => eliminarPrecio(p.idPresentacion)}>Quitar</button></td>
            </tr>
          ))}
          {precios.length === 0 && <tr><td colSpan={7} className="muted">Sin precios cargados.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}

/** Mismo redondeo que la regla de dominio (PrecioPorBulto), para que la vista previa no mienta. */
function redondear(n: number): number {
  return Math.round((n + Number.EPSILON) * 100) / 100;
}
