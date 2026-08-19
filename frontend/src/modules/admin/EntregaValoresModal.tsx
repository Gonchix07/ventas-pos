import { useEffect, useState } from "react";
import { tesoreria, type LoteResumen, type MedioPagoLookup } from "../../shared/api/tesoreria";
import { MonedaInput } from "../../shared/ui/moneda";

interface Props {
  lote: LoteResumen;
  onCerrar: () => void;
  /** Se llama después de guardar, para que la pantalla recargue el lote/detalle. */
  onGuardado: () => void;
}

/**
 * Corrección manual +/- de Tesorería sobre un lote: cualquier medio de pago, funciona aunque el
 * lote ya esté cerrado (a diferencia del retiro que carga el cajero, que solo opera sobre su propio
 * lote abierto). El monto entra con su propio signo — positivo suma, negativo resta.
 */
export function EntregaValoresModal({ lote, onCerrar, onGuardado }: Props) {
  const [medios, setMedios] = useState<MedioPagoLookup[]>([]);
  const [idMedioPago, setIdMedioPago] = useState(0);
  const [monto, setMonto] = useState<number | null>(null);
  const [concepto, setConcepto] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);

  useEffect(() => {
    tesoreria.mediosPago().then((m) => { setMedios(m); if (m.length) setIdMedioPago(m[0].id); }).catch(() => {});
  }, []);

  const guardar = async () => {
    if (!idMedioPago || !monto || monto === 0 || !concepto.trim()) return;
    setError(null); setGuardando(true);
    try {
      await tesoreria.corregir(lote.idSucursal, lote.idLote, { idMedioPago, monto, concepto: concepto.trim() });
      onGuardado();
      onCerrar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo registrar la corrección.");
    } finally {
      setGuardando(false);
    }
  };

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(460px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        <div className="page-head"><h3>Entrega de valores — Lote {lote.idLote}</h3></div>
        <p className="muted" style={{ margin: 0 }}>
          Corrección +/- sobre este lote (positivo suma, negativo resta). Se aplica aunque el lote
          ya esté cerrado; queda registrada con tu usuario y el motivo.
        </p>
        {error && <p className="error">{error}</p>}
        <div className="form-grid" style={{ marginTop: 10 }}>
          <label>Medio de pago
            <select value={idMedioPago} onChange={(e) => setIdMedioPago(Number(e.target.value))}>
              {medios.map((m) => <option key={m.id} value={m.id}>{m.descripcion}</option>)}
            </select>
          </label>
          <label>Monto (+/-)
            <MonedaInput value={monto} onChange={setMonto} autoFocus />
          </label>
          <label>Motivo *
            <input value={concepto} onChange={(e) => setConcepto(e.target.value)}
              placeholder="Por qué se ajusta este lote" maxLength={200} />
          </label>
        </div>
        <div className="row-actions" style={{ marginTop: 16 }}>
          <button onClick={onCerrar} disabled={guardando}>Cancelar</button>
          <button className="primary" disabled={guardando || !idMedioPago || !monto || !concepto.trim()}
            onClick={guardar}>
            {guardando ? "Guardando…" : "Registrar corrección"}
          </button>
        </div>
      </div>
    </div>
  );
}
