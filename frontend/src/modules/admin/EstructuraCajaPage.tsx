import { useEffect, useState } from "react";
import {
  cajaEstructura, referencias, TIPOS_TERMINAL,
  type Lookup, type TipoPuntoVenta, type PuntoVenta, type TerminalTarjeta,
} from "../../shared/api/admin";

/**
 * Catálogo de tipos de punto de venta (fijo), alta/edición de los puntos de venta concretos de
 * cada sucursal, y las terminales de tarjeta (posnet) físicas dadas de alta. Qué caja usa cuál
 * punto de venta se asigna en "Asignación de cajas".
 */
export function EstructuraCajaPage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [suc, setSuc] = useState<number>(0);
  const [tipos, setTipos] = useState<TipoPuntoVenta[]>([]);
  const [pvs, setPvs] = useState<PuntoVenta[]>([]);
  const [terminales, setTerminales] = useState<TerminalTarjeta[]>([]);
  const [error, setError] = useState<string | null>(null);

  // inputs
  const [pvTipo, setPvTipo] = useState(0); const [pvNum, setPvNum] = useState(1);
  const [pvIp, setPvIp] = useState("");
  const [pvEditId, setPvEditId] = useState<number | null>(null);

  const [tNumero, setTNumero] = useState(""); const [tTipo, setTTipo] = useState(TIPOS_TERMINAL[0].v);
  const [tEditId, setTEditId] = useState<number | null>(null);
  // La caja asignada NO se toca desde este ABM (se administra en "Asignación de cajas"): al editar
  // se preserva la que ya tenía la terminal, y al crear una terminal nueva siempre nace sin asignar.
  const [tCajaAsignada, setTCajaAsignada] = useState<number | null>(null);
  const cancelarTerminal = () => { setTEditId(null); setTNumero(""); setTTipo(TIPOS_TERMINAL[0].v); setTCajaAsignada(null); };

  useEffect(() => {
    referencias.sucursales().then((s) => { setSucursales(s); if (s.length) setSuc(s[0].id); }).catch(() => {});
  }, []);

  const cargar = async (s: number) => {
    if (!s) return;
    setError(null);
    try {
      const [t, p, term] = await Promise.all([cajaEstructura.tiposPv(s), cajaEstructura.puntosVenta(s), cajaEstructura.terminales(s)]);
      setTipos(t); setPvs(p); setTerminales(term);
      if (t.length) setPvTipo((v) => v || t[0].idTipoPuntoVenta);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(suc); /* eslint-disable-next-line */ }, [suc]);

  const tipoSel = tipos.find((t) => t.idTipoPuntoVenta === pvTipo);

  const cancelarPv = () => { setPvEditId(null); setPvIp(""); setPvNum(1); };

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(suc); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      <div className="page-head">
        <h1>Estructura de caja</h1>
        <label className="inline-label">Sucursal
          <select value={suc} onChange={(e) => setSuc(Number(e.target.value))}>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}

      <h3>Tipos de punto de venta</h3>
      {/* Catálogo fijo: cada tipo implica un camino de emisión y una impresora distintos, que
          están resueltos en el código. Por eso no se dan de alta ni se borran. */}
      <p className="muted" style={{ marginTop: -8 }}>
        Son los tres tipos que soporta el sistema; no se agregan ni se eliminan.
      </p>
      <table className="grid">
        <thead><tr><th style={{ width: 60 }}>ID</th><th>Tipo</th><th>Cómo emite</th></tr></thead>
        <tbody>
          {tipos.map((t) => (
            <tr key={t.idTipoPuntoVenta}>
              <td className="mono">{t.idTipoPuntoVenta}</td>
              <td className="stack">
                {t.descripcion}
                {t.tipoArca && <small>{t.tipoArca}</small>}
              </td>
              <td className="muted">{t.detalle}</td>
            </tr>
          ))}
          {tipos.length === 0 && <tr><td colSpan={3} className="muted">Sin tipos.</td></tr>}
        </tbody>
      </table>

      <h3 style={{ marginTop: 20 }}>Puntos de venta</h3>
      <div className="field-row">
        <label>Tipo
          <select value={pvTipo} onChange={(e) => setPvTipo(Number(e.target.value))}>
            {tipos.map((t) => <option key={t.idTipoPuntoVenta} value={t.idTipoPuntoVenta}>{t.descripcion}</option>)}
          </select>
        </label>
        <label>Nº ARCA
          <input type="number" min={1} value={pvNum} onChange={(e) => setPvNum(Number(e.target.value))} style={{ width: 100 }} />
        </label>
        {/* La IP solo existe para el tipo FISCAL: es el controlador Hasar con el que habla la
            DLL. Los otros dos imprimen en la comandera local, no hay a quién apuntar. */}
        {tipoSel?.requiereIpControlador && (
          <label>IP del controlador fiscal
            <input placeholder="ej. 192.168.4.50" value={pvIp} onChange={(e) => setPvIp(e.target.value)}
              style={{ width: 160 }} />
          </label>
        )}
        <button className="primary" disabled={!pvTipo || pvNum <= 0 || (tipoSel?.requiereIpControlador && !pvIp.trim())}
          onClick={() => run(async () => {
            const ip = tipoSel?.requiereIpControlador ? pvIp.trim() : null;
            if (pvEditId != null) await cajaEstructura.updatePv(suc, pvEditId, pvTipo, pvNum, ip);
            else await cajaEstructura.createPv(suc, pvTipo, pvNum, ip);
            cancelarPv();
          })}>
          {pvEditId != null ? "Guardar" : "Agregar"}
        </button>
        {pvEditId != null && <button onClick={cancelarPv}>Cancelar</button>}
      </div>
      <table className="grid">
        <thead><tr><th>ID</th><th>Tipo</th><th>Nº ARCA</th><th>Controlador</th><th></th></tr></thead>
        <tbody>
          {pvs.map((p) => (
            <tr key={p.idPuntoVenta}>
              <td className="mono">{p.idPuntoVenta}</td><td>{p.tipoDescripcion}</td><td className="mono">{p.numeroPuntoVenta}</td>
              <td className="mono">{p.ipControlador ?? <span className="muted">—</span>}</td>
              <td className="row-actions">
                <button onClick={() => {
                  setPvEditId(p.idPuntoVenta); setPvTipo(p.idTipoPuntoVenta);
                  setPvNum(p.numeroPuntoVenta); setPvIp(p.ipControlador ?? "");
                }}>✎</button>
                <button className="danger" onClick={() => run(() => cajaEstructura.removePv(suc, p.idPuntoVenta))}>×</button>
              </td>
            </tr>
          ))}
          {pvs.length === 0 && <tr><td colSpan={5} className="muted">Sin puntos de venta.</td></tr>}
        </tbody>
      </table>

      <h3 style={{ marginTop: 20 }}>Terminales de tarjeta</h3>
      <div className="field-row">
        <label>Nro. de terminal
          <input value={tNumero} onChange={(e) => setTNumero(e.target.value)} placeholder="alfanumérico" style={{ width: 160 }} />
        </label>
        <label>Tipo
          <select value={tTipo} onChange={(e) => setTTipo(Number(e.target.value))}>
            {TIPOS_TERMINAL.map((t) => <option key={t.v} value={t.v}>{t.l}</option>)}
          </select>
        </label>
        <button className="primary" disabled={!tNumero.trim()}
          onClick={() => run(async () => {
            if (tEditId != null) await cajaEstructura.updateTerminal(suc, tEditId, tNumero.trim(), tTipo, tCajaAsignada);
            else await cajaEstructura.createTerminal(suc, tNumero.trim(), tTipo);
            cancelarTerminal();
          })}>
          {tEditId != null ? "Guardar" : "Agregar"}
        </button>
        {tEditId != null && <button onClick={cancelarTerminal}>Cancelar</button>}
      </div>
      <table className="grid">
        <thead><tr><th>ID</th><th>Nro. de terminal</th><th>Tipo</th><th>Caja asignada</th><th></th></tr></thead>
        <tbody>
          {terminales.map((t) => (
            <tr key={t.idTerminal}>
              <td className="mono">{t.idTerminal}</td><td className="mono">{t.numeroTerminal}</td><td>{t.tipoDescripcion}</td>
              <td className="muted">{t.cajaDescripcion ?? "sin asignar"}</td>
              <td className="row-actions">
                <button onClick={() => { setTEditId(t.idTerminal); setTNumero(t.numeroTerminal); setTTipo(t.tipo); setTCajaAsignada(t.idCajaAsignada ?? null); }}>✎</button>
                <button className="danger" onClick={() => run(() => cajaEstructura.removeTerminal(suc, t.idTerminal))}>×</button>
              </td>
            </tr>
          ))}
          {terminales.length === 0 && <tr><td colSpan={5} className="muted">Sin terminales.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
