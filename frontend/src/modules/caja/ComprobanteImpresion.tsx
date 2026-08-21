import type { ComprobanteImpresion as Comprobante } from "../../shared/api/facturacion";
import "./comprobante-print.css";

const money = (n: number) =>
  n.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const cantidad = (n: number) =>
  n.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

const fecha = (iso?: string | null) => {
  if (!iso) return "";
  const d = new Date(iso);
  return d.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" });
};

const porcentaje = (alicuota: number) =>
  (alicuota * 100).toLocaleString("es-AR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/**
 * Comprobante impreso en sus formatos reales:
 *  - **A** (Responsable Inscripto / Monotributista): identifica al comprador con CUIT, domicilio,
 *    localidad y provincia; las líneas van en NETO y el IVA se discrimina por alícuota al pie.
 *  - **B** (consumidor final y demás): precio final por línea, sin discriminar IVA.
 *  - **X** (Presupuesto): documento sin valor fiscal, mismo formato de líneas que la B (precio
 *    final, sin discriminar impuestos, sea cual sea la condición de IVA del cliente).
 * La letra la decide el backend — acá solo se dibuja.
 *
 * Nota: A/B se emiten por controlador fiscal (equipo Hasar), no por factura electrónica — no hay
 * CAE/CAEA que mostrar acá. El comprobante fiscal real (con sus leyendas oficiales) lo imprime el
 * propio controlador aparte; esta vista es una copia/resumen para pantalla y reimpresión de respaldo
 * desde el navegador.
 */
export function ComprobanteImpresionView({ c, onCerrar, esReimpresion, textoVolver }: {
  c: Comprobante; onCerrar?: () => void;
  /** true cuando esto se reimprime tiempo después de la emisión original (ej. desde el módulo de
      Reimpresión), no en el momento mismo del cobro. Cambia el rótulo "ORIGINAL" a "COPIA" — un
      comprobante fiscal reimpreso no puede volver a decir "ORIGINAL". */
  esReimpresion?: boolean;
  textoVolver?: string;
}) {
  const esA = c.letra?.toUpperCase() === "A";
  const esPresupuesto = c.letra?.toUpperCase() === "X";
  const { emisor, cliente } = c;

  return (
    <>
      <div className={`cbte${esPresupuesto ? " cbte--presupuesto" : ""}`}>
        <div className="cbte__tipo">
          {esPresupuesto && <div className="cbte__x-grande">X</div>}
          <h2>{esPresupuesto ? "PRESUPUESTO" : c.tipoComprobante.toUpperCase()}</h2>
          <small>
            {esPresupuesto ? "Documento sin valor fiscal"
              : `${esReimpresion ? "COPIA" : "ORIGINAL"}${c.codigoArca ? ` Cod.: ${c.codigoArca}` : ""}`}
          </small>
        </div>

        {!esPresupuesto && (
          <div className="cbte__emisor">
            <strong>{emisor.razonSocial}</strong>
            <div>
              CUIT: {emisor.cuit ?? "—"}
              {emisor.condicionIva ? ` ${emisor.condicionIva}` : ""}
            </div>
            {emisor.domicilio && <div>{emisor.domicilio}</div>}
            {(emisor.localidad || emisor.provincia) && (
              <div>{[emisor.localidad, emisor.provincia].filter(Boolean).join(" - ")}</div>
            )}
            {emisor.ingresosBrutos && <div>Ing. Brutos: {emisor.ingresosBrutos}</div>}
            {emisor.inicioActividad && <div>Inicio Actividad: {fecha(emisor.inicioActividad)}</div>}
          </div>
        )}

        <div className="cbte__meta">
          <span>FECHA: {fecha(c.fecha)}</span>
          <span>Nro. T: {c.numeroCompleto}</span>
        </div>

        <div className="cbte__cliente">
          <div><span>Cliente:</span><span>{cliente.descripcion}</span></div>
          {esA ? (
            <>
              {cliente.cuit && <div><span>CUIT:</span><span>{cliente.cuit}</span></div>}
              {!cliente.cuit && cliente.documento && (
                <div><span>Documento:</span><span>{cliente.documento}</span></div>
              )}
              {cliente.domicilio && <div><span>DIRECCION:</span><span>{cliente.domicilio}</span></div>}
              <div><span>Cond. Ante IVA:</span><span>{cliente.condicionIva ?? "—"}</span></div>
              {cliente.localidad && <div><span>LOCALIDAD:</span><span>{cliente.localidad}</span></div>}
              {cliente.provincia && <div><span>PROVINCIA:</span><span>{cliente.provincia}</span></div>}
            </>
          ) : (
            !esPresupuesto && (
              <div><span>Cond. Ante IVA:</span><span>{cliente.condicionIva ?? "Consumidor final"}</span></div>
            )
          )}
        </div>

        {esA ? (
          <table className="cbte__lineas">
            <thead>
              <tr>
                <th>Descripcion</th>
                <th className="num">Unid</th>
                <th className="num">$ Unid.</th>
                <th className="num">$ Total</th>
              </tr>
            </thead>
            <tbody>
              {c.lineas.map((l, i) => (
                <tr key={i}>
                  <td className="desc">{l.descripcion}</td>
                  <td className="num">{cantidad(l.cantidad)}</td>
                  <td className="num">${money(l.precioUnitario)}</td>
                  <td className="num">${money(l.importe)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <table className="cbte__lineas">
            <thead>
              <tr>
                <th>Cantidad / Precio Unit.<br />Descripcion</th>
                <th className="num">IMPORTE</th>
              </tr>
            </thead>
            <tbody>
              {c.lineas.map((l, i) => (
                <tr key={i} className="b-linea">
                  <td className="desc">
                    {cantidad(l.cantidad)} x {money(l.precioUnitario)}
                    <small>{l.descripcion}</small>
                  </td>
                  <td className="num">${money(l.importe)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <div className="cbte__totales">
          <div><span>Descuento</span><span>${money(c.descuento)}</span></div>
          {esA && (
            <>
              <div><span>Subtotal</span><span>${money(c.neto)}</span></div>
              {c.ivaDiscriminado.map((iva) => (
                <div key={iva.alicuota}>
                  <span>IVA {porcentaje(iva.alicuota)}%</span>
                  <span>${money(iva.importe)}</span>
                </div>
              ))}
            </>
          )}
          {c.percepcionIva21 > 0 && (
            <div><span>Percepción IVA 21%</span><span>${money(c.percepcionIva21)}</span></div>
          )}
          {c.percepcionIva105 > 0 && (
            <div><span>Percepción IVA 10,5%</span><span>${money(c.percepcionIva105)}</span></div>
          )}
          {c.percepcionIibb > 0 && (
            <div><span>Percepción IIBB ({c.alicuotaIibb.toFixed(2)}%)</span><span>${money(c.percepcionIibb)}</span></div>
          )}
          <div className="total"><span>Total</span><span>${money(c.total)}</span></div>
        </div>

        {!esPresupuesto && c.pagos.length > 0 && (
          <div className="cbte__pagos">
            {c.pagos.map((p, i) => (
              <div key={i}><span>{p.descripcion}</span><span>${money(p.monto)}</span></div>
            ))}
          </div>
        )}

        <div className="cbte__cae">
          {esPresupuesto ? (
            <div className="cbte__leyenda">
              Documento no válido como factura
            </div>
          ) : (
            <div className="cbte__leyenda">
              Comprobante fiscal emitido por controlador fiscal homologado — vale como factura.
            </div>
          )}
        </div>
      </div>

      <div className="cbte__acciones cbte-no-print">
        <button className="primary" onClick={() => window.print()}>Imprimir</button>
        {onCerrar && <button onClick={onCerrar}>{textoVolver ?? "Nueva venta"}</button>}
      </div>
    </>
  );
}
