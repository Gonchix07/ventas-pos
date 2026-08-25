import { useEffect, useRef } from "react";
import JsBarcode from "jsbarcode";
import type { ClienteTicket } from "../../shared/api/clientes";
import "../caja/comprobante-print.css";

interface Props {
  cliente: ClienteTicket;
  onImpreso: () => void;
}

/**
 * Ticket de mostrador del módulo "Clientes": se imprime solo (comandera no fiscal, vía el
 * navegador, window.print()) igual que TicketIngresoInicial — mismo motivo por el que vive como
 * pantalla propia y no dentro de un modal: si el print viviera adentro del popup de selección, el
 * cambio de pantalla lo desmontaría antes de terminar de imprimir.
 *
 * El código de barras de la tarjeta es un gráfico real (Code128, vía jsbarcode) para que se pueda
 * volver a escanear el ticket en cualquier puesto — no un número impreso como texto.
 */
export function TicketCliente({ cliente, onImpreso }: Props) {
  const barcodeRef = useRef<SVGSVGElement>(null);

  useEffect(() => {
    if (cliente.nroTarjeta && barcodeRef.current) {
      JsBarcode(barcodeRef.current, cliente.nroTarjeta, {
        format: "CODE128",
        displayValue: false,
        margin: 0,
        height: 50,
      });
    }
    const t = setTimeout(() => {
      window.print();
      onImpreso();
    }, 150);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="cbte cbte--cliente cbte--sinPantalla">
      <div className="cbte__tipo"><h2>HERGO MAYORISTA</h2></div>
      <div className="cbte__cliente">
        <div><span>Código de Cliente:</span><span>{cliente.codigoInt}</span></div>
        <div><span>Nombre:</span><span>{cliente.descripcion}</span></div>
        {cliente.origen === "Autorizado" && <div><span /><span>(cuenta donde está autorizado a comprar)</span></div>}
        <div><span>DNI:</span><span>{cliente.documento ?? "—"}</span></div>
        <div><span>Nro. de Tarjeta:</span><span>{cliente.nroTarjeta ?? "—"}</span></div>
        <div><span>Tipo de Tarjeta:</span><span>{(cliente.tipoTarjeta ?? "—").toUpperCase()}</span></div>
      </div>
      {cliente.nroTarjeta && (
        <div className="cbte__barcode">
          <svg ref={barcodeRef} />
          <span className="mono">{cliente.nroTarjeta}</span>
        </div>
      )}
    </div>
  );
}
