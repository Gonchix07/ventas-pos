import { useEffect, useState } from "react";
import {
  cupones, type Cupon, type CorreccionCupon,
} from "../../shared/api/tesoreria";
import { type PlanCuotaResumen } from "../../shared/api/caja";
import { referencias, type Lookup } from "../../shared/api/admin";
import { useAuth } from "../../shared/auth/auth";
import { formatearMoneda } from "../../shared/ui/moneda";
import { abreviarTipoComprobante } from "../../shared/ui/tipoComprobante";
import { useNavigate } from "react-router-dom";

const hoyMenosUno = () => { const d = new Date(); d.setDate(d.getDate() - 1); return d; };
const fechaISO = (d: Date) => d.toISOString().slice(0, 10);

/**
 * Cupones de tarjeta (ver CuponesService en el backend): viven en MovimientoPago, no en una entidad
 * separada. Se filtran por vigencia y cajero, y se pueden corregir retroactivamente (número de
 * cupón/lote/plan) cuando el cajero tipeó mal algo — con historial de auditoría.
 */
export function CuponesPage() {
  const { usuario, rol, logout, ip } = useAuth();
  const navigate = useNavigate();

  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [idSucursal, setIdSucursal] = useState<number | 0>(0);
  const [desde, setDesde] = useState(fechaISO(hoyMenosUno()));
  const [hasta, setHasta] = useState(fechaISO(hoyMenosUno()));
  const [cajero, setCajero] = useState("");

  const [items, setItems] = useState<Cupon[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  const [corrigiendo, setCorrigiendo] = useState<Cupon | null>(null);
  const [historialDe, setHistorialDe] = useState<Cupon | null>(null);

  const cargar = async () => {
    setError(null); setCargando(true);
    try {
      setItems(await cupones.listar(idSucursal || undefined, desde, hasta, cajero.trim() || undefined));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setCargando(false); }
  };

  useEffect(() => { referencias.sucursales().then(setSucursales).catch(() => {}); }, []);
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, [idSucursal, desde, hasta]);

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Cupones</span>
        </div>
        <div className="user-box">
          <span>{usuario} · <strong>{rol}</strong></span>
          <span className="mono ip-badge">IP {ip ?? "—"}</span>
          <button onClick={() => navigate("/tesoreria")}>Volver a Tesorería</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      <div className="page-shell">
      <div className="page-head">
        <h1>Cupones de tarjeta</h1>
      </div>

      <div className="filter-bar">
        <label>Sucursal
          <select value={idSucursal} onChange={(e) => setIdSucursal(Number(e.target.value))}>
            <option value={0}>(todas)</option>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
        <label>Desde
          <input type="date" value={desde} onChange={(e) => setDesde(e.target.value)} />
        </label>
        <label>Hasta
          <input type="date" value={hasta} onChange={(e) => setHasta(e.target.value)} />
        </label>
        <label>Cajero
          <input value={cajero} onChange={(e) => setCajero(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && cargar()} placeholder="Usuario del cajero" />
        </label>
        <button onClick={cargar}>Buscar</button>
        <span className="filter-count">
          {cargando ? "Buscando…" : `${items.length} cupón${items.length === 1 ? "" : "es"}`}
        </span>
      </div>

      {error && <p className="error">{error}</p>}

      <div className="table-scroll">
        <table className="grid">
          <thead>
            <tr>
              <th>Fecha</th><th>Lote</th><th>Cajero</th><th>Medio</th><th>Monto</th>
              <th>N° cupón</th><th>N° lote tarjeta</th><th>Plan</th><th>Tipo</th><th>Comprobante</th><th>Estado</th><th></th>
            </tr>
          </thead>
          <tbody>
            {items.map((c) => (
              <tr key={c.idMovPagos}>
                <td>{new Date(c.fecha).toLocaleString()}</td>
                <td className="mono">{c.idLote}</td>
                <td>{c.cajero ?? "—"}</td>
                <td>{c.medioDescripcion}</td>
                <td className="mono">{formatearMoneda(c.monto)}</td>
                <td className="mono">{c.numeroCupon ?? "—"}</td>
                <td className="mono">{c.numeroLote ?? "—"}</td>
                <td>{c.planDescripcion ?? "—"}</td>
                <td className="mono">{c.tipoComprobante ? abreviarTipoComprobante(c.tipoComprobante) : "—"}</td>
                <td className="mono">{c.numeroComprobante ?? "—"}</td>
                <td>
                  {c.anulado
                    ? <span className="badge off" title={c.fechaAnulacion ? new Date(c.fechaAnulacion).toLocaleString() : ""}>Anulado</span>
                    : <span className="badge on">Vigente</span>}
                </td>
                <td className="row-actions">
                  {c.corregido && <span className="badge on" title="Tiene correcciones">Corregido</span>}
                  <button onClick={() => setCorrigiendo(c)} disabled={c.anulado}>Corregir</button>
                  {c.corregido && <button onClick={() => setHistorialDe(c)}>Historial</button>}
                </td>
              </tr>
            ))}
            {items.length === 0 && !cargando && (
              <tr><td colSpan={12} className="muted">Sin cupones en la vigencia elegida.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {corrigiendo && (
        <CorregirCuponModal cupon={corrigiendo} onCerrar={() => setCorrigiendo(null)}
          onGuardado={() => { void cargar(); }} />
      )}
      {historialDe && (
        <HistorialCuponModal cupon={historialDe} onCerrar={() => setHistorialDe(null)} />
      )}
      </div>
    </>
  );
}

function CorregirCuponModal({ cupon, onCerrar, onGuardado }: {
  cupon: Cupon; onCerrar: () => void; onGuardado: () => void;
}) {
  const [numeroCupon, setNumeroCupon] = useState(cupon.numeroCupon ?? "");
  const [numeroLote, setNumeroLote] = useState(cupon.numeroLote ?? "");
  const [idPlanCuota, setIdPlanCuota] = useState<number | 0>(cupon.idPlanCuota ?? 0);
  const [planes, setPlanes] = useState<PlanCuotaResumen[]>([]);
  const [motivo, setMotivo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  useEffect(() => {
    cupones.planes(cupon.idSucursal, cupon.idMovPagos).then(setPlanes).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const guardar = async () => {
    if (!motivo.trim()) return;
    setError(null); setGuardando(true);
    try {
      await cupones.corregir(cupon.idSucursal, cupon.idMovPagos, {
        numeroCupon: numeroCupon.trim() || null, numeroLote: numeroLote.trim() || null,
        idPlanCuota: idPlanCuota || null, motivo: motivo.trim(),
      });
      onGuardado();
      onCerrar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo corregir el cupón.");
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(460px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        <div className="page-head"><h3>Corregir cupón — {cupon.medioDescripcion}</h3></div>
        <p className="muted" style={{ margin: 0 }}>
          Venta {cupon.numeroComprobante ?? "—"} del {new Date(cupon.fecha).toLocaleString()}.
        </p>
        {error && <p className="error">{error}</p>}
        <div className="form-grid" style={{ marginTop: 10 }}>
          <label>N° de cupón
            <input value={numeroCupon} onChange={(e) => setNumeroCupon(e.target.value)} />
          </label>
          <label>N° de lote de tarjeta
            <input value={numeroLote} onChange={(e) => setNumeroLote(e.target.value)} />
          </label>
          <label>Plan de cuotas
            <select value={idPlanCuota} onChange={(e) => setIdPlanCuota(Number(e.target.value))}>
              <option value={0}>(ninguno)</option>
              {planes.map((p) => <option key={p.idPlan} value={p.idPlan}>{p.denominacion}</option>)}
            </select>
          </label>
          <label>Motivo *
            <input value={motivo} onChange={(e) => setMotivo(e.target.value)}
              placeholder="Por qué se corrige" maxLength={200} />
          </label>
        </div>
        <div className="row-actions" style={{ marginTop: 16 }}>
          <button onClick={onCerrar} disabled={guardando}>Cancelar</button>
          <button className="primary" disabled={guardando || !motivo.trim()} onClick={guardar}>
            {guardando ? "Guardando…" : "Guardar corrección"}
          </button>
        </div>
      </div>
    </div>
  );
}

function HistorialCuponModal({ cupon, onCerrar }: { cupon: Cupon; onCerrar: () => void }) {
  const [items, setItems] = useState<CorreccionCupon[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    cupones.historial(cupon.idSucursal, cupon.idMovPagos).then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : "Error"));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(640px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        <div className="page-head">
          <h3>Historial de correcciones — {cupon.medioDescripcion}</h3>
          <button onClick={onCerrar}>Cerrar</button>
        </div>
        {error && <p className="error">{error}</p>}
        {items === null && !error && <p className="muted">Cargando…</p>}
        {items && (
          <table className="grid">
            <thead><tr><th>Fecha</th><th>Usuario</th><th>Antes</th><th>Después</th><th>Motivo</th></tr></thead>
            <tbody>
              {items.map((h) => (
                <tr key={h.idCorreccionCupon}>
                  <td>{new Date(h.fecha).toLocaleString()}</td>
                  <td>{h.usuario ?? "—"}</td>
                  <td className="mono">
                    {h.numeroCuponAnterior ?? "—"} / {h.numeroLoteAnterior ?? "—"}
                  </td>
                  <td className="mono">
                    {h.numeroCuponNuevo ?? "—"} / {h.numeroLoteNuevo ?? "—"}
                  </td>
                  <td>{h.motivo}</td>
                </tr>
              ))}
              {items.length === 0 && <tr><td colSpan={5} className="muted">Sin correcciones.</td></tr>}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
