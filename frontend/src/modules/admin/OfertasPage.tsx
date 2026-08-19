import { useEffect, useState } from "react";
import {
  ofertas, referencias, lookups, clusters as clustersApi, familias as familiasApi, articulos as articulosApi,
  TipoOfertaCodigo, RolItemCanasta,
  type OfertaListItem, type OfertaInput, type Lookup, type Accion, type Alcance, type Cluster,
  type Familia, type TipoOferta, type ItemCanasta, type ArticuloListItem,
} from "../../shared/api/admin";

// Fecha en hora LOCAL: con toISOString() (UTC) después de las 21 hs la oferta nueva arrancaba
// mañana y no aplicaba en el día, que es justo cuando el operador la carga para usarla ya.
const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const hoy = () => iso(new Date());
const enUnMes = () => { const d = new Date(); d.setMonth(d.getMonth() + 1); return iso(d); };

const accionVacia = (idTipo: number): Accion => ({ idTipoOferta: idTipo, idPresentacion: null, porcentaje: null, montoFijo: null, cantidadMin: null, cantidadBonif: null, items: [] });
const alcanceVacio = (): Alcance => ({ idCluster: null, idLinea: null, idSector: null, idFamilia: null, idArticulo: null, esExcepcion: false });

/** Qué explica cada tipo en el alta (el comportamiento vive en el motor, no se configura). */
const AYUDA: Record<number, string> = {
  [TipoOfertaCodigo.Descuento]: "Aplica el porcentaje a todas las líneas del carrito que caigan dentro del alcance (artículos sueltos, un sector o una familia completa).",
  [TipoOfertaCodigo.DosPorUno]: "Por cada 2 unidades iguales de un artículo del alcance, la 2ª se bonifica al 100%.",
  [TipoOfertaCodigo.SegundaUnidad]: "Por cada 2 unidades iguales de un artículo del alcance, la 2ª se bonifica en el porcentaje indicado.",
  [TipoOfertaCodigo.MixCanasta]: "Dos canastas: si el carrito cumple entera la que activa, se bonifica al 100% la canasta bonificada (pueden ser artículos distintos). Se repite tantas veces como la canasta que activa entre en el carrito, y solo se bonifican unidades que el cliente efectivamente lleve.",
  [TipoOfertaCodigo.Bonificacion]: "Oferta vieja «lleva N + M, paga N». Ya no se ofrece para ofertas nuevas.",
};

