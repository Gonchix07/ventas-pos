import { useEffect, useRef } from "react";

interface Opciones {
  /** Sólo escucha cuando la pantalla que lo usa está visible y espera lecturas. */
  activo: boolean;
  onCodigo: (codigo: string) => void;
  /** Descarta ráfagas cortas (una tecla suelta apretada sin querer no es un código). */
  minLargo?: number;
}

/**
 * Captura lecturas del lector de código de barras cuando NINGÚN campo de texto tiene el foco.
 *
 * Los lectores tipo "wedge" se comportan como un teclado: emiten los dígitos y cierran con Enter (o
 * Tab, según cómo esté configurado el lector). Si el foco no está en el input de escaneo —porque el
 * cajero hizo clic en otro lado, apretó un botón, o la pantalla recién se montó— esas teclas se
 * pierden y la lectura no hace nada. Este hook las junta a nivel de documento y las entrega igual.
 *
 * Cuando el foco SÍ está en un input/textarea/select se mantiene fuera del camino: ahí escribe el
 * campo (y el input de escaneo ya maneja su propio Enter), así no se duplica la lectura ni se
 * ensucia lo que el cajero está tipeando a mano.
 */
export function useLectorCodigo({ activo, onCodigo, minLargo = 3 }: Opciones) {
  const buffer = useRef("");
  const ultimaTecla = useRef(0);
  // El callback va por ref para no reinstalar el listener en cada render (la pantalla de caja se
  // renderiza en cada tecla), pero leyendo siempre la versión más nueva.
  const callback = useRef(onCodigo);
  callback.current = onCodigo;

  useEffect(() => {
    if (!activo) return;

    const manejar = (e: KeyboardEvent) => {
      const destino = e.target as HTMLElement | null;
      const tag = destino?.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || destino?.isContentEditable) {
        buffer.current = "";
        return;
      }
      if (e.ctrlKey || e.altKey || e.metaKey) return;

      const ahora = Date.now();
      // Corta restos de una lectura anterior o de teclas sueltas apretadas hace rato.
      if (ahora - ultimaTecla.current > 1000) buffer.current = "";
      ultimaTecla.current = ahora;

      if (e.key === "Enter" || e.key === "Tab") {
        const codigo = buffer.current.trim();
        buffer.current = "";
        if (codigo.length >= minLargo) {
          // Sin esto, un Enter con el foco en un botón (ej. "Anular") lo activaría.
          e.preventDefault();
          callback.current(codigo);
        }
        return;
      }

      if (e.key.length === 1) {
        buffer.current += e.key;
        e.preventDefault(); // que no dispare atajos ni haga scroll con la barra espaciadora
      }
    };

    document.addEventListener("keydown", manejar);
    return () => document.removeEventListener("keydown", manejar);
  }, [activo, minLargo]);
}
