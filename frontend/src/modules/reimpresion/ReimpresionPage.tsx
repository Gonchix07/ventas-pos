import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import { referencias, type Lookup } from "../../shared/api/admin";
import {
  reimpresion, type ComprobanteReimpresion, type RendicionReimpresion, type TipoReimpresion,
} from "../../shared/api/reimpresion";
import { type ComprobanteImpresion } from "../../shared/api/facturacion";
import { ComprobanteImpresionView } from "../caja/ComprobanteImpresion";
import { abrirPestañaParaRendicion, generarYAbrirRendicionPdf } from "../caja/RendicionPdf";
import { formatearMoneda } from "../../shared/ui/moneda";
import { abreviarTipoComprobante } from "../../shared/ui/tipoComprobante";

const TIPOS: { v: TipoReimpresion; l: string }[] = [
  { v: "", l: "(todos)" },
  { v: "Factura", l: "Facturas" },
  { v: "NotaCredito", l: "Notas de crédito" },
  { v: "Presupuesto", l: "Presupuestos" },
  { v: "Rendicion", l: "Rendiciones" },
];

/**
 * Reimpresión de comprobantes (Supervisor/Tesorero/Administrador): buscar una factura o nota de
 * crédito ya emitida — misma UX de búsqueda que la Nota de Crédito en caja (número, cliente o CUIT
 * + rango de fechas opcional) — y volver a mostrarla en pantalla para imprimir.
 *
 * No reemite ni reabre nada fiscal: reusa el mismo armado que ya se usa para la vista posterior a
 * emitir (ComprobanteImpresionView + window.print()), así que sale por la impresora de Windows en
 * papel común. Si el comprobante original salió por controlador fiscal (letra A/B), esto NO
 * reimprime el rollo fiscal original — el protocolo Hasar sí tiene un comando para eso
 * (CopiarComprobante) pero no está implementado todavía.
 */
