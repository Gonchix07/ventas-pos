import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";

/**
 * Notificación emergente (toast), esquina inferior derecha: verde (--petrol) al confirmar un
 * guardado, roja (--danger) si la acción falló. Se usa sobre todo desde `client.ts`, que la
 * dispara sola en cualquier POST/PUT/DELETE a `/admin/*` (éxito o error) — mismo patrón que
 * `setSessionExpiredHandler`: el cliente HTTP no es un componente React, así que se registra un
 * callback en vez de usar el contexto directo.
 */
type TipoToast = "ok" | "error";
interface Toast { id: number; texto: string; tipo: TipoToast; }
type Notificar = (texto: string, tipo?: TipoToast) => void;

const ToastContext = createContext<Notificar | null>(null);

let idSeq = 0;
const DURACION_MS = 3000;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const mostrar = useCallback<Notificar>((texto, tipo = "ok") => {
    const id = ++idSeq;
    setToasts((t) => [...t, { id, texto, tipo }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), DURACION_MS);
  }, []);

  return (
    <ToastContext.Provider value={mostrar}>
      {children}
      <div className="toast-stack">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast--${t.tipo}`}>{t.texto}</div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

/** Para usar desde un componente: `const notificar = useToast(); notificar("Guardado");` */
export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error("useToast debe usarse dentro de <ToastProvider>");
  return ctx;
}

/**
 * Puente para código fuera de React (el interceptor de axios en client.ts). `ToastBridge` se monta
 * una sola vez adentro del `<ToastProvider>` y conecta el hook con la función global.
 */
let notificarGlobal: Notificar | null = null;
export function notificarAdmin(texto: string, tipo: TipoToast = "ok") {
  notificarGlobal?.(texto, tipo);
}
export function ToastBridge() {
  const notificar = useToast();
  const ref = useRef(notificar);
  ref.current = notificar;
  useEffect(() => {
    notificarGlobal = (texto, tipo) => ref.current(texto, tipo);
    return () => { notificarGlobal = null; };
  }, []);
  return null;
}
