import { useEffect } from "react";
import { formatearMoneda } from "../../shared/ui/moneda";
import "./comprobante-print.css";

export interface ItemVoucherPago {
  /** Descripción del medio de pago (ej. "VALE"). */
  descripcionMedio: string;
  monto: number;
}

interface Props {
  fecha: Date;
  clienteCodigo: string;
  clienteDescripcion: string;
  numeroComprobante: string;
  items: ItemVoucherPago[];
  onImpreso: () => void;
}

const fechaHora = (d: Date) =>
  d.toLocaleString("es-AR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });

/**
 * Comprobante no fiscal para medios de pago marcados "Imprime comprobante" (hoy, VALE): se imprime
 * solo, apenas se emite la venta, para que el empleado lo firme — mismo circuito de comandera que
 * Retiro de efectivo (window.print() contra la impresora del sistema, no la fiscal).
 *
 * Se muestra a solas (nada más en el DOM aparte de este ticket) porque window.print() imprime
 * TODO lo que esté marcado visible con la regla `.cbte`: si conviviera con el comprobante fiscal en
 * pantalla se imprimirían pisados uno sobre el otro. CajaPage lo renderiza en reemplazo de la
 * pantalla del comprobante mientras imprime, y recién after eso destraba la vista normal.
 */
export function VoucherComprobantePago({
  fecha, clienteCodigo, clienteDescripcion, numeroComprobante, items, onImpreso,
}: Props) {
  useEffect(() => {
    const t = setTimeout(() => {
      window.print();
      onImpreso();
    }, 150);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const total = items.reduce((acc, i) => acc + i.monto, 0);

  return (
    <div className="cbte cbte--vale cbte--sinPantalla">
      <div className="cbte__tipo">
        <h2>COMPROBANTE DE PAGO</h2>
      </div>
      <div className="cbte__meta">
        <span>{fechaHora(fecha)}</span>
        <span>Cbte. {numeroComprobante}</span>
      </div>
      <div className="cbte__cliente">
        <div><span>Cliente:</span><span>{clienteCodigo} · {clienteDescripcion}</span></div>
      </div>
      <div className="cbte__pagos">
        {items.map((i, idx) => (
          <div key={idx}><span>{i.descripcionMedio}</span><span>{formatearMoneda(i.monto)}</span></div>
        ))}
      </div>
      <div className="cbte__totales">
        <div className="total"><span>Total</span><span>{formatearMoneda(total)}</span></div>
      </div>
      {/* 4 saltos de línea de aire antes de la firma, para que el empleado tenga lugar de sobra. */}
      <br /><br /><br /><br />
      <div className="cbte__firma">
        <div className="cbte__firma-linea" />
        <div className="cbte__firma-etiquetas"><span>Firma</span><span>Aclaración</span></div>
      </div>
    </div>
  );
}
