import type { ArqueoX, CierreTurnoResultado } from "../../shared/api/caja";
import "./cierre-print.css";

const money = (n: number) =>
  n.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const fechaHora = (iso?: string | null) => {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
};

/**
 * Reporte de rendición del cajero (cierre de turno), formato A4, para firmar y presentar en
 * Tesorería. Se imprime desde el navegador (window.print()), mismo mecanismo que el resto de los
 * comprobantes de caja (ver comprobante-print.css) — acá con su propia página nombrada
 * "rendicion-caja" porque el formato es A4 y no ticket de 76/99mm.
 *
 * Reúne datos de dos respuestas ya disponibles en CajaPage al confirmar el cierre: el arqueo
 * tomado justo antes de cerrar (`arqueo`, con retiros/vueltos/anulaciones/saldo inicial) y el
 * resultado del cierre en sí (`cierreResultado`, con lo declarado por el cajero y la diferencia
 * final, que es la fuente de verdad ya persistida en CierresLotesCaja).
 */
export function ReporteCierreTurno({
  arqueo, cierre, usuario, motivoDescripcion, observaciones, onCerrar,
}: {
  arqueo: ArqueoX;
  cierre: CierreTurnoResultado;
  usuario: string;
  motivoDescripcion?: string | null;
  observaciones?: string | null;
  onCerrar?: () => void;
}) {
  const totalEsperado = cierre.detalle.reduce((acc, d) => acc + d.esperado, 0);
  const totalDeclarado = cierre.detalle.reduce((acc, d) => acc + d.declarado, 0);
  const hayDiferencia = Math.abs(cierre.diferenciaTotal) >= 0.01;

  return (
    <>
      <div className="rendicion">
        <header className="rendicion__header">
          <div>
            <h1>Rendición de caja</h1>
            <p className="rendicion__subtitulo">Cierre de turno del cajero — comprobante para Tesorería</p>
          </div>
          <div className="rendicion__folio">
            <span>N.º de cierre</span>
            <strong>T-{String(cierre.numeroCierre).padStart(6, "0")}</strong>
          </div>
        </header>

        <section className="rendicion__meta">
          <div><span>Cajero</span><strong>{usuario}</strong></div>
          <div><span>Caja</span><strong>{arqueo.descripcionCaja}</strong></div>
          <div><span>Lote</span><strong>#{arqueo.idLote}</strong></div>
          <div className="rendicion__meta-full"><span>Apertura del turno</span><strong>{fechaHora(arqueo.fechaApertura)}</strong></div>
          <div className="rendicion__meta-full"><span>Cierre del turno</span><strong>{fechaHora(cierre.fechaCierre)}</strong></div>
        </section>

        {arqueo.ingresoInicial && (
          <section className="rendicion__bloque">
            <h2>Saldo inicial</h2>
            <table className="rendicion__tabla">
              <thead><tr><th>Hora</th><th>Concepto</th><th className="num">Importe</th></tr></thead>
              <tbody>
                <tr>
                  <td className="mono">{fechaHora(arqueo.ingresoInicial.fecha)}</td>
                  <td>{arqueo.ingresoInicial.concepto ?? "Fondo de apertura"}</td>
                  <td className="num mono">${money(arqueo.ingresoInicial.monto)}</td>
                </tr>
              </tbody>
            </table>
          </section>
        )}

        <section className="rendicion__bloque">
          <h2>Operaciones por medio de pago</h2>
          <table className="rendicion__tabla">
            <thead>
              <tr>
                <th>Medio de pago</th>
                <th className="num">Esperado (sistema)</th>
                <th className="num">Declarado (contado)</th>
                <th className="num">Diferencia</th>
              </tr>
            </thead>
            <tbody>
              {cierre.detalle.map((d) => (
                <tr key={d.idMedioPago}>
                  <td>{d.descripcion}</td>
                  <td className="num mono">${money(d.esperado)}</td>
                  <td className="num mono">${money(d.declarado)}</td>
                  <td className={`num mono${d.requiereMotivo ? " rendicion__diferencia" : ""}`}>
                    ${money(d.diferencia)}
                  </td>
                </tr>
              ))}
              {cierre.detalle.length === 0 && (
                <tr><td colSpan={4} className="muted">Sin movimientos en este lote.</td></tr>
              )}
            </tbody>
            <tfoot>
              <tr>
                <td>Total</td>
                <td className="num mono">${money(totalEsperado)}</td>
                <td className="num mono">${money(totalDeclarado)}</td>
                <td className={`num mono${hayDiferencia ? " rendicion__diferencia" : ""}`}>
                  ${money(cierre.diferenciaTotal)}
                </td>
              </tr>
            </tfoot>
          </table>
        </section>

        {/* Anulaciones, retiros y vueltos ya están descontados del "esperado" de arriba (la plata
            salió realmente del cajón) — se listan aparte para justificar el faltante ante Tesorería. */}
        {cierre.anulaciones.length > 0 && (
          <section className="rendicion__bloque">
            <h2>Notas de crédito emitidas</h2>
            <table className="rendicion__tabla">
              <thead>
                <tr><th>Nota de crédito</th><th>Anula comprobante</th><th>Motivo</th><th className="num">Importe</th></tr>
              </thead>
              <tbody>
                {cierre.anulaciones.map((a) => (
                  <tr key={a.idComprobante}>
                    <td className="mono">{a.numeroCompleto} {a.letra}</td>
                    <td className="mono">{a.comprobanteOrigen ?? "—"}</td>
                    <td>{a.motivo ?? "—"}</td>
                    <td className="num mono">−${money(a.total)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr><td colSpan={3}>Total notas de crédito</td><td className="num mono">−${money(cierre.totalAnulaciones)}</td></tr>
              </tfoot>
            </table>
          </section>
        )}

        {arqueo.retiros.length > 0 && (
          <section className="rendicion__bloque">
            <h2>Retiros de efectivo</h2>
            <table className="rendicion__tabla">
              <thead><tr><th>Hora</th><th>Concepto</th><th>Autorizó / retiró</th><th className="num">Importe</th></tr></thead>
              <tbody>
                {arqueo.retiros.map((r) => (
                  <tr key={r.idMovCaja}>
                    <td className="mono">{fechaHora(r.fecha)}</td>
                    <td>{r.concepto ?? "—"}</td>
                    <td>{r.usuario ?? "—"}</td>
                    <td className="num mono">−${money(r.monto)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr><td colSpan={3}>Total retiros</td><td className="num mono">−${money(arqueo.totalRetiros)}</td></tr>
              </tfoot>
            </table>
          </section>
        )}

        {arqueo.vueltos.length > 0 && (
          <section className="rendicion__bloque">
            <h2>Vueltos entregados</h2>
            <table className="rendicion__tabla">
              <thead><tr><th>Hora</th><th>Concepto</th><th>Cajero</th><th className="num">Importe</th></tr></thead>
              <tbody>
                {arqueo.vueltos.map((v) => (
                  <tr key={v.idMovCaja}>
                    <td className="mono">{fechaHora(v.fecha)}</td>
                    <td>{v.concepto ?? "—"}</td>
                    <td>{v.usuario ?? "—"}</td>
                    <td className="num mono">−${money(v.monto)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr><td colSpan={3}>Total vueltos</td><td className="num mono">−${money(arqueo.totalVueltos)}</td></tr>
              </tfoot>
            </table>
          </section>
        )}

        <section className="rendicion__resumen">
          <h2>Resumen del turno</h2>
          <div className="rendicion__resumen-grid">
            {arqueo.ingresoInicial && (
              <div><span>Saldo inicial</span><strong>${money(arqueo.ingresoInicial.monto)}</strong></div>
            )}
            <div><span>Total esperado (sistema)</span><strong>${money(totalEsperado)}</strong></div>
            <div><span>Total declarado (contado)</span><strong>${money(totalDeclarado)}</strong></div>
            {cierre.anulaciones.length > 0 && (
              <div><span>Notas de crédito</span><strong>−${money(cierre.totalAnulaciones)}</strong></div>
            )}
            {arqueo.retiros.length > 0 && (
              <div><span>Retiros de efectivo</span><strong>−${money(arqueo.totalRetiros)}</strong></div>
            )}
            {arqueo.vueltos.length > 0 && (
              <div><span>Vueltos entregados</span><strong>−${money(arqueo.totalVueltos)}</strong></div>
            )}
            <div className={`rendicion__resumen-total${hayDiferencia ? " rendicion__diferencia" : ""}`}>
              <span>Diferencia total</span><strong>${money(cierre.diferenciaTotal)}</strong>
            </div>
          </div>
        </section>

        {(motivoDescripcion || observaciones) && (
          <section className="rendicion__bloque">
            <h2>Justificación de diferencias</h2>
            {motivoDescripcion && <p><span>Motivo:</span> {motivoDescripcion}</p>}
            {observaciones && <p><span>Observaciones del cajero:</span> {observaciones}</p>}
          </section>
        )}

        <section className="rendicion__firmas">
          <div className="rendicion__firma">
            <div className="rendicion__firma-linea" />
            <span>Firma del cajero</span>
            <small>{usuario}</small>
          </div>
          <div className="rendicion__firma">
            <div className="rendicion__firma-linea" />
            <span>Recibido por Tesorería</span>
            <small>Aclaración y fecha</small>
          </div>
        </section>

        <footer className="rendicion__pie">
          Este comprobante se generó automáticamente al confirmar el cierre de turno — el cierre es
          irreversible. Impreso el {fechaHora(new Date().toISOString())}.
        </footer>
      </div>

      <div className="rendicion__acciones cbte-no-print">
        <button className="primary" onClick={() => window.print()}>Imprimir rendición</button>
        {onCerrar && <button onClick={onCerrar}>Volver a Caja</button>}
      </div>
    </>
  );
}
