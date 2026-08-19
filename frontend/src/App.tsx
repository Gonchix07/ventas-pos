import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
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
import { ConveniosPage } from "./modules/admin/ConveniosPage";
import { CuentaCorrientePage } from "./modules/admin/CuentaCorrientePage";
import { ClustersPage } from "./modules/admin/ClustersPage";
import { TarjetasPage } from "./modules/admin/TarjetasPage";
import { PadronesPage } from "./modules/admin/PadronesPage";
import { OfertasPage } from "./modules/admin/OfertasPage";
import { OfertasMedioPagoPage } from "./modules/admin/OfertasMedioPagoPage";
import { VentasEstadisticasPage } from "./modules/admin/VentasEstadisticasPage";
import { CajaPage } from "./modules/caja/CajaPage";
import { TesoreriaPage } from "./modules/admin/TesoreriaPage";
import { EtiquetasPage } from "./modules/etiquetas/EtiquetasPage";
import "./App.css";

const queryClient = new QueryClient();

function RequireAuth({ children, roles }: { children: ReactNode; roles?: string[] }) {
  const { isAuthenticated, isLoading, rol } = useAuth();
  // Mientras se rehidrata la sesión desde el token guardado (F5), no se decide nada todavía: ni
  // se manda a /login ni se renderiza la pantalla protegida con datos a medio cargar.
  if (isLoading) return null;
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  if (roles && (!rol || !roles.includes(rol))) return <Navigate to="/" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<RequireAuth><DashboardPage /></RequireAuth>} />
            <Route path="/caja" element={
              <RequireAuth roles={["Cajero", "Supervisor", "Administrador"]}><CajaPage /></RequireAuth>
            } />
            <Route path="/tesoreria" element={
              <RequireAuth roles={["Tesorero", "Administrador"]}><TesoreriaPage /></RequireAuth>
            } />
            <Route path="/etiquetas" element={
              <RequireAuth roles={["Repositor", "Tesorero", "Cajero", "Administrador"]}><EtiquetasPage /></RequireAuth>
            } />
            <Route path="/admin" element={
              <RequireAuth roles={["Administrador"]}><AdminLayout /></RequireAuth>
            }>
              <Route index element={<Navigate to="/admin/articulos" replace />} />
              <Route path="ventas" element={<VentasEstadisticasPage />} />
              <Route path="articulos" element={<ArticulosPage />} />
              <Route path="clientes" element={<ClientesPage />} />
              <Route path="listas-precios" element={<ListasPreciosPage />} />
              <Route path="pagos" element={<PagosPage />} />
              <Route path="estructura" element={<EstructuraPage />} />
              <Route path="configuraciones" element={<ConfiguracionesPage />} />
              <Route path="estructura-caja" element={<EstructuraCajaPage />} />
              <Route path="asignacion-cajas" element={<AsignacionCajasPage />} />
              <Route path="usuarios" element={<UsuariosPage />} />
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
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}
