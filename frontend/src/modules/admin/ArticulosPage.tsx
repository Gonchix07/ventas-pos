import { useEffect, useMemo, useState } from "react";
import {
  articulos, familias as familiasApi, lookups, referencias,
  type ArticuloListItem, type ArticuloInput, type Familia, type Lookup, type Presentacion,
  type ComparadorArticulos,
} from "../../shared/api/admin";
import { IconEditar, IconBaja } from "../../shared/ui/icons";

// Debe coincidir con ArticuloService.MaxResultados (backend).
const MAX_RESULTADOS = 50;

const VACIO: ArticuloInput = {
  codigoInterno: "", descripcion: "",
  idSector: 0, idLinea: 0, idFamilia: 0, idModoIva: 0,
  activo: true, unidadMedida: 0, contenidoNetoUnitario: null, unidadXBulto: 1, ventaPorPeso: false,
  minimaUnidadVenta: 1,
  presentaciones: [],
};

const UNIDADES_MEDIDA = [
  { v: 0, l: "(ninguna)" },
  { v: 1, l: "Kilogramo" },
  { v: 2, l: "Litro" },
];

const nuevaPresentacion = (): Presentacion => ({
  unidadXBulto: 1, descripcionTicket: "", barras: [],
});

const formatearFechaHora = (iso?: string | null) => {
  if (!iso) return null;
  const d = new Date(iso);
  return `${d.toLocaleDateString("es-AR")} ${d.toLocaleTimeString("es-AR", { hour12: false, hour: "2-digit", minute: "2-digit" })}`;
};

