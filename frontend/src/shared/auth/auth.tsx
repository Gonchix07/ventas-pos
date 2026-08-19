import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  api, unwrap, setToken, getToken, setRefreshToken, getRefreshToken, setSessionExpiredHandler,
  type ApiResult,
} from "../api/client";

export interface LoginResult {
  token: string;
  expiraUtc: string;
  usuario: string;
  rol: string;
  idSucursal: number | null;
  idCaja: number | null;
  modulos: string[];
  refreshToken: string;
  refreshExpiraUtc: string;
  /** IP de origen vista por el servidor al loguear — la misma que usó para resolver la caja. */
  ip: string | null;
}

// Forma de /auth/me: mismo contenido que LoginResult salvo el token/expiraUtc (que no se
// reemiten al rehidratar) y con idSucursal/idCaja como string (vienen de claims JWT). El ip acá
// es el del request ACTUAL a /auth/me, no el del login original (útil si cambia).
interface MeResponse {
  usuario: string;
  rol: string;
  idSucursal: string | null;
  idCaja: string | null;
  modulos: string[];
  ip: string | null;
}

interface AuthState {
  usuario: string | null;
  rol: string | null;
  modulos: string[];
  idSucursal: number | null;
  idCaja: number | null;
  /** IP con la que el servidor ve esta sesión — para mostrarle al usuario "dónde está". */
  ip: string | null;
  isAuthenticated: boolean;
  /** True mientras se intenta rehidratar la sesión desde el token guardado (al recargar la página). */
  isLoading: boolean;
  login: (usuario: string, clave: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<LoginResult | null>(null);
  // Arranca en "cargando" solo si hay algo que rehidratar (access o refresh token guardados); si
  // no hay ninguno, no hay nada que esperar y se puede ir directo a /login.
  const [isLoading, setIsLoading] = useState(() => !!getToken() || !!getRefreshToken());

  // Al montar (F5 / primera carga), se rehidrata contra /auth/me. Sin esto, tras un refresh el
  // frontend quedaba "autenticado" por la sola presencia del token pero con idSucursal/idCaja/rol
  // en null, lo que hacía que pantallas como Caja cayeran a valores por defecto (idCaja=1) en vez
  // de los reales del usuario. Si el access token ya venció, el interceptor de client.ts hace un
  // refresh silencioso y reintenta esta misma llamada — no hace falta manejarlo acá.
  useEffect(() => {
    if (!getToken() && !getRefreshToken()) {
      setIsLoading(false);
      return;
    }
    let cancelado = false;
    api
      .get<ApiResult<MeResponse>>("/auth/me")
      .then(({ data }) => {
        if (cancelado) return;
        if (!data.ok || !data.data) {
          setToken(null);
          setRefreshToken(null);
          return;
        }
        const me = data.data;
        setSession({
          token: getToken() ?? "",
          expiraUtc: "",
          usuario: me.usuario,
          rol: me.rol,
          idSucursal: me.idSucursal ? Number(me.idSucursal) : null,
          idCaja: me.idCaja ? Number(me.idCaja) : null,
          modulos: me.modulos,
          refreshToken: getRefreshToken() ?? "",
          refreshExpiraUtc: "",
          ip: me.ip,
        });
      })
      .catch(() => {
        // Ni el access token ni el refresh (el interceptor ya lo intentó) sirvieron: no queda
        // "autenticado a medias".
        if (!cancelado) { setToken(null); setRefreshToken(null); }
      })
      .finally(() => {
        if (!cancelado) setIsLoading(false);
      });
    return () => {
      cancelado = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Se dispara solo cuando la sesión terminó DE VERDAD (access vencido y el refresh también
  // falló, o no había refresh) — ver client.ts. El <RequireAuth> de App.tsx hace el redirect a
  // /login al ver isAuthenticated=false.
  useEffect(() => {
    setSessionExpiredHandler(() => setSession(null));
    return () => setSessionExpiredHandler(null);
  }, []);

  const login = async (usuario: string, clave: string) => {
    // Vía unwrap: si el login falla con un status HTTP real (401 credenciales, 403/429
    // bloqueo por intentos), muestra el mensaje de negocio del backend en vez del genérico de
    // axios ("Request failed with status code 401"). La caja/sucursal ya NO se resuelve por un
    // "nombre de PC" mandado por el navegador (window.location.hostname sería igual en todas las
    // cajas, que acceden a la misma URL) — el backend la resuelve por la IP de origen del request.
    const data = await unwrap(
      api.post<ApiResult<LoginResult>>("/auth/login", { usuario, clave }),
    );
    setToken(data.token);
    setRefreshToken(data.refreshToken);
    setSession(data);
  };

  const logout = () => {
    // Best-effort: revoca el refresh token del lado del servidor. Si falla (sin red, etc.) igual
    // se cierra la sesión localmente — no bloquea al usuario por esto.
    const refreshToken = getRefreshToken();
    if (refreshToken) {
      api.post("/auth/logout", { refreshToken }).catch(() => {});
    }
    setToken(null);
    setRefreshToken(null);
    setSession(null);
  };

  const value = useMemo<AuthState>(
    () => ({
      usuario: session?.usuario ?? null,
      rol: session?.rol ?? null,
      modulos: session?.modulos ?? [],
      idSucursal: session?.idSucursal ?? null,
      idCaja: session?.idCaja ?? null,
      ip: session?.ip ?? null,
      isAuthenticated: !!session,
      isLoading,
      login,
      logout,
    }),
    [session, isLoading],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth debe usarse dentro de AuthProvider");
  return ctx;
}
