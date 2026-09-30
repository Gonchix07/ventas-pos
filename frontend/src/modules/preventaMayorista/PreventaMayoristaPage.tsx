import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import { preventaMayorista, type PreventaCliente, type Vendedor } from "../../shared/api/preventaMayorista";
import { formatearMoneda } from "../../shared/ui/moneda";

type Columna = "codigoCliente" | "nombreCliente" | "condicionIva" | "admitePresupuesto"
  | "cantidadReserva" | "importeFinal" | "recargoLogistica" | "cantidadPrecargas" | "revision";

// No es un campo directo de PreventaCliente (se calcula a partir de sus líneas), así que el orden
// por esta columna se resuelve aparte en clientesOrdenados en vez del acceso genérico a[col].
const necesitaRevision = (c: PreventaCliente) => c.lineas.some((l) => l.autorizado === false);

const COLUMNAS: { key: Columna; label: string }[] = [
  { key: "codigoCliente", label: "Código" },
  { key: "nombreCliente", label: "Cliente" },
  { key: "condicionIva", label: "Cond. IVA" },
  { key: "admitePresupuesto", label: "Presupuesto" },
  { key: "cantidadReserva", label: "RESERVA / ORIG" },
  { key: "importeFinal", label: "Importe final" },
  { key: "recargoLogistica", label: "Recargo" },
  { key: "cantidadPrecargas", label: "Precargas" },
];

