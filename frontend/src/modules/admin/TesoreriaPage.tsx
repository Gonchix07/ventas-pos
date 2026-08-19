import { Fragment, useEffect, useState } from "react";
import {
  tesoreria, type LoteResumen, type LoteDetalle, type MotivoCierre,
} from "../../shared/api/tesoreria";
import { referencias, type Lookup } from "../../shared/api/admin";
import { useAuth } from "../../shared/auth/auth";
import { formatearMoneda } from "../../shared/ui/moneda";
import { EntregaValoresModal } from "./EntregaValoresModal";
import { ComprobantesLoteModal } from "./ComprobantesLoteModal";
import { useNavigate } from "react-router-dom";

const hoy = () => new Date();
const fechaISO = (d: Date) => d.toISOString().slice(0, 10);

const ESTADOS_TESORERIA = [
  { v: "Abierto", l: "Cerrar caja" },
  { v: "CierreCajero", l: "Pendiente" },
  { v: "CierreTesoreria", l: "Validado" },
];
const esMismoDia = (isoA: string, b: Date) => new Date(isoA).toDateString() === b.toDateString();

// Columna "Tesorería": no es el estado del lote (Abierto/Cerrado, esa es otra columna), es en qué
// paso está de cara a Tesorería — de ahí que "Abierto" se lea acá como "hay que cerrar la caja".
const claseEstadoCierre = (e: string) =>
  e === "CierreTesoreria" ? "badge on" : e === "CierreCajero" ? "badge warn" : "badge off";
const textoEstadoCierre = (e: string) =>
  e === "CierreTesoreria" ? "Validado" : e === "CierreCajero" ? "Pendiente" : "Cerrar caja";

/**
 * Vista principal de Tesorería: un lote (turno de cajero) por fila, abierto o cerrado, dentro de la
 * vigencia elegida (por defecto hoy). Reemplaza al dashboard/cierres/lotes-pendientes de la versión
 * anterior — todo eso ahora vive en esta única tabla + su subfila de detalle.
 */
