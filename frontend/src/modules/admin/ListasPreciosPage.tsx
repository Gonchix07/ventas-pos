import { useEffect, useMemo, useState } from "react";
import {
  listasPrecios, referencias, articulos, lookups, recargoLogistica,
  type ListaPrecio, type ListaPrecioInput, type Lookup, type PrecioRow,
  type ArticuloListItem, type Presentacion,
  type DiferencialListaPrecio,
  type RecargoLogistica, type RecargoLogisticaInput,
} from "../../shared/api/admin";
import { MonedaInput, formatearMoneda } from "../../shared/ui/moneda";
import { IconEditar, IconEliminar } from "../../shared/ui/icons";
import { useAuth } from "../../shared/auth/auth";

const TIPO_ENLAZADA = 4;
const TIPOS = [
  { v: 1, l: "Base" },
  { v: 2, l: "Temporal" },
  { v: 3, l: "Folder" },
  { v: TIPO_ENLAZADA, l: "Enlazada" },
];

const VACIO: ListaPrecioInput = {
  idSucursal: 0, codigoInterno: "", tipo: 1, prioridad: 0, fechaInicio: null, fechaFin: null,
  idListaBase: null,
};

// Debe coincidir con ListaPrecioService.MaxResultados (backend).
const MAX_PRECIOS = 50;

// Cuántos artículos muestra el buscador de "asignar precio". El tope lo aplica el backend
// (ArticuloFiltro.Max): con 14 mil artículos no tiene sentido traer cientos para elegir uno.
const MAX_BUSQUEDA = 20;

