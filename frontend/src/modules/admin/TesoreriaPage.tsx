import { Fragment, useEffect, useState } from "react";
import { tesoreria, type Dashboard, type CierreListItem, type MotivoCierre, type LotePendiente } from "../../shared/api/tesoreria";
import { referencias, type Lookup } from "../../shared/api/admin";
import { useAuth } from "../../shared/auth/auth";

export function TesoreriaPage() {
  const { usuario, rol, logout, ip } = useAuth();
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [idSucursal, setIdSucursal] = useState<number | 0>(0);
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [cierres, setCierres] = useState<CierreListItem[]>([]);
  const [motivos, setMotivos] = useState<MotivoCierre[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [validando, setValidando] = useState<number | null>(null);
  const [idMotivoCierre, setIdMotivoCierre] = useState<number | 0>(0);
  const [observacion, setObservacion] = useState("");

  // Cierre administrativo de lotes que quedaron abiertos en días anteriores.
  const [pendientes, setPendientes] = useState<LotePendiente[]>([]);
  const [motivosDif, setMotivosDif] = useState<MotivoCierre[]>([]);
  const [cerrando, setCerrando] = useState<number | null>(null);
  const [declarado, setDeclarado] = useState<Record<number, string>>({});
  const [idMotivoDif, setIdMotivoDif] = useState<number | 0>(0);
  const [idMotivoCierrePend, setIdMotivoCierrePend] = useState<number | 0>(0);
  const [obsPend, setObsPend] = useState("");
  const [aviso, setAviso] = useState<string | null>(null);

  const cargar = async () => {
    setError(null);
    try {
      const suc = idSucursal || undefined;
      const [d, c, p] = await Promise.all([
        tesoreria.dashboard(suc), tesoreria.cierres(suc), tesoreria.lotesPendientes(suc),
      ]);
      setDashboard(d); setCierres(c); setPendientes(p);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  useEffect(() => {
    referencias.sucursales().then(setSucursales).catch(() => {});
    tesoreria.motivosCierre().then(setMotivos).catch(() => {});
    tesoreria.motivosDiferencia().then(setMotivosDif).catch(() => {});
  }, []);
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, [idSucursal]);

  const validar = async (idSuc: number, idLote: number) => {
    setError(null);
    try {
      await tesoreria.validar(idSuc, idLote, idMotivoCierre || null, observacion || null);
      setValidando(null); setIdMotivoCierre(0); setObservacion("");
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const abrirCierrePendiente = (l: LotePendiente) => {
    // Se arranca declarando lo esperado: el caso habitual es confirmar los números del sistema, y
    // así una diferencia queda siempre como resultado de un cambio explícito de Tesorería.
    setCerrando(l.idLote);
    setDeclarado(Object.fromEntries(l.acumulados.map((a) => [a.idMedioPago, a.total.toFixed(2)])));
    setIdMotivoDif(0); setIdMotivoCierrePend(0); setObsPend(""); setError(null); setAviso(null);
  };

  const cerrarPendiente = async (l: LotePendiente) => {
    setError(null); setAviso(null);
    if (!idMotivoCierrePend) { setError("Elegí un motivo de cierre para regularizar el lote."); return; }
    const declaraciones = l.acumulados.map((a) => ({
      idMedioPago: a.idMedioPago,
      montoDeclarado: Number(declarado[a.idMedioPago] ?? "0") || 0,
    }));
    try {
      const r = await tesoreria.cerrarLotePendiente(l.idSucursal, l.idLote, declaraciones,
        idMotivoDif || null, idMotivoCierrePend, obsPend || null);
      setCerrando(null);
      setAviso(`Lote ${l.idLote} cerrado (cierre N° ${r.numeroCierre}). Queda pendiente de validación.`);
      await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const totalDeclarado = (l: LotePendiente) =>
    l.acumulados.reduce((s, a) => s + (Number(declarado[a.idMedioPago] ?? "0") || 0), 0);

  return (
    <div className="page-shell">
      <div className="page-head">
        <h1>Tesorería</h1>
        <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
          <label className="inline-label">Sucursal
            <select value={idSucursal} onChange={(e) => setIdSucursal(Number(e.target.value))}>
              <option value={0}>(todas)</option>
              {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
            </select>
          </label>
          <span className="muted">{usuario} · {rol}</span>
          <span className="mono ip-badge">IP {ip ?? "—"}</span>
          <button onClick={logout}>Salir</button>
        </div>
      </div>
      {error && <p className="error">{error}</p>}
      {aviso && <p className="ok-msg">{aviso}</p>}

      {pendientes.length > 0 && (
        <>
          <h3>Lotes pendientes de días anteriores ({pendientes.length})</h3>
          <p className="muted">
            Quedaron abiertos sin cierre Z. El cajero ya no puede cerrarlos desde Caja, así que se
            regularizan acá. No se imprime Z fiscal: la impresora está en la caja física.
          </p>
          <table className="grid">
            <thead><tr><th>Sucursal</th><th>Caja</th><th>Lote</th><th>Cajero</th><th>Apertura</th><th>Días</th><th>Esperado</th><th></th></tr></thead>
            <tbody>
              {pendientes.map((l) => (
                <Fragment key={`${l.idSucursal}-${l.idLote}`}>
                  <tr>
                    <td>{l.sucursalDescripcion}</td>
                    <td>{l.cajaDescripcion}</td>
                    <td className="mono">{l.idLote}</td>
                    <td>{l.cajero ?? "—"}</td>
                    <td>{new Date(l.fechaApertura).toLocaleString()}</td>
                    <td className="mono">{l.diasPendiente}</td>
                    <td className="mono">${l.totalEsperado.toFixed(2)}</td>
                    <td>
                      {cerrando === l.idLote
                        ? <button onClick={() => setCerrando(null)}>Cancelar</button>
                        : <button onClick={() => abrirCierrePendiente(l)}>Cerrar lote</button>}
                    </td>
                  </tr>
                  {cerrando === l.idLote && (
                    <tr>
                      <td colSpan={8}>
                        <div style={{ display: "flex", flexDirection: "column", gap: 8, padding: "8px 0" }}>
                          {l.acumulados.length === 0
                            ? <p className="muted">El lote no tiene movimientos: se cierra en $0.</p>
                            : (
                              <table className="grid">
                                <thead><tr><th>Medio</th><th>Esperado</th><th>Declarado</th><th>Diferencia</th></tr></thead>
                                <tbody>
                                  {l.acumulados.map((a) => {
                                    const dec = Number(declarado[a.idMedioPago] ?? "0") || 0;
                                    const dif = dec - a.total;
                                    return (
                                      <tr key={a.idMedioPago}>
                                        <td>{a.descripcion}</td>
                                        <td className="mono">${a.total.toFixed(2)}</td>
                                        <td>
                                          <input type="number" step="0.01" style={{ width: 110 }}
                                            value={declarado[a.idMedioPago] ?? ""}
                                            onChange={(e) => setDeclarado({ ...declarado, [a.idMedioPago]: e.target.value })} />
                                        </td>
                                        <td className={Math.abs(dif) > 0.01 ? "error" : "mono"}>${dif.toFixed(2)}</td>
                                      </tr>
                                    );
                                  })}
                                  <tr>
                                    <td><strong>Total</strong></td>
                                    <td className="mono">${l.totalEsperado.toFixed(2)}</td>
                                    <td className="mono">${totalDeclarado(l).toFixed(2)}</td>
                                    <td className={Math.abs(totalDeclarado(l) - l.totalEsperado) > 0.01 ? "error" : "mono"}>
                                      ${(totalDeclarado(l) - l.totalEsperado).toFixed(2)}
                                    </td>
                                  </tr>
                                </tbody>
                              </table>
                            )}
                          <div className="row-actions">
                            <label className="inline-label">Motivo de cierre *
                              <select value={idMotivoCierrePend} onChange={(e) => setIdMotivoCierrePend(Number(e.target.value))}>
                                <option value={0}>(elegir)</option>
                                {motivos.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                              </select>
                            </label>
                            {Math.abs(totalDeclarado(l) - l.totalEsperado) > 0.01 && (
                              <label className="inline-label">Motivo de diferencia *
                                <select value={idMotivoDif} onChange={(e) => setIdMotivoDif(Number(e.target.value))}>
                                  <option value={0}>(elegir)</option>
                                  {motivosDif.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                                </select>
                              </label>
                            )}
                            <input placeholder="Observación de tesorería" value={obsPend}
                              onChange={(e) => setObsPend(e.target.value)} style={{ width: 220 }} />
                            <button className="primary" onClick={() => cerrarPendiente(l)}>Confirmar cierre</button>
                          </div>
                          <p className="muted">El cierre es irreversible.</p>
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </>
      )}

      {dashboard && (
        <>
          <h3>Cajas</h3>
          <table className="grid">
            <thead><tr><th>Sucursal</th><th>Caja</th><th>Estado</th><th>Lote</th><th>Cajero</th><th>Apertura</th><th>Cierre</th><th>Total</th></tr></thead>
            <tbody>
              {dashboard.cajas.map((c) => (
                <tr key={`${c.idSucursal}-${c.idCaja}-${c.idLote ?? "sin-lote"}`}>
                  <td>{c.sucursalDescripcion}</td>
                  <td>{c.cajaDescripcion}</td>
                  <td><span className={`badge ${c.estado === "Abierto" ? "on" : "off"}`}>{c.estado}</span></td>
                  <td className="mono">{c.idLote ?? "—"}</td>
                  <td>{c.cajero ?? "—"}</td>
                  <td>{c.fechaApertura ? new Date(c.fechaApertura).toLocaleString() : "—"}</td>
                  <td>{c.fechaCierre ? new Date(c.fechaCierre).toLocaleString() : "—"}</td>
                  <td className="mono">{c.totalLote != null ? `$${c.totalLote.toFixed(2)}` : "—"}</td>
                </tr>
              ))}
              {dashboard.cajas.length === 0 && <tr><td colSpan={8} className="muted">Sin cajas.</td></tr>}
            </tbody>
          </table>

          <h3 style={{ marginTop: 20 }}>Acumulado del día por medio de pago (total: ${dashboard.acumuladoGeneral.toFixed(2)})</h3>
          <table className="grid">
            <thead><tr><th>Medio</th><th>Total</th><th>Redondeo</th></tr></thead>
            <tbody>
              {dashboard.acumuladoPorMedio.map((a) => (
                <tr key={a.idMedioPago}><td>{a.descripcion}</td><td className="mono">${a.total.toFixed(2)}</td><td className="mono">${a.redondeo.toFixed(2)}</td></tr>
              ))}
              {dashboard.acumuladoPorMedio.length === 0 && <tr><td colSpan={3} className="muted">Sin movimientos hoy.</td></tr>}
            </tbody>
          </table>
        </>
      )}

      <h3 style={{ marginTop: 20 }}>Cierres (por lote y medio de pago)</h3>
      <table className="grid">
        <thead><tr><th>Sucursal</th><th>Caja</th><th>Lote</th><th>Medio</th><th>Total</th><th>Diferencia</th><th>Validado</th><th></th></tr></thead>
        <tbody>
          {cierres.map((c) => (
            <tr key={`${c.idSucursal}-${c.idLote}-${c.idMedioPago}`}>
              <td className="mono">{c.idSucursal}</td>
              <td className="mono">{c.idCaja}</td>
              <td className="mono">{c.idLote}</td>
              <td>{c.medioDescripcion}</td>
              <td className="mono">${c.total.toFixed(2)}</td>
              <td className={Math.abs(c.diferenciaTotal) > 0.01 ? "error" : "mono"}>${c.diferenciaTotal.toFixed(2)}</td>
              <td>{c.verificaTesoreria ? <span className="badge on">Validado</span> : <span className="badge off">Pendiente</span>}</td>
              <td>
                {!c.verificaTesoreria && (
                  validando === c.idLote ? (
                    <div className="row-actions">
                      <select value={idMotivoCierre} onChange={(e) => setIdMotivoCierre(Number(e.target.value))}>
                        <option value={0}>(motivo de cierre)</option>
                        {motivos.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
                      </select>
                      <input placeholder="Observación" value={observacion} onChange={(e) => setObservacion(e.target.value)} style={{ width: 140 }} />
                      <button className="primary" onClick={() => validar(c.idSucursal, c.idLote)}>Confirmar</button>
                      <button onClick={() => setValidando(null)}>Cancelar</button>
                    </div>
                  ) : (
                    <button onClick={() => setValidando(c.idLote)}>Validar</button>
                  )
                )}
              </td>
            </tr>
          ))}
          {cierres.length === 0 && <tr><td colSpan={8} className="muted">Sin cierres registrados.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
