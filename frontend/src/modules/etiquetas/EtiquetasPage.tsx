import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import {
  etiquetas, type ArticuloParaEtiqueta, type Clasificaciones, type Etiqueta, type LookupSimple,
} from "../../shared/api/etiquetas";
import { abrirPestañaParaPdf, generarYAbrirPdf, type FormatoEtiqueta } from "./EtiquetaPdf";
import { IconAgregar } from "../../shared/ui/icons";
import { useToast } from "../../shared/ui/toast";
import { formatearMoneda } from "../../shared/ui/moneda";

type Formato = FormatoEtiqueta;

export function EtiquetasPage() {
  const { usuario, logout, idSucursalPredeterminada } = useAuth();
  const navigate = useNavigate();
  const notificar = useToast();
  const [sucursales, setSucursales] = useState<LookupSimple[]>([]);
  const [idSucursal, setIdSucursal] = useState<number>(0);
  const [clasif, setClasif] = useState<Clasificaciones | null>(null);
  const [idSector, setIdSector] = useState<number>(0);
  const [idLinea, setIdLinea] = useState<number>(0);
  const [idFamilia, setIdFamilia] = useState<number>(0);

  const [modoBusqueda, setModoBusqueda] = useState<"articulo" | "clasificacion">("articulo");
  const [q, setQ] = useState("");
  const [resultados, setResultados] = useState<ArticuloParaEtiqueta[]>([]);
  // Plegar/desplegar la tabla de resultados de la búsqueda — puede quedar larga y tapar el resto
  // del panel; mismo criterio visual que "Autorizados" en Caja (flecha + cantidad al lado).
  const [resultadosAbierto, setResultadosAbierto] = useState(true);
  const [lista, setLista] = useState<ArticuloParaEtiqueta[]>([]);
  // idPresentacion → precios ya resueltos (AZUL/ROJA), para mostrarlos en la lista armada sin
  // esperar a "Generar PDF". Se completa en segundo plano al agregar cada artículo (o de nuevo si
  // cambia la sucursal, porque el precio vigente es por sucursal).
  const [precios, setPrecios] = useState<Map<number, Etiqueta>>(new Map());
  const [formato, setFormato] = useState<Formato>("Fleje");
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  useEffect(() => {
    etiquetas.sucursales().then((s) => {
      setSucursales(s);
      if (idSucursalPredeterminada) setIdSucursal(idSucursalPredeterminada);
      else if (s.length) setIdSucursal(s[0].id);
    }).catch(() => {});
    etiquetas.clasificaciones().then(setClasif).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // La familia pertenece a un sector: al elegir sector el combo muestra solo sus familias (las que
  // no tienen sector, el cajón "SIN FAMILIA", se ofrecen siempre).
  const familiasDelSector = (clasif?.familias ?? [])
    .filter((f) => f.idSector == null || !idSector || f.idSector === idSector);

  useEffect(() => {
    if (idFamilia && !familiasDelSector.some((f) => f.id === idFamilia)) setIdFamilia(0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idSector, clasif]);

  // El aviso va AFUERA del updater de setLista: en React StrictMode (dev) los updaters se llaman
  // dos veces para detectar efectos impuros, y una notificación disparada adentro se duplicaría.
  // Se agrega AL PRINCIPIO (no al final): al escanear en cadena, lo último agregado es lo que el
  // operador quiere ver arriba para confirmar de un vistazo que entró bien.
  // Resuelve AZUL/ROJA (u otras tarjetas configuradas) para mostrarlas en la lista armada, sin
  // esperar a "Generar PDF" — mismo endpoint que ya usa el PDF, así que el precio que se ve acá es
  // el mismo que sale impreso. Best-effort: si falla, la fila simplemente queda sin precio (no
  // bloquea agregar/buscar artículos, que es la acción principal de la pantalla).
  const cargarPrecios = async (idsPresentacion: number[]) => {
    if (!idSucursal || idsPresentacion.length === 0) return;
    try {
      const r = await etiquetas.generar(idSucursal, idsPresentacion);
      setPrecios((p) => {
        const m = new Map(p);
        r.forEach((e) => m.set(e.idPresentacion, e));
        return m;
      });
    } catch {
      // Silencioso: la columna de precio queda vacía para esos artículos, nada más.
    }
  };

  const agregar = (a: ArticuloParaEtiqueta) => {
    if (lista.some((x) => x.idPresentacion === a.idPresentacion)) return;
    notificar(`${a.descripcion} agregado a la lista`);
    setLista((l) => (l.some((x) => x.idPresentacion === a.idPresentacion) ? l : [a, ...l]));
    cargarPrecios([a.idPresentacion]);
  };

  const buscar = async () => {
    setError(null);
    if (!q.trim()) return;
    try {
      const r = await etiquetas.buscar(q.trim());
      // Resultado único (típico al escanear un código de barra): va directo a la lista armada, sin
      // el paso intermedio de "+ Agregar" — y limpia el campo para poder seguir escaneando ya
      // mismo. Con más de un resultado (búsqueda por texto ambigua) se elige a mano, como antes.
      if (r.length === 1) {
        agregar(r[0]);
        setResultados([]);
        setQ("");
      } else {
        setResultados(r);
      }
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const agregarTodosPorClasificacion = async () => {
    setError(null);
    try {
      const items = await etiquetas.porClasificacion(idSector || undefined, idLinea || undefined, idFamilia || undefined);
      const existentes = new Set(lista.map((x) => x.idPresentacion));
      const nuevos = items.filter((i) => !existentes.has(i.idPresentacion));
      notificar(nuevos.length > 0
        ? `${nuevos.length} artículo${nuevos.length === 1 ? "" : "s"} agregado${nuevos.length === 1 ? "" : "s"} a la lista`
        : "No hay artículos nuevos para agregar");
      setLista((l) => [...nuevos, ...l]);
      cargarPrecios(nuevos.map((x) => x.idPresentacion));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const quitar = (idPresentacion: number) => {
    setLista((l) => l.filter((x) => x.idPresentacion !== idPresentacion));
    setPrecios((p) => { const m = new Map(p); m.delete(idPresentacion); return m; });
  };
  const quitarTodo = () => { if (confirm("¿Vaciar toda la lista armada?")) { setLista([]); setPrecios(new Map()); } };

  // El precio vigente es por sucursal: si se cambia la sucursal con artículos ya en la lista, hay
  // que volver a resolverlos todos (los que había quedan con el precio viejo hasta que llega esto).
  useEffect(() => {
    if (lista.length > 0) cargarPrecios(lista.map((x) => x.idPresentacion));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idSucursal]);

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

  // Precio de una tarjeta puntual (AZUL/ROJA) para la fila de la lista armada. Si las tarjetas
  // colapsaron en un precio único (folder vigente, o Rojo/Azul coincidiendo — ver EtiquetaService)
  // no hay entradas individuales: en ese caso el precio único vale para las dos, así que se usa ese.
  // null = todavía no se resolvió (o esa tarjeta puntual no tiene precio cargado para este artículo).
  const precioTarjeta = (idPresentacion: number, contiene: string): number | null => {
    const e = precios.get(idPresentacion);
    if (!e) return null;
    if (e.preciosTarjeta.length === 0) return e.precioBase;
    return e.preciosTarjeta.find((t) => t.nombreTarjeta.toUpperCase().includes(contiene))?.precio ?? null;
  };

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Etiquetas</span>
        </div>
        <div className="user-box">
          <span>{usuario}</span>
          <button onClick={() => navigate("/")}>Módulos</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      {cargando && <PantallaBloqueada mensaje="Generando PDF…" />}
      <div className="page-shell etiquetas-compact">
        {error && <p className="error">{error}</p>}

        <div className="field-row" style={{ marginTop: 0 }}>
          <label className="inline-label">Sucursal
            <select value={idSucursal} onChange={(e) => setIdSucursal(Number(e.target.value))}>
              {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
        </div>

        <div className="two-col">
        <div className="card form">
          <div className="field-row" style={{ marginTop: 0 }}>
            <label>Buscar por
              <select value={modoBusqueda} onChange={(e) => setModoBusqueda(e.target.value as typeof modoBusqueda)}>
                <option value="articulo">Artículo (código, código de barra o descripción)</option>
                <option value="clasificacion">Clasificación completa (línea/sector/familia)</option>
              </select>
            </label>
          </div>

          {modoBusqueda === "articulo" ? (
            <>
              <div className="toolbar">
                <input placeholder="Código, código de barra o descripción" value={q}
                  onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => e.key === "Enter" && buscar()}
                  style={{ flex: "0 1 min(180px, 100%)" }} />
                <button className="primary" onClick={buscar}>Buscar</button>
                <button type="button" className="toggle-flecha"
                  onClick={() => setResultadosAbierto((v) => !v)}
                  aria-expanded={resultadosAbierto}
                  title={resultadosAbierto ? "Ocultar resultados" : "Mostrar resultados"}>
                  {resultadosAbierto ? "▾" : "▸"}
                </button>
              </div>
              {resultadosAbierto && (
                <div className="table-scroll">
                  <table className="grid">
                    <thead><tr><th>Código</th><th>Artículo</th><th></th></tr></thead>
                    <tbody>
                      {resultados.map((a) => (
                        <tr key={a.idPresentacion}>
                          <td className="mono">{a.codigoInterno}</td>
                          <td>{a.descripcion}</td>
                          <td>
                            <button className="icon-btn icon-agregar" title="Agregar" aria-label="Agregar"
                              onClick={() => agregar(a)}><IconAgregar /></button>
                          </td>
                        </tr>
                      ))}
                      {resultados.length === 0 && (
                        <tr><td colSpan={3} className="muted">Buscá un artículo por código, código de barra o descripción.</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          ) : (
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
          )}
        </div>

        <div>
          <div className="page-head">
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8, alignItems: "center" }}>
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
            </div>
          </div>
          <div className="table-scroll">
            <table className="grid">
              <thead>
                <tr>
                  <th>Código</th><th>Artículo</th><th>Azul</th><th>Roja</th>
                  <th>
                    <button className="danger" disabled={lista.length === 0 || cargando} onClick={quitarTodo}>
                      Limpiar
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody>
                {lista.map((a) => {
                  const azul = precioTarjeta(a.idPresentacion, "AZUL");
                  const roja = precioTarjeta(a.idPresentacion, "ROJA");
                  // "Precio Único": folder vigente, o Azul/Roja cargados con el mismo precio (ver
                  // EtiquetaService) — se resalta para que se note de un vistazo que no son dos
                  // precios independientes, aunque las dos columnas muestren el mismo número.
                  const esUnico = !!precios.get(a.idPresentacion)?.aclaracionPrecio;
                  const claseUnico = esUnico ? "precio-etiqueta-unico" : undefined;
                  return (
                    <tr key={a.idPresentacion}>
                      <td className="mono">{a.codigoInterno}</td>
                      <td>{a.descripcion}</td>
                      <td className={`mono ${claseUnico ?? ""}`}>{azul != null ? formatearMoneda(azul) : "—"}</td>
                      <td className={`mono ${claseUnico ?? ""}`}>{roja != null ? formatearMoneda(roja) : "—"}</td>
                      <td><button className="danger" onClick={() => quitar(a.idPresentacion)}>Quitar</button></td>
                    </tr>
                  );
                })}
                {lista.length === 0 && <tr><td colSpan={5} className="muted">Sin artículos en la lista.</td></tr>}
              </tbody>
            </table>
          </div>
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
