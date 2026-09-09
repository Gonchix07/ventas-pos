/**
 * Íconos de acción de fila (editar/eliminar), compartidos por todas las tablas del admin para no
 * repetir el mismo SVG en cada página. Van inline en el JSX (no hace falta data-URI: el CSP solo
 * restringe recursos externos, y esto es markup, no una imagen cargada aparte).
 */
export function IconEditar() {
  return (
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
      <path d="M13.5 3.5 16.5 6.5 6.5 16.5 3 17l.5-3.5 10-10Z" />
    </svg>
  );
}

export function IconEliminar() {
  return (
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4.5 5.5h11M8 5.5V4a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v1.5M6 5.5 6.7 16a1 1 0 0 0 1 .9h4.6a1 1 0 0 0 1-.9l.7-10.5" />
      <path d="M8.3 8.5v5M11.7 8.5v5" />
    </svg>
  );
}

export function IconAgregar() {
  return (
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M10 4.5v11M4.5 10h11" />
    </svg>
  );
}

/** Baja lógica (desactivar), distinto de eliminar: un círculo tachado en vez de un tacho de basura,
 * para no insinuar un borrado físico donde en realidad el registro se conserva inactivo. */
export function IconBaja() {
  return (
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="10" cy="10" r="7" />
      <path d="M5.5 5.5 14.5 14.5" />
    </svg>
  );
}
