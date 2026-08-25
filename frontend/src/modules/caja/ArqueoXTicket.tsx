import { useEffect, useRef } from "react";
import type { ArqueoX } from "../../shared/api/caja";
import { formatearMoneda } from "../../shared/ui/moneda";
import "./comprobante-print.css";

const fechaHora = (iso: string) =>
  new Date(iso).toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });

/**
 * Arqueo X en caja Electrónica: no hay controlador fiscal de por medio (ver ArqueoXAsync en el
 * backend — solo llama al Hasar cuando la caja es Fiscal), así que el reporte lo imprime el propio
 * navegador contra la comandera. Se imprime solo apenas se pide el arqueo, mismo momento en que una
 * caja Fiscal dispara el ticket del controlador — no hace falta un botón aparte, y como con
 * Retiro/Ingreso inicial, el ticket no se muestra en pantalla (solo al imprimir, ver
 * .cbte--sinPantalla), porque la pantalla de Arqueo X ya muestra los mismos datos en una tabla.
 */
export function ArqueoXTicket({ arqueo, usuario }: { arqueo: ArqueoX; usuario: string }) {
  const impreso = useRef(false);
  useEffect(() => {
    if (impreso.current) return;
    impreso.current = true;
    const t = setTimeout(() => window.print(), 150);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="cbte cbte--arqueo cbte--sinPantalla">
      <div className="cbte__tipo">
        <h2>ARQUEO X</h2>
        <small>Vista del lote — no cierra la caja</small>
      </div>
      <div className="cbte__meta">
        <span>{fechaHora(new Date().toISOString())}</span>
        <span>Caja {arqueo.descripcionCaja}</span>
      </div>
      <div className="cbte__cliente">
        <div><span>Lote:</span><span>#{arqueo.idLote}</span></div>
        <div><span>Usuario:</span><span>{usuario}</span></div>
        <div><span>Abierto:</span><span>{fechaHora(arqueo.fechaApertura)}</span></div>
      </div>

      {arqueo.ingresoInicial && (
        <div className="cbte__pagos">
          <div><span>Fondo inicial</span><span>{formatearMoneda(arqueo.ingresoInicial.monto)}</span></div>
        </div>
      )}

      <table className="cbte__lineas">
        <thead><tr><th>Medio de pago</th><th className="num">Total</th></tr></thead>
        <tbody>
          {arqueo.acumulados.map((a) => (
            <tr key={a.idMedioPago}>
              <td className="desc">{a.descripcion}</td>
              <td className="num">{formatearMoneda(a.total)}</td>
            </tr>
          ))}
          {arqueo.acumulados.length === 0 && (
            <tr><td colSpan={2}>Sin movimientos todavía.</td></tr>
          )}
        </tbody>
      </table>

      {/* Anulaciones/retiros/vueltos ya están descontados de los acumulados de arriba — se listan
          aparte para que el cajero pueda justificar el faltante, igual que en la vista en pantalla. */}
      {arqueo.anulaciones.length > 0 && (
        <div className="cbte__pagos">
          <div><span>Anulaciones (NC)</span><span>−{formatearMoneda(arqueo.totalAnulaciones)}</span></div>
        </div>
      )}
      {arqueo.retiros.length > 0 && (
        <div className="cbte__pagos">
          <div><span>Retiros</span><span>−{formatearMoneda(arqueo.totalRetiros)}</span></div>
        </div>
      )}
      {arqueo.vueltos.length > 0 && (
        <div className="cbte__pagos">
          <div><span>Vueltos</span><span>−{formatearMoneda(arqueo.totalVueltos)}</span></div>
        </div>
      )}

      <div className="cbte__totales">
        <div className="total"><span>Total general</span><span>{formatearMoneda(arqueo.totalGeneral)}</span></div>
      </div>
    </div>
  );
}
