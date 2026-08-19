import { useEffect, useState } from "react";
import { useAuth } from "../../shared/auth/auth";
import {
  etiquetas, type ArticuloParaEtiqueta, type Clasificaciones,
  type Etiqueta, type LookupSimple,
} from "../../shared/api/etiquetas";
import "./etiquetas-print.css";

type Formato = "Fleje" | "A4" | "A5";

const PAGE_SIZE: Record<Formato, string> = {
  Fleje: "90mm 40mm",
  A4: "A4",
  A5: "A5",
};

export function EtiquetasPage() {
  const { usuario, rol, logout, ip } = useAuth();
  const [sucursales, setSucursales] = useState<LookupSimple[]>([]);
  const [idSucursal, setIdSucursal] = useState<number>(0);
  const [clasif, setClasif] = useState<Clasificaciones | null>(null);
  const [idSector, setIdSector] = useState<number>(0);
  const [idLinea, setIdLinea] = useState<number>(0);
  const [idFamilia, setIdFamilia] = useState<number>(0);

  const [q, setQ] = useState("");
  const [resultados, setResultados] = useState<ArticuloParaEtiqueta[]>([]);
  const [lista, setLista] = useState<ArticuloParaEtiqueta[]>([]);
  const [formato, setFormato] = useState<Formato>("Fleje");
  const [generadas, setGeneradas] = useState<Etiqueta[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  useEffect(() => {
    etiquetas.sucursales().then((s) => { setSucursales(s); if (s.length) setIdSucursal(s[0].id); }).catch(() => {});
    etiquetas.clasificaciones().then(setClasif).catch(() => {});
  }, []);

  // La familia pertenece a un sector: al elegir sector el combo muestra solo sus familias (las que
  // no tienen sector, el cajón "SIN FAMILIA", se ofrecen siempre).
  const familiasDelSector = (clasif?.familias ?? [])
    .filter((f) => f.idSector == null || !idSector || f.idSector === idSector);

  useEffect(() => {
    if (idFamilia && !familiasDelSector.some((f) => f.id === idFamilia)) setIdFamilia(0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idSector, clasif]);

  const buscar = async () => {
    setError(null);
    if (!q.trim()) return;
    try { setResultados(await etiquetas.buscar(q.trim())); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const agregar = (a: ArticuloParaEtiqueta) => {
    setLista((l) => (l.some((x) => x.idPresentacion === a.idPresentacion) ? l : [...l, a]));
  };

  const agregarTodosPorClasificacion = async () => {
    setError(null);
    try {
      const items = await etiquetas.porClasificacion(idSector || undefined, idLinea || undefined, idFamilia || undefined);
      setLista((l) => {
        const existentes = new Set(l.map((x) => x.idPresentacion));
        return [...l, ...items.filter((i) => !existentes.has(i.idPresentacion))];
      });
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const quitar = (idPresentacion: number) => setLista((l) => l.filter((x) => x.idPresentacion !== idPresentacion));

  const generar = async () => {
    if (!idSucursal || lista.length === 0) return;
    setError(null);
    setCargando(true);
    try {
      const r = await etiquetas.generar(idSucursal, lista.map((x) => x.idPresentacion));
      setGeneradas(r);
    } catch (e) { setError(e instanceof Error ? e.message : "Error al generar las etiquetas"); }
    finally { setCargando(false); }
  };

  const fmt = (n: number) => n.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

  if (generadas) {
    return (
      <div className="page-shell">
        <style>{`@page { size: ${PAGE_SIZE[formato]}; margin: 0; }`}</style>
        <div className="page-head et-no-print">
          <h1>Vista de impresión — {formato}</h1>
          <div className="row-actions">
            <button className="primary" onClick={() => window.print()}>Imprimir</button>
            <button onClick={() => setGeneradas(null)}>Volver</button>
          </div>
        </div>
        {generadas.length === 0 && <p className="muted et-no-print">Ningún artículo tiene precio vigente en esta sucursal.</p>}

        {formato === "Fleje" && generadas.map((e) => (
          <div key={e.idPresentacion} className="et-sheet et-fleje">
            <div className="et-fleje__titulo">{e.descripcion}</div>
            <div className="et-fleje__codigos">
              <span>Cod. {e.codigoInterno}</span>
              <span>Cod.Bar {e.codigoBarra}</span>
            </div>
            <div className="et-fleje__precios">
              {(e.preciosTarjeta.length > 0
                ? e.preciosTarjeta.map((t) => ({ nombre: `Tarj. ${t.nombreTarjeta}`, precio: t.precio, pxu: t.precioPorUnidadMedida, si: t.precioSinImpuestos }))
                : [{ nombre: e.aclaracionPrecio ?? "", precio: e.precioBase, pxu: e.precioBasePorUnidadMedida, si: e.precioBaseSinImpuestos }]
              ).map((row, i) => (
                <div key={i} className="et-fleje__fila">
                  <span className="et-fleje__tarjeta">{row.nombre} <span className="et-fleje__precio">$ {fmt(row.precio)}</span></span>
                  <span className="et-fleje__detalle">
                    {row.pxu != null && <>Precio por {e.unidadMedidaTexto} ${fmt(row.pxu)}<br /></>}
                    Sin imp. nac.: ${fmt(row.si)}
                  </span>
                </div>
              ))}
            </div>
            <div className="et-fleje__footer">
              Compra minima: {e.compraMinima} Unidad(es) - Precio unitario final con IVA
            </div>
          </div>
        ))}

        {(formato === "A4" || formato === "A5") && generadas.map((e) => (
          <div key={e.idPresentacion} className={`et-sheet et-hoja ${formato.toLowerCase()}`}>
            <div className="et-hoja__titulo">{e.descripcion}</div>

            {(e.preciosTarjeta.length > 0
              ? e.preciosTarjeta.map((t) => ({ nombre: t.nombreTarjeta, precio: t.precio, pxu: t.precioPorUnidadMedida, si: t.precioSinImpuestos }))
              : [{ nombre: e.aclaracionPrecio ?? "", precio: e.precioBase, pxu: e.precioBasePorUnidadMedida, si: e.precioBaseSinImpuestos }]
            ).map((row, i) => (
              <div key={i} className="et-hoja__bloque">
                {row.nombre && <div className="et-hoja__nombre-tarjeta">{row.nombre}</div>}
                <div className="et-hoja__precio">$ {fmt(row.precio)}</div>
                <div className="et-hoja__detalle">
                  {row.pxu != null && <div>Precio por {e.unidadMedidaTexto} $ {fmt(row.pxu)}</div>}
                  <div>Precio sin impuestos nacionales: $ {fmt(row.si)}</div>
                </div>
              </div>
            ))}

            <div className="et-hoja__pie-precio">
              Compra mínima: {e.compraMinima} Unidad(es)<br />Precio final, IVA incluido
            </div>
            <div className="et-hoja__footer">
              <span>Cod. {e.codigoInterno}</span>
              <span>Cod. Barras: {e.codigoBarra}</span>
            </div>
          </div>
        ))}
      </div>
    );
  }

  return (
    <div className="page-shell">
      <div className="page-head">
        <h1>Etiquetas</h1>
        <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
          <span className="muted">{usuario} · {rol}</span>
          <span className="mono ip-badge">IP {ip ?? "—"}</span>
          <button onClick={logout}>Salir</button>
        </div>
      </div>
      {error && <p className="error">{error}</p>}

      <div className="card form">
        <h3>Buscar o escanear</h3>
        <div className="toolbar">
          <input placeholder="Código, código de barra o descripción" value={q}
            onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === "Enter" && buscar()} style={{ flex: 1 }} />
          <button className="primary" onClick={buscar}>Buscar</button>
        </div>
        {resultados.length > 0 && (
          <div className="art-results">
            {resultados.map((a) => (
              <button key={a.idPresentacion} onClick={() => agregar(a)}>
                <span className="mono">{a.codigoInterno}</span> · {a.descripcion}
              </button>
            ))}
          </div>
        )}

        <h3 style={{ marginTop: 16 }}>O seleccionar por clasificación completa</h3>
        <div className="form-grid">
          <label>Sector
            <select value={idSector} onChange={(e) => setIdSector(Number(e.target.value))}>
              <option value={0}>(todos)</option>
              {clasif?.sectores.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
          <label>Línea
            <select value={idLinea} onChange={(e) => setIdLinea(Number(e.target.value))}>
              <option value={0}>(todas)</option>
              {clasif?.lineas.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
          <label>Familia
            <select value={idFamilia} onChange={(e) => setIdFamilia(Number(e.target.value))}>
              <option value={0}>(todas)</option>
              {familiasDelSector.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
          <button onClick={agregarTodosPorClasificacion}>+ Agregar todos los que coincidan</button>
        </div>
      </div>

      <div className="page-head">
        <h3>Lista armada ({lista.length})</h3>
        <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
          <label className="inline-label">Sucursal
            <select value={idSucursal} onChange={(e) => setIdSucursal(Number(e.target.value))}>
              {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
          <label className="inline-label">Formato
            <select value={formato} onChange={(e) => setFormato(e.target.value as Formato)}>
              <option value="Fleje">Fleje</option>
              <option value="A4">A4</option>
              <option value="A5">A5</option>
            </select>
          </label>
          <button className="primary" disabled={lista.length === 0 || cargando} onClick={generar}>
            {cargando ? "Generando…" : "Generar etiquetas"}
          </button>
        </div>
      </div>
      <table className="grid">
        <thead><tr><th>Código</th><th>Artículo</th><th></th></tr></thead>
        <tbody>
          {lista.map((a) => (
            <tr key={a.idPresentacion}>
              <td className="mono">{a.codigoInterno}</td>
              <td>{a.descripcion}</td>
              <td><button className="danger" onClick={() => quitar(a.idPresentacion)}>Quitar</button></td>
            </tr>
          ))}
          {lista.length === 0 && <tr><td colSpan={3} className="muted">Sin artículos en la lista.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