export function ArticulosPage() {
  const [items, setItems] = useState<ArticuloListItem[]>([]);
  const [sectores, setSectores] = useState<Lookup[]>([]);
  const [lineas, setLineas] = useState<Lookup[]>([]);
  const [familias, setFamilias] = useState<Familia[]>([]);
  const [modosIva, setModosIva] = useState<Lookup[]>([]);
  const [form, setForm] = useState<ArticuloInput | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  // La imagen no viaja en ArticuloInput (el detalle no la trae, solo el listado): se guarda la del
  // renglón sobre el que se apretó "Editar", nada más que para mostrarla junto al formulario.
  const [formImagenUrl, setFormImagenUrl] = useState<string | null>(null);
  // Igual que formImagenUrl: no viaja en ArticuloInput (eso es lo que se manda al guardar), así que
  // se guarda aparte solo para mostrarlo junto al formulario.
  const [formUltimaSyncErp, setFormUltimaSyncErp] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [vistaImportar, setVistaImportar] = useState(false);

  // ---- Filtros del listado (se resuelven en el backend, ver ArticuloFiltro) ----
  const [texto, setTexto] = useState("");
  const [fSector, setFSector] = useState<number | 0>(0);
  const [fLinea, setFLinea] = useState<number | 0>(0);
  const [fFamilia, setFFamilia] = useState<number | 0>(0);
  // Arranca filtrando solo los activos: es lo que se busca en el día a día, y así no hay que
  // aplicar el filtro a mano cada vez que se entra a la pantalla.
  const [fActivo, setFActivo] = useState<"" | "1" | "0">("1");
  const [buscando, setBuscando] = useState(false);

  const cargar = async () => {
    setError(null);
    setBuscando(true);
    try {
      setItems(await articulos.list({
        texto: texto.trim() || undefined,
        idSector: fSector || undefined,
        idLinea: fLinea || undefined,
        idFamilia: fFamilia || undefined,
        activo: fActivo === "" ? undefined : fActivo === "1",
      }));
    }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setBuscando(false); }
  };

  useEffect(() => {
    lookups.list("sectores").then(setSectores).catch(() => {});
    lookups.list("lineas").then(setLineas).catch(() => {});
    familiasApi.list().then(setFamilias).catch(() => {});
    referencias.modosIva().then(setModosIva).catch(() => {});
  }, []);

  // La familia cuelga de un sector: los combos muestran solo las del sector elegido. Las familias
  // sin sector (el cajón "SIN FAMILIA") se ofrecen siempre, porque valen para cualquier sector.
  const familiasDe = (idSector: number) =>
    familias.filter((f) => f.idSector == null || !idSector || f.idSector === idSector);

  const familiasFiltro = useMemo(() => familiasDe(fSector), [familias, fSector]);
  const familiasForm = useMemo(() => familiasDe(form?.idSector ?? 0), [familias, form?.idSector]);

  // Si el sector cambia y la familia elegida no es de ese sector, se limpia para no filtrar por una
  // combinación imposible (que daría 0 resultados sin explicar por qué).
  useEffect(() => {
    if (fFamilia && !familiasFiltro.some((f) => f.id === fFamilia)) setFFamilia(0);
  }, [familiasFiltro, fFamilia]);

  // Debounce del texto para no pegarle al backend en cada tecla; los selects filtran al instante.
  useEffect(() => {
    const t = setTimeout(() => { void cargar(); }, texto ? 300 : 0);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [texto, fSector, fLinea, fFamilia, fActivo]);

  const limpiarFiltros = () => {
    setTexto(""); setFSector(0); setFLinea(0); setFFamilia(0); setFActivo("1");
  };
  const hayFiltros = texto !== "" || fSector !== 0 || fLinea !== 0 || fFamilia !== 0 || fActivo !== "1";

  const nuevo = () => {
    setEditId(null);
    setFormImagenUrl(null);
    setFormUltimaSyncErp(null);
    const idSector = sectores[0]?.id ?? 0;
    setForm({
      ...VACIO,
      idSector, idLinea: lineas[0]?.id ?? 0,
      idFamilia: familiasDe(idSector)[0]?.id ?? 0, idModoIva: modosIva[0]?.id ?? 0,
      presentaciones: [nuevaPresentacion()],
    });
  };

  // Al cambiar el sector del artículo hay que reelegir la familia: la que estaba puede ser de otro
  // sector (el backend lo rechaza con FAMILIA_DE_OTRO_SECTOR).
  const cambiarSector = (idSector: number) => {
    const opciones = familiasDe(idSector);
    setForm((f) => f ? {
      ...f, idSector,
      idFamilia: opciones.some((o) => o.id === f.idFamilia) ? f.idFamilia : (opciones[0]?.id ?? 0),
    } : f);
  };

  const editar = async (id: number) => {
    setError(null);
    try {
      const a = await articulos.get(id);
      setEditId(id);
      setFormImagenUrl(items.find((i) => i.idArticulo === id)?.imagenUrl ?? null);
      setFormUltimaSyncErp(a.ultimaSincronizacionErpUtc ?? null);
      setForm({
        codigoInterno: a.codigoInterno, descripcion: a.descripcion,
        idSector: a.idSector, idLinea: a.idLinea, idFamilia: a.idFamilia, idModoIva: a.idModoIva,
        activo: a.activo, unidadMedida: a.unidadMedida, contenidoNetoUnitario: a.contenidoNetoUnitario,
        unidadXBulto: a.unidadXBulto, ventaPorPeso: a.ventaPorPeso, minimaUnidadVenta: a.minimaUnidadVenta,
        presentaciones: a.presentaciones.length ? a.presentaciones : [nuevaPresentacion()],
      });
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const guardar = async () => {
    if (!form) return;
    setError(null);
    try {
      if (editId) await articulos.update(editId, form);
      else await articulos.create(form);
      setForm(null); setEditId(null);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const eliminar = async (id: number) => {
    if (!confirm("¿Dar de baja el artículo?")) return;
    try { await articulos.remove(id); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const set = (patch: Partial<ArticuloInput>) => setForm((f) => (f ? { ...f, ...patch } : f));
  const setPres = (i: number, patch: Partial<Presentacion>) =>
    setForm((f) => f ? { ...f, presentaciones: f.presentaciones.map((p, idx) => idx === i ? { ...p, ...patch } : p) } : f);

  if (vistaImportar) {
    return <ImportarArticulosDbf onBack={() => { setVistaImportar(false); void cargar(); }} />;
  }

  return (
    <div>
      <div className="page-head">
        <h1>Artículos</h1>
        <div className="row-actions">
          <button onClick={() => setVistaImportar(true)}>Importar artículos nuevos (DBF)</button>
          <button className="primary" onClick={nuevo}>Nuevo artículo</button>
        </div>
      </div>

      {error && <p className="error">{error}</p>}

      {form && (
        <div className="card form">
          <h3>{editId ? "Editar artículo" : "Nuevo artículo"}</h3>
          {editId && (
            <p className="muted">
              {formatearFechaHora(formUltimaSyncErp)
                ? `Última sincronización con el ERP: ${formatearFechaHora(formUltimaSyncErp)}`
                : "Nunca sincronizado con el ERP (cargado a mano)"}
            </p>
          )}
          <div className="form-con-imagen">
            {formImagenUrl && (
              <img className="form-imagen" src={formImagenUrl} alt=""
                onError={(e) => (e.currentTarget.style.visibility = "hidden")} />
            )}
            <div style={{ flex: 1 }}>
            <div className="form-grid">
            <label>Código interno<input value={form.codigoInterno} onChange={(e) => set({ codigoInterno: e.target.value })} /></label>
            <label>Descripción<input value={form.descripcion} onChange={(e) => set({ descripcion: e.target.value })} /></label>
            <label>Sector
              <select value={form.idSector} onChange={(e) => cambiarSector(Number(e.target.value))}>
                {sectores.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Línea
              <select value={form.idLinea} onChange={(e) => set({ idLinea: Number(e.target.value) })}>
                {lineas.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Familia (del sector elegido)
              <select value={form.idFamilia} onChange={(e) => set({ idFamilia: Number(e.target.value) })}>
                {familiasForm.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Modo IVA
              <select value={form.idModoIva} onChange={(e) => set({ idModoIva: Number(e.target.value) })}>
                {modosIva.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            </div>
            {/* Fila propia, más angosta que el form-grid (que fuerza mínimo 220px por columna y
                acá solo entrarían 3): así los 4 campos quedan en una sola línea. */}
            <div className="field-row" style={{ marginTop: 12 }}>
              <label style={{ width: 150 }}>Unidad de medida
                <select value={form.unidadMedida} onChange={(e) => set({ unidadMedida: Number(e.target.value) })}>
                  {UNIDADES_MEDIDA.map((u) => <option key={u.v} value={u.v}>{u.l}</option>)}
                </select>
              </label>
              {form.unidadMedida !== 0 && (
                <label style={{ width: 110 }}>Contenido
                  <input type="number" step="0.001" value={form.contenidoNetoUnitario ?? ""}
                    onChange={(e) => set({ contenidoNetoUnitario: e.target.value === "" ? null : Number(e.target.value) })} />
                </label>
              )}
              <label style={{ width: 110 }}>Unidad por Bulto
                <input type="number" min={1} step="1" value={form.unidadXBulto}
                  onChange={(e) => set({ unidadXBulto: Number(e.target.value) || 1 })} />
              </label>
              <label style={{ width: 150 }}>Mínima Unidad de Venta
                <input type="number" min={1} step="1" value={form.minimaUnidadVenta}
                  onChange={(e) => set({ minimaUnidadVenta: Number(e.target.value) || 1 })} />
              </label>
            </div>
            <div className="field-row" style={{ marginTop: 12 }}>
              <label className="check-box">
                <input type="checkbox" checked={form.activo} onChange={(e) => set({ activo: e.target.checked })} />
                Activo
              </label>
              <label className="check-box">
                <input type="checkbox" checked={form.ventaPorPeso}
                  onChange={(e) => set({ ventaPorPeso: e.target.checked })} />
                Venta por peso (balanza)
              </label>
            </div>
            </div>
          </div>

          <div className="presentaciones">
            <div className="page-head">
              <h4>Presentaciones</h4>
              <button onClick={() => set({ presentaciones: [...form.presentaciones, nuevaPresentacion()] })}>+ Presentación</button>
            </div>
            {form.presentaciones.map((p, i) => (
              <div key={i} className="pres-card">
                <div className="form-grid">
                  <label>Unidad × bulto<input type="number" min={1} value={p.unidadXBulto} onChange={(e) => setPres(i, { unidadXBulto: Number(e.target.value) })} /></label>
                  <label>Descripción ticket<input value={p.descripcionTicket ?? ""} onChange={(e) => setPres(i, { descripcionTicket: e.target.value })} /></label>
                  <button className="danger" onClick={() => set({ presentaciones: form.presentaciones.filter((_, idx) => idx !== i) })}>Quitar presentación</button>
                </div>
                <div className="barras">
                  <div className="page-head">
                    <strong>Códigos de barra</strong>
                    <button onClick={() => setPres(i, { barras: [...p.barras, { codigoBarra: "", tipo: 1 }] })}>+ Barra</button>
                  </div>
                  {p.barras.map((b, j) => (
                    <div key={j} className="barra-row">
                      <input placeholder="Código de barra" value={b.codigoBarra}
                        onChange={(e) => setPres(i, { barras: p.barras.map((x, k) => k === j ? { ...x, codigoBarra: e.target.value } : x) })} />
                      <select value={b.tipo}
                        onChange={(e) => setPres(i, { barras: p.barras.map((x, k) => k === j ? { ...x, tipo: Number(e.target.value) } : x) })}>
                        <option value={1}>EAN13</option>
                        <option value={2}>DUN14</option>
                      </select>
                      <button className="danger" onClick={() => setPres(i, { barras: p.barras.filter((_, k) => k !== j) })}>×</button>
                    </div>
                  ))}
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

      <div className="filter-bar filtros-articulos">
        <label className="grow">Buscar (código, descripción o código de barra)
          <input value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Escribí o escaneá un producto…" />
        </label>
        <label>Línea
          <select value={fLinea} onChange={(e) => setFLinea(Number(e.target.value))}>
            <option value={0}>(todas)</option>
            {lineas.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
        <label>Sector
          <select value={fSector} onChange={(e) => setFSector(Number(e.target.value))}>
            <option value={0}>(todos)</option>
            {sectores.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
        <label>Familia
          <select value={fFamilia} onChange={(e) => setFFamilia(Number(e.target.value))}>
            <option value={0}>(todas)</option>
            {/* Sin sector elegido hay nombres repetidos (DESODORANTES está en 2 sectores), así que
                se antepone el sector para poder distinguirlos. */}
            {familiasFiltro.map((s) => (
              <option key={s.id} value={s.id}>
                {fSector || !s.sectorDescripcion ? s.descripcion : `${s.sectorDescripcion} · ${s.descripcion}`}
              </option>
            ))}
          </select>
        </label>
        <label>Estado
          <select value={fActivo} onChange={(e) => setFActivo(e.target.value as "" | "1" | "0")}>
            <option value="">(todos)</option>
            <option value="1">Activos</option>
            <option value="0">Dados de baja</option>
          </select>
        </label>
        {hayFiltros && <button onClick={limpiarFiltros}>Limpiar</button>}
      </div>
      <p className="resultado-count">
        {buscando ? "Buscando…" : `${items.length} artículo${items.length === 1 ? "" : "s"}${items.length === MAX_RESULTADOS ? " (máx.) — refiná la búsqueda" : ""}`}
      </p>

      <div className="table-scroll">
        <table className="grid tabla-compacta">
          <thead>
            <tr>
              <th style={{ width: 70 }}>Imagen</th><th>Código</th><th>Descripción</th>
              <th>Clasificación</th><th>IVA</th>
              <th>Estado</th><th></th>
            </tr>
          </thead>
          <tbody>
            {items.map((a) => (
              <tr key={a.idArticulo} className={a.activo ? "" : "inactive"}>
                <td><img className="thumb" src={a.imagenUrl} alt="" onError={(e) => (e.currentTarget.style.visibility = "hidden")} /></td>
                <td className="mono">{a.codigoInterno}</td>
                <td className="stack">
                  {a.descripcion}
                  {a.unidadMedida !== 0 && (
                    <small>
                      {a.contenidoNetoUnitario ?? "?"} {UNIDADES_MEDIDA.find((u) => u.v === a.unidadMedida)?.l ?? ""}
                    </small>
                  )}
                </td>
                <td className="stack">
                  {a.sectorDescripcion ?? "—"}
                  <small>{[a.lineaDescripcion, a.familiaDescripcion].filter(Boolean).join(" · ") || "—"}</small>
                </td>
                <td>{a.modoIvaDescripcion ?? "—"}</td>
                <td>{a.activo ? <span className="badge on">Activo</span> : <span className="badge off">Baja</span>}</td>
                <td className="row-actions">
                  <button className="icon-btn" title="Editar" aria-label="Editar" onClick={() => editar(a.idArticulo)}><IconEditar /></button>
                  <button className="icon-btn icon-danger" title="Dar de baja" aria-label="Dar de baja"
                    onClick={() => eliminar(a.idArticulo)}><IconBaja /></button>
                </td>
              </tr>
            ))}
            {items.length === 0 && (
              <tr><td colSpan={7} className="muted">
                {hayFiltros ? "Ningún artículo coincide con los filtros." : "Sin artículos."}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ---- Comparador de diferencias / import de artículos nuevos desde articulo.dbf + cbarras.dbf ----
function ImportarArticulosDbf({ onBack }: { onBack: () => void }) {
  const [comparador, setComparador] = useState<ComparadorArticulos | null>(null);
  const [cargando, setCargando] = useState(false);
  const [importando, setImportando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const cargar = async () => {
    setError(null); setCargando(true);
    try { setComparador(await articulos.nuevosDbf()); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setCargando(false); }
  };

  useEffect(() => { void cargar(); }, []);

  const importables = comparador?.importables ?? 0;

  const importar = async () => {
    if (!confirm(`¿Importar ${importables} artículo(s) nuevo(s) con sus presentaciones y códigos de barra? ` +
      `(de ${comparador?.total ?? 0} detectados). Los ya cargados no se tocan.`)) return;
    setImportando(true); setError(null); setAviso(null);
    try {
      const r = await articulos.importarNuevosDbf();
      setAviso(`Importados ${r.articulos} artículo(s), ${r.presentaciones} presentaciones y ${r.barras} códigos de barra` +
        (r.noImportables > 0 ? ` · ${r.noImportables} sin línea/IVA equivalente quedaron afuera.` : "."));
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setImportando(false); }
  };

  return (
    <div>
      <div className="page-head">
        <h1>Importar artículos nuevos (DBF)</h1>
        <div className="row-actions">
          <button className="primary" disabled={importando || cargando || importables === 0} onClick={importar}>
            Importar {comparador ? `(${importables})` : ""}
          </button>
          <button onClick={onBack}>← Volver a artículos</button>
        </div>
      </div>
      <p className="muted">
        Artículos que están en articulo.dbf (app de preventa) pero todavía no existen acá por código. No se
        toca ningún artículo ya cargado: solo se dan de alta los nuevos, con su presentación unitaria, la de
        bulto (si viene en bulto) y los códigos de barra de cbarras.dbf (EAN en la unidad, DUN en el bulto).
        Los que no tienen sector o familia equivalente quedan en "SIN SECTOR" / "SIN FAMILIA"; los que no
        tienen línea o IVA equivalente no se importan.
      </p>
      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      {(cargando || importando) && (
        <div className="modal-fondo">
          <div className="modal-caja" style={{ width: "min(320px, 100%)" }}>
            <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: "24px 0" }}>
              <div className="spinner" aria-hidden="true" />
              <p style={{ margin: 0 }}>{importando ? "Importando…" : "Comparando…"}</p>
            </div>
          </div>
        </div>
      )}

      {comparador !== null && (
        <>
          <p className="resultado-count">
            {`${comparador.total} artículo${comparador.total === 1 ? "" : "s"} nuevo${comparador.total === 1 ? "" : "s"}` +
              ` · ${importables} importable${importables === 1 ? "" : "s"}` +
              ` · ${comparador.total - importables} sin línea/IVA equivalente (no se importan)`}
            {comparador.items.length < comparador.total && ` · mostrando ${comparador.items.length}`}
          </p>
          <div className="table-scroll">
            <table className="grid tabla-compacta">
              <thead>
                <tr>
                  <th>Código</th><th>Descripción</th><th>Sector</th><th>Línea</th><th>Familia</th>
                  <th>IVA</th><th>Un. x bulto</th><th>Barras</th><th>Estado</th>
                </tr>
              </thead>
              <tbody>
                {comparador.items.map((a) => (
                  <tr key={a.codigoInterno} className={a.motivo ? "inactive" : ""} title={a.motivo ?? undefined}>
                    <td className="mono">{a.codigoInterno}</td>
                    <td>{a.descripcion}</td>
                    <td>{a.sector ?? <span className="muted">—</span>}</td>
                    <td>{a.linea ?? <span className="muted">—</span>}</td>
                    <td>{a.familia ?? <span className="muted">—</span>}</td>
                    <td>{a.modoIva ?? <span className="muted">—</span>}</td>
                    <td className="money">{a.unidadXBulto}</td>
                    <td className="money">{a.cantidadBarras}</td>
                    <td>{a.motivo ? <span className="error">{a.motivo}</span> : a.activo ? "Activo" : "Inactivo"}</td>
                  </tr>
                ))}
                {comparador.items.length === 0 && <tr><td colSpan={9} className="muted">Sin artículos nuevos.</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
