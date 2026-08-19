import { useEffect, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";

type Item = { to: string; label: string };
type Grupo = { id: string; label: string; to?: string; items: Item[] };

// El menú de administración se agrupa por entidad de negocio. Los grupos con `to` propio
// (Artículos, Clientes) tienen además su ABM principal en el encabezado del grupo.
const GRUPOS: Grupo[] = [
  {
    id: "ventas",
    label: "Ventas",
    to: "/admin/ventas",
    items: [],
  },
  {
    id: "articulos",
    label: "Artículos",
    to: "/admin/articulos",
    items: [
      { to: "/admin/lineas", label: "Líneas" },
      { to: "/admin/sectores", label: "Sectores" },
      { to: "/admin/familias", label: "Familias" },
    ],
  },
  {
    id: "clientes",
    label: "Clientes",
    to: "/admin/clientes",
    items: [
      { to: "/admin/clusters", label: "Clusters" },
      { to: "/admin/cuenta-corriente", label: "Cuentas corrientes" },
      { to: "/admin/padrones", label: "Padrones" },
    ],
  },
  {
    id: "precios",
    label: "Precios y ofertas",
    items: [
      { to: "/admin/listas-precios", label: "Listas de precios" },
      { to: "/admin/ofertas", label: "Ofertas" },
      { to: "/admin/ofertas-medio-pago", label: "Ofertas por medio de pago" },
      { to: "/admin/convenios", label: "Convenios" },
      { to: "/admin/tarjetas", label: "Tarjetas" },
    ],
  },
  {
    id: "caja",
    label: "Caja y pagos",
    items: [
      { to: "/admin/pagos", label: "Medios de pago" },
      { to: "/admin/estructura-caja", label: "Estructura de caja" },
      { to: "/admin/asignacion-cajas", label: "Asignación de cajas" },
      { to: "/admin/motivos-diferencia", label: "Motivos de diferencia" },
      { to: "/admin/motivos-cierre", label: "Motivos de cierre" },
    ],
  },
  {
    id: "sistema",
    label: "Sistema",
    items: [
      { to: "/admin/estructura", label: "Empresas / Sucursales" },
      { to: "/admin/usuarios", label: "Usuarios" },
      { to: "/admin/configuraciones", label: "Configuraciones" },
    ],
  },
];

function grupoDeRuta(path: string): string | undefined {
  return GRUPOS.find((g) => g.to === path || g.items.some((i) => i.to === path))?.id;
}

export function AdminLayout() {
  const { usuario, rol, logout, ip } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const [abiertos, setAbiertos] = useState<string[]>(() => {
    const g = grupoDeRuta(pathname);
    return g ? [g] : ["articulos"];
  });

  // Si se navega a una sección por URL directa (o desde otra pantalla), abrir su grupo.
  useEffect(() => {
    const g = grupoDeRuta(pathname);
    if (g) setAbiertos((prev) => (prev.includes(g) ? prev : [...prev, g]));
  }, [pathname]);

  const toggle = (id: string) =>
    setAbiertos((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));

  return (
    <div className="admin">
      <aside className="admin-side">
        <div className="brand" onClick={() => navigate("/")} style={{ cursor: "pointer" }}>
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Admin</span>
        </div>
        <div className="admin-user">
          <div className="admin-user-nombre">
            <span className="admin-user-usuario">{usuario}</span>
            <span className="muted"> · {rol}</span>
          </div>
          <div className="admin-user-ip mono">IP {ip ?? "—"}</div>
          <button className="admin-user-salir" onClick={logout}>Salir</button>
        </div>
        <nav>
          {GRUPOS.map((g) => {
            const abierto = abiertos.includes(g.id);
            return (
              <div key={g.id} className={`nav-grupo${abierto ? " abierto" : ""}`}>
                <div className="nav-grupo-head">
                  {g.to ? (
                    <NavLink to={g.to} className={({ isActive }) => (isActive ? "active" : "")}>
                      {g.label}
                    </NavLink>
                  ) : (
                    <button type="button" className="nav-grupo-label" onClick={() => toggle(g.id)}>
                      {g.label}
                    </button>
                  )}
                  {g.items.length > 0 && (
                    <button
                      type="button"
                      className="nav-grupo-caret"
                      onClick={() => toggle(g.id)}
                      aria-expanded={abierto}
                      aria-label={`${abierto ? "Contraer" : "Expandir"} ${g.label}`}
                    >
                      {abierto ? "▾" : "▸"}
                    </button>
                  )}
                </div>
                {abierto && g.items.length > 0 && (
                  <div className="nav-sub">
                    {g.items.map((i) => (
                      <NavLink
                        key={i.to}
                        to={i.to}
                        className={({ isActive }) => (isActive ? "active" : "")}
                      >
                        {i.label}
                      </NavLink>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </nav>
      </aside>
      {/* Ventas es un dashboard, no un ABM: en vez del ancho fijo del resto de las pantallas de
          Admin, ocupa todo el espacio disponible (útil con muchos gráficos lado a lado). */}
      <section className={`admin-content${pathname.startsWith("/admin/ventas") ? " admin-content-full" : ""}`}>
        <Outlet />
      </section>
    </div>
  );
}
