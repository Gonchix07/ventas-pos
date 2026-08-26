import { useEffect, useState } from "react";
import {
  cajaEstructura, referencias,
  type Lookup, type PuntoVenta, type Puesto, type CajaFisica, type TerminalTarjeta,
} from "../../shared/api/admin";

// ModalidadPuntoVenta.Presupuesto en el backend: no se asigna a una caja como su PV principal — es
// un único punto de venta compartido por toda la sucursal, que el servidor resuelve solo al
// facturar. Lo que SÍ se controla por caja es si esa caja lo tiene habilitado (ver AdmitePresupuesto).
const TIPO_PV_PRESUPUESTO = 3;

/**
 * Asignación de cajas: qué PC (puesto) y qué punto de venta usa cada caja física, y si esa caja
 * admite Presupuesto. Los puntos de venta en sí (tipos/altas de PV) se administran en
 * "Estructura de caja" — acá solo se referencian para armar el selector.
 */
export function AsignacionCajasPage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [suc, setSuc] = useState<number>(0);
  const [pvs, setPvs] = useState<PuntoVenta[]>([]);
  const [puestos, setPuestos] = useState<Puesto[]>([]);
  const [cajas, setCajas] = useState<CajaFisica[]>([]);
  const [terminales, setTerminales] = useState<TerminalTarjeta[]>([]);
  const [error, setError] = useState<string | null>(null);

  // Terminal elegida en el selector "Asignar terminal" de cada fila de caja (una caja puede tener
  // varias terminales; una terminal solo puede estar en UNA caja — se garantiza en el modelo, no acá).
  const [terminalSel, setTerminalSel] = useState<Record<number, number>>({});

  // inputs — Puestos (PC)
  const [puNombre, setPuNombre] = useState("");
  const [puEditId, setPuEditId] = useState<number | null>(null);
  // Ip ya no se edita a mano (ver tabla: se muestra el GUID del equipo vinculado en su lugar) —
  // se preserva tal cual estaba al guardar, para no pisarla con null al editar solo el nombre.
  const [puEditIpActual, setPuEditIpActual] = useState<string | null>(null);

  // inputs — Cajas
  const [cDesc, setCDesc] = useState(""); const [cPv, setCPv] = useState(0); const [cPuesto, setCPuesto] = useState<number | 0>(0);
  const [cAdmitePresupuesto, setCAdmitePresupuesto] = useState(true);
  const [cEditId, setCEditId] = useState<number | null>(null);

  useEffect(() => {
    referencias.sucursales().then((s) => { setSucursales(s); if (s.length) setSuc(s[0].id); }).catch(() => {});
  }, []);

  const cargar = async (s: number) => {
    if (!s) return;
    setError(null);
    try {
      const [p, pu, c, term] = await Promise.all([
        cajaEstructura.puntosVenta(s), cajaEstructura.puestos(s), cajaEstructura.cajas(s), cajaEstructura.terminales(s),
      ]);
      setPvs(p); setPuestos(pu); setCajas(c); setTerminales(term);
      // Una caja nunca se asigna directo al PV Presupuesto (ver TIPO_PV_PRESUPUESTO): el default
      // toma el primero que NO sea de ese tipo.
      const primerNoPresupuesto = p.find((x) => x.idTipoPuntoVenta !== TIPO_PV_PRESUPUESTO)?.idPuntoVenta;
      if (primerNoPresupuesto) setCPv((v) => v || primerNoPresupuesto);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(suc); /* eslint-disable-next-line */ }, [suc]);

  const cancelarPuesto = () => { setPuEditId(null); setPuNombre(""); setPuEditIpActual(null); };

  const editarCaja = (c: CajaFisica) => {
    setCEditId(c.idCaja); setCDesc(c.descripcion); setCPv(c.idPuntoVenta);
    setCPuesto(c.idPuestoAsignado ?? 0); setCAdmitePresupuesto(c.admitePresupuesto);
  };
  const cancelarCaja = () => {
    setCEditId(null); setCDesc(""); setCPuesto(0); setCAdmitePresupuesto(true);
  };

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(suc); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      <div className="page-head">
        <h1>Asignación de cajas</h1>
        <label className="inline-label">Sucursal
          <select value={suc} onChange={(e) => setSuc(Number(e.target.value))}>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}

      <div>
        <div>
          <h3>Puestos (PC)</h3>
          <p className="muted" style={{ marginTop: -8 }}>
            El nombre es solo una etiqueta. La caja se resuelve al loguear por el <strong>equipo vinculado</strong>:
            parate frente a la PC real de ese puesto (con la app abierta ahí) y tocá "Vincular este equipo" en su
            fila — no se puede vincular a distancia. No se asigna a mano: lo genera solo el navegador de esa PC.
          </p>
          <div className="toolbar">
            <input placeholder="Nombre (etiqueta)" value={puNombre} onChange={(e) => setPuNombre(e.target.value)} />
            <button className="primary" disabled={!puNombre.trim()}
              onClick={() => run(async () => {
                if (puEditId != null) await cajaEstructura.updatePuesto(suc, puEditId, puNombre.trim(), puEditIpActual);
                else await cajaEstructura.createPuesto(suc, puNombre.trim(), null);
                cancelarPuesto();
              })}>{puEditId != null ? "Guardar" : "Agregar"}</button>
            {puEditId != null && <button onClick={cancelarPuesto}>Cancelar</button>}
          </div>
          <table className="grid">
            <thead><tr><th>ID</th><th>Nombre</th><th>Equipo (GUID)</th><th></th></tr></thead>
            <tbody>
              {puestos.map((p) => (
                <tr key={p.idPuestoAsignado}>
                  <td className="mono">{p.idPuestoAsignado}</td><td>{p.nombrePc}</td>
                  <td className="mono">
                    {p.identificadorEquipo ?? <span className="muted">Sin vincular</span>}
                  </td>
                  <td>
                    <button title="Vincula este puesto a la PC desde la que estás usando la app AHORA"
                      onClick={() => run(() => cajaEstructura.vincularEquipo(suc, p.idPuestoAsignado))}>
                      Vincular este equipo
                    </button>
                    <button onClick={() => { setPuEditId(p.idPuestoAsignado); setPuNombre(p.nombrePc); setPuEditIpActual(p.ip ?? null); }}>✎</button>
                    <button className="danger" onClick={() => run(() => cajaEstructura.removePuesto(suc, p.idPuestoAsignado))}>×</button>
                  </td>
                </tr>
              ))}
              {puestos.length === 0 && <tr><td colSpan={4} className="muted">Sin puestos.</td></tr>}
            </tbody>
          </table>
        </div>

        <div>
          <h3 style={{ marginTop: 20 }}>Cajas</h3>
          <p className="muted" style={{ marginTop: -8 }}>
            Cada caja factura por UN punto de venta Fiscal o Electrónica (nunca los dos a la vez).
            El Presupuesto es un punto de venta único de la sucursal, no se elige acá — pero cada
            caja puede habilitarlo o no con el tilde de abajo (además el cliente necesita su propio
            permiso para comprar con Presupuesto). Los puntos de venta en sí se dan de alta en
            «Estructura de caja».
          </p>
          <div className="toolbar">
            <input placeholder="Descripción" value={cDesc} onChange={(e) => setCDesc(e.target.value)} />
            <select value={cPv} onChange={(e) => setCPv(Number(e.target.value))}>
              {pvs.filter((p) => p.idTipoPuntoVenta !== TIPO_PV_PRESUPUESTO).map((p) => (
                <option key={p.idPuntoVenta} value={p.idPuntoVenta}>
                  {p.tipoDescripcion ?? "PV"} {p.numeroPuntoVenta}
                </option>
              ))}
            </select>
            <select value={cPuesto} onChange={(e) => setCPuesto(Number(e.target.value))}>
              <option value={0}>(sin puesto)</option>
              {puestos.map((p) => <option key={p.idPuestoAsignado} value={p.idPuestoAsignado}>{p.nombrePc}</option>)}
            </select>
            <label className="check-box">
              <input type="checkbox" checked={cAdmitePresupuesto}
                onChange={(e) => setCAdmitePresupuesto(e.target.checked)} />
              Admite Presupuesto
            </label>
            <button className="primary" disabled={!cDesc.trim() || !cPv}
              onClick={() => run(async () => {
                if (cEditId) {
                  await cajaEstructura.updateCaja(suc, cEditId, cPv, cDesc.trim(), cPuesto || null, cAdmitePresupuesto);
                } else {
                  await cajaEstructura.createCaja(suc, cPv, cDesc.trim(), cPuesto || null, cAdmitePresupuesto);
                }
                cancelarCaja();
              })}>{cEditId ? "Guardar" : "Agregar"}</button>
            {cEditId && <button onClick={cancelarCaja}>Cancelar</button>}
          </div>
          <table className="grid">
            <thead><tr><th>ID</th><th>Descripción</th><th>PV</th><th>Puesto</th><th>Presupuesto</th><th>Terminales de tarjeta</th><th></th></tr></thead>
            <tbody>
              {cajas.map((c) => {
                const pv = pvs.find((p) => p.idPuntoVenta === c.idPuntoVenta);
                const asignadas = terminales.filter((t) => t.idCajaAsignada === c.idCaja);
                const disponibles = terminales.filter((t) => t.idCajaAsignada == null);
                const selId = terminalSel[c.idCaja] ?? disponibles[0]?.idTerminal ?? 0;
                return (
                  <tr key={c.idCaja}>
                    <td className="mono">{c.idCaja}</td><td>{c.descripcion}</td>
                    <td className="mono">{pv ? `${pv.tipoDescripcion ?? "PV"} ${pv.numeroPuntoVenta}` : c.idPuntoVenta}</td>
                    <td className="mono">{c.nombrePc}</td>
                    <td>{c.admitePresupuesto ? <span className="badge on">Sí</span> : <span className="badge off">No</span>}</td>
                    <td>
                      <div className="chip-list">
                        {asignadas.map((t) => (
                          <span key={t.idTerminal} className="chip">
                            {t.numeroTerminal} ({t.tipoDescripcion})
                            <button title="Desasignar"
                              onClick={() => run(() => cajaEstructura.updateTerminal(suc, t.idTerminal, t.numeroTerminal, t.tipo, null))}>
                              ×
                            </button>
                          </span>
                        ))}
                        {asignadas.length === 0 && <span className="muted">sin terminales</span>}
                        {disponibles.length > 0 && (
                          <div className="row-actions">
                            <select value={selId} onChange={(e) => setTerminalSel((m) => ({ ...m, [c.idCaja]: Number(e.target.value) }))}>
                              {disponibles.map((t) => (
                                <option key={t.idTerminal} value={t.idTerminal}>{t.numeroTerminal} ({t.tipoDescripcion})</option>
                              ))}
                            </select>
                            <button onClick={() => run(async () => {
                              const t = disponibles.find((x) => x.idTerminal === selId);
                              if (t) await cajaEstructura.updateTerminal(suc, t.idTerminal, t.numeroTerminal, t.tipo, c.idCaja);
                            })}>
                              Asignar
                            </button>
                          </div>
                        )}
                      </div>
                    </td>
                    <td>
                      <button onClick={() => editarCaja(c)}>✎</button>
                      <button className="danger" onClick={() => run(() => cajaEstructura.removeCaja(suc, c.idCaja))}>×</button>
                    </td>
                  </tr>
                );
              })}
              {cajas.length === 0 && <tr><td colSpan={7} className="muted">Sin cajas.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
