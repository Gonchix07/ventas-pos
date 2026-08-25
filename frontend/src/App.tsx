import { BrowserRouter, Routes, Route, Navigate, useLocation } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { AuthProvider, useAuth } from "./shared/auth/auth";
import { LoginPage } from "./modules/login/LoginPage";
import { DashboardPage } from "./modules/dashboard/DashboardPage";
import { AdminLayout } from "./modules/admin/AdminLayout";
import { LookupPage } from "./modules/admin/LookupPage";
import { FamiliasPage } from "./modules/admin/FamiliasPage";
import { ClientesPage } from "./modules/admin/ClientesPage";
import { ArticulosPage } from "./modules/admin/ArticulosPage";
import { ListasPreciosPage } from "./modules/admin/ListasPreciosPage";
import { PagosPage } from "./modules/admin/PagosPage";
import { EstructuraPage } from "./modules/admin/EstructuraPage";
import { ConfiguracionesPage } from "./modules/admin/ConfiguracionesPage";
import { EstructuraCajaPage } from "./modules/admin/EstructuraCajaPage";
import { AsignacionCajasPage } from "./modules/admin/AsignacionCajasPage";
import { UsuariosPage } from "./modules/admin/UsuariosPage";
import { PermisosPage } from "./modules/admin/PermisosPage";
import { ConveniosPage } from "./modules/admin/ConveniosPage";
import { CuentaCorrientePage } from "./modules/admin/CuentaCorrientePage";
import { ClustersPage } from "./modules/admin/ClustersPage";
import { TarjetasPage } from "./modules/admin/TarjetasPage";
import { PadronesPage } from "./modules/admin/PadronesPage";
import { OfertasPage } from "./modules/admin/OfertasPage";
import { OfertasMedioPagoPage } from "./modules/admin/OfertasMedioPagoPage";
import { CajaPage } from "./modules/caja/CajaPage";
// Alias: ya existe un ClientesPage (ABM de Administración) importado abajo con el mismo nombre de
// componente — este es la ficha/ticket de mostrador del módulo "Clientes" del menú principal.
import { ClientesPage as ClientesModuloPage } from "./modules/clientes/ClientesPage";
import { TesoreriaPage } from "./modules/admin/TesoreriaPage";
import { CuponesPage } from "./modules/admin/CuponesPage";
import { EtiquetasPage } from "./modules/etiquetas/EtiquetasPage";
import { ReimpresionPage } from "./modules/reimpresion/ReimpresionPage";
import { VentasPage } from "./modules/ventas/VentasPage";
import { FacturacionCaeaPage } from "./modules/caea/FacturacionCaeaPage";
import "./App.css";

const queryClient = new QueryClient();

/**
 * `roles`: lista fija de siempre (igual criterio que el backend, ver ModuloAutorizadoAttribute).
 * `modulo`: además de esa lista, deja pasar a cualquier rol al que "Permisos por rol" (Admin →
 * Sistema) le haya dado acceso a este módulo — mismo claim "modulo" del JWT que ya valida el
 * backend, así que un 403 de la API y el bloqueo de esta ruta siempre están de acuerdo. Si no se
 * pasa `modulo`, el criterio es solo por rol, como antes.
 */
function RequireAuth({ children, roles, modulo }: { children: ReactNode; roles?: string[]; modulo?: string }) {
  const { isAuthenticated, isLoading, rol, modulos } = useAuth();
  // Mientras se rehidrata la sesión desde el token guardado (F5), no se decide nada todavía: ni
  // se manda a /login ni se renderiza la pantalla protegida con datos a medio cargar.
  if (isLoading) return null;
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  const porRol = !roles || (!!rol && roles.includes(rol));
  const porModulo = !!modulo && modulos.includes(modulo);
  if (roles && !porRol && !porModulo) return <Navigate to="/" replace />;
  if (!roles && modulo && !porModulo) return <Navigate to="/" replace />;
  return <>{children}</>;
}

/**
 * Envuelve <Routes> para animar la ENTRADA a un módulo (desplazamiento lateral + desvanecimiento,
 * ver .page-transition en App.css). La key es solo el primer segmento del path ("caja", "admin",
 * "tesoreria", ...) — así remonta (y por lo tanto anima) al cambiar de módulo desde el menú
 * principal o entre módulos entre sí, pero NO en cada navegación interna dentro de un mismo módulo
 * (ej. moverse entre secciones del ABM de Administración no debe repetir la animación ni perder el
 * estado del layout).
 */
