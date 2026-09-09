import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import { clientesFicha } from "../../shared/api/clientes";
import type { Cliente } from "../../shared/api/admin";
import type { ClienteTicket } from "../../shared/api/clientes";
import { TicketCliente } from "../clientes/TicketCliente";
import { IconAgregar } from "../../shared/ui/icons";

// Debe coincidir con ClienteService.MaxResultados (backend).
const MAX_RESULTADOS = 50;

/**
 * Módulo "Clientes" del menú principal: mismo buscador y columnas que el ABM de Administración
 * (ClientesPage), pero de SOLO LECTURA — sin editar ni dar de baja. Al hacer clic en una fila se
 * abren sus datos en un panel de solo lectura (mismos campos que el formulario de edición del
 * admin, pero deshabilitados). El botón de imprimir usa el mismo ticket que el módulo de
 * autoservicio (TicketCliente) — pensado para que un cajero/supervisor pueda buscar e imprimir la
 * ficha de un cliente sin tener que escanear su DNI.
 */
export function ClientesFichaPage() {
  const { usuario, rol, logout } = useAuth();
  const navigate = useNavigate();

  const [q, setQ] = useState("");
  const [items, setItems] = useState<Cliente[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [buscando, setBuscando] = useState(false);
  const [buscadoAlMenosUnaVez, setBuscadoAlMenosUnaVez] = useState(false);

  const [detalle, setDetalle] = useState<Cliente | null>(null);
  const [aImprimir, setAImprimir] = useState<ClienteTicket | null>(null);

  const buscar = async () => {
    setError(null);
    setBuscando(true);
    try {
      setItems(await clientesFicha.buscar(q.trim()));
      setBuscadoAlMenosUnaVez(true);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setBuscando(false); }
  };

  const abrirDetalle = async (c: Cliente) => {
    setError(null);
    try { setDetalle(await clientesFicha.get(c.idCliente)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const imprimir = async (idCliente: number) => {
    setError(null);
    try { setAImprimir(await clientesFicha.ticket(idCliente)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  if (aImprimir) {
    return <TicketCliente cliente={aImprimir} onImpreso={() => setAImprimir(null)} />;
  }

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">POS</span>
          <span className="brand-sub">Clientes</span>
        </div>
        <div className="user-box">
          <span>{usuario} · <strong>{rol}</strong></span>
          <button onClick={() => navigate("/")}>Módulos</button>
          <button onClick={logout}>Salir</button>
        </div>
      </header>
      <div className="page-shell">
        <h1>Clientes</h1>
        {error && <p className="error">{error}</p>}

        <div className="toolbar">
          <input placeholder="Buscar por nombre, fantasía, código, CUIT, documento o domicilio"
            value={q} onChange={(e) => setQ(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && buscar()} style={{ flex: "1 1 320px", minWidth: 200 }} />
          <button className="primary" onClick={buscar}>Buscar</button>
        </div>
        {buscadoAlMenosUnaVez && (
          <p className="resultado-count">
            {buscando ? "Buscando…" : `${items.length} cliente${items.length === 1 ? "" : "s"}${items.length === MAX_RESULTADOS ? " (máx.) — refiná la búsqueda" : ""}`}
          </p>
        )}

        <div className="table-scroll">
          <table className="grid tabla-compacta">
            <thead>
              <tr>
                <th>Código</th><th>Descripción</th><th>Fantasía</th><th>Domicilio</th><th>CUIT</th>
                <th>Cond. IVA</th><th>Presup.</th><th>Cta. cte.</th><th>Estado</th><th></th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.idCliente} className={c.activo ? "" : "inactive"} style={{ cursor: "pointer" }}
                  onClick={() => abrirDetalle(c)}>
                  <td className="mono">{c.codigoInt}</td>
                  <td>{c.descripcion}</td>
                  <td>{c.nombreFantasia ?? <span className="muted">—</span>}</td>
                  <td>{c.domicilio ?? <span className="muted">—</span>}</td>
                  <td className="mono">{c.cuit}</td>
                  <td>{c.condIvaDescripcion}</td>
                  <td>{c.permitePresupuesto ? "Sí" : "No"}</td>
                  <td>{c.admiteCuentaCorriente ? <span className="badge on">Sí</span> : <span className="muted">No</span>}</td>
                  <td>{c.activo ? <span className="badge on">Activo</span> : <span className="badge off">Baja</span>}</td>
                  <td className="row-actions">
                    <button className="icon-btn icon-agregar" title="Imprimir ticket" aria-label="Imprimir ticket"
                      onClick={(e) => { e.stopPropagation(); void imprimir(c.idCliente); }}>
                      <IconAgregar />
                    </button>
                  </td>
                </tr>
              ))}
              {items.length === 0 && (
                <tr><td colSpan={10} className="muted">
                  {buscadoAlMenosUnaVez ? "No se encontró ningún cliente." : "Buscá un cliente por nombre, fantasía, código, CUIT o documento."}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>

        {detalle && (
          <div className="modal-fondo" onClick={() => setDetalle(null)}>
            <div className="modal-caja" style={{ width: "min(760px, 100%)" }} onClick={(e) => e.stopPropagation()}>
              <h3 style={{ marginTop: 0, marginBottom: 18 }}>{detalle.descripcion} <span className="muted mono">({detalle.codigoInt})</span></h3>
              <div className="form-grid ficha-lectura">
                <label>Código<input value={detalle.codigoInt} disabled /></label>
                <label>Razón social / Nombre<input value={detalle.descripcion} disabled /></label>
                <label>Nombre de fantasía<input value={detalle.nombreFantasia ?? ""} disabled /></label>
                <label>CUIT<input value={detalle.cuit ?? ""} disabled /></label>
                <label>Documento<input value={detalle.documento ?? ""} disabled /></label>
                <label>Condición IVA<input value={detalle.condIvaDescripcion ?? ""} disabled /></label>
                <label>Domicilio<input value={detalle.domicilio ?? ""} disabled /></label>
                <label>Localidad<input value={detalle.localidad ?? ""} disabled /></label>
                <label>Provincia<input value={detalle.provincia ?? ""} disabled /></label>
                <label>Código postal<input value={detalle.codigoPostal ?? ""} disabled /></label>
                <label>Email<input value={detalle.email ?? ""} disabled /></label>
              </div>
              {/* Los 3 checks en su propia fila (afuera del form-grid): adentro del grid, según
                  cuántas columnas entraran por ancho, "Permite presupuesto" podía terminar
                  alineado con un campo de texto en vez de con los otros dos checks. */}
              <div className="row-actions" style={{ flexWrap: "wrap", marginTop: 10 }}>
                <label className="check"><input type="checkbox" checked={detalle.permitePresupuesto} disabled /> Permite presupuesto</label>
                <label className="check"><input type="checkbox" checked={detalle.admiteCuentaCorriente} disabled /> Admite cuenta corriente</label>
                <label className="check"><input type="checkbox" checked={detalle.activo} disabled /> Activo</label>
              </div>

              {/* Autorizados: mismo dato que el ABM de Administración, pero listado nomás — acá no
                  se agregan/quitan (eso es edición, fuera del alcance de esta pantalla). */}
              <h4 style={{ margin: "18px 0 6px" }}>Autorizados a comprar</h4>
              {detalle.autorizados && detalle.autorizados.length > 0 ? (
                <table className="grid tabla-compacta">
                  <thead><tr><th>DNI</th><th>Nombre</th><th>Estado</th></tr></thead>
                  <tbody>
                    {detalle.autorizados.map((a) => (
                      <tr key={a.idAutorizado} className={a.activo ? "" : "inactive"}>
                        <td className="mono">{a.dni}</td>
                        <td>{a.descripcion}</td>
                        <td>{a.activo ? <span className="badge on">Activo</span> : <span className="badge off">Baja</span>}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <p className="muted" style={{ margin: 0 }}>Sin autorizados.</p>
              )}

              <div className="row-actions" style={{ marginTop: 14 }}>
                <button className="primary" onClick={() => void imprimir(detalle.idCliente)}>Imprimir ticket</button>
                <button onClick={() => setDetalle(null)}>Cerrar</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </>
  );
}
