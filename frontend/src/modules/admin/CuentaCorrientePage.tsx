import { useEffect, useState } from "react";
import {
  cuentaCorriente, referencias, clientes,
  type CuentaCorrienteLimite, type Lookup, type Cliente,
} from "../../shared/api/admin";
import { MonedaInput, formatearMoneda } from "../../shared/ui/moneda";

export function CuentaCorrientePage() {
  const [sucursales, setSucursales] = useState<Lookup[]>([]);
  const [suc, setSuc] = useState(0);
  const [items, setItems] = useState<CuentaCorrienteLimite[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [q, setQ] = useState("");
  const [cli, setCli] = useState<Cliente[]>([]);
  const [buscado, setBuscado] = useState(false);
  const [idCliente, setIdCliente] = useState(0);
  const [limite, setLimite] = useState<number | null>(null);

  useEffect(() => {
    referencias.sucursales().then((s) => { setSucursales(s); if (s.length) setSuc(s[0].id); }).catch(() => {});
  }, []);

  const cargar = async (s: number) => {
    if (!s) return;
    setError(null);
    try { setItems(await cuentaCorriente.list(s)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(suc); /* eslint-disable-next-line */ }, [suc]);

  // Solo clientes con "Admite cuenta corriente" tildado en su ficha: cargarle un límite a alguien
  // que no lo admite no tendría efecto (el backend rechaza el upsert con CLIENTE_NO_ADMITE_...).
  const buscarCli = async () => {
    setError(null);
    try { setCli(await clientes.list(q, true)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setBuscado(true); }
  };

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(suc); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      <div className="page-head">
        <h1>Cuenta corriente</h1>
        <label className="inline-label">Sucursal
          <select value={suc} onChange={(e) => setSuc(Number(e.target.value))}>
            {sucursales.map((s) => <option key={s.id} value={s.id}>{s.descripcion}</option>)}
          </select>
        </label>
      </div>
      {error && <p className="error">{error}</p>}
      <p className="muted">
        Límite de crédito por cliente. Al facturar a cuenta corriente, el sistema rechaza el
        pago si saldo actual + monto supera este límite.
      </p>

      <div className="card form">
        <h3>Habilitar / actualizar límite</h3>
        <p className="muted" style={{ margin: 0 }}>
          Solo aparecen los clientes con «Admite cuenta corriente» tildado en su ficha.
        </p>
        <div className="toolbar">
          <input placeholder="Buscar cliente por nombre, fantasía, código o CUIT" value={q}
            onChange={(e) => setQ(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && buscarCli()} style={{ minWidth: 320 }} />
          <button onClick={buscarCli}>Buscar</button>
        </div>
        {cli.length > 0 && (
          <select value={idCliente} onChange={(e) => setIdCliente(Number(e.target.value))} style={{ maxWidth: 420 }}>
            <option value={0}>— elegir cliente —</option>
            {cli.map((c) => <option key={c.idCliente} value={c.idCliente}>{c.codigoInt} · {c.descripcion}</option>)}
          </select>
        )}
        {buscado && cli.length === 0 && (
          <p className="muted">
            Ningún cliente habilitado coincide con la búsqueda. Tildá «Admite cuenta corriente» en
            su ficha de cliente para poder asignarle un límite.
          </p>
        )}
        <div className="field-row">
          <label>Límite de crédito
            <MonedaInput value={limite} onChange={setLimite} style={{ width: 180 }} />
          </label>
          <button className="primary" disabled={!idCliente}
            onClick={() => run(async () => {
              await cuentaCorriente.upsert(suc, idCliente, limite ?? 0);
              setIdCliente(0); setLimite(null); setCli([]); setQ(""); setBuscado(false);
            })}>
            Guardar límite
          </button>
        </div>
      </div>

      <table className="grid">
        <thead>
          <tr>
            <th>Cliente</th>
            <th className="money">Límite</th>
            <th className="money">Saldo actual</th>
            <th className="money">Disponible</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {items.map((c) => (
            <tr key={c.idCliente}>
              <td>{c.clienteDescripcion}</td>
              <td className="money">{formatearMoneda(c.limiteCredito)}</td>
              <td className="money">{formatearMoneda(c.saldoActual)}</td>
              <td className="money">{formatearMoneda(c.limiteCredito - c.saldoActual)}</td>
              <td><button className="danger" onClick={() => run(() => cuentaCorriente.remove(suc, c.idCliente))}>Quitar</button></td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan={5} className="muted">Sin cuentas corrientes habilitadas.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