function AnimatedRoutes() {
  const { pathname } = useLocation();
  const modulo = pathname.split("/")[1] || "home";
  return (
    <div key={modulo} className="page-transition">
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/" element={<RequireAuth><DashboardPage /></RequireAuth>} />
        <Route path="/caja" element={
          <RequireAuth roles={["Cajero", "Supervisor", "Administrador"]} modulo="Caja"><CajaPage /></RequireAuth>
        } />
        <Route path="/clientes" element={
          <RequireAuth roles={["Cajero", "Supervisor", "Tesorero", "Administrador"]} modulo="Clientes"><ClientesModuloPage /></RequireAuth>
        } />
        <Route path="/tesoreria" element={
          <RequireAuth roles={["Tesorero", "Administrador"]} modulo="Tesoreria"><TesoreriaPage /></RequireAuth>
        } />
        {/* Cupones también lo puede corregir Supervisor (no tiene acceso al resto de Tesorería) */}
        <Route path="/tesoreria/cupones" element={
          <RequireAuth roles={["Tesorero", "Supervisor", "Administrador"]} modulo="Tesoreria"><CuponesPage /></RequireAuth>
        } />
        <Route path="/etiquetas" element={
          <RequireAuth roles={["Repositor", "Tesorero", "Cajero", "Supervisor", "Administrador"]} modulo="Etiquetas"><EtiquetasPage /></RequireAuth>
        } />
        <Route path="/reimpresion" element={
          <RequireAuth roles={["Supervisor", "Tesorero", "Administrador"]} modulo="Reimpresion"><ReimpresionPage /></RequireAuth>
        } />
        <Route path="/ventas" element={
          <RequireAuth roles={["Administrador"]} modulo="Ventas"><VentasPage /></RequireAuth>
        } />
        <Route path="/facturacion-caea" element={
          <RequireAuth roles={["Tesorero", "Administrador"]} modulo="FacturacionCaea"><FacturacionCaeaPage /></RequireAuth>
        } />
        <Route path="/admin" element={
          <RequireAuth roles={["Administrador"]} modulo="Administracion"><AdminLayout /></RequireAuth>
        }>
          <Route index element={<Navigate to="/admin/articulos" replace />} />
          <Route path="articulos" element={<ArticulosPage />} />
          <Route path="clientes" element={<ClientesPage />} />
          <Route path="listas-precios" element={<ListasPreciosPage />} />
          <Route path="pagos" element={<PagosPage />} />
          <Route path="estructura" element={<EstructuraPage />} />
          <Route path="configuraciones" element={<ConfiguracionesPage />} />
          <Route path="estructura-caja" element={<EstructuraCajaPage />} />
          <Route path="asignacion-cajas" element={<AsignacionCajasPage />} />
          <Route path="usuarios" element={<UsuariosPage />} />
          <Route path="permisos" element={<PermisosPage />} />
          <Route path="convenios" element={<ConveniosPage />} />
          <Route path="cuenta-corriente" element={<CuentaCorrientePage />} />
          <Route path="ofertas" element={<OfertasPage />} />
          <Route path="ofertas-medio-pago" element={<OfertasMedioPagoPage />} />
          <Route path="clusters" element={<ClustersPage />} />
          <Route path="tarjetas" element={<TarjetasPage />} />
          <Route path="padrones" element={<PadronesPage />} />
          <Route path="sectores" element={<LookupPage resource="sectores" title="Sectores" />} />
          <Route path="lineas" element={<LookupPage resource="lineas" title="Líneas" />} />
          <Route path="familias" element={<FamiliasPage />} />
          <Route path="motivos-diferencia" element={<LookupPage resource="motivos-diferencia" title="Motivos de diferencia" />} />
          <Route path="motivos-cierre" element={<LookupPage resource="motivos-cierre" title="Motivos de cierre" />} />
          <Route path="bancos" element={<LookupPage resource="bancos" title="Bancos" />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </div>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <AnimatedRoutes />
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}
