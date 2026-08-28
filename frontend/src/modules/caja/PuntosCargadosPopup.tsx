interface Props {
  cliente: string;
  puntosOtorgados: number;
  puntosTotales: number;
  onCerrar: () => void;
}

const formatearPuntos = (n: number) => n.toLocaleString("es-AR");

/**
 * Popup de confirmación tras facturar: se muestra SOLO cuando la carga de puntos en puntos-app fue
 * exitosa (ver EmitirComprobanteResponse.fidelizacion, FacturacionService.EmitirAsync). Usa la
 * identidad visual de puntos-app (degradado violeta + estrella animada, ver Login.jsx/index.css de
 * ese proyecto) para que el cajero reconozca de un vistazo que es sobre el programa de fidelización,
 * no sobre la factura en sí. Al cerrar, Caja recién ahí muestra el comprobante para imprimir — ver
 * el uso de puntosPopupVisible en CajaPage.tsx.
 */
export function PuntosCargadosPopup({ cliente, puntosOtorgados, puntosTotales, onCerrar }: Props) {
  return (
    <div className="puntos-popup-fondo" role="dialog" aria-modal="true" aria-label="Puntos de fidelización cargados">
      <div className="puntos-popup-card">
        <span className="puntos-popup-star star-anim" aria-hidden="true">⭐</span>
        <h2>¡Puntos cargados!</h2>
        <p className="puntos-popup-cliente">{cliente}</p>
        <div className="puntos-popup-stats">
          <div>
            <span className="puntos-popup-valor">+{formatearPuntos(puntosOtorgados)}</span>
            <span className="puntos-popup-label">Puntos otorgados</span>
          </div>
          <div>
            <span className="puntos-popup-valor">{formatearPuntos(puntosTotales)}</span>
            <span className="puntos-popup-label">Puntos acumulados</span>
          </div>
        </div>
        <button className="puntos-popup-cerrar" onClick={onCerrar} autoFocus>Continuar</button>
      </div>
    </div>
  );
}