export function TesoreriaPage() {
  const { usuario, rol, logout, ip } = useAuth();
  const navigate = useNavigate();

  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [idSucursal, setIdSucursal] = useState<number | 0>(0);
  const [desde, setDesde] = useState(fechaISO(hoy()));
  const [hasta, setHasta] = useState(fechaISO(hoy()));
  const [estadoTesoreria, setEstadoTesoreria] = useState("");

  const [lotes, setLotes] = useState<LoteResumen[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  const [motivos, setMotivos] = useState<MotivoCierre[]>([]);
  const [motivosDif, setMotivosDif] = useState<MotivoCierre[]>([]);

  // Subfila expandida: detalle de rendición de un solo lote a la vez.
  const [expandido, setExpandido] = useState<string | null>(null);
  const [detalle, setDetalle] = useState<LoteDetalle | null>(null);
  const [detalleError, setDetalleError] = useState<string | null>(null);

  // Cierre administrativo de un lote pendiente (Abierto, de un día anterior).
  const [declarado, setDeclarado] = useState<Record<number, string>>({});
  const [idMotivoDif, setIdMotivoDif] = useState<number | 0>(0);
  const [idMotivoCierrePend, setIdMotivoCierrePend] = useState<number | 0>(0);
  const [obsPend, setObsPend] = useState("");
  const [cerrando, setCerrando] = useState(false);

  // Validación de tesorería sobre un lote ya cerrado por el cajero.
  const [idMotivoCierreValidar, setIdMotivoCierreValidar] = useState<number | 0>(0);
  const [obsValidar, setObsValidar] = useState("");
  const [validando, setValidando] = useState(false);

  const [aviso, setAviso] = useState<string | null>(null);

  // Popups.
  const [entregaValores, setEntregaValores] = useState<LoteResumen | null>(null);
  const [comprobantes, setComprobantes] = useState<
    { idSucursal: number; idLote: number; idMedioPago?: number; medioDescripcion?: string } | null
  >(null);

  const cargar = async () => {
    setError(null); setCargando(true);
    try {
      setLotes(await tesoreria.lotes(idSucursal || undefined, desde, hasta));
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setCargando(false); }
  };

  useEffect(() => {
    referencias.sucursales().then(setSucursales).catch(() => {});
    tesoreria.motivosCierre().then(setMotivos).catch(() => {});
    tesoreria.motivosDiferencia().then(setMotivosDif).catch(() => {});
  }, []);
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, [idSucursal, desde, hasta]);

  // El estado de Tesorería es calculado (no viene filtrable del backend): se filtra acá, sobre lo
  // ya cargado para la vigencia elegida.
  const lotesFiltrados = estadoTesoreria
    ? lotes.filter((l) => l.estadoCierre === estadoTesoreria)
    : lotes;

  const clave = (l: LoteResumen) => `${l.idSucursal}-${l.idLote}`;

  const cargarDetalle = async (l: LoteResumen) => {
    setDetalleError(null); setDetalle(null);
    try {
      const d = await tesoreria.detalleLote(l.idSucursal, l.idLote);
      setDetalle(d);
      // Se arranca declarando lo esperado: el caso habitual es confirmar los números del sistema,
      // así que una diferencia queda siempre como resultado de un cambio explícito de Tesorería.
      setDeclarado(Object.fromEntries(d.acumulados.map((a) => [a.idMedioPago, a.total.toFixed(2)])));
    } catch (e) { setDetalleError(e instanceof Error ? e.message : "Error"); }
  };

  const toggleExpandir = (l: LoteResumen) => {
    const k = clave(l);
    if (expandido === k) { setExpandido(null); setDetalle(null); return; }
    setExpandido(k);
    setIdMotivoDif(0); setIdMotivoCierrePend(0); setObsPend("");
    setIdMotivoCierreValidar(0); setObsValidar("");
    void cargarDetalle(l);
  };

  const refrescarTodo = async (l: LoteResumen) => {
    await cargar();
    await cargarDetalle(l);
  };

  const totalDeclarado = () =>
    Object.values(declarado).reduce((s, v) => s + (Number(v) || 0), 0);

  const cerrarPendiente = async (l: LoteResumen) => {
    if (!detalle) return;
    setError(null); setAviso(null);
    if (!idMotivoCierrePend) { setError("Elegí un motivo de cierre para regularizar el lote."); return; }
    const declaraciones = detalle.acumulados.map((a) => ({
      idMedioPago: a.idMedioPago, montoDeclarado: Number(declarado[a.idMedioPago] ?? "0") || 0,
    }));
    setCerrando(true);
    try {
      const r = await tesoreria.cerrarLotePendiente(l.idSucursal, l.idLote, declaraciones,
        idMotivoDif || null, idMotivoCierrePend, obsPend || null);
      setAviso(`Lote ${l.idLote} cerrado (cierre N° ${r.numeroCierre}). Queda pendiente de validación.`);
      await refrescarTodo(l);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setCerrando(false); }
  };

  const validarLote = async (l: LoteResumen) => {
    setError(null); setAviso(null);
    setValidando(true);
    try {
      await tesoreria.validar(l.idSucursal, l.idLote, idMotivoCierreValidar || null, obsValidar || null);
      setAviso(`Lote ${l.idLote} validado.`);
      await refrescarTodo(l);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setValidando(false); }
  };

  return (
    <div className="page-shell">
      <div className="page-head">
        <h1>Tesorería</h1>
        <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
          <button onClick={() => navigate("/tesoreria/cupones")}>Cupones de tarjeta</button>
          <span className="muted">{usuario} · {rol}</span>
          <span className="mono ip-badge">IP {ip ?? "—"}</span>
          <button onClick={logout}>Salir</button>
        </div>
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
        <label>Estado (Tesorería)
          <select value={estadoTesoreria} onChange={(e) => setEstadoTesoreria(e.target.value)}>
            <option value="">(todos)</option>
            {ESTADOS_TESORERIA.map((e) => <option key={e.v} value={e.v}>{e.l}</option>)}
          </select>
        </label>
        <span className="filter-count">
          {cargando ? "Buscando…" : `${lotesFiltrados.length} lote${lotesFiltrados.length === 1 ? "" : "s"}`}
        </span>
        {/* Sin función todavía. */}
        <button className="primary" style={{ marginLeft: "auto" }}>Imprimir reporte</button>
      </div>

      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      <div className="table-scroll">
        <table className="grid">
          <thead>
            <tr>
              <th></th><th>Lote</th><th>Usuario</th>
              <th>Apertura</th><th>Cierre</th><th>Estado</th><th>Tesorería</th>
              <th>Saldo inicial</th><th>Rendición</th><th>Cambio</th>
              <th>Saldo esperado</th><th>Saldo</th><th></th>
            </tr>
          </thead>
          <tbody>
            {lotesFiltrados.map((l) => {
              const k = clave(l);
              const expandidoAca = expandido === k;
              const esPendiente = l.estadoLote === "Abierto" && !esMismoDia(l.fechaApertura, hoy());
              return (
                <Fragment key={k}>
                  <tr className={expandidoAca ? "lote-sel" : ""} style={{ cursor: "pointer" }}
                    onClick={() => toggleExpandir(l)}>
                    <td>{expandidoAca ? "▾" : "▸"}</td>
                    <td className="mono">{l.idLote}</td>
                    <td>{l.usuario ?? "—"}</td>
                    <td>{new Date(l.fechaApertura).toLocaleString()}</td>
                    <td>{l.fechaCierre ? new Date(l.fechaCierre).toLocaleString() : "—"}</td>
                    <td><span className={`badge ${l.estadoLote === "Abierto" ? "on" : "off"}`}>{l.estadoLote}</span></td>
                    <td><span className={claseEstadoCierre(l.estadoCierre)}>{textoEstadoCierre(l.estadoCierre)}</span></td>
                    <td className="mono">{formatearMoneda(l.saldoInicial)}</td>
                    <td className="mono">{formatearMoneda(l.rendicionTotal)}</td>
                    <td className="mono">{formatearMoneda(l.cambioAcumulado)}</td>
                    <td className="mono">{formatearMoneda(l.saldoEsperado)}</td>
                    <td className="mono">{l.saldo != null ? formatearMoneda(l.saldo) : "—"}</td>
                    <td className="row-actions" onClick={(e) => e.stopPropagation()}>
                      <button onClick={() => setEntregaValores(l)}>Entrega de valores</button>
                    </td>
                  </tr>
                  {expandidoAca && (
                    <tr>
                      <td colSpan={13} style={{ background: "#fafbfa" }}>
                        {detalleError && <p className="error">{detalleError}</p>}
                        {!detalle && !detalleError && <p className="muted">Cargando…</p>}
                        {detalle && (
                          <div style={{ display: "flex", flexDirection: "column", gap: 14, padding: "10px 4px" }}>
                            <div>
                              <h4 style={{ margin: "0 0 4px" }}>Rendición por medio de pago</h4>
                              <table className="grid">
                                <thead>
                                  <tr>
                                    <th>Medio</th><th>Esperado</th>
                                    {detalle.declarado.length > 0 && <><th>Declarado</th><th>Diferencia</th></>}
                                  </tr>
                                </thead>
                                <tbody>
                                  {detalle.acumulados.map((a) => {
                                    const d = detalle.declarado.find((x) => x.idMedioPago === a.idMedioPago);
                                    return (
                                      <tr key={a.idMedioPago}>
                                        <td>
                                          <button className="link-btn"
                                            onClick={() => setComprobantes({
                                              idSucursal: l.idSucursal, idLote: l.idLote,
                                              idMedioPago: a.idMedioPago, medioDescripcion: a.descripcion,
                                            })}>
                                            {a.descripcion}
                                          </button>
                                        </td>
                                        <td className="mono">{formatearMoneda(a.total)}</td>
                                        {detalle.declarado.length > 0 && (
                                          <>
                                            <td className="mono">{d ? formatearMoneda(d.declarado) : "—"}</td>
                                            <td className={d && Math.abs(d.diferencia) > 0.01 ? "error" : "mono"}>
                                              {d ? formatearMoneda(d.diferencia) : "—"}
                                            </td>
                                          </>
                                        )}
                                      </tr>
                                    );
                                  })}
                                  {detalle.acumulados.length === 0 && (
                                    <tr><td colSpan={detalle.declarado.length > 0 ? 4 : 2} className="muted">Sin movimientos.</td></tr>
                                  )}
                                  {detalle.declarado.length > 0 && detalle.vueltos.length > 0 && (
                                    <tr>
                                      <td>Vueltos</td>
                                      <td className="mono">
                                        {formatearMoneda(detalle.vueltos.reduce((s, v) => s + v.monto, 0))}
                                      </td>
                                      <td className="muted">—</td>
                                      <td className="muted">—</td>
                                    </tr>
                                  )}
                                </tbody>
                              </table>
                              <button style={{ marginTop: 6 }}
                                onClick={() => setComprobantes({ idSucursal: l.idSucursal, idLote: l.idLote })}>
                                Ver todos los comprobantes del lote
                              </button>
                            </div>

                            {detalle.ingresoInicial && (
                              <p className="muted" style={{ margin: 0 }}>
                                Fondo inicial: {formatearMoneda(detalle.ingresoInicial.monto)}
                                {detalle.ingresoInicial.concepto ? ` — ${detalle.ingresoInicial.concepto}` : ""}
                              </p>
                            )}

                            {(detalle.retiros.length > 0 || detalle.correcciones.length > 0
                              || detalle.anulaciones.length > 0) && (
                              <div style={{ display: "flex", gap: 24, flexWrap: "wrap" }}>
                                {detalle.retiros.length > 0 && (
                                  <div>
                                    <strong>Retiros</strong>
                                    <ul>
                                      {detalle.retiros.map((r) => (
                                        <li key={r.idMovCaja}>{formatearMoneda(r.monto)} — {r.concepto ?? "Retiro"} ({r.usuario ?? "—"})</li>
                                      ))}
                                    </ul>
                                  </div>
                                )}
                                {detalle.correcciones.length > 0 && (
                                  <div>
                                    <strong>Correcciones de Tesorería</strong>
                                    <ul>
                                      {detalle.correcciones.map((c) => (
                                        <li key={c.idMovCaja}>{formatearMoneda(c.monto)} — {c.concepto ?? "—"} ({c.usuario ?? "—"})</li>
                                      ))}
                                    </ul>
                                  </div>
                                )}
                                {detalle.anulaciones.length > 0 && (
                                  <div>
                                    <strong>Notas de crédito</strong>
                                    <ul>
                                      {detalle.anulaciones.map((a) => (
                                        <li key={a.idComprobante}>{formatearMoneda(a.total)} — {a.motivo ?? "—"}</li>
                                      ))}
                                    </ul>
                                  </div>
                                )}
                              </div>
                            )}

                            {l.estadoLote === "Cerrado" && (
                              <p className="muted" style={{ margin: 0 }}>
                                <strong>Motivo de cierre:</strong> {detalle.motivoCierreDescripcion ?? "—"}
                                {" · "}
                                <strong>Observaciones del cajero:</strong> {detalle.observacionesCajero ?? "—"}
                              </p>
                            )}

                            {esPendiente && (
                              <div>
                                <h4 style={{ margin: "0 0 4px" }}>Cerrar lote pendiente</h4>
                                <p className="muted" style={{ margin: "0 0 6px" }}>
                                  Quedó abierto sin cierre Z. El cajero ya no puede cerrarlo desde Caja: se
                                  regulariza acá. No se imprime Z fiscal (la impresora está en la caja física).
                                </p>
                                <table className="grid">
                                  <thead><tr><th>Medio</th><th>Esperado</th><th>Declarar</th><th>Diferencia</th></tr></thead>
                                  <tbody>
                                    {detalle.acumulados.map((a) => {
                                      const dec = Number(declarado[a.idMedioPago] ?? "0") || 0;
                                      const dif = dec - a.total;
                                      return (
                                        <tr key={a.idMedioPago}>
                                          <td>{a.descripcion}</td>
                                          <td className="mono">{formatearMoneda(a.total)}</td>
                                          <td>
                                            <input type="number" step="0.01" style={{ width: 110 }}
                                              value={declarado[a.idMedioPago] ?? ""}
                                              onChange={(e) => setDeclarado({ ...declarado, [a.idMedioPago]: e.target.value })} />
                                          </td>
                                          <td className={Math.abs(dif) > 0.01 ? "error" : "mono"}>{formatearMoneda(dif)}</td>
                                        </tr>
                                      );
                                    })}
                                  </tbody>
                                </table>
                                <div className="row-actions" style={{ marginTop: 8 }}>
                                  <label className="inline-label">Motivo de cierre *
                                    <select value={idMotivoCierrePend} onChange={(e) => setIdMotivoCierrePend(Number(e.target.value))}>
                                      <option value={0}>(elegir)</option>
                                      {motivos.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                                    </select>
                                  </label>
                                  {Math.abs(totalDeclarado() - detalle.acumulados.reduce((s, a) => s + a.total, 0)) > 0.01 && (
                                    <label className="inline-label">Motivo de diferencia *
                                      <select value={idMotivoDif} onChange={(e) => setIdMotivoDif(Number(e.target.value))}>
                                        <option value={0}>(elegir)</option>
                                        {motivosDif.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                                      </select>
                                    </label>
                                  )}
                                  <input placeholder="Observación" value={obsPend}
                                    onChange={(e) => setObsPend(e.target.value)} style={{ width: 200 }} />
                                  <button className="primary" disabled={cerrando} onClick={() => cerrarPendiente(l)}>
                                    {cerrando ? "Cerrando…" : "Confirmar cierre"}
                                  </button>
                                </div>
                                <p className="muted">El cierre es irreversible.</p>
                              </div>
                            )}

                            {l.estadoCierre === "CierreCajero" && (
                              <div>
                                <h4 style={{ margin: "0 0 4px" }}>Validar cierre</h4>
                                <div className="row-actions">
                                  <select value={idMotivoCierreValidar} onChange={(e) => setIdMotivoCierreValidar(Number(e.target.value))}>
                                    <option value={0}>(motivo de cierre, opcional)</option>
                                    {motivos.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                                  </select>
                                  <input placeholder="Observación" value={obsValidar}
                                    onChange={(e) => setObsValidar(e.target.value)} style={{ width: 200 }} />
                                  <button className="primary" disabled={validando} onClick={() => validarLote(l)}>
                                    {validando ? "Validando…" : "Confirmar validación"}
                                  </button>
                                </div>
                              </div>
                            )}
                          </div>
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
            {lotesFiltrados.length === 0 && !cargando && (
              <tr><td colSpan={13} className="muted">Sin lotes en la vigencia elegida.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {entregaValores && (
        <EntregaValoresModal lote={entregaValores} onCerrar={() => setEntregaValores(null)}
          onGuardado={() => { void refrescarTodo(entregaValores); }} />
      )}
      {comprobantes && (
        <ComprobantesLoteModal idSucursal={comprobantes.idSucursal} idLote={comprobantes.idLote}
          idMedioPago={comprobantes.idMedioPago} medioDescripcion={comprobantes.medioDescripcion}
          onCerrar={() => setComprobantes(null)} />
      )}
    </div>
  );
}