export function PreventaMayoristaPage() {
  const { usuario, logout } = useAuth();
  const navigate = useNavigate();

  const [clientes, setClientes] = useState<PreventaCliente[] | null>(null);
  const [cargando, setCargando] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [orden, setOrden] = useState<{ col: Columna; asc: boolean }>({ col: "codigoCliente", asc: true });
  const [expandidos, setExpandidos] = useState<Set<string>>(new Set());
  const [busqueda, setBusqueda] = useState("");
  const [incluirMenorCoste, setIncluirMenorCoste] = useState(false);
  const [vendedores, setVendedores] = useState<Vendedor[]>([]);
  const [vendedorFiltro, setVendedorFiltro] = useState("");

  const cargar = async (forzarRefresco = false) => {
    setError(null); setCargando(true);
    try {
      setClientes(await preventaMayorista.pedidos(forzarRefresco));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudieron obtener los pedidos.");
    } finally {
      setCargando(false);
    }
  };

  useEffect(() => { void cargar(); }, []);
  useEffect(() => { void preventaMayorista.vendedores().then(setVendedores).catch(() => {}); }, []);

  const ordenarPor = (col: Columna) =>
    setOrden((o) => (o.col === col ? { col, asc: !o.asc } : { col, asc: true }));

  // Búsqueda por toda la tabla: cliente (código/nombre) o cualquier línea de sus precargas
  // (artículo, descripción, número de precarga). No auto-expande filas: la expansión queda
  // exclusivamente a cargo del click del usuario, aunque el match venga de una línea.
  const textoBusqueda = busqueda.trim().toLowerCase();
  const coincideCliente = (c: PreventaCliente) =>
    c.codigoCliente.toLowerCase().includes(textoBusqueda) ||
    !!c.nombreCliente?.toLowerCase().includes(textoBusqueda);
  const coincideLinea = (c: PreventaCliente) =>
    c.lineas.some((l) =>
      l.codigoArticulo.toLowerCase().includes(textoBusqueda) ||
      !!l.descripcionArticulo?.toLowerCase().includes(textoBusqueda) ||
      l.precarga.toLowerCase().includes(textoBusqueda) ||
      l.estado.toLowerCase().includes(textoBusqueda) ||
      l.pVentista.toLowerCase().includes(textoBusqueda));

  const clientesFiltrados = useMemo(() => {
    if (!clientes) return [];
    let base = incluirMenorCoste
      ? clientes
      : clientes.filter((c) => !c.nombreCliente?.toUpperCase().includes("MENOR COSTE"));
    if (vendedorFiltro) base = base.filter((c) => c.lineas.some((l) => l.pVentista === vendedorFiltro));
    if (textoBusqueda) base = base.filter((c) => coincideCliente(c) || coincideLinea(c));
    return base;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clientes, textoBusqueda, incluirMenorCoste, vendedorFiltro]);

  const clientesOrdenados = useMemo(() => {
    const { col, asc } = orden;
    const signo = asc ? 1 : -1;
    return [...clientesFiltrados].sort((a, b) => {
      if (col === "revision") return (Number(necesitaRevision(a)) - Number(necesitaRevision(b))) * signo;
      const va = a[col], vb = b[col];
      if (typeof va === "number" && typeof vb === "number") return (va - vb) * signo;
      return String(va ?? "").localeCompare(String(vb ?? "")) * signo;
    });
  }, [clientesFiltrados, orden]);

  const alternarExpandido = (codigoCliente: string) =>
    setExpandidos((prev) => {
      const next = new Set(prev);
      if (next.has(codigoCliente)) next.delete(codigoCliente); else next.add(codigoCliente);
      return next;
    });

  // Estadísticas sobre el conjunto filtrado (lo que efectivamente se ve en la tabla), no sobre el
  // total sin filtrar.
  const stats = useMemo(() => ({
    cantidadClientes: clientesOrdenados.length,
    totalBultos: clientesOrdenados.reduce((acc, c) => acc + c.cantidadReserva, 0),
    totalImporte: clientesOrdenados.reduce((acc, c) => acc + c.importeFinal, 0),
    totalRecargo: clientesOrdenados.reduce((acc, c) => acc + c.recargoLogistica, 0),
  }), [clientesOrdenados]);

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Mayorista</span></div>
        <div className="user-box">
          <span>{usuario}</span>
          <button onClick={() => navigate("/")}>Módulos</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      <main className="app-main app-main--ancho">
        <h1>Preventa Mayorista</h1>
        <p className="muted">
          Pedidos pendientes tomados por la app de preventa (estados PEDIPEND/PEDIAPP), cruzados
          contra el padrón de clientes y artículos. Solo consulta — no genera ninguna operación.
        </p>

        <div className="field-row">
          <input value={busqueda} onChange={(e) => setBusqueda(e.target.value)}
            placeholder="Buscar por cliente, artículo, precarga o vendedor…" style={{ minWidth: 320 }} />
          <button className="primary" onClick={() => cargar(true)} disabled={cargando}>Refrescar</button>
          <label className="check-box grande">
            <input type="checkbox" checked={incluirMenorCoste}
              onChange={(e) => setIncluirMenorCoste(e.target.checked)} />
            Incluir Menor Coste
          </label>
          <select value={vendedorFiltro} onChange={(e) => setVendedorFiltro(e.target.value)}
            className={vendedorFiltro ? "" : "sin-valor"}>
            <option value="">Todos los vendedores</option>
            {vendedores.map((v) => (
              <option key={v.codigo} value={v.codigo}>{v.codigo} | {v.nombre}</option>
            ))}
          </select>
        </div>
        {error && <p className="error">{error}</p>}

        {cargando && (
          <div className="modal-fondo">
            <div className="modal-caja" style={{ width: "min(320px, 100%)" }}>
              <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: "24px 0" }}>
                <div className="spinner" aria-hidden="true" />
                <p style={{ margin: 0 }}>Actualizando…</p>
              </div>
            </div>
          </div>
        )}

        {clientes !== null && (
          <div className="kpi-grid">
            <div className="kpi-card">
              <span className="kpi-label">Cantidad de clientes</span>
              <span className="kpi-valor">{stats.cantidadClientes}</span>
            </div>
            <div className="kpi-card kpi-card--bultos">
              <span className="kpi-label">Total bultos</span>
              <span className="kpi-valor">{Math.round(stats.totalBultos).toLocaleString("es-AR")}</span>
            </div>
            <div className="kpi-card">
              <span className="kpi-label">Total importe</span>
              <span className="kpi-valor">{formatearMoneda(stats.totalImporte)}</span>
            </div>
            <div className="kpi-card kpi-card--recargo">
              <span className="kpi-label">Total recargo logístico</span>
              <span className="kpi-valor">{formatearMoneda(stats.totalRecargo)}</span>
            </div>
          </div>
        )}

        {clientesOrdenados.length > 0 && (
          <table className="grid tabla-compacta">
            <thead>
              <tr>
                <th />
                {COLUMNAS.map((c) => (
                  <th key={c.key} className="ordenable" onClick={() => ordenarPor(c.key)}>
                    {c.label}{orden.col === c.key ? (orden.asc ? " ▲" : " ▼") : ""}
                  </th>
                ))}
                <th className="ordenable" onClick={() => ordenarPor("revision")}>
                  Revisión{orden.col === "revision" ? (orden.asc ? " ▲" : " ▼") : ""}
                </th>
              </tr>
            </thead>
            <tbody>
              {clientesOrdenados.map((c) => (
                <PreventaClienteFila key={c.codigoCliente} cliente={c}
                  expandido={expandidos.has(c.codigoCliente)}
                  onToggle={() => alternarExpandido(c.codigoCliente)} />
              ))}
            </tbody>
          </table>
        )}

        {clientes !== null && clientes.length === 0 && !cargando && (
          <p className="muted">No hay pedidos pendientes (PEDIPEND/PEDIAPP) en este momento.</p>
        )}
      </main>
    </div>
  );
}

