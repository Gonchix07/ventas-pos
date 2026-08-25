import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import {
  etiquetas, type ArticuloParaEtiqueta, type Clasificaciones, type LookupSimple,
} from "../../shared/api/etiquetas";
import { abrirPestañaParaPdf, generarYAbrirPdf, type FormatoEtiqueta } from "./EtiquetaPdf";

type Formato = FormatoEtiqueta;

export function EtiquetasPage() {
  const { usuario, rol, logout, ip } = useAuth();
  const navigate = useNavigate();
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
  const quitarTodo = () => { if (confirm("¿Vaciar toda la lista armada?")) setLista([]); };

  // Genera el PDF real (fleje 90x40mm o A4/A5) y lo abre en una pestaña nueva para imprimir desde
  // el visor de PDF del navegador — ver EtiquetaPdf.tsx. Ya no hay una "vista de impresión" propia
  // en HTML: ese mecanismo (@page + window.print()) salía en blanco en algunas instalaciones de
  // Chrome (bug de capas compuestas al combinar CSS transform con paginación de impresión).
  const generar = async () => {
    if (!idSucursal || lista.length === 0) return;
    setError(null);
    setCargando(true);
    // Se abre ANTES de pedirle los datos al backend (que es async): si se abre después de un
    // await, el navegador ya no lo asocia al click y lo bloquea como popup.
    const ventana = abrirPestañaParaPdf();
    try {
      const r = await etiquetas.generar(idSucursal, lista.map((x) => x.idPresentacion));
      if (r.length === 0) {
        setError("Ningún artículo tiene precio vigente en esta sucursal.");
        ventana?.close();
        return;
      }
      await generarYAbrirPdf(formato, r, ventana);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error al generar las etiquetas");
      ventana?.close();
    } finally { setCargando(false); }
  };

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Etiquetas</span>
        </div>
        <div className="user-box">
          <span>{usuario} · <strong>{rol}</strong></span>
          <span className="mono ip-badge">IP {ip ?? "—"}</span>
          <button onClick={() => navigate("/")}>Módulos</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      {cargando && <PantallaBloqueada mensaje="Generando PDF…" />}
      <div className="page-shell">
        <h1>Etiquetas</h1>
        {error && <p className="error">{error}</p>}

        <div className="two-col">
        <div className="card form">
          <h3>Buscar o escanear</h3>
          <div className="toolbar">
            <input placeholder="Código, código de barra o descripción" value={q}
              onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === "Enter" && buscar()} style={{ flex: 1 }} />
            <button className="primary" onClick={buscar}>Buscar</button>
          </div>
          <table className="grid">
            <thead><tr><th>Código</th><th>Artículo</th><th></th></tr></thead>
            <tbody>
              {resultados.map((a) => (
                <tr key={a.idPresentacion}>
                  <td className="mono">{a.codigoInterno}</td>
                  <td>{a.descripcion}</td>
                  <td><button onClick={() => agregar(a)}>+ Agregar</button></td>
                </tr>
              ))}
              {resultados.length === 0 && (
                <tr><td colSpan={3} className="muted">Buscá un artículo por código, código de barra o descripción.</td></tr>
              )}
            </tbody>
          </table>

          <h3 style={{ marginTop: 16 }}>O seleccionar por clasificación completa</h3>
          <div className="form-grid">
            <label>Línea
              <select value={idLinea} onChange={(e) => setIdLinea(Number(e.target.value))}>
                <option value={0}>(todas)</option>
                {clasif?.lineas.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Sector
              <select value={idSector} onChange={(e) => setIdSector(Number(e.target.value))}>
                <option value={0}>(todos)</option>
                {clasif?.sectores.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Familia
              <select value={idFamilia} onChange={(e) => setIdFamilia(Number(e.target.value))}>
                <option value={0}>(todas)</option>
                {familiasDelSector.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <button className="success-solid" onClick={agregarTodosPorClasificacion}>+ Agregar todos los que coincidan</button>
          </div>
        </div>

        <div>
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
                {cargando ? "Generando PDF…" : "Generar PDF"}
              </button>
              <button className="danger" disabled={lista.length === 0 || cargando} onClick={quitarTodo}>
                Quitar todo
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
        </div>
      </div>
    </>
  );
}

// Igual que en CajaPage/PadronesPage: tapa la pantalla con blur + spinner mientras se arma el PDF
// (puede tardar con listas largas). Reutiliza las clases globales de App.css (pantalla-bloqueada /
// pantalla-bloqueada-caja / spinner).
function PantallaBloqueada({ mensaje }: { mensaje: string }) {
  return (
    <div className="pantalla-bloqueada" role="alert" aria-busy="true">
      <div className="pantalla-bloqueada-caja">
        <div className="spinner" aria-hidden="true" />
        <p>{mensaje}</p>
      </div>
    </div>
  );
}
