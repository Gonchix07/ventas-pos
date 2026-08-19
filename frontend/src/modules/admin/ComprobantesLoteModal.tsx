import { useEffect, useState } from "react";
import { tesoreria, type ComprobanteLote } from "../../shared/api/tesoreria";
import { formatearMoneda } from "../../shared/ui/moneda";

interface Props {
  idSucursal: number;
  idLote: number;
  /** Si viene, el título aclara de qué medio (el popup que se abre al hacer click en un valor por
   *  medio de pago); sin él, muestra todos los comprobantes del lote. */
  idMedioPago?: number;
  medioDescripcion?: string;
  onCerrar: () => void;
}

/** Ventas hechas en el lote (cabeceras de comprobantes) — el popup al hacer click en un valor por medio de pago. */
export function ComprobantesLoteModal({ idSucursal, idLote, idMedioPago, medioDescripcion, onCerrar }: Props) {
  const [items, setItems] = useState<ComprobanteLote[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    tesoreria.comprobantesLote(idSucursal, idLote, idMedioPago)
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : "Error"));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idSucursal, idLote, idMedioPago]);

  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(990px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        <div className="page-head">
          <h3>Comprobantes — Lote {idLote}{medioDescripcion ? ` · ${medioDescripcion}` : ""}</h3>
          <button onClick={onCerrar}>Cerrar</button>
        </div>
        {error && <p className="error">{error}</p>}
        {items === null && !error && <p className="muted">Cargando…</p>}
        {items && (
          <table className="grid">
            <thead>
              <tr>
                <th>Comprobante</th><th>Tipo</th><th>Fecha</th><th>Cliente</th>
                <th>Total</th>{idMedioPago && <th>En este medio</th>}
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.idComprobante}>
                  <td className="mono">{c.letra ? `${c.letra} ` : ""}{c.numeroCompleto ?? "—"}</td>
                  <td>{c.tipoDescripcion}</td>
                  <td>{new Date(c.fecha).toLocaleString()}</td>
                  <td>{c.clienteDescripcion ? `${c.clienteCodigo} · ${c.clienteDescripcion}` : "Consumidor final"}</td>
                  <td className="mono">{formatearMoneda(c.total)}</td>
                  {idMedioPago && <td className="mono">{formatearMoneda(c.montoEnMedio)}</td>}
                </tr>
              ))}
              {items.length === 0 && (
                <tr><td colSpan={idMedioPago ? 6 : 5} className="muted">Sin comprobantes.</td></tr>
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
