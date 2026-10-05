import { useState } from "react";
import {
  EMAIL_LARGO_MAXIMO, emailValido, facturacion, type EnviarComprobanteMailResponse,
} from "../../shared/api/facturacion";

/**
 * Popup "Enviar por mail" de la factura recién emitida (solo A/B). Arranca con el mail guardado en
 * la ficha del cliente; si no tiene, el cajero lo completa. No deja enviar mientras el formato no
 * sea válido. Si el mail escrito difiere del de la ficha (o la ficha no tiene), ofrece guardarlo
 * para las próximas compras — tildado por defecto, el backend lo guarda recién tras un envío exitoso.
 */
export function EnviarMailModal({ idSucursal, idComprobante, numeroCompleto, clienteDescripcion,
  emailGuardado, puedeGuardar, enviarFn, onCerrar, onEnviado }: {
  idSucursal: number; idComprobante: number; numeroCompleto: string; clienteDescripcion: string;
  /** Mail que ya tiene la ficha del cliente (null/"" si no tiene). */
  emailGuardado?: string | null;
  /** false cuando no hay ficha de cliente donde guardarlo (B a consumidor final sin identificar). */
  puedeGuardar: boolean;
  /** Cómo se envía. Por defecto el endpoint de Facturación (Caja); Reimpresión pasa el suyo porque se
   *  autoriza por otro módulo (ej. Tesorero no tiene Caja). */
  enviarFn?: (idSucursal: number, idComprobante: number, email: string, guardar: boolean) => Promise<EnviarComprobanteMailResponse>;
  onCerrar: () => void;
  /** Se llama tras un envío exitoso, con el mail usado y si quedó guardado en la ficha. */
  onEnviado: (email: string, guardadoEnCliente: boolean) => void;
}) {
  const guardado = (emailGuardado ?? "").trim();
  const [email, setEmail] = useState(guardado);
  const [guardar, setGuardar] = useState(true);
  const [enviando, setEnviando] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const limpio = email.trim();
  const valido = emailValido(limpio);
  const mostrarError = limpio.length > 0 && !valido;
  const difiereDeLaFicha = limpio.toLowerCase() !== guardado.toLowerCase();
  const ofrecerGuardar = puedeGuardar && valido && difiereDeLaFicha;

  const enviar = async () => {
    if (!valido || enviando) return;
    setEnviando(true);
    setError(null);
    try {
      const r = await (enviarFn ?? facturacion.enviarMail)(idSucursal, idComprobante, limpio, ofrecerGuardar && guardar);
      onEnviado(r.email, r.emailGuardadoEnCliente);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo enviar el mail.");
      setEnviando(false);
    }
  };

  return (
    <div className="modal-fondo cbte-no-print" onClick={enviando ? undefined : onCerrar}>
      <form className="modal-caja" style={{ width: "min(460px, 100%)" }}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => { if (e.key === "Escape" && !enviando) onCerrar(); }}
        onSubmit={(e) => { e.preventDefault(); void enviar(); }}>
        <div className="page-head">
          <h3>Enviar factura por mail</h3>
        </div>
        <p className="muted" style={{ marginTop: 0 }}>
          {numeroCompleto} · {clienteDescripcion}
        </p>
        <label className="mail-campo-etiqueta" htmlFor="mail-cliente">Mail del cliente</label>
        <div className={`mail-campo${mostrarError ? " mail-campo--error" : valido ? " mail-campo--ok" : ""}`}>
          <span className="mail-campo__icono" aria-hidden="true">✉</span>
          <input id="mail-cliente" type="email" autoFocus value={email} maxLength={EMAIL_LARGO_MAXIMO}
            onChange={(e) => { setEmail(e.target.value); setError(null); }}
            placeholder="cliente@dominio.com" disabled={enviando}
            aria-invalid={mostrarError} />
          {valido && <span className="mail-campo__check" aria-hidden="true">✓</span>}
        </div>
        {mostrarError && (
          <p className="error" style={{ margin: "6px 0 0" }}>El mail no tiene un formato válido (ej.: cliente@dominio.com).</p>
        )}
        {!guardado && limpio.length === 0 && (
          <p className="muted" style={{ margin: "6px 0 0" }}>El cliente no tiene mail cargado: completalo para enviar la factura.</p>
        )}
        {ofrecerGuardar && (
          <label style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 12 }}>
            <input type="checkbox" checked={guardar} onChange={(e) => setGuardar(e.target.checked)} disabled={enviando} />
            {guardado ? "Reemplazar el mail guardado del cliente por este" : "Guardar este mail en la ficha del cliente"}
          </label>
        )}
        {error && <p className="error" role="alert" style={{ marginTop: 10 }}>{error}</p>}
        <div className="row-actions" style={{ marginTop: 16 }}>
          <button type="submit" className="primary" disabled={!valido || enviando}>
            {enviando ? "Enviando…" : "Enviar"}
          </button>
          <button type="button" onClick={onCerrar} disabled={enviando}>Cancelar</button>
        </div>
      </form>
    </div>
  );
}
