import { useEffect, useState } from "react";
import { caja, type RetiroEfectivoResultado } from "../../shared/api/caja";
import { formatearMoneda, MonedaInput } from "../../shared/ui/moneda";
import "./comprobante-print.css";

interface Props {
  idSucursal: number;
  idCaja: number;
  usuario: string;
  descripcionCaja: string;
  onCerrar: () => void;
}

const fechaHora = (iso: string) =>
  new Date(iso).toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });

/**
 * Retiro de efectivo del turno: el cajero saca plata de su caja para enviarla a otro lado (ej.
 * tesorería). Se descuenta del efectivo esperado en la rendición (arqueo X / cierre de turno) y
 * queda etiquetado con el concepto — mismo mecanismo que usa una nota de crédito para restar del
 * cajón, pero sin comprobante fiscal detrás.
 */
export function RetiroEfectivoModal({ idSucursal, idCaja, usuario, descripcionCaja, onCerrar }: Props) {
  const [monto, setMonto] = useState<number | null>(null);
  const [concepto, setConcepto] = useState("");
  const [resultado, setResultado] = useState<RetiroEfectivoResultado | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);

  const confirmar = async () => {
    if (!monto || monto <= 0) return;
    setError(null);
    setCargando(true);
    try {
      setResultado(await caja.retiroEfectivo(idSucursal, idCaja, monto, concepto.trim() || null));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo registrar el retiro.");
    } finally {
      setCargando(false);
    }
  };

  // Apenas se confirma el retiro, se manda solo a imprimir (comandera no fiscal, vía el navegador
  // — mismo circuito que el Presupuesto, no toca el controlador fiscal) y se cierra el modal solo:
  // no hay pantalla de "resultado" que el cajero tenga que cerrar a mano, el ticket impreso es la
  // única confirmación. Un pequeño delay para que el ticket ya esté en el DOM antes de imprimir.
  useEffect(() => {
    if (!resultado) return;
    const t = setTimeout(() => {
      window.print();
      onCerrar();
    }, 150);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resultado]);

  if (resultado) {
    // Nada visible en pantalla — el ticket vive fuera de la vista (ver .cbte--sinPantalla en
    // comprobante-print.css) pero sigue en el DOM para que window.print() lo capture.
    return (
      <div className="cbte cbte--retiro cbte--sinPantalla">
        <div className="cbte__tipo">
          <h2>RETIRO DE DINERO</h2>
        </div>
        <div className="cbte__meta">
          <span>{fechaHora(resultado.fecha)}</span>
          <span>Caja {descripcionCaja}</span>
        </div>
        <div className="cbte__cliente">
          <div><span>Usuario:</span><span>{usuario}</span></div>
        </div>
        <div className="cbte__totales">
          <div className="total"><span>Retirado</span><span>{formatearMoneda(resultado.monto)}</span></div>
        </div>
        {resultado.concepto && <div className="cbte__leyenda">{resultado.concepto}</div>}
      </div>
    );
  }

  return (
    <Overlay onCerrar={onCerrar}>
      <h2>Retiro de efectivo</h2>
      <p className="muted">
        Para sacar plata de la caja y enviarla a otro lado (tesorería, depósito, etc.). Se
        descuenta del efectivo esperado en la rendición de este turno.
      </p>
      {error && <p className="error">{error}</p>}
      <div className="form-grid">
        <label>Monto a retirar
          <MonedaInput value={monto} onChange={setMonto} autoFocus />
        </label>
        <label>Concepto (opcional)
          <input value={concepto} onChange={(e) => setConcepto(e.target.value)}
            placeholder="Envío a tesorería, depósito bancario…" maxLength={150} />
        </label>
      </div>
      <div className="row-actions" style={{ marginTop: 16 }}>
        <button onClick={onCerrar} disabled={cargando}>Cancelar</button>
        <button className="primary" onClick={confirmar} disabled={cargando || !monto || monto <= 0}>
          {cargando ? "Registrando…" : "Registrar retiro"}
        </button>
      </div>
    </Overlay>
  );
}

// Mismo overlay genérico que usa NotaCreditoModal (.modal-fondo/.modal-caja) — un solo estilo de
// diálogo en toda la pantalla de caja.
function Overlay({ children, onCerrar }: { children: React.ReactNode; onCerrar: () => void }) {
  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" style={{ width: "min(420px, 100%)" }} onClick={(e) => e.stopPropagation()}>
        {children}
      </div>
    </div>
  );
}