/** Buscador de artículo por código, descripción o barra (el mismo filtro que el ABM de Artículos). */
function BuscadorArticulo({ idArticulo, descripcion, onElegir, onLimpiar }: {
  idArticulo: number | null | undefined;
  descripcion?: string | null;
  onElegir: (a: ArticuloListItem) => void;
  onLimpiar: () => void;
}) {
  const [texto, setTexto] = useState("");
  const [opciones, setOpciones] = useState<ArticuloListItem[]>([]);
  const [buscando, setBuscando] = useState(false);

  useEffect(() => {
    if (idArticulo) return;
    const t = texto.trim();
    if (t.length < 2) { setOpciones([]); return; }
    // Con debounce: el filtro pega contra la BD y el operador tipea de a poco.
    const timer = setTimeout(() => {
      setBuscando(true);
      articulosApi.list({ texto: t, activo: true, max: 10 })
        .then(setOpciones).catch(() => setOpciones([])).finally(() => setBuscando(false));
    }, 300);
    return () => clearTimeout(timer);
  }, [texto, idArticulo]);

  if (idArticulo) {
    // Elegido: el campo queda ocupado por el código (así se ve que HAY algo seleccionado) y
    // la descripción completa va debajo, sin recortar, para poder confirmar qué se eligió.
    const [codigo, ...resto] = (descripcion ?? "").split(" · ");
    const detalle = resto.join(" · ");
    return (
      <div className="art-box">
        <div className="art-elegido">
          <span className="grow">{codigo || `Artículo #${idArticulo}`}</span>
          <button onClick={() => { setTexto(""); setOpciones([]); onLimpiar(); }}>Cambiar</button>
        </div>
        {detalle && <p className="art-desc">{detalle}</p>}
      </div>
    );
  }

  return (
    <div className="art-box">
      <input value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Código, descripción o barra…" />
      {buscando && <p className="muted">Buscando…</p>}
      {!buscando && opciones.length > 0 && (
        <div className="picker">
          {opciones.map((a) => (
            // onMouseDown y no onClick: el mousedown gana a cualquier blur/re-render de la lista,
            // que si no puede tragarse el click y dejar la búsqueda como si no se hubiera elegido nada.
            <div key={a.idArticulo} className="picker-row"
              onMouseDown={(e) => { e.preventDefault(); setTexto(""); setOpciones([]); onElegir(a); }}>
              <span className="grow">{a.codigoInterno} · {a.descripcion}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function OfertasPage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [suc, setSuc] = useState(0);
  const [items, setItems] = useState<OfertaListItem[]>([]);
  const [tipos, setTipos] = useState<TipoOferta[]>([]);
  const [sectores, setSectores] = useState<Lookup[]>([]);
  const [lineas, setLineas] = useState<Lookup[]>([]);
  const [familias, setFamilias] = useState<Familia[]>([]);
  const [cls, setCls] = useState<Cluster[]>([]);
  const [form, setForm] = useState<OfertaInput | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    referencias.sucursales().then((s) => { setSucursales(s); if (s.length) setSuc(s[0].id); }).catch(() => {});
    ofertas.tipos().then(setTipos).catch(() => {});
    lookups.list("sectores").then(setSectores).catch(() => {});
    lookups.list("lineas").then(setLineas).catch(() => {});
    familiasApi.list().then(setFamilias).catch(() => {});
    clustersApi.list().then(setCls).catch(() => {});
  }, []);

  const codigoDe = (idTipo: number) => tipos.find((t) => t.id === idTipo)?.codigo ?? 0;

  // El alcance por familia se acota al sector del mismo alcance: elegir "PERFUMERIA + DESODORANTES"
  // y que el combo ofreciera el DESODORANTES de LIMPIEZA sería una oferta que nunca aplica.
  const familiasDe = (idSector?: number | null) =>
    familias.filter((f) => f.idSector == null || !idSector || f.idSector === idSector);

  const cambiarSectorAlcance = (i: number, idSector: number | null) => {
    const opciones = familiasDe(idSector);
    setForm((f) => f ? {
      ...f,
      alcances: f.alcances.map((a, idx) => idx !== i ? a : {
        ...a, idSector,
        idFamilia: a.idFamilia && !opciones.some((o) => o.id === a.idFamilia) ? null : a.idFamilia,
      }),
    } : f);
  };

  const cargar = async (s: number) => {
    if (!s) return;
    setError(null);
    try { setItems(await ofertas.list(s)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(suc); /* eslint-disable-next-line */ }, [suc]);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(suc); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const nuevo = () => {
    setEditId(null);
    setForm({
      descripcion: "", fechaInicio: hoy(), fechaFin: enUnMes(), acumula: false, permiteConvenio: true,
      acciones: [accionVacia(tipos[0]?.id ?? 1)], alcances: [alcanceVacio()],
    });
  };

  const editar = async (idOferta: number) => {
    setError(null);
    try {
      const d = await ofertas.get(suc, idOferta);
      setEditId(idOferta);
      setForm({
        descripcion: d.descripcion,
        // El backend devuelve fecha completa; los <input type="date"> quieren yyyy-MM-dd.
        fechaInicio: d.fechaInicio.slice(0, 10), fechaFin: d.fechaFin.slice(0, 10),
        acumula: d.acumula, permiteConvenio: d.permiteConvenio,
        alcances: d.alcances.map((a) => ({ ...a })),
        acciones: d.acciones.map((a) => ({ ...a, items: (a.items ?? []).map((i) => ({ ...i })) })),
      });
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const set = (patch: Partial<OfertaInput>) => setForm((f) => (f ? { ...f, ...patch } : f));
  const setAccion = (i: number, patch: Partial<Accion>) =>
    setForm((f) => f ? { ...f, acciones: f.acciones.map((a, idx) => idx === i ? { ...a, ...patch } : a) } : f);
  const setAlcance = (i: number, patch: Partial<Alcance>) =>
    setForm((f) => f ? { ...f, alcances: f.alcances.map((a, idx) => idx === i ? { ...a, ...patch } : a) } : f);

  // Cambiar de tipo limpia lo que no corresponde: cada tipo pide campos distintos y dejar
  // residuos de otro tipo es lo que hace que una oferta aplique donde no se esperaba.
  const cambiarTipo = (i: number, idTipoOferta: number) => {
    const codigo = codigoDe(idTipoOferta);
    setAccion(i, {
      idTipoOferta,
      porcentaje: codigo === TipoOfertaCodigo.SegundaUnidad ? 70 : null,
      montoFijo: null, cantidadMin: null, cantidadBonif: null, items: [],
    });
  };

  const setItem = (iAccion: number, iItem: number, patch: Partial<ItemCanasta>) =>
    setForm((f) => f ? {
      ...f,
      acciones: f.acciones.map((a, idx) => idx !== iAccion ? a : {
        ...a, items: (a.items ?? []).map((it, j) => j === iItem ? { ...it, ...patch } : it),
      }),
    } : f);

  const setItemsCanasta = (iAccion: number, items: ItemCanasta[]) => setAccion(iAccion, { items });

  const num = (v: string): number | null => v === "" ? null : Number(v);

  const guardar = async () => {
    if (!form) return;
    await run(async () => {
      if (editId) await ofertas.update(suc, editId, form);
      else await ofertas.create(suc, form);
      setForm(null); setEditId(null);
    });
  };

  return (
    <div>
      <div className="page-head">
        <h1>Ofertas</h1>
        <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
          <label className="inline-label">Sucursal
            <select value={suc} onChange={(e) => setSuc(Number(e.target.value))}>
              {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
          <button className="primary" onClick={nuevo}>Nueva oferta</button>
        </div>
      </div>
      {error && <p className="error">{error}</p>}

      {form && (
        <div className="card form">
          <h3>{editId ? `Editar oferta #${editId}` : "Nueva oferta"}</h3>
          <div className="form-grid">
            <label>Descripción<input value={form.descripcion} onChange={(e) => set({ descripcion: e.target.value })} /></label>
            <label>Vigencia desde<input type="date" value={form.fechaInicio} onChange={(e) => set({ fechaInicio: e.target.value })} /></label>
            <label>Vigencia hasta<input type="date" value={form.fechaFin} onChange={(e) => set({ fechaFin: e.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={form.acumula} onChange={(e) => set({ acumula: e.target.checked })} /> Acumulable</label>
            <label className="check"><input type="checkbox" checked={form.permiteConvenio} onChange={(e) => set({ permiteConvenio: e.target.checked })} /> Permite convenio</label>
          </div>

          <div className="presentaciones">
            <div className="page-head"><h4>Alcances</h4>
              <button onClick={() => set({ alcances: [...form.alcances, alcanceVacio()] })}>+ Alcance</button>
            </div>
            <p className="muted">Dejá en «(todos)» los criterios que no apliquen. Sin alcances = toda la sucursal.
              Para una lista de artículos, agregá un alcance por artículo.</p>
            {form.alcances.map((a, i) => (
              <div key={i} className="pres-card">
                <div className="form-grid">
                  <label>Cluster
                    <select value={a.idCluster ?? 0} onChange={(e) => setAlcance(i, { idCluster: Number(e.target.value) || null })}>
                      <option value={0}>(todos)</option>
                      {cls.map((c) => <option key={c.idCluster} value={c.idCluster}>{c.descripcion}</option>)}
                    </select>
                  </label>
                  <label>Sector
                    <select value={a.idSector ?? 0} onChange={(e) => cambiarSectorAlcance(i, Number(e.target.value) || null)}>
                      <option value={0}>(todos)</option>
                      {sectores.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
                    </select>
                  </label>
                  <label>Línea
                    <select value={a.idLinea ?? 0} onChange={(e) => setAlcance(i, { idLinea: Number(e.target.value) || null })}>
                      <option value={0}>(todas)</option>
                      {lineas.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
                    </select>
                  </label>
                  <label>Familia
                    <select value={a.idFamilia ?? 0} onChange={(e) => setAlcance(i, { idFamilia: Number(e.target.value) || null })}>
                      <option value={0}>(todas)</option>
                      {familiasDe(a.idSector).map((s) => (
                        <option key={s.id} value={s.id}>
                          {a.idSector || !s.sectorDescripcion ? s.descripcion : `${s.sectorDescripcion} · ${s.descripcion}`}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>Artículo
                    <BuscadorArticulo
                      idArticulo={a.idArticulo} descripcion={a.articuloDescripcion}
                      onElegir={(art) => setAlcance(i, { idArticulo: art.idArticulo, articuloDescripcion: `${art.codigoInterno} · ${art.descripcion}` })}
                      onLimpiar={() => setAlcance(i, { idArticulo: null, articuloDescripcion: null })}
                    />
                  </label>
                  <label className="check"><input type="checkbox" checked={a.esExcepcion} onChange={(e) => setAlcance(i, { esExcepcion: e.target.checked })} /> Es excepción</label>
                  <button className="danger" onClick={() => set({ alcances: form.alcances.filter((_, idx) => idx !== i) })}>Quitar</button>
                </div>
              </div>
            ))}
          </div>

          <div className="presentaciones">
            <div className="page-head"><h4>Acciones</h4>
              <button onClick={() => set({ acciones: [...form.acciones, accionVacia(tipos[0]?.id ?? 1)] })}>+ Acción</button>
            </div>
            {form.acciones.map((a, i) => {
              const codigo = codigoDe(a.idTipoOferta);
              const canasta = a.items ?? [];
              return (
                <div key={i} className="pres-card">
                  <div className="form-grid">
                    <label>Tipo
                      <select value={a.idTipoOferta} onChange={(e) => cambiarTipo(i, Number(e.target.value))}>
                        {tipos.map((t) => <option key={t.id} value={t.id}>{t.descripcion}</option>)}
                      </select>
                    </label>
                    {codigo === TipoOfertaCodigo.Descuento && (
                      <label>Porcentaje %<input type="number" step="0.01" value={a.porcentaje ?? ""} onChange={(e) => setAccion(i, { porcentaje: num(e.target.value) })} /></label>
                    )}
                    {codigo === TipoOfertaCodigo.SegundaUnidad && (
                      <label>% bonificado de la 2ª unidad<input type="number" step="0.01" value={a.porcentaje ?? ""} onChange={(e) => setAccion(i, { porcentaje: num(e.target.value) })} /></label>
                    )}
                    {codigo === TipoOfertaCodigo.Bonificacion && (
                      <>
                        <label>Cant. mínima<input type="number" value={a.cantidadMin ?? ""} onChange={(e) => setAccion(i, { cantidadMin: num(e.target.value) })} /></label>
                        <label>Cant. bonificada<input type="number" value={a.cantidadBonif ?? ""} onChange={(e) => setAccion(i, { cantidadBonif: num(e.target.value) })} /></label>
                      </>
                    )}
                    <button className="danger" onClick={() => set({ acciones: form.acciones.filter((_, idx) => idx !== i) })}>Quitar</button>
                  </div>

                  <p className="muted">{AYUDA[codigo] ?? ""}</p>

                  {codigo === TipoOfertaCodigo.MixCanasta && (
                    <>
                      {[
                        { rol: RolItemCanasta.Condicion, titulo: "Canasta que activa la oferta", vacio: "Agregá los artículos que el cliente tiene que llevar." },
                        { rol: RolItemCanasta.Bonificado, titulo: "Canasta bonificada al 100%", vacio: "Agregá los artículos que se regalan al cumplirse la primera." },
                      ].map(({ rol, titulo, vacio }) => {
                        // La lista es una sola: cada renglón se ubica de un lado por su `rol`, pero
                        // se edita por su índice real dentro de items.
                        const renglones = canasta.map((it, j) => ({ it, j })).filter((x) => x.it.rol === rol);
                        return (
                          <div key={rol} className="canasta">
                            <div className="page-head"><h5>{titulo}</h5>
                              <button onClick={() => setItemsCanasta(i, [...canasta, { idArticulo: 0, cantidad: 1, rol }])}>+ Artículo</button>
                            </div>
                            {renglones.length === 0 && <p className="muted">{vacio}</p>}
                            {renglones.map(({ it, j }) => (
                              <div key={j} className="form-grid">
                                <label>Artículo
                                  <BuscadorArticulo
                                    idArticulo={it.idArticulo || null} descripcion={it.articuloDescripcion}
                                    onElegir={(art) => setItem(i, j, { idArticulo: art.idArticulo, articuloDescripcion: `${art.codigoInterno} · ${art.descripcion}` })}
                                    onLimpiar={() => setItem(i, j, { idArticulo: 0, articuloDescripcion: null })}
                                  />
                                </label>
                                <label>{rol === RolItemCanasta.Condicion ? "Cantidad requerida" : "Cantidad bonificada"}
                                  <input type="number" step="0.01" value={it.cantidad}
                                    onChange={(e) => setItem(i, j, { cantidad: Number(e.target.value) })} />
                                </label>
                                <button className="danger" onClick={() => setItemsCanasta(i, canasta.filter((_, k) => k !== j))}>Quitar</button>
                              </div>
                            ))}
                          </div>
                        );
                      })}
                    </>
                  )}
                </div>
              );
            })}
          </div>

          <div className="row-actions">
            <button className="primary" disabled={!form.descripcion.trim() || form.acciones.length === 0} onClick={guardar}>Guardar</button>
            <button onClick={() => { setForm(null); setEditId(null); }}>Cancelar</button>
          </div>
        </div>
      )}

      <table className="grid">
        <thead><tr><th>ID</th><th>Descripción</th><th>Vigencia</th><th>Acum.</th><th>Alcances</th><th>Acciones</th><th></th></tr></thead>
        <tbody>
          {items.map((o) => (
            <tr key={o.idOferta}>
              <td className="mono">{o.idOferta}</td>
              <td>{o.descripcion}</td>
              <td>{o.fechaInicio.slice(0, 10)} → {o.fechaFin.slice(0, 10)}</td>
              <td>{o.acumula ? "Sí" : "No"}</td>
              <td className="mono">{o.cantAlcances}</td>
              <td className="mono">{o.cantAcciones}</td>
              <td className="row-actions">
                <button onClick={() => editar(o.idOferta)}>Editar</button>
                <button className="danger" onClick={() => run(() => ofertas.remove(suc, o.idOferta))}>Eliminar</button>
              </td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan={7} className="muted">Sin ofertas.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
