import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";

const MODULOS = [
  { key: "Caja", desc: "Operativa de cobros y armado de operación", to: "/caja" },
  { key: "Clientes", desc: "Buscar cliente por documento/tarjeta e imprimir su ficha por comandera", to: "/clientes" },
  { key: "VerificarPrecios", label: "Verificar Precios", desc: "Kiosco de autoconsulta: escanear un producto y ver imagen, precios de lista y ofertas", to: "/verificar-precios" },
  { key: "Tesoreria", desc: "Cierres, validaciones y dashboard", to: "/tesoreria" },
  { key: "Etiquetas", desc: "Impresión de etiquetas de precios", to: "/etiquetas" },
  { key: "Reimpresion", desc: "Buscar y reimprimir facturas o notas de crédito ya emitidas", to: "/reimpresion" },
  { key: "Ventas", desc: "Dashboard de estadísticas de ventas", to: "/ventas" },
  // "label" solo para el título de la tarjeta: la sigla CAEA va en mayúsculas, a diferencia de
  // "key" (que tiene que coincidir tal cual con Modulo.Descripcion del backend/permisos).
  { key: "FacturacionCaea", label: "Facturación CAEA", desc: "Comprobantes emitidos en contingencia (CAEA) pendientes de informar a ARCA", to: "/facturacion-caea" },
  // Al final a propósito: es el módulo con más opciones y el que menos se usa día a día.
  { key: "Administracion", desc: "ABM de datos maestros y configuración", to: "/admin" },
];

export function DashboardPage() {
  const { usuario, rol, modulos, logout } = useAuth();
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
                className={`module-card ${on ? "" : "disabled"} ${clickable ? "clickable" : ""} ${m.key === "Administracion" ? "module-card--admin" : ""}`}
                onClick={() => clickable && navigate(m.to!)}
              >
                <h3>{m.label ?? m.key}</h3>
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
