import axios from "axios";

// Si no se fija VITE_API_URL, se usa el MISMO host desde el que se abrió el frontend (no un
// "localhost" fijo) — así funciona igual si se entra por localhost, por la IP de LAN del
// servidor (ej. una caja real conectándose a http://192.168.4.4:5173) o por cualquier otro host.
// "localhost" fijo rompería en cualquier PC que no sea la del propio servidor: cada PC tiene su
// propio "localhost", que no es el servidor.
const baseURL = import.meta.env.VITE_API_URL ?? `http://${window.location.hostname}:5038/api/v1`;

export const api = axios.create({ baseURL });

const TOKEN_KEY = "pos.token";
const REFRESH_KEY = "pos.refreshToken";

export function setToken(token: string | null) {
  if (token) localStorage.setItem(TOKEN_KEY, token);
  else localStorage.removeItem(TOKEN_KEY);
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setRefreshToken(token: string | null) {
  if (token) localStorage.setItem(REFRESH_KEY, token);
  else localStorage.removeItem(REFRESH_KEY);
}

export function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_KEY);
}

// Adjunta el JWT a cada request.
api.interceptors.request.use((config) => {
  const token = getToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// El AuthProvider registra acá qué hacer cuando la sesión termina de verdad (el access token
// venció Y el refresh también falló, o no hay refresh token): limpiar la sesión y mandar al
// login, en vez de dejar a la pantalla actual reintentando en loop con un error crudo.
let onSessionExpired: (() => void) | null = null;
export function setSessionExpiredHandler(handler: (() => void) | null) {
  onSessionExpired = handler;
}

interface RefreshResponseData {
  token: string;
  refreshToken: string;
}

// El access token dura poco (15 min) a propósito, precisamente porque existe este refresh
// silencioso: varios requests pueden pisarse un 401 al mismo tiempo (ej. varias pantallas
// cargando datos juntas) — se comparte una única promesa de refresh en vuelo para no canjear el
// refresh token (de un solo uso) más de una vez en simultáneo.
let refreshEnCurso: Promise<string | null> | null = null;

async function intentarRefrescar(): Promise<string | null> {
  const refreshToken = getRefreshToken();
  if (!refreshToken) return null;
  try {
    const { data } = await api.post<{ ok: boolean; data: RefreshResponseData | null }>(
      "/auth/refresh",
      { refreshToken },
    );
    if (!data.ok || !data.data) return null;
    setToken(data.data.token);
    setRefreshToken(data.data.refreshToken);
    return data.data.token;
  } catch {
    return null;
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error?.config;
    const url: string = original?.url ?? "";
    // /auth/login y /auth/refresh nunca disparan un refresh de sí mismos (evita recursión); un
    // 401 ahí es una credencial/token realmente inválido, no una sesión para renovar.
    const esEndpointDeAuth = url.includes("/auth/login") || url.includes("/auth/refresh");

    if (error?.response?.status === 401 && !esEndpointDeAuth && original && !original._reintentadoTrasRefresh) {
      original._reintentadoTrasRefresh = true;
      refreshEnCurso ??= intentarRefrescar().finally(() => { refreshEnCurso = null; });
      const nuevoToken = await refreshEnCurso;

      if (nuevoToken) {
        original.headers = original.headers ?? {};
        original.headers.Authorization = `Bearer ${nuevoToken}`;
        return api(original); // reintenta el request original, ahora con el token renovado
      }

      // El refresh también falló (vencido/revocado/inexistente): la sesión terminó de verdad.
      setToken(null);
      setRefreshToken(null);
      onSessionExpired?.();
    }
    return Promise.reject(error);
  },
);

// Envoltura uniforme del backend: { ok, data, error }.
export interface ApiResult<T> {
  ok: boolean;
  data: T | null;
  error: { code: string; message: string } | null;
}

// Desenvuelve la respuesta del backend y homogeniza los errores para la UI.
// Dos casos: (a) HTTP 200 con ok:false (poco usado hoy) y (b) un status de error real
// (409/403/404/...) que hace que axios rechace la promesa ANTES de llegar al body — en ese
// caso el mensaje por defecto de axios es genérico ("Request failed with status code 409") y
// tapa el mensaje de negocio real que sí viene en response.data.error.message
// (ver ExceptionMiddleware). Por eso se prioriza siempre el mensaje del backend si está presente.
export async function unwrap<T>(p: Promise<{ data: ApiResult<T> }>): Promise<T> {
  try {
    const { data } = await p;
    if (!data.ok || data.data === null) throw new Error(data.error?.message ?? "Error de servidor");
    return data.data;
  } catch (e) {
    if (axios.isAxiosError<ApiResult<T>>(e)) {
      throw new Error(e.response?.data?.error?.message ?? e.message);
    }
    throw e;
  }
}