function PreventaClienteFila({ cliente, expandido, onToggle }:
  { cliente: PreventaCliente; expandido: boolean; onToggle: () => void }) {
  return (
    <>
      <tr className="clickable" onClick={onToggle}>
        <td className="mono" style={{ fontSize: 28, color: "#0c4a3e" }}>{expandido ? "▾" : "▸"}</td>
        <td className="mono">{cliente.codigoCliente}</td>
        <td>{cliente.nombreCliente ?? <span className="muted">(no encontrado en el padrón)</span>}</td>
        <td>{cliente.condicionIva ?? "—"}</td>
        <td>
          {cliente.admitePresupuesto === null
            ? <span className="muted">—</span>
            : <span className={`badge ${cliente.admitePresupuesto ? "on" : "rosa"}`}>
                {cliente.admitePresupuesto ? "Sí" : "No"}
              </span>}
        </td>
        <td className="mono" style={cliente.cantidadReserva !== cliente.cantidadOriginal ? { color: "#c0392b" } : undefined}>
          {cliente.cantidadReserva} / {cliente.cantidadOriginal}
        </td>
        <td className="mono">{formatearMoneda(cliente.importeFinal)}</td>
        <td className="mono" style={{ whiteSpace: "nowrap" }}>
          {cliente.recargoLogistica > 0 &&
            <span style={{ color: "#1a7f37" }}>+{formatearMoneda(cliente.recargoLogistica)}</span>}
        </td>
        <td className="mono">{cliente.cantidadPrecargas}</td>
        <td className="mono">
          {necesitaRevision(cliente) &&
            <span title="Tiene al menos un artículo con descuento sin autorizar" style={{ fontSize: 20 }}>⚠️</span>}
        </td>
      </tr>
      {expandido && (
        <tr>
          <td />
          <td colSpan={9}>
            <table className="grid tabla-compacta tabla-detalle">
              <thead>
                <tr>
                  <th>Fecha</th><th>Estado</th><th>Precarga</th><th>Logis</th><th>VEND.</th><th>Artículo</th><th>UxB</th>
                  <th style={{ minWidth: 110, whiteSpace: "nowrap" }}>RSV / ORIG</th><th style={{ minWidth: 120, whiteSpace: "nowrap" }}>Unitario</th><th>Descuento</th><th>Autorizado</th>
                </tr>
              </thead>
              <tbody>
                {cliente.lineas.map((l, i) => (
                  <tr key={i} className={l.descuento > 0 ? "fila-con-descuento" : undefined}>
                    <td className="mono">
                      {l.fechaPedido ? new Date(l.fechaPedido + "T00:00:00").toLocaleDateString("es-AR") : "—"}
                    </td>
                    <td className="mono">{l.estado}</td>
                    <td className="mono">{l.precarga}</td>
                    <td className="mono">{l.logis}</td>
                    <td className="mono">{l.pVentista}</td>
                    <td>
                      ({l.codigoArticulo})
                      {l.descripcionArticulo ? ` ${l.descripcionArticulo}` : <span className="muted"> (no encontrado)</span>}
                    </td>
                    <td className="mono">{l.unidadXBulto}</td>
                    <td className="mono" style={{ whiteSpace: "nowrap", ...(l.cantidad !== l.cantidadOriginal ? { color: "#c0392b" } : {}) }}>
                      {l.cantidad} / {l.cantidadOriginal}
                    </td>
                    <td className="mono" style={{ whiteSpace: "nowrap" }}>{formatearMoneda(l.importe)}</td>
                    <td className="mono">
                      {l.descuento > 0
                        ? `${l.descuento.toLocaleString("es-AR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`
                        : ""}
                    </td>
                    <td>
                      {l.autorizado !== null &&
                        <span className={`badge ${l.autorizado ? "on" : "rosa"}`}>{l.autorizado ? "Sí" : "No"}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </td>
        </tr>
      )}
    </>
  );
}
