import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import { caeaLote, type LoteCaeaPendiente, type ComprobanteCaea } from "../../shared/api/caeaLote";
import { formatearMoneda } from "../../shared/ui/moneda";

const fechaHora = (iso: string) =>
  new Date(iso).toLocaleString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });

/**
 * Módulo "Facturación CAEA": comprobantes emitidos en contingencia (el CAE de ARCA no respondió al
 * momento de facturar, se usó el CAEA precargado — ver ICaeaCargadoService/EstructuraPage) que
 * todavía no se informaron a ARCA. Es una obligación dentro de las 48hs de emitidos; si no aparece
 * nada acá es buena señal (no hubo contingencia, o ya está todo informado).
 *
 * Cada fila agrupa por sucursal + punto de venta + tipo de comprobante + valor de CAEA porque
 * ARCA exige informar cada combinación por separado (FECAEARegInformativo no mezcla puntos de
 * venta ni tipos). "Ver comprobantes" es solo para revisar antes de subir; "Subir a ARCA" informa
 * el lote completo y, si sale bien, lo saca de esta lista.
 */
export function FacturacionCaeaPage() {
  const { usuario, logout } = useAuth();
  const navigate = useNavigate();

  const [lotes, setLotes] = useState<LoteCaeaPendiente[] | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [detalleAbierto, setDetalleAbierto] = useState<LoteCaeaPendiente | null>(null);
  const [detalle, setDetalle] = useState<ComprobanteCaea[] | null>(null);

  const [subiendo, setSubiendo] = useState<string | null>(null); // clave del lote en curso

  const cargar = async () => {
    setError(null); setCargando(true);
    try {
      setLotes(await caeaLote.pendientes());
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudieron cargar los lotes pendientes.");
    } finally {
      setCargando(false);
    }
  };

  useEffect(() => { void cargar(); }, []);

  const clave = (l: LoteCaeaPendiente) => `${l.idSucursal}-${l.idPuntoVenta}-${l.idTipoComprobante}-${l.caea}`;

  const verComprobantes = async (l: LoteCaeaPendiente) => {
    setError(null); setDetalleAbierto(l); setDetalle(null);
    try {
      setDetalle(await caeaLote.comprobantes(l.idSucursal, l.idPuntoVenta, l.idTipoComprobante, l.caea));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo traer el detalle del lote.");
    }
  };

  const subir = async (l: LoteCaeaPendiente) => {
    if (!confirm(
      `Se va a informar a ARCA el lote de ${l.cantidad} comprobante(s) — ${l.tipoDescripcion} ` +
      `(CAEA ${l.caea}, Pto. Vta. ${l.numeroPuntoVenta}). Esta acción no se puede deshacer. ¿Continuar?`
    )) return;

    setError(null); setSubiendo(clave(l));
    try {
      const r = await caeaLote.informar(l.idSucursal, l.idPuntoVenta, l.idTipoComprobante, l.caea);
      if (!r.ok) {
        setError(r.error ?? "ARCA no aceptó el lote.");
        return;
      }
      if (detalleAbierto && clave(detalleAbierto) === clave(l)) { setDetalleAbierto(null); setDetalle(null); }
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo informar el lote a ARCA.");
    } finally {
      setSubiendo(null);
    }
  };

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
        <h1>Facturación CAEA</h1>
        <p className="muted">
          Comprobantes emitidos en contingencia (CAEA) todavía sin informar a ARCA — obligatorio
          dentro de las 48hs de emitidos. Un comprobante con CAE normal nunca aparece acá: ya quedó
          autorizado al pedirlo.
        </p>
        {error && <p className="error">{error}</p>}

        <table className="grid">
          <thead>
            <tr>
              <th>Sucursal</th><th>Pto. Vta.</th><th>Tipo</th><th>CAEA</th>
              <th>Cantidad</th><th>Total</th><th>Desde</th><th>Hasta</th><th />
            </tr>
          </thead>
          <tbody>
            {lotes?.map((l) => (
              <tr key={clave(l)}>
                <td>{l.sucursalDescripcion}</td>
                <td className="mono">{l.numeroPuntoVenta}</td>
                <td>{l.tipoDescripcion} {l.letra}</td>
                <td className="mono">{l.caea}</td>
                <td className="mono">{l.cantidad}</td>
                <td className="mono">{formatearMoneda(l.total)}</td>
                <td>{fechaHora(l.fechaDesde)}</td>
                <td>{fechaHora(l.fechaHasta)}</td>
                <td className="row-actions">
                  <button disabled={!!subiendo} onClick={() => verComprobantes(l)}>Ver comprobantes</button>
                  <button className="primary" disabled={!!subiendo} onClick={() => subir(l)}>
                    {subiendo === clave(l) ? "Subiendo…" : "Subir a ARCA"}
                  </button>
                </td>
              </tr>
            ))}
            {lotes && lotes.length === 0 && (
              <tr><td colSpan={9} className="muted">No hay comprobantes CAEA pendientes de informar.</td></tr>
            )}
            {!lotes && cargando && (
              <tr><td colSpan={9} className="muted">Cargando…</td></tr>
            )}
          </tbody>
        </table>

        {detalleAbierto && (
          <div className="modal-fondo" onClick={() => setDetalleAbierto(null)}>
            <div className="modal-caja" onClick={(e) => e.stopPropagation()}>
              <h2>
                {detalleAbierto.tipoDescripcion} {detalleAbierto.letra} · CAEA {detalleAbierto.caea} ·
                Pto. Vta. {detalleAbierto.numeroPuntoVenta}
              </h2>
              <table className="grid">
                <thead><tr><th>Comprobante</th><th>Fecha</th><th>Cliente</th><th>Total</th></tr></thead>
                <tbody>
                  {detalle?.map((c) => (
                    <tr key={c.idComprobante}>
                      <td className="mono">{c.numeroCompleto} {c.letra}</td>
                      <td>{fechaHora(c.fecha)}</td>
                      <td>{c.clienteDescripcion ?? "Consumidor final"}</td>
                      <td className="mono">{formatearMoneda(c.total)}</td>
                    </tr>
                  ))}
                  {!detalle && <tr><td colSpan={4} className="muted">Cargando…</td></tr>}
                </tbody>
              </table>
              <div className="row-actions" style={{ marginTop: 16 }}>
                <button className="primary" disabled={!!subiendo} onClick={() => subir(detalleAbierto)}>
                  {subiendo === clave(detalleAbierto) ? "Subiendo…" : "Subir a ARCA"}
                </button>
                <button onClick={() => setDetalleAbierto(null)}>Cerrar</button>
              </div>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
