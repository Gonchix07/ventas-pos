import { useState } from "react";
import { caja, type GiftcardConsulta } from "../../shared/api/caja";
import { MonedaInput, formatearMoneda } from "../../shared/ui/moneda";

interface Props {
  idSucursal: number;
  info: GiftcardConsulta;
  /** Clave estable por operación+código (misma operación+código → misma clave): si el cajero
   *  reintenta tras un timeout de red, giftcards-app no vuelve a descontar saldo. */
  idempotencyKey: string;
  onCerrar: () => void;
  /** Se dispara SOLO cuando el canje se aplicó de verdad (POST /caja/giftcard/usar respondió OK) —
   *  a diferencia de "Validar" (solo lectura), esto ya descontó saldo en giftcards-app. */
  onConfirmado: (monto: number, transaccionId: string | null) => void;
}

const fechaAr = (iso: string) => new Date(iso + "T00:00:00").toLocaleDateString("es-AR");

/**
 * Popup "Confirmar uso" de una gift card, calcado del propio panel de cajero de giftcards-app
 * (mismos colores/estructura: código+badge de estado, Campaña/Cliente, caja de saldo indigo,
 * "Usar todo"/"Confirmar uso") — para que el cajero reconozca la misma pantalla que ya conoce de
 * ese sistema. A diferencia de "Validar" (solo consulta), "Confirmar uso" acá SÍ descuenta saldo de
 * inmediato: no hay un paso posterior que vuelva a cobrar al facturar.
 */
export function GiftcardValidacionModal({ idSucursal, info, idempotencyKey, onCerrar, onConfirmado }: Props) {
  const saldo = info.saldo ?? 0;
  const vencida = !!info.fechaVencimiento && new Date(info.fechaVencimiento + "T00:00:00") < new Date(new Date().toDateString());
  const usable = info.estado === "activa" && saldo > 0 && !vencida && !!info.cliente;

  const [monto, setMonto] = useState<number | null>(info.usoParcial === false ? saldo : null);
  const [error, setError] = useState<string | null>(null);
  const [confirmando, setConfirmando] = useState(false);

  const confirmar = async () => {
    setError(null);
    const m = monto ?? 0;
    if (!(m > 0)) { setError("Ingresá un monto válido."); return; }
    if (m > saldo) { setError("El monto supera el saldo disponible."); return; }
    if (info.usoParcial === false && m !== saldo) { setError("Esta gift card es de uso total: tenés que usar el saldo completo."); return; }
    setConfirmando(true);
    try {
      const r = await caja.giftcardUsar(idSucursal, info.codigo, m, idempotencyKey);
      onConfirmado(m, r.transaccionId ?? null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo canjear la gift card.");
    } finally {
      setConfirmando(false);
    }
  };

  const motivoNoUsable = info.estado !== "activa" ? `La gift card está ${info.estado}.`
    : vencida ? "La gift card está vencida."
    : !info.cliente ? "La gift card no tiene un cliente asignado."
    : "Sin saldo disponible.";

  return (
    <div className="modal-fondo">
      <div className="modal-caja giftcard-modal">
        <div className="giftcard-modal-head">
          <span className="giftcard-modal-codigo">{info.codigo}</span>
          <span className={`giftcard-badge giftcard-badge-${info.estado === "activa" ? "green" : info.estado === "agotada" ? "slate" : "red"}`}>
            {info.estado}
          </span>
        </div>
        <div className="giftcard-modal-grid">
          <div>
            <p className="giftcard-modal-label">Campaña</p>
            <p className="giftcard-modal-valor">{info.campana ?? "—"}</p>
          </div>
          <div>
            <p className="giftcard-modal-label">Cliente</p>
            <p className="giftcard-modal-valor">{info.cliente ? `${info.cliente}${info.dni ? ` (${info.dni})` : ""}` : "Sin asignar"}</p>
          </div>
        </div>
        <div className="giftcard-modal-saldo">
          <p className="giftcard-modal-saldo-label">Saldo disponible</p>
          <p className="giftcard-modal-saldo-valor">{formatearMoneda(saldo)}</p>
          <p className="giftcard-modal-saldo-max">Monto máximo original: {formatearMoneda(info.montoMax ?? 0)}</p>
          {info.fechaVencimiento && (
            <p className={vencida ? "giftcard-modal-vencida" : "giftcard-modal-vence"}>
              {vencida ? "VENCIDA el " : "Vence el "}{fechaAr(info.fechaVencimiento)}
            </p>
          )}
        </div>

        {usable ? (
          <>
            <label className="giftcard-modal-monto-label">Monto a usar
              <MonedaInput value={monto} onChange={setMonto} disabled={info.usoParcial === false} />
            </label>
            {info.usoParcial === false && (
              <p className="giftcard-modal-uso-total">Esta gift card es de uso total: se descuenta el saldo completo en un solo uso.</p>
            )}
            {error && <p className="error">{error}</p>}
            <div className="row-actions">
              {info.usoParcial !== false && (
                <button type="button" onClick={() => setMonto(saldo)} disabled={confirmando}>Usar todo</button>
              )}
              <button type="button" className="giftcard-modal-confirmar" onClick={() => void confirmar()} disabled={confirmando}>
                {confirmando ? "Procesando…" : "Confirmar uso"}
              </button>
            </div>
          </>
        ) : (
          <p className="muted" style={{ textAlign: "center" }}>{motivoNoUsable}</p>
        )}

        <button type="button" onClick={onCerrar} disabled={confirmando} style={{ marginTop: 12, width: "100%" }}>
          Cancelar
        </button>
      </div>
    </div>
  );
}
