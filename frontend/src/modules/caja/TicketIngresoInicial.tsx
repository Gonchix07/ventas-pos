import { useEffect } from "react";
import { formatearMoneda } from "../../shared/ui/moneda";
import "./comprobante-print.css";

interface Props {
  fecha: Date;
  monto: number;
  descripcionCaja: string;
  usuario: string;
  onImpreso: () => void;
}

const fechaHora = (d: Date) =>
  d.toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });

/**
 * Ticket del fondo inicial de caja: se imprime solo (comandera no fiscal, vía el navegador) apenas
 * la caja queda abierta — mismo circuito que Retiro/Vale, ver VoucherComprobantePago. Va como
 * pantalla propia (gate en CajaPage, no dentro de IngresoInicialModal) porque el popup se cierra y
 * la pantalla pasa de "apertura" a "caja abierta" en el mismo momento: si el print viviera adentro
 * del popup, ese cambio de pantalla lo desmontaría antes de terminar de imprimir.
 */
export function TicketIngresoInicial({ fecha, monto, descripcionCaja, usuario, onImpreso }: Props) {
  useEffect(() => {
    const t = setTimeout(() => {
      window.print();
      onImpreso();
    }, 150);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="cbte cbte--ingreso cbte--sinPantalla">
      <div className="cbte__tipo"><h2>INGRESO DE FONDO INICIAL</h2></div>
      <div className="cbte__meta">
        <span>{fechaHora(fecha)}</span>
        <span>Caja {descripcionCaja}</span>
      </div>
      <div className="cbte__cliente">
        <div><span>Usuario:</span><span>{usuario}</span></div>
      </div>
      <div className="cbte__totales">
        <div className="total"><span>Ingresado</span><span>{formatearMoneda(monto)}</span></div>
      </div>
    </div>
  );
}
