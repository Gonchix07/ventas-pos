import { useCallback, useEffect, useRef, useState } from "react";
import { estadisticas, PERIODOS, type EstadisticasVentas, type Periodo } from "../../shared/api/estadisticas";
import { referencias, type Lookup } from "../../shared/api/admin";
import { formatearMoneda } from "../../shared/ui/moneda";
import { GraficoLinea, GraficoTorta, ListaRankeada } from "../../shared/ui/charts";

// El dashboard se refresca solo cada tanto para que los números se vean "en vivo" sin que el
// administrador tenga que recargar la página. No hay push por WebSocket: es un simple polling,
// que alcanza para un tablero de lectura que no necesita actualizarse al segundo.
const INTERVALO_REFRESCO_MS = 30_000;

export function VentasEstadisticasPage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [idSucursal, setIdSucursal] = useState<number | 0>(0);
  const [periodo, setPeriodo] = useState<Periodo>(0);
  const [datos, setDatos] = useState<EstadisticasVentas | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const [ultimaActualizacion, setUltimaActualizacion] = useState<Date | null>(null);

  useEffect(() => { referencias.sucursales().then(setSucursales).catch(() => {}); }, []);

  const cargar = useCallback(async (mostrarSpinner: boolean) => {
    if (mostrarSpinner) setCargando(true);
    try {
      const r = await estadisticas.ventas(periodo, idSucursal || undefined);
      setDatos(r);
      setUltimaActualizacion(new Date());
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error al obtener las estadísticas");
    } finally {
      if (mostrarSpinner) setCargando(false);
    }
  }, [periodo, idSucursal]);

  // Al cambiar período/sucursal se recarga con spinner (cambio pedido por el usuario); el
  // refresco automático de fondo no lo muestra, para no hacer parpadear todo el tablero cada 30s.
  useEffect(() => { void cargar(true); }, [cargar]);

  const cargarRef = useRef(cargar);
  cargarRef.current = cargar;
  useEffect(() => {
    const id = setInterval(() => void cargarRef.current(false), INTERVALO_REFRESCO_MS);
    return () => clearInterval(id);
  }, []);

  const segsDesdeActualizacion = useSegundosDesde(ultimaActualizacion);

  return (
    <div>
      <div className="page-head">
        <h1>Ventas</h1>
        <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
          <span className="muted" title="Se refresca solo cada 30 segundos">
            {cargando ? "Actualizando…" : ultimaActualizacion ? `Actualizado hace ${segsDesdeActualizacion}s` : ""}
          </span>
          <button onClick={() => void cargar(true)} disabled={cargando}>Actualizar</button>
        </div>
      </div>

      <div className="filter-bar">
        <div className="periodo-selector">
          {PERIODOS.map((p) => (
            <button key={p.valor} className={periodo === p.valor ? "primary" : ""}
              onClick={() => setPeriodo(p.valor)}>
              {p.label}
            </button>
          ))}
        </div>
        <label className="grow" style={{ maxWidth: 260 }}>Sucursal
          <select value={idSucursal} onChange={(e) => setIdSucursal(Number(e.target.value))}>
            <option value={0}>(todas)</option>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
      </div>

      {error && <p className="error">{error}</p>}
      {!datos && !error && <p className="muted">Cargando…</p>}

      {datos && (
        <>
          <div className="kpi-grid">
            <div className="kpi-card">
              <span className="kpi-label">Total ventas</span>
              <span className="kpi-valor">{formatearMoneda(datos.resumen.totalVentas)}</span>
            </div>
            <div className="kpi-card">
              <span className="kpi-label">Tickets</span>
              <span className="kpi-valor">{datos.resumen.cantidadTickets}</span>
            </div>
            <div className="kpi-card">
              <span className="kpi-label">Ticket promedio</span>
              <span className="kpi-valor">{formatearMoneda(datos.resumen.ticketPromedio)}</span>
            </div>
            <div className="kpi-card">
              <span className="kpi-label">Clientes atendidos</span>
              <span className="kpi-valor">{datos.resumen.cantidadClientes}</span>
            </div>
            <div className="kpi-card">
              <span className="kpi-label">Descuentos</span>
              <span className="kpi-valor kpi-valor-descuento">{formatearMoneda(datos.resumen.totalDescuentos)}</span>
            </div>
            <div className="kpi-card">
              <span className="kpi-label">Notas de crédito</span>
              <span className="kpi-valor kpi-valor-nc">{formatearMoneda(datos.resumen.totalNotasCredito)}</span>
              <span className="muted" style={{ fontSize: 12 }}>{datos.resumen.cantidadNotasCredito} emitidas</span>
            </div>
          </div>

          <div className="two-col" style={{ marginTop: 20 }}>
            <div>
              <h3>Evolución de ventas</h3>
              <GraficoLinea datos={datos.evolucion} />
            </div>
            <div>
              <h3>Familias más vendidas</h3>
              <GraficoTorta datos={datos.familiasMasVendidas.map((f) => ({ label: f.descripcion, total: f.total }))} />
            </div>
          </div>

          <div className="two-col" style={{ marginTop: 20 }}>
            <div>
              <h3>Sectores más consumidos</h3>
              <ListaRankeada items={datos.sectoresMasConsumidos.map((s) => ({
                label: s.descripcion, valor: s.total,
                valorLabel: `${formatearMoneda(s.total)} · ${s.participacion.toFixed(1)}%`,
              }))} />
            </div>
            <div>
              <h3>Productos más vendidos</h3>
              <ListaRankeada items={datos.productosMasVendidos.map((p) => ({
                label: p.descripcion, sub: p.codigoInterno, valor: p.total, valorLabel: formatearMoneda(p.total),
              }))} />
            </div>
          </div>

          <div className="two-col" style={{ marginTop: 20 }}>
            <div>
              <h3>Top clientes</h3>
              <ListaRankeada items={datos.topClientes.map((c) => ({
                label: c.descripcion, sub: `${c.cantidadTickets} tickets`, valor: c.total, valorLabel: formatearMoneda(c.total),
              }))} />
            </div>
            <div>
              <h3>Efectividad de ofertas</h3>
              {datos.ofertas.length === 0
                ? <p className="muted">No hubo ofertas aplicadas en el período.</p>
                : (
                  <table className="grid">
                    <thead><tr><th>Oferta</th><th>Veces aplicada</th><th>Descuento otorgado</th><th>Importe afectado</th></tr></thead>
                    <tbody>
                      {datos.ofertas.map((o, i) => (
                        <tr key={i}>
                          <td>{o.descripcion}</td>
                          <td className="mono">{o.vecesAplicada}</td>
                          <td className="mono">{formatearMoneda(o.descuentoOtorgado)}</td>
                          <td className="mono">{formatearMoneda(o.importeAfectado)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}

// Segundos transcurridos desde `desde`, actualizado cada segundo — solo para el cartelito
// "Actualizado hace Ns" del encabezado.
function useSegundosDesde(desde: Date | null): number {
  const [segs, setSegs] = useState(0);
  useEffect(() => {
    if (!desde) return;
    const calcular = () => setSegs(Math.max(0, Math.round((Date.now() - desde.getTime()) / 1000)));
    calcular();
    const id = setInterval(calcular, 1000);
    return () => clearInterval(id);
  }, [desde]);
  return segs;
}