export function ReimpresionPage() {
  const { usuario, logout, idSucursal: idSucursalAuth } = useAuth();
  const navigate = useNavigate();

  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [idSucursal, setIdSucursal] = useState(0);
  const [tipo, setTipo] = useState<TipoReimpresion>("");
  const [texto, setTexto] = useState("");
  const [desde, setDesde] = useState("");
  const [hasta, setHasta] = useState("");
  const [resultados, setResultados] = useState<ComprobanteReimpresion[] | null>(null);
  const [resultadosRendicion, setResultadosRendicion] = useState<RendicionReimpresion[] | null>(null);
  const [impresion, setImpresion] = useState<ComprobanteImpresion | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Si la sesión está atada a una sucursal (Supervisor con puesto asignado, mismo criterio que
    // Caja), se fuerza esa — el backend rechaza (403 SUCURSAL_NO_AUTORIZADA) cualquier otra, y antes
    // esta pantalla siempre arrancaba con la PRIMERA sucursal de la lista sin mirar la propia, lo que
    // le impedía operar a un Supervisor cuya sucursal no era la primera. Tesorero/Administrador sin
    // puesto asignado siguen pudiendo elegir cualquiera.
    referencias.sucursales().then((s) => {
      setSucursales(s);
      if (idSucursalAuth) setIdSucursal(idSucursalAuth);
      else if (s.length) setIdSucursal(s[0].id);
    }).catch(() => {});
  }, [idSucursalAuth]);

  const buscar = async () => {
    if (!idSucursal) return;
    setError(null); setCargando(true);
    setResultados(null); setResultadosRendicion(null);
    try {
      if (tipo === "Rendicion") {
        setResultadosRendicion(await reimpresion.buscarRendiciones(idSucursal, texto.trim(), desde || undefined, hasta || undefined));
      } else {
        setResultados(await reimpresion.buscar(idSucursal, texto.trim(), desde || undefined, hasta || undefined, tipo || undefined));
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo buscar.");
    } finally {
      setCargando(false);
    }
  };

  const reimprimir = async (c: ComprobanteReimpresion) => {
    setError(null); setCargando(true);
    try {
      setImpresion(await reimpresion.impresion(c.idSucursal, c.idComprobante));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo armar el comprobante para imprimir.");
    } finally {
      setCargando(false);
    }
  };

  // La rendición NO pasa por window.print(): genera un PDF real y lo abre en una pestaña nueva —
  // mismo motivo que en el cierre de turno recién hecho (ver RendicionPdf.tsx), el puesto de Caja
  // corre en modo --kiosk-printing y esto evita que salga derecho por la comandera.
  const reimprimirRendicion = async (r: RendicionReimpresion) => {
    const ventana = abrirPestañaParaRendicion(); // sincrónico, antes del await — ver esa función
    setError(null); setCargando(true);
    try {
      const rd = await reimpresion.rendicion(r.idSucursal, r.idLote);
      await generarYAbrirRendicionPdf(rd, ventana);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo armar la rendición para imprimir.");
      ventana?.close();
    } finally {
      setCargando(false);
    }
  };

  if (impresion) {
    return (
      <div className="caja-shell">
        <header className="caja-header">
          <span className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Reimpresión</span></span>
          <div className="user-box"><span>{usuario}</span><button onClick={logout}>Salir</button></div>
        </header>
        <div className="caja-center">
          <ComprobanteImpresionView c={impresion} onCerrar={() => setImpresion(null)}
            esReimpresion textoVolver="Volver a la búsqueda" />
        </div>
      </div>
    );
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Mayorista</span></div>
        <div className="user-box">
          <span>{usuario}</span>
          <button onClick={() => navigate("/")}>Módulos</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      <main className="app-main">
        <h1>Reimpresión</h1>
        <p className="muted">
          {tipo === "Rendicion"
            ? "Buscá la rendición (cierre de turno) por número de lote o cajero. Se genera un PDF real para imprimir eligiendo la impresora — no sale directo por la comandera."
            : "Buscá la factura, nota de crédito o presupuesto por número, cliente o CUIT. Se imprime en pantalla (papel común) — si el comprobante original salió por controlador fiscal, esto NO reimprime el rollo fiscal original."}
        </p>

        <div className="card form">
          <div className="form-grid">
            <label>Sucursal
              <select value={idSucursal} disabled={!!idSucursalAuth}
                onChange={(e) => setIdSucursal(Number(e.target.value))}>
                {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
              </select>
            </label>
            <label>Desde<input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} /></label>
            <label>Hasta<input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} /></label>
          </div>
          <div className="ident-search field-row">
            <label>Tipo
              <select value={tipo} onChange={(e) => setTipo(e.target.value as TipoReimpresion)}>
                {TIPOS.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
              </select>
            </label>
            <input autoFocus value={texto}
              placeholder={tipo === "Rendicion" ? "Número de lote o cajero…" : "Número de comprobante, cliente o CUIT…"}
              onChange={(e) => setTexto(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && buscar()} />
            <button className="primary" onClick={buscar} disabled={cargando || !idSucursal}>Buscar</button>
          </div>
        </div>
        {error && <p className="error">{error}</p>}

        {resultados && (
          <table className="grid">
            <thead>
              <tr><th>Tipo</th><th>Comprobante</th><th>Fecha</th><th>Cliente</th><th>Total</th><th>Estado</th><th /></tr>
            </thead>
            <tbody>
              {resultados.map((c) => (
                <tr key={c.idComprobante}>
                  <td className="mono">{abreviarTipoComprobante(c.tipoComprobante)}</td>
                  <td className="mono">{c.numeroCompleto} {c.letra}</td>
                  <td>{new Date(c.fecha).toLocaleDateString()}</td>
                  <td>{c.clienteDescripcion ?? "Consumidor final"}</td>
                  <td className="mono">{formatearMoneda(c.total)}</td>
                  <td>{c.estado}</td>
                  <td><button disabled={cargando} onClick={() => reimprimir(c)}>Reimprimir</button></td>
                </tr>
              ))}
              {resultados.length === 0 && (
                <tr><td colSpan={7} className="muted">No se encontraron comprobantes.</td></tr>
              )}
            </tbody>
          </table>
        )}

        {resultadosRendicion && (
          <table className="grid">
            <thead>
              <tr><th>Lote</th><th>Caja</th><th>Cajero</th><th>Cierre</th><th>N° cierre</th><th>Total</th><th /></tr>
            </thead>
            <tbody>
              {resultadosRendicion.map((r) => (
                <tr key={r.idLote}>
                  <td className="mono">{r.idLote}</td>
                  <td>{r.descripcionCaja}</td>
                  <td>{r.cajero ?? "—"}</td>
                  <td>{new Date(r.fechaCierre).toLocaleString()}</td>
                  <td className="mono">{r.numeroCierre ? `T-${String(r.numeroCierre).padStart(6, "0")}` : "—"}</td>
                  <td className="mono">{formatearMoneda(r.total)}</td>
                  <td><button disabled={cargando} onClick={() => reimprimirRendicion(r)}>Reimprimir</button></td>
                </tr>
              ))}
              {resultadosRendicion.length === 0 && (
                <tr><td colSpan={7} className="muted">No se encontraron rendiciones.</td></tr>
              )}
            </tbody>
          </table>
        )}
      </main>
    </div>
  );
}
