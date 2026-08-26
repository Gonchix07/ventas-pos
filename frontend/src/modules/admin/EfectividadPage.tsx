import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { tesoreria, type Efectividad } from "../../shared/api/tesoreria";
import { referencias, type Lookup } from "../../shared/api/admin";
import { useAuth } from "../../shared/auth/auth";
import { GraficoLinea } from "../../shared/ui/charts";
import { formatearMoneda } from "../../shared/ui/moneda";

const hoy = () => new Date();
const hoyMenos = (dias: number) => { const d = new Date(); d.setDate(d.getDate() - dias); return d; };
const fechaISO = (d: Date) => d.toISOString().slice(0, 10);
const pct = (n: number) => `${n.toFixed(1)}%`;

/**
 * Efectividad de cierre de caja: qué proporción de los lotes cerrados coincidió el saldo declarado
 * por el cajero con el esperado por el sistema, evolución en el tiempo y ranking de quién acumula
 * más diferencias — para que Tesorería vea de un vistazo a quién conviene repasar.
 *
 * Mismo criterio de "módulo con su propia barra" que Cupones/Ventas: tiene su propio header en vez
 * de depender del layout de Admin, ya que vive fuera del Outlet de Administración.
 */
export function EfectividadPage() {
  const { usuario, rol, logout } = useAuth();
  const navigate = useNavigate();

  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [idSucursal, setIdSucursal] = useState<number | 0>(0);
  const [desde, setDesde] = useState(fechaISO(hoyMenos(29)));
  const [hasta, setHasta] = useState(fechaISO(hoy()));
  const [cajero, setCajero] = useState("");

  const [datos, setDatos] = useState<Efectividad | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  const cargar = async () => {
    setError(null); setCargando(true);
    try {
      setDatos(await tesoreria.efectividad(idSucursal || undefined, desde, hasta, cajero.trim() || undefined));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setCargando(false); }
  };

  useEffect(() => { referencias.sucursales().then(setSucursales).catch(() => {}); }, []);
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, [idSucursal, desde, hasta, cajero]);

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Efectividad</span>
        </div>
        <div className="user-box">
          <span>{usuario} · <strong>{rol}</strong></span>
          <button onClick={() => navigate("/tesoreria")}>Volver a Tesorería</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      <div className="page-shell">
        <div className="page-head">
          <h1>Efectividad de cajeros</h1>
        </div>
        <p className="muted" style={{ marginTop: -8 }}>
          % de lotes cerrados donde lo declarado por el cajero coincidió con lo que esperaba el
          sistema. El ranking marca a quién le conviene más repasar el arqueo antes de cerrar.
        </p>

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
              placeholder="(todos)" style={{ width: 160 }} />
          </label>
          {cajero && <button onClick={() => setCajero("")}>Quitar filtro</button>}
          <span className="filter-count">{cargando ? "Buscando…" : ""}</span>
        </div>

        {error && <p className="error">{error}</p>}
        {!datos && !error && <p className="muted">Cargando…</p>}

        {datos && (
          <>
            <div className="kpi-grid">
              <div className="kpi-card">
                <span className="kpi-label">Lotes cerrados</span>
                <span className="kpi-valor">{datos.totalLotes}</span>
              </div>
              <div className="kpi-card">
                <span className="kpi-label">Con diferencia</span>
                <span className="kpi-valor kpi-valor-nc">{datos.totalConDiferencia}</span>
              </div>
              <div className="kpi-card">
                <span className="kpi-label">Efectividad general</span>
                <span className="kpi-valor">{pct(datos.efectividadGeneral)}</span>
              </div>
            </div>

            <div style={{ marginTop: 20 }}>
              <h3>Evolución de efectividad{cajero ? ` — ${cajero}` : ""}</h3>
              <GraficoLinea
                datos={datos.evolucion.map((p) => ({ etiqueta: p.etiqueta, total: p.efectividad }))}
                formatear={pct} ariaLabel="Evolución de efectividad de cajeros en el período" />
            </div>

            <div style={{ marginTop: 20 }}>
              <h3>{cajero ? "Detalle del cajero" : "Top cajeros con diferencias"}</h3>
              {datos.rankingCajeros.length === 0 ? (
                <p className="muted">Sin lotes cerrados en el período.</p>
              ) : (
                <table className="grid">
                  <thead>
                    <tr>
                      <th>Cajero</th><th>Lotes</th><th>Con diferencia</th>
                      <th>Efectividad</th><th>Suma de diferencias</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {datos.rankingCajeros.map((c) => (
                      <tr key={c.cajero}>
                        <td>{c.cajero}</td>
                        <td className="mono">{c.totalLotes}</td>
                        <td className={c.lotesConDiferencia > 0 ? "error mono" : "mono"}>{c.lotesConDiferencia}</td>
                        <td className="mono">{pct(c.efectividad)}</td>
                        <td className="mono">{formatearMoneda(c.sumaDiferenciasAbs)}</td>
                        <td>
                          {cajero !== c.cajero && (
                            <button className="link-btn" onClick={() => setCajero(c.cajero)}>Ver</button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </>
        )}
      </div>
    </>
  );
}
