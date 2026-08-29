import { useEffect, useRef, useState } from "react";

/**
 * Formato de moneda argentino: separador de miles "." y decimal "," → `$ 1.000,99`.
 *
 * Se usa `Intl` con es-AR en vez de armar la cadena a mano para que el agrupado de miles y el
 * redondeo salgan del runtime y no de una regex propia.
 */
const NF = new Intl.NumberFormat("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** `1234.5` → `"$ 1.234,50"`. Para mostrar importes en tablas y totales. */
export function formatearMoneda(valor: number | null | undefined): string {
  if (valor === null || valor === undefined || Number.isNaN(valor)) return "—";
  return `$ ${NF.format(valor)}`;
}

/** `1234.5` → `"1.234,50"` (sin el signo, para meter dentro de un input con `$` al costado). */
export function formatearNumero(valor: number): string {
  return NF.format(valor);
}

/**
 * Convierte lo tipeado a número. Acepta lo que el usuario realmente escribe: con o sin separadores
 * de miles, y usando "," o "." como decimal. Devuelve null si no hay un número válido.
 *
 * Ojo con el caso ambiguo "1.234": en es-AR el "." es separador de miles, así que se interpreta
 * como mil doscientos treinta y cuatro, NO como 1,234.
 */
export function parsearMoneda(texto: string): number | null {
  const limpio = (texto ?? "").replace(/[^\d.,-]/g, "").trim();
  if (limpio === "" || limpio === "-") return null;

  let normalizado: string;
  if (limpio.includes(",")) {
    // Hay coma → la coma es el decimal y los puntos son miles.
    normalizado = limpio.replace(/\./g, "").replace(",", ".");
  } else {
    // Sin coma: los puntos son separadores de miles (es-AR), salvo que quede un solo punto con
    // 1 o 2 decimales detrás, que es cómo la gente teclea rápido un importe ("1234.5").
    const partes = limpio.split(".");
    normalizado = partes.length === 2 && partes[1].length > 0 && partes[1].length <= 2
      ? limpio
      : limpio.replace(/\./g, "");
  }

  const n = Number(normalizado);
  return Number.isFinite(n) ? n : null;
}

/** Agrupa la parte entera con "." mientras se escribe, respetando los decimales a medio tipear. */
function formatearMientrasEscribe(texto: string): string {
  const negativo = texto.trim().startsWith("-");
  const soloValidos = texto.replace(/[^\d.,]/g, "");

  // Se toma la PRIMERA coma como separador decimal; el resto de comas/puntos se descartan como
  // separadores de miles que el usuario haya tipeado.
  const iComa = soloValidos.indexOf(",");
  let entero = iComa >= 0 ? soloValidos.slice(0, iComa) : soloValidos;
  let decimales = iComa >= 0 ? soloValidos.slice(iComa + 1) : null;

  entero = entero.replace(/[.,]/g, "");
  if (decimales !== null) decimales = decimales.replace(/[.,]/g, "").slice(0, 2);

  const enteroAgrupado = entero === "" ? "" : Number(entero).toLocaleString("es-AR");

  let salida = enteroAgrupado;
  if (decimales !== null) salida = `${enteroAgrupado === "" ? "0" : enteroAgrupado},${decimales}`;
  return (negativo ? "-" : "") + salida;
}

interface Props {
  /** Valor numérico; null = campo vacío. */
  value: number | null;
  onChange: (valor: number | null) => void;
  placeholder?: string;
  disabled?: boolean;
  autoFocus?: boolean;
  className?: string;
  style?: React.CSSProperties;
  onEnter?: () => void;
  /** Se dispara al salir del campo, DESPUÉS de normalizar el texto y llamar a onChange — para
   *  refrescar cálculos derivados que a propósito no siguen cada tecla (ver "Falta cubrir" en
   *  CajaPage), sin que quede desactualizado una vez que el cajero terminó de escribir. */
  onBlur?: () => void;
}

/**
 * Input de importe que se va formateando a medida que se escribe (`1000,99` → `1.000,99`) y
 * muestra el `$` fijo adelante. Hacia afuera trabaja siempre con `number`, así que el que lo usa
 * no se entera del formateo.
 */
export function MonedaInput({ value, onChange, placeholder, disabled, autoFocus, className, style, onEnter, onBlur }: Props) {
  const [texto, setTexto] = useState(value === null ? "" : formatearNumero(value));

  // Último valor que emitimos hacia arriba. Sirve para distinguir "el padre me devolvió lo que yo
  // mismo mandé" (no hay que tocar el texto: rompería lo que se está tipeando, ej. "1.234," se
  // volvería "1.234,00" en medio de la escritura) de un cambio genuinamente externo (se eligió
  // otro artículo), que sí debe reflejarse. No se puede depender del foco para esto: el evento
  // puede no llegar y el texto quedaría pisado en cada tecla.
  const ultimoEmitido = useRef<number | null>(value);

  useEffect(() => {
    if (value === ultimoEmitido.current) return;
    ultimoEmitido.current = value;
    setTexto(value === null ? "" : formatearNumero(value));
  }, [value]);

  const alEscribir = (crudo: string) => {
    const formateado = formatearMientrasEscribe(crudo);
    setTexto(formateado);
    const n = parsearMoneda(formateado);
    ultimoEmitido.current = n;
    onChange(n);
  };

  return (
    <span className={`moneda-input${disabled ? " disabled" : ""}${className ? ` ${className}` : ""}`} style={style}>
      <span className="moneda-signo">$</span>
      <input
        inputMode="decimal"
        value={texto}
        placeholder={placeholder ?? "0,00"}
        disabled={disabled}
        autoFocus={autoFocus}
        onBlur={() => {
          // Al salir se normaliza a 2 decimales ("1.000,5" → "1.000,50").
          const n = parsearMoneda(texto);
          ultimoEmitido.current = n;
          setTexto(n === null ? "" : formatearNumero(n));
          onChange(n);
          onBlur?.();
        }}
        onKeyDown={(e) => {
          if (e.key === "Enter" && onEnter) { onEnter(); return; }
          // El punto del teclado numérico (Decimal del numpad) tiene que cargar como coma, que es
          // el separador decimal es-AR — si no, "50." se lee como separador de miles y se pierden
          // los centavos. insertText (no un setState manual) para no perder la posición del cursor.
          if (e.key === ".") {
            e.preventDefault();
            document.execCommand("insertText", false, ",");
          }
        }}
        onChange={(e) => alEscribir(e.target.value)}
      />
    </span>
  );
}
