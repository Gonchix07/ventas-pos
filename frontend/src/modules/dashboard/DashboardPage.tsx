import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";

const MODULOS = [
  { key: "Caja", desc: "Operativa de cobros y armado de operación", to: "/caja" },
  { key: "Tesoreria", desc: "Cierres, validaciones y dashboard", to: "/tesoreria" },
  { key: "Etiquetas", desc: "Impresión de etiquetas de precios", to: "/etiquetas" },
  { key: "Administracion", desc: "ABM de datos maestros y configuración", to: "/admin" },
];

export function DashboardPage() {
  const { usuario, rol, modulos, logout, ip } = useAuth();
  const navigate = useNavigate();
  const habilitados = new Set(modulos);

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Mayorista</span>
        </div>
        <div className="user-box">
          <span>{usuario} · <strong>{rol}</strong></span>
          <span className="mono ip-badge">IP {ip ?? "—"}</span>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      <main className="app-main">
        <h1>Módulos</h1>
        <div className="module-grid">
          {MODULOS.map((m) => {
            // Sin fail-open: un usuario sin módulos asignados no debe ver todo habilitado.
            const on = habilitados.has(m.key);
            const clickable = on && m.to;
            return (
              <div
                key={m.key}
                className={`module-card ${on ? "" : "disabled"} ${clickable ? "clickable" : ""}`}
                onClick={() => clickable && navigate(m.to!)}
              >
                <h3>{m.key}</h3>
                <p>{m.desc}</p>
                <span className="badge">{on ? (m.to ? "Abrir" : "Habilitado") : "Sin permiso"}</span>
              </div>
            );
          })}
        </div>
      </main>
    </div>
  );
}