export function ListasPreciosPage() {
  const { idSucursalPredeterminada } = useAuth();
  const [listas, setListas] = useState<ListaPrecio[]>([]);
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [form, setForm] = useState<ListaPrecioInput | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [selLista, setSelLista] = useState<ListaPrecio | null>(null);
  const [selDiferenciales, setSelDiferenciales] = useState<ListaPrecio | null>(null);
  const [verRecargoLogistica, setVerRecargoLogistica] = useState(false);
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

  const nuevo = () => { setEditId(null); setForm({ ...VACIO, idSucursal: idSucursalPredeterminada ?? sucursales[0]?.id ?? 0 }); };
  const editar = (l: ListaPrecio) => {
    setEditId(l.idListaPrecio);
    setForm({
      idSucursal: l.idSucursal, codigoInterno: l.codigoInterno, tipo: l.tipo,
      prioridad: l.prioridad, fechaInicio: l.fechaInicio ?? null, fechaFin: l.fechaFin ?? null,
      idListaBase: l.idListaBase ?? null,
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
  if (selDiferenciales) {
    return <DiferencialesEditor lista={selDiferenciales} onBack={() => { setSelDiferenciales(null); void cargar(); }} />;
  }
  if (verRecargoLogistica) {
    return <RecargoLogisticaEditor onBack={() => setVerRecargoLogistica(false)} />;
  }

  return (
    <div>
      <div className="page-head">
        <h1>Listas de precios</h1>
        <div className="row-actions">
          <button onClick={() => setVerRecargoLogistica(true)}>Recargo logística</button>
          <button className="primary" onClick={nuevo}>Nueva lista</button>
        </div>
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
            {form.tipo === TIPO_ENLAZADA && (
              <label>Lista base
                <select value={form.idListaBase ?? 0} onChange={(e) => set({ idListaBase: Number(e.target.value) || null })}>
                  <option value={0}>— Elegir —</option>
                  {listas.filter((l) => l.tipo !== TIPO_ENLAZADA && l.idListaPrecio !== editId).map((l) => (
                    <option key={l.idListaPrecio} value={l.idListaPrecio}>{l.codigoInterno}</option>
                  ))}
                </select>
              </label>
            )}
          </div>
          <p className="muted">Prioridad de resolución: Folder &gt; Temporal vigente &gt; Base (mayor prioridad gana).</p>
          {form.tipo === TIPO_ENLAZADA && (
            <p className="muted">
              Enlazada no tiene precios propios: toma los de la lista base y les aplica el % de sus
              diferenciales (por artículo puntual, si no por línea).
            </p>
          )}
          <div className="row-actions">
            <button className="primary" onClick={guardar}>Guardar</button>
            <button onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </div>
      )}

      <table className="grid">
        <thead>
          <tr><th>Código</th><th>Sucursal</th><th>Tipo</th><th>Prioridad</th><th>Vigencia</th><th># Precios/Dif.</th><th></th></tr>
        </thead>
        <tbody>
          {listas.map((l) => (
            <tr key={l.idListaPrecio} className={editId === l.idListaPrecio ? "lote-sel" : ""}>
              <td className="mono">{l.codigoInterno}</td>
              <td>{l.sucursalDescripcion}</td>
              <td>
                {l.tipoDescripcion}
                {l.tipo === TIPO_ENLAZADA && l.listaBaseCodigoInterno &&
                  <span className="muted"> · base: {l.listaBaseCodigoInterno}</span>}
              </td>
              <td className="mono">{l.prioridad}</td>
              <td>{l.fechaInicio ? `${l.fechaInicio.slice(0, 10)} → ${l.fechaFin?.slice(0, 10) ?? "—"}` : "—"}</td>
              <td className="mono">{l.tipo === TIPO_ENLAZADA ? l.cantidadDiferenciales : l.cantidadPrecios}</td>
              <td className="row-actions">
                {l.tipo === TIPO_ENLAZADA
                  ? <button className="primary" onClick={() => setSelDiferenciales(l)}>Diferenciales</button>
                  : <button className="primary" onClick={() => setSelLista(l)}>Precios</button>}
                <button className="icon-btn" title="Editar" aria-label="Editar" onClick={() => editar(l)}><IconEditar /></button>
                <button className="icon-btn icon-danger" title="Eliminar" aria-label="Eliminar"
                  onClick={() => eliminar(l.idListaPrecio)}><IconEliminar /></button>
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
              <td className="row-actions">
                <button className="icon-btn icon-danger" title="Quitar" aria-label="Quitar"
                  onClick={() => eliminarPrecio(p.idPresentacion)}><IconEliminar /></button>
              </td>
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

// ---- Editor de diferenciales de una lista Enlazada ----
/**
 * Un diferencial es por línea completa O por artículo puntual (nunca ambos) — si un artículo
 * matchea los dos, el del artículo puntual gana (ver DiferencialListaPrecioResolver en el backend).
 * Se puede cargar a mano acá o traer de una sola vez con "Importar desde DBF" (descxtipocli_art.dbf,
 * TIPO_TARJE='03', solo vigentes).
 */
function DiferencialesEditor({ lista, onBack }: { lista: ListaPrecio; onBack: () => void }) {
  const [diferenciales, setDiferenciales] = useState<DiferencialListaPrecio[]>([]);
  const [lineas, setLineas] = useState<Lookup[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [importando, setImportando] = useState(false);

  // Alta por línea
  const [lineaSel, setLineaSel] = useState(0);
  const [porcentajeLinea, setPorcentajeLinea] = useState<number | null>(null);

  // Alta por artículo
  const [q, setQ] = useState("");
  const [buscando, setBuscando] = useState(false);
  const [resultados, setResultados] = useState<ArticuloListItem[]>([]);
  const [articuloSel, setArticuloSel] = useState<ArticuloListItem | null>(null);
  const [porcentajeArticulo, setPorcentajeArticulo] = useState<number | null>(null);

  // Edición inline del % de un diferencial existente
  const [editandoId, setEditandoId] = useState<number | null>(null);
  const [porcentajeEditado, setPorcentajeEditado] = useState<number | null>(null);

  const cargar = async () => {
    setError(null);
    try {
      const [dif, lin] = await Promise.all([listasPrecios.diferenciales(lista.idListaPrecio), lookups.list("lineas")]);
      setDiferenciales(dif); setLineas(lin);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  useEffect(() => { void cargar(); }, [lista.idListaPrecio]);

  useEffect(() => {
    if (!q.trim()) { setResultados([]); setBuscando(false); return; }
    const t = setTimeout(async () => {
      setBuscando(true);
      try { setResultados((await articulos.list({ texto: q.trim(), activo: true, max: 20 })).slice(0, 20)); }
      catch { setResultados([]); }
      finally { setBuscando(false); }
    }, 300);
    return () => clearTimeout(t);
  }, [q]);

  const agregarPorLinea = async () => {
    if (!lineaSel || porcentajeLinea === null) return;
    setError(null);
    try {
      await listasPrecios.createDiferencial(lista.idListaPrecio, { idLinea: lineaSel, idArticulo: null, porcentaje: porcentajeLinea });
      setLineaSel(0); setPorcentajeLinea(null);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const agregarPorArticulo = async () => {
    if (!articuloSel || porcentajeArticulo === null) return;
    setError(null);
    try {
      await listasPrecios.createDiferencial(lista.idListaPrecio,
        { idLinea: null, idArticulo: articuloSel.idArticulo, porcentaje: porcentajeArticulo });
      setArticuloSel(null); setPorcentajeArticulo(null); setQ("");
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const empezarEdicion = (d: DiferencialListaPrecio) => { setEditandoId(d.idDiferencial); setPorcentajeEditado(d.porcentaje); };

  const guardarEdicion = async (d: DiferencialListaPrecio) => {
    if (porcentajeEditado === null) return;
    setError(null);
    try {
      await listasPrecios.updateDiferencial(lista.idListaPrecio, d.idDiferencial,
        { idLinea: d.idLinea ?? null, idArticulo: d.idArticulo ?? null, porcentaje: porcentajeEditado });
      setEditandoId(null);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const eliminar = async (d: DiferencialListaPrecio) => {
    if (!confirm("¿Eliminar este diferencial?")) return;
    try { await listasPrecios.removeDiferencial(lista.idListaPrecio, d.idDiferencial); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const importar = async () => {
    if (!confirm("Esto reemplaza TODOS los diferenciales actuales de esta lista por lo que haya " +
      "vigente en descxtipocli_art.dbf (TIPO_TARJE='03'). ¿Continuar?")) return;
    setImportando(true); setError(null); setAviso(null);
    try {
      const cantidad = await listasPrecios.importarDiferenciales(lista.idListaPrecio);
      setAviso(`Importados ${cantidad} diferenciales.`);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setImportando(false); }
  };

  return (
    <div>
      <div className="page-head">
        <h1>Diferenciales · {lista.codigoInterno}{" "}
          <span className="muted">(Enlazada, base: {lista.listaBaseCodigoInterno ?? "—"})</span></h1>
        <div className="row-actions">
          <button disabled={importando} onClick={importar}>
            {importando ? "Importando…" : "Importar desde DBF"}
          </button>
          <button onClick={onBack}>← Volver a listas</button>
        </div>
      </div>
      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      {importando && (
        <div className="modal-fondo">
          <div className="modal-caja" style={{ width: "min(320px, 100%)" }}>
            <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: "24px 0" }}>
              <div className="spinner" aria-hidden="true" />
              <p style={{ margin: 0 }}>Importando…</p>
            </div>
          </div>
        </div>
      )}

      <div className="card form">
        <h3>Agregar por línea completa</h3>
        <div className="field-row">
          <label>Línea
            <select value={lineaSel} onChange={(e) => setLineaSel(Number(e.target.value))}>
              <option value={0}>— Elegir —</option>
              {lineas.map((l) => <option key={l.id} value={l.id}>{l.descripcion}</option>)}
            </select>
          </label>
          <label>Recargo %
            <input type="number" step="0.01" value={porcentajeLinea ?? ""}
              onChange={(e) => setPorcentajeLinea(e.target.value === "" ? null : Number(e.target.value))}
              style={{ width: 100 }} />
          </label>
          <button className="primary" disabled={!lineaSel || porcentajeLinea === null} onClick={agregarPorLinea}>
            Agregar
          </button>
        </div>
      </div>

      <div className="card form">
        <h3>Agregar por artículo puntual</h3>
        <div className="search-field">
          <label htmlFor="buscar-articulo-diferencial">Buscar artículo por código o descripción</label>
          <span className="search-box">
            <input id="buscar-articulo-diferencial" value={q} onChange={(e) => setQ(e.target.value)}
              placeholder="Escribí o escaneá un producto…" />
            {q && <button type="button" className="search-clear" title="Limpiar" onClick={() => setQ("")}>×</button>}
          </span>
        </div>
        <table className="grid picker-table">
          <thead><tr><th>Código</th><th>Descripción</th><th></th></tr></thead>
          <tbody>
            {buscando && <tr><td colSpan={3} className="muted">Buscando…</td></tr>}
            {!buscando && resultados.map((a) => (
              <tr key={a.idArticulo} className={articuloSel?.idArticulo === a.idArticulo ? "sel" : ""}>
                <td className="mono">{a.codigoInterno}</td>
                <td>{a.descripcion}</td>
                <td><button className={articuloSel?.idArticulo === a.idArticulo ? "primary" : ""}
                  onClick={() => setArticuloSel(a)}>Elegir</button></td>
              </tr>
            ))}
            {!buscando && resultados.length === 0 && (
              <tr><td colSpan={3} className="muted">
                {q.trim() ? "Ningún artículo coincide con la búsqueda." : "Escribí un producto para buscarlo."}
              </td></tr>
            )}
          </tbody>
        </table>
        {articuloSel && (
          <div className="field-row">
            <span><span className="mono">{articuloSel.codigoInterno}</span> · {articuloSel.descripcion}</span>
            <label>Recargo %
              <input type="number" step="0.01" value={porcentajeArticulo ?? ""}
                onChange={(e) => setPorcentajeArticulo(e.target.value === "" ? null : Number(e.target.value))}
                style={{ width: 100 }} />
            </label>
            <button className="primary" disabled={porcentajeArticulo === null} onClick={agregarPorArticulo}>
              Agregar
            </button>
            <button onClick={() => setArticuloSel(null)}>Cancelar</button>
          </div>
        )}
      </div>

      <h3>Diferenciales cargados</h3>
      <table className="grid">
        <thead>
          <tr><th>Alcance</th><th className="money">Recargo %</th><th></th></tr>
        </thead>
        <tbody>
          {diferenciales.map((d) => (
            <tr key={d.idDiferencial}>
              <td>
                {d.idArticulo
                  ? <>Artículo: <span className="mono">{d.articuloCodigoInterno}</span> {d.articuloDescripcion}</>
                  : <>Línea: {d.lineaDescripcion}</>}
              </td>
              <td className="money">
                {editandoId === d.idDiferencial
                  ? <input type="number" step="0.01" value={porcentajeEditado ?? ""} autoFocus
                      onChange={(e) => setPorcentajeEditado(e.target.value === "" ? null : Number(e.target.value))}
                      style={{ width: 90 }} />
                  : `${d.porcentaje.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`}
              </td>
              <td className="row-actions">
                {editandoId === d.idDiferencial ? (
                  <>
                    <button className="primary" onClick={() => guardarEdicion(d)}>Guardar</button>
                    <button onClick={() => setEditandoId(null)}>Cancelar</button>
                  </>
                ) : (
                  <>
                    <button className="icon-btn" title="Editar" aria-label="Editar" onClick={() => empezarEdicion(d)}><IconEditar /></button>
                    <button className="icon-btn icon-danger" title="Eliminar" aria-label="Eliminar"
                      onClick={() => eliminar(d)}><IconEliminar /></button>
                  </>
                )}
              </td>
            </tr>
          ))}
          {diferenciales.length === 0 && <tr><td colSpan={3} className="muted">Sin diferenciales cargados.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}

const VACIO_RECARGO: RecargoLogisticaInput = { inicio: 0, fin: 0, porcentaje: 0 };

/**
 * Tramos de recargo logístico de Preventa Mayorista (por rango de importe, Fin=-1 = sin límite
 * superior): lo aplica PreventaMayoristaService.CalcularRecargoLogistica sobre la suma de líneas
 * en "Entrega" de un pedido. Se puede cargar a mano acá o traer de una sola vez con "Actualizar
 * desde DBF" (recargo_logistica.dbf, reemplaza todos los tramos).
 */
function RecargoLogisticaEditor({ onBack }: { onBack: () => void }) {
  const [tramos, setTramos] = useState<RecargoLogistica[]>([]);
  const [form, setForm] = useState<RecargoLogisticaInput | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [importando, setImportando] = useState(false);

  const cargar = async () => {
    setError(null);
    try { setTramos(await recargoLogistica.list()); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  useEffect(() => { void cargar(); }, []);

  const nuevo = () => { setEditId(null); setForm({ ...VACIO_RECARGO }); };
  const editar = (t: RecargoLogistica) => {
    setEditId(t.idRecargoLogistica);
    setForm({ inicio: t.inicio, fin: t.fin, porcentaje: t.porcentaje });
  };

  const guardar = async () => {
    if (!form) return;
    setError(null);
    try {
      if (editId) await recargoLogistica.update(editId, form);
      else await recargoLogistica.create(form);
      setForm(null); setEditId(null);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const eliminar = async (id: number) => {
    if (!confirm("¿Eliminar este tramo?")) return;
    try { await recargoLogistica.remove(id); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const actualizarDesdeDbf = async () => {
    if (!confirm("Esto reemplaza TODOS los tramos actuales por lo que haya en recargo_logistica.dbf. ¿Continuar?")) return;
    setImportando(true); setError(null); setAviso(null);
    try {
      const cantidad = await recargoLogistica.importar();
      setAviso(`Actualizados ${cantidad} tramos.`);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setImportando(false); }
  };

  const set = (patch: Partial<RecargoLogisticaInput>) => setForm((f) => (f ? { ...f, ...patch } : f));

  return (
    <div>
      <div className="page-head">
        <h1>Recargo logística <span className="muted">(Preventa Mayorista)</span></h1>
        <div className="row-actions">
          <button disabled={importando} onClick={actualizarDesdeDbf}>
            {importando ? "Actualizando…" : "Actualizar desde DBF"}
          </button>
          <button className="primary" onClick={nuevo}>Nuevo tramo</button>
          <button onClick={onBack}>← Volver a listas</button>
        </div>
      </div>
      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      {importando && (
        <div className="modal-fondo">
          <div className="modal-caja" style={{ width: "min(320px, 100%)" }}>
            <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: "24px 0" }}>
              <div className="spinner" aria-hidden="true" />
              <p style={{ margin: 0 }}>Actualizando…</p>
            </div>
          </div>
        </div>
      )}

      {form && (
        <div className="card form">
          <h3>{editId ? "Editar tramo" : "Nuevo tramo"}</h3>
          <div className="field-row">
            <label>Desde (importe)
              <input type="number" step="0.01" value={form.inicio}
                onChange={(e) => set({ inicio: Number(e.target.value) })} style={{ width: 120 }} />
            </label>
            <label>Hasta (importe, -1 = sin límite)
              <input type="number" step="0.01" value={form.fin}
                onChange={(e) => set({ fin: Number(e.target.value) })} style={{ width: 120 }} />
            </label>
            <label>Recargo %
              <input type="number" step="0.01" value={form.porcentaje}
                onChange={(e) => set({ porcentaje: Number(e.target.value) })} style={{ width: 100 }} />
            </label>
            <button className="primary" onClick={guardar}>Guardar</button>
            <button onClick={() => { setForm(null); setEditId(null); }}>Cancelar</button>
          </div>
        </div>
      )}

      <table className="grid">
        <thead>
          <tr><th className="money">Desde</th><th className="money">Hasta</th><th className="money">Recargo %</th><th></th></tr>
        </thead>
        <tbody>
          {tramos.map((t) => (
            <tr key={t.idRecargoLogistica}>
              <td className="money">{formatearMoneda(t.inicio)}</td>
              <td className="money">{t.fin === -1 ? "Sin límite" : formatearMoneda(t.fin)}</td>
              <td className="money">
                {t.porcentaje.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%
              </td>
              <td className="row-actions">
                <button className="icon-btn" title="Editar" aria-label="Editar" onClick={() => editar(t)}><IconEditar /></button>
                <button className="icon-btn icon-danger" title="Eliminar" aria-label="Eliminar"
                  onClick={() => eliminar(t.idRecargoLogistica)}><IconEliminar /></button>
              </td>
            </tr>
          ))}
          {tramos.length === 0 && <tr><td colSpan={4} className="muted">Sin tramos cargados.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
