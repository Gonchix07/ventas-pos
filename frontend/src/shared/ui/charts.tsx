import { formatearMoneda } from "./moneda";

/**
 * Gráficos propios en SVG plano, sin librería externa: el frontend no traía ninguna (ver
 * package.json) y esto alcanza para lo que necesita el dashboard de Ventas (Admin). Si en algún
 * momento se necesitan gráficos más ricos (zoom, tooltips interactivos, exportar imagen) esto es
 * candidato a reemplazarse por una librería dedicada.
 */

interface PuntoSerie { etiqueta: string; total: number; }

/** Línea de evolución en el tiempo (por hora/día/mes según el período elegido). `formatear` deja
 * usarlo para series que no son plata (ej. Efectividad de cajeros, en %) — por defecto sigue
 * formateando como moneda, que es el uso original (Ventas). */
export function GraficoLinea({ datos, alto = 220, formatear = formatearMoneda, ariaLabel = "Evolución en el período" }: {
  datos: PuntoSerie[]; alto?: number; formatear?: (n: number) => string; ariaLabel?: string;
}) {
  const ancho = 720;
  const padIzq = 60, padDer = 16, padSup = 16, padInf = 34;
  const areaAncho = ancho - padIzq - padDer;
  const areaAlto = alto - padSup - padInf;

  if (datos.length === 0) return <p className="muted">Sin datos en el período.</p>;

  const max = Math.max(...datos.map((d) => d.total), 1);
  const paso = datos.length > 1 ? areaAncho / (datos.length - 1) : 0;
  const puntos = datos.map((d, i) => ({
    x: padIzq + i * paso,
    y: padSup + areaAlto - (d.total / max) * areaAlto,
    ...d,
  }));

  const linea = puntos.map((p) => `${p.x},${p.y}`).join(" ");
  const area = `${padIzq},${padSup + areaAlto} ${linea} ${puntos[puntos.length - 1].x},${padSup + areaAlto}`;

  // No todas las etiquetas entran sin superponerse: se muestra 1 de cada N según cuántos puntos haya.
  const saltoEtiquetas = Math.max(1, Math.ceil(datos.length / 10));

  return (
    <svg viewBox={`0 0 ${ancho} ${alto}`} className="chart-svg" role="img"
      aria-label={ariaLabel}>
      {[0, 0.5, 1].map((f) => {
        const y = padSup + areaAlto * (1 - f);
        return (
          <g key={f}>
            <line x1={padIzq} y1={y} x2={ancho - padDer} y2={y} className="chart-grid" />
            <text x={padIzq - 8} y={y + 4} className="chart-eje-y" textAnchor="end">
              {formatear(max * f)}
            </text>
          </g>
        );
      })}
      <polygon points={area} className="chart-area" />
      <polyline points={linea} className="chart-linea" />
      {puntos.map((p, i) => (
        <g key={i}>
          <circle cx={p.x} cy={p.y} r={3} className="chart-punto">
            <title>{`${p.etiqueta}: ${formatear(p.total)}`}</title>
          </circle>
          {i % saltoEtiquetas === 0 && (
            <text x={p.x} y={alto - 10} className="chart-eje-x" textAnchor="middle">{p.etiqueta}</text>
          )}
        </g>
      ))}
    </svg>
  );
}

// Paleta fija de 10 colores (una por porción, alcanza para el top 10 de familias): variaciones de
// tono sobre el verde petróleo de la marca, elegidas para que se distingan bien entre sí en una
// torta chica. Si en algún momento se necesitan más de 10 porciones, hay que sumar colores acá.
const PALETA_TORTA = [
  "#0c6b63", "#2f6690", "#b8763e", "#7b5ea7", "#3f8f5c",
  "#b3475a", "#5d7a8c", "#a3372e", "#8a8f5c", "#4a4e69",
];

/** Torta (donut) de un ranking de categorías — hoy la usa "Familias más vendidas". */
export function GraficoTorta({ datos }: { datos: { label: string; total: number }[] }) {
  if (datos.length === 0) return <p className="muted">Sin datos en el período.</p>;

  const total = datos.reduce((s, d) => s + d.total, 0);
  const cx = 110, cy = 110, r = 80, grosor = 34;
  const circunferencia = 2 * Math.PI * r;

  let acumulado = 0;
  const porciones = datos.map((d, i) => {
    const fraccion = total > 0 ? d.total / total : 0;
    const largo = fraccion * circunferencia;
    const offset = -acumulado * circunferencia;
    acumulado += fraccion;
    return { ...d, fraccion, largo, offset, color: PALETA_TORTA[i % PALETA_TORTA.length] };
  });

  return (
    <div className="torta-wrap">
      <svg viewBox="0 0 220 220" className="chart-svg torta-svg" role="img" aria-label="Familias más vendidas">
        <g transform={`rotate(-90 ${cx} ${cy})`}>
          {porciones.map((p, i) => (
            <circle key={i} cx={cx} cy={cy} r={r} fill="none" stroke={p.color} strokeWidth={grosor}
              strokeDasharray={`${p.largo} ${circunferencia - p.largo}`} strokeDashoffset={p.offset}>
              <title>{`${p.label}: ${formatearMoneda(p.total)} (${(p.fraccion * 100).toFixed(1)}%)`}</title>
            </circle>
          ))}
        </g>
        <text x={cx} y={cy - 4} textAnchor="middle" className="torta-centro-valor">{formatearMoneda(total)}</text>
        <text x={cx} y={cy + 14} textAnchor="middle" className="torta-centro-label">total</text>
      </svg>
      <ul className="torta-legend">
        {porciones.map((p, i) => (
          <li key={i}>
            <span className="torta-color" style={{ background: p.color }} />
            <span className="torta-legend-label">{p.label}</span>
            <span className="mono torta-legend-valor">{(p.fraccion * 100).toFixed(1)}%</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * Lista rankeada con barra proporcional al máximo de la lista (top productos/clientes/sectores).
 * Es más legible que un gráfico de barras cuando las etiquetas son texto largo (descripciones de
 * artículo, razón social) y compite por poco espacio horizontal.
 */
export function ListaRankeada({ items }: {
  items: { label: string; sub?: string; valor: number; valorLabel: string }[];
}) {
  if (items.length === 0) return <p className="muted">Sin datos en el período.</p>;
  const max = Math.max(...items.map((i) => i.valor), 1);
  return (
    <ul className="ranking-lista">
      {items.map((it, i) => (
        <li key={i}>
          <div className="ranking-cabecera">
            <span className="ranking-pos">{i + 1}</span>
            <span className="ranking-label">{it.label}{it.sub && <span className="muted"> · {it.sub}</span>}</span>
            <span className="mono ranking-valor">{it.valorLabel}</span>
          </div>
          <div className="ranking-barra-fondo">
            <div className="ranking-barra" style={{ width: `${(it.valor / max) * 100}%` }} />
          </div>
        </li>
      ))}
    </ul>
  );
}
