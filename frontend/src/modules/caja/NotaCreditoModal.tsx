import { useState } from "react";
import {
  notasCredito, TipoAnulacion,
  type ComprobanteAnulable, type ComprobanteAnulableDetalle, type NotaCreditoResultado,
} from "../../shared/api/notasCredito";
import { formatearMoneda, MonedaInput } from "../../shared/ui/moneda";
import { useSupervisorGate } from "../../shared/ui/SupervisorGate";

interface Props {
  idSucursal: number;
  idCaja: number;
  onCerrar: () => void;
}

/**
 * Emisión de notas de crédito desde la caja: se busca la factura, se elige qué anular (todo,
 * artículos sueltos o un importe por diferencia de precio) y se devuelve el importe en efectivo.
 *
 * La anulación por artículos es siempre de la línea completa — para devolver parte de una línea
 * se usa la opción por monto.
 */
export function NotaCreditoModal({ idSucursal, idCaja, onCerrar }: Props) {
  const [texto, setTexto] = useState("");
  const [resultados, setResultados] = useState<ComprobanteAnulable[] | null>(null);
  const [detalle, setDetalle] = useState<ComprobanteAnulableDetalle | null>(null);
  const [tipo, setTipo] = useState<TipoAnulacion>(TipoAnulacion.Total);
  const [seleccion, setSeleccion] = useState<Set<number>>(new Set());
  const [monto, setMonto] = useState<number | null>(null);
  const [motivo, setMotivo] = useState("");
  const [emitida, setEmitida] = useState<NotaCreditoResultado | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const { ejecutarConSupervisor, modal: modalSupervisor } = useSupervisorGate();

  const buscar = async () => {
    setError(null); setCargando(true);
    try {
      setResultados(await notasCredito.buscar(idSucursal, texto.trim()));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo buscar.");
    } finally {
      setCargando(false);
    }
  };

  const elegir = async (c: ComprobanteAnulable) => {
    setError(null); setCargando(true);
    try {
      const d = await notasCredito.obtener(idSucursal, c.idComprobante);
      setDetalle(d);
      setTipo(TipoAnulacion.Total);
      setSeleccion(new Set());
      setMonto(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo abrir el comprobante.");
    } finally {
      setCargando(false);
    }
  };

  const alternarLinea = (id: number) => {
    const s = new Set(seleccion);
    if (s.has(id)) s.delete(id); else s.add(id);
    setSeleccion(s);
  };

  // Lo que se va a acreditar según la opción elegida, para mostrarlo antes de confirmar.
  const totalAAnular = (() => {
    if (!detalle) return 0;
    if (tipo === TipoAnulacion.Total) {
      return detalle.lineas.filter((l) => !l.yaAnulada).reduce((a, l) => a + l.importe, 0);
    }
    if (tipo === TipoAnulacion.PorArticulos) {
      return detalle.lineas.filter((l) => seleccion.has(l.idDetalleComprobante))
        .reduce((a, l) => a + l.importe, 0);
    }
    return monto ?? 0;
  })();

  const saldo = detalle?.comprobante.saldoAnulable ?? 0;
  const excede = totalAAnular - saldo > 0.01;
  const puedeEmitir = !cargando && totalAAnular > 0 && !excede
    && (tipo !== TipoAnulacion.PorArticulos || seleccion.size > 0);

  const emitir = () => ejecutarConSupervisor(async (codigoSupervisor) => {
    if (!detalle) return;
    setError(null); setCargando(true);
    try {
      setEmitida(await notasCredito.emitir({
        idSucursal,
        idComprobanteOrigen: detalle.comprobante.idComprobante,
        idCaja,
        tipo,
        idsDetalle: tipo === TipoAnulacion.PorArticulos ? [...seleccion] : null,
        monto: tipo === TipoAnulacion.PorMonto ? monto : null,
        motivo: motivo.trim() || null,
        codigoSupervisor,
      }));
    } catch (e) {
      const mensaje = e instanceof Error ? e.message : "No se pudo emitir la nota de crédito.";
      setError(mensaje);
      throw e; // así el popup de supervisor sabe que falló y no se cierra solo.
    } finally {
      setCargando(false);
    }
  });

  // ---- Comprobante emitido ----
  if (emitida) {
    return (
      <Overlay onCerrar={onCerrar}>
        <h2>Nota de crédito emitida</h2>
        <p className="nc-numero mono">{emitida.numeroCompleto} <span className="nc-letra">{emitida.letra}</span></p>
        <table className="grid">
          <tbody>
            <tr><td>Neto</td><td className="mono">{formatearMoneda(emitida.neto)}</td></tr>
            <tr><td>IVA</td><td className="mono">{formatearMoneda(emitida.iva)}</td></tr>
            <tr><td><b>Total acreditado</b></td><td className="mono"><b>{formatearMoneda(emitida.total)}</b></td></tr>
            {emitida.reversionCompleta ? (
              emitida.devoluciones.map((d) => (
                <tr key={d.idMedioPago}>
                  <td><b>Devuelto en {d.medioDescripcion}</b></td>
                  <td className="mono"><b>{formatearMoneda(d.monto)}</b></td>
                </tr>
              ))
            ) : (
              <tr><td><b>A devolver en efectivo</b></td><td className="mono"><b>{formatearMoneda(emitida.devueltoEnEfectivo)}</b></td></tr>
            )}
            {emitida.cae && <tr><td>CAE</td><td className="mono">{emitida.cae}</td></tr>}
          </tbody>
        </table>
        {emitida.reversionCompleta && (
          <p className="muted" style={{ margin: 0 }}>
            Anulación total del mismo día: se revirtieron todos los medios de pago de la venta
            original (los cupones de tarjeta quedaron marcados como anulados).
          </p>
        )}
        {!emitida.impreso && (
          <p className="error">
            La nota de crédito quedó registrada pero NO se imprimió: {emitida.errorImpresion}
          </p>
        )}
        <div className="row-actions" style={{ marginTop: 16 }}>
          <button className="primary" onClick={onCerrar}>Cerrar</button>
        </div>
      </Overlay>
    );
  }

  // ---- Elección de qué anular ----
  if (detalle) {
    const c = detalle.comprobante;
    return (
      <>
      <Overlay onCerrar={onCerrar}>
        <h2>Nota de crédito</h2>
        <p className="muted">
          Sobre <b className="mono">{c.numeroCompleto}</b> {c.letra} · {new Date(c.fecha).toLocaleDateString()} ·{" "}
          {c.clienteDescripcion ?? "Consumidor final"}
        </p>
        <div className="nc-saldos">
          <span>Total factura <b className="mono">{formatearMoneda(c.total)}</b></span>
          <span>Ya acreditado <b className="mono">{formatearMoneda(c.yaAcreditado)}</b></span>
          <span>Saldo anulable <b className="mono">{formatearMoneda(c.saldoAnulable)}</b></span>
        </div>

        <div className="nc-tipos">
          <label>
            <input type="radio" checked={tipo === TipoAnulacion.Total}
              onChange={() => setTipo(TipoAnulacion.Total)} />
            Anulación total
          </label>
          <label>
            <input type="radio" checked={tipo === TipoAnulacion.PorArticulos}
              onChange={() => setTipo(TipoAnulacion.PorArticulos)} />
            Por artículos
          </label>
          <label>
            <input type="radio" checked={tipo === TipoAnulacion.PorMonto}
              onChange={() => setTipo(TipoAnulacion.PorMonto)} />
            Por diferencia de precio
          </label>
        </div>

        {tipo !== TipoAnulacion.PorMonto && (
          <table className="grid">
            <thead>
              <tr>
                {tipo === TipoAnulacion.PorArticulos && <th style={{ width: 36 }} />}
                <th>Artículo</th><th>Cant.</th><th>P. unit.</th><th>Importe</th><th>Estado</th>
              </tr>
            </thead>
            <tbody>
              {detalle.lineas.map((l) => (
                <tr key={l.idDetalleComprobante} className={l.yaAnulada ? "muted" : undefined}>
                  {tipo === TipoAnulacion.PorArticulos && (
                    <td>
                      <input type="checkbox" disabled={l.yaAnulada}
                        checked={seleccion.has(l.idDetalleComprobante)}
                        onChange={() => alternarLinea(l.idDetalleComprobante)} />
                    </td>
                  )}
                  <td>{l.descripcionTicket}</td>
                  <td className="mono">{l.cantidad}</td>
                  <td className="mono">{formatearMoneda(l.precioUnit)}</td>
                  <td className="mono">{formatearMoneda(l.importe)}</td>
                  <td>{l.yaAnulada ? "Ya anulada" : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tipo === TipoAnulacion.PorMonto && (
          <div className="nc-monto">
            <label>Importe a acreditar</label>
            <MonedaInput value={monto} onChange={setMonto} autoFocus />
            <p className="muted">
              Se reparte entre las alícuotas de IVA de la factura en la misma proporción. No puede
              superar {formatearMoneda(saldo)}.
            </p>
          </div>
        )}

        <div className="nc-motivo">
          <label>Motivo</label>
          <input value={motivo} onChange={(e) => setMotivo(e.target.value)}
            placeholder="Devolución de mercadería, error de precio…" />
        </div>

        <div className="caja-totales">
          <div className="total"><span>A acreditar</span><b>{formatearMoneda(totalAAnular)}</b></div>
        </div>
        {excede && <p className="error">Supera el saldo anulable del comprobante ({formatearMoneda(saldo)}).</p>}
        {error && <p className="error">{error}</p>}

        <div className="row-actions" style={{ marginTop: 16 }}>
          <button onClick={() => setDetalle(null)} disabled={cargando}>Volver</button>
          <button className="primary" onClick={emitir} disabled={!puedeEmitir}>
            {cargando ? "Emitiendo…" : "Emitir nota de crédito"}
          </button>
        </div>
      </Overlay>
      {modalSupervisor}
      </>
    );
  }

  // ---- Búsqueda ----
  return (
    <Overlay onCerrar={onCerrar}>
      <h2>Notas de crédito</h2>
      <p className="muted">Buscá la factura por número, cliente o CUIT.</p>
      <div className="ident-search">
        <input autoFocus value={texto} placeholder="Número de comprobante, cliente o CUIT…"
          onChange={(e) => setTexto(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && buscar()} />
        <button className="primary" onClick={buscar} disabled={cargando}>Buscar</button>
      </div>
      {error && <p className="error">{error}</p>}
      {resultados && (
        <table className="grid">
          <thead>
            <tr><th>Comprobante</th><th>Fecha</th><th>Cliente</th><th>Total</th><th>Saldo anulable</th><th /></tr>
          </thead>
          <tbody>
            {resultados.map((c) => (
              <tr key={c.idComprobante}>
                <td className="mono">{c.numeroCompleto} {c.letra}</td>
                <td>{new Date(c.fecha).toLocaleDateString()}</td>
                <td>{c.clienteDescripcion ?? "Consumidor final"}</td>
                <td className="mono">{formatearMoneda(c.total)}</td>
                <td className="mono">{formatearMoneda(c.saldoAnulable)}</td>
                <td>
                  <button disabled={!c.anulable || cargando} onClick={() => elegir(c)}>
                    {c.anulable ? "Anular" : "Sin saldo"}
                  </button>
                </td>
              </tr>
            ))}
            {resultados.length === 0 && (
              <tr><td colSpan={6} className="muted">No se encontraron comprobantes.</td></tr>
            )}
          </tbody>
        </table>
      )}
      <div className="row-actions" style={{ marginTop: 16 }}>
        <button onClick={onCerrar}>Cerrar</button>
      </div>
    </Overlay>
  );
}

// Reusa el modal genérico del buscador de artículos (.modal-fondo/.modal-caja) para no tener dos
// estilos de diálogo distintos en la misma pantalla de caja.
function Overlay({ children, onCerrar }: { children: React.ReactNode; onCerrar: () => void }) {
  return (
    <div className="modal-fondo" onClick={onCerrar}>
      <div className="modal-caja" onClick={(e) => e.stopPropagation()}>{children}</div>
    </div>
  );
}
