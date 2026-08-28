import { useEffect, useState } from "react";
import { configuraciones, conexionExterna, conexionPuntosApp, type Configuracion, type ConexionExternaMySql, type ConexionPuntosApp } from "../../shared/api/admin";

export function ConfiguracionesPage() {
  const [items, setItems] = useState<Configuracion[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [editVal, setEditVal] = useState("");

  const [nClave, setNClave] = useState(""); const [nDesc, setNDesc] = useState(""); const [nVal, setNVal] = useState("");

  const cargar = async () => {
    setError(null);
    try { setItems(await configuraciones.list()); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(); }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      <h1>Configuraciones</h1>
      {error && <p className="error">{error}</p>}

      <div className="card form">
        <h3>Nueva configuración</h3>
        <div className="form-grid">
          <label>Clave<input value={nClave} onChange={(e) => setNClave(e.target.value)} /></label>
          <label>Descripción<input value={nDesc} onChange={(e) => setNDesc(e.target.value)} /></label>
          <label>Valor<input value={nVal} onChange={(e) => setNVal(e.target.value)} /></label>
        </div>
        <div className="row-actions">
          <button className="primary" disabled={!nClave.trim() || !nDesc.trim()}
            onClick={() => run(async () => { await configuraciones.create({ clave: nClave.trim(), descripcion: nDesc.trim(), valor: nVal }); setNClave(""); setNDesc(""); setNVal(""); })}>
            Agregar
          </button>
        </div>
      </div>

      <table className="grid">
        <thead><tr><th>Clave</th><th>Descripción</th><th>Valor</th><th></th></tr></thead>
        <tbody>
          {items.map((c) => (
            <tr key={c.idConfiguracion}>
              <td className="mono">{c.clave}</td>
              <td>{c.descripcion}</td>
              <td>
                {editId === c.idConfiguracion
                  ? <input value={editVal} onChange={(e) => setEditVal(e.target.value)} />
                  : <span className="mono">{c.valor}</span>}
              </td>
              <td className="row-actions">
                {editId === c.idConfiguracion ? (
                  <>
                    <button className="primary" onClick={() => run(async () => { await configuraciones.update(c.idConfiguracion, { clave: c.clave, descripcion: c.descripcion, valor: editVal }); setEditId(null); })}>Guardar</button>
                    <button onClick={() => setEditId(null)}>Cancelar</button>
                  </>
                ) : (
                  <>
                    <button onClick={() => { setEditId(c.idConfiguracion); setEditVal(c.valor ?? ""); }}>Editar valor</button>
                    <button className="danger" onClick={() => run(() => configuraciones.remove(c.idConfiguracion))}>Eliminar</button>
                  </>
                )}
              </td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan={4} className="muted">Sin configuraciones.</td></tr>}
        </tbody>
      </table>

      <ConexionExternaSection />
      <ConexionPuntosAppSection />
    </div>
  );
}

/**
 * Conexión a datos externa (MySQL): a futuro, la aplicación deposita acá datos generados para que
 * los consuma otro sistema. Es una fila única (no un ABM) y la contraseña nunca vuelve del backend
 * en texto plano — dejar el campo vacío al guardar conserva la que ya está guardada.
 */
function ConexionExternaSection() {
  const [datos, setDatos] = useState<ConexionExternaMySql | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);
  const [probando, setProbando] = useState(false);
  // null = todavía no se probó en esta sesión de edición; se limpia al tocar cualquier campo, para
  // no dejar mostrado el resultado de una prueba con datos que ya cambiaron.
  const [resultadoPrueba, setResultadoPrueba] = useState<{ ok: boolean; error?: string | null } | null>(null);

  const [host, setHost] = useState("");
  const [puerto, setPuerto] = useState(3306);
  const [baseDatos, setBaseDatos] = useState("");
  const [usuario, setUsuario] = useState("");
  const [password, setPassword] = useState("");
  const [habilitada, setHabilitada] = useState(false);

  const cargar = async () => {
    setError(null);
    try {
      const d = await conexionExterna.get();
      setDatos(d);
      setHost(d.host); setPuerto(d.puerto); setBaseDatos(d.baseDatos); setUsuario(d.usuario);
      setHabilitada(d.habilitada); setPassword("");
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(); }, []);

  const guardar = async () => {
    setError(null); setOk(false); setResultadoPrueba(null);
    try {
      await conexionExterna.update({ host: host.trim(), puerto, baseDatos: baseDatos.trim(), usuario: usuario.trim(), password: password || null, habilitada });
      await cargar();
      setOk(true);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // Prueba una conexión REAL (TCP + login) con los datos tal como están en el formulario ahora
  // mismo — no hace falta guardar primero. Si la contraseña quedó vacía (no se retipeó), el
  // backend usa la ya guardada, mismo criterio que "Guardar".
  const probar = async () => {
    setError(null); setOk(false); setResultadoPrueba(null); setProbando(true);
    try {
      const r = await conexionExterna.probar({
        host: host.trim(), puerto, baseDatos: baseDatos.trim(), usuario: usuario.trim(),
        password: password || null, habilitada,
      });
      setResultadoPrueba(r);
    } catch (e) {
      setResultadoPrueba({ ok: false, error: e instanceof Error ? e.message : "Error" });
    } finally {
      setProbando(false);
    }
  };

  return (
    <div className="card form">
      <h3>Conexión a datos externa (MySQL)</h3>
      <p className="muted">
        Configuración para depositar datos generados por la aplicación en una base MySQL externa, a
        consumir por otro sistema. Todavía no hay ningún proceso que se conecte con esto.
      </p>
      {error && <p className="error">{error}</p>}
      {ok && !error && <p className="muted">Guardado.</p>}
      <div className="field-row">
        <label>IP / host del servidor<input value={host} onChange={(e) => setHost(e.target.value)} placeholder="192.168.x.x" /></label>
        <label>Puerto
          <input type="text" inputMode="numeric" pattern="[0-9]*" maxLength={5} style={{ width: "7ch", minWidth: "60px" }}
            value={puerto} onChange={(e) => setPuerto(Number(e.target.value.replace(/\D/g, "")) || 0)} />
        </label>
        <label>Nombre de la base<input value={baseDatos} onChange={(e) => setBaseDatos(e.target.value)} /></label>
        <label>Usuario<input value={usuario} onChange={(e) => setUsuario(e.target.value)} /></label>
        <label>
          Contraseña
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            placeholder={datos?.tieneContrasena ? "•••••••• (dejar vacío para no cambiarla)" : "Sin configurar"} />
        </label>
        <label className="check-box">
          <input type="checkbox" checked={habilitada} onChange={(e) => setHabilitada(e.target.checked)} />
          Habilitada
        </label>
      </div>
      <div className="row-actions">
        <button className="primary" disabled={!host.trim() || !baseDatos.trim() || !usuario.trim()} onClick={() => void guardar()}>
          Guardar
        </button>
        <button className="success-solid" disabled={probando || !host.trim() || !baseDatos.trim() || !usuario.trim()}
          onClick={() => void probar()}>
          {probando ? "Probando…" : "Probar conexión"}
        </button>
      </div>
      {resultadoPrueba && (
        resultadoPrueba.ok
          ? <p className="muted">✔ Conexión exitosa.</p>
          : <p className="error">✘ No se pudo conectar: {resultadoPrueba.error}</p>
      )}
    </div>
  );
}

/**
 * Integración con puntos-app (programa de fidelización externo, otro proyecto): cada Factura de
 * venta con cliente identificado (DNI cargado en el ABM de Clientes) suma puntos allá. Es una fila
 * única y el token nunca vuelve del backend en texto plano — dejar el campo vacío al guardar
 * conserva el que ya está guardado. Best-effort: si esto falla o está deshabilitado, la venta se
 * factura igual, solo no suma puntos.
 */
function ConexionPuntosAppSection() {
  const [datos, setDatos] = useState<ConexionPuntosApp | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState(false);
  const [probando, setProbando] = useState(false);
  const [resultadoPrueba, setResultadoPrueba] = useState<{ ok: boolean; error?: string | null } | null>(null);

  const [urlBase, setUrlBase] = useState("");
  const [comercio, setComercio] = useState("");
  const [token, setToken] = useState("");
  const [habilitada, setHabilitada] = useState(false);

  const cargar = async () => {
    setError(null);
    try {
      const d = await conexionPuntosApp.get();
      setDatos(d);
      setUrlBase(d.urlBase); setComercio(d.comercio); setHabilitada(d.habilitada); setToken("");
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(); }, []);

  const guardar = async () => {
    setError(null); setOk(false); setResultadoPrueba(null);
    try {
      await conexionPuntosApp.update({ urlBase: urlBase.trim(), comercio: comercio.trim(), token: token || null, habilitada });
      await cargar();
      setOk(true);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  // Prueba real contra puntos-app con los datos tal como están en el formulario ahora mismo — no
  // hace falta guardar primero. Token vacío = usar el ya guardado (mismo criterio que "Guardar").
  const probar = async () => {
    setError(null); setOk(false); setResultadoPrueba(null); setProbando(true);
    try {
      const r = await conexionPuntosApp.probar({
        urlBase: urlBase.trim(), comercio: comercio.trim(), token: token || null, habilitada,
      });
      setResultadoPrueba(r);
    } catch (e) {
      setResultadoPrueba({ ok: false, error: e instanceof Error ? e.message : "Error" });
    } finally {
      setProbando(false);
    }
  };

  return (
    <div className="card form">
      <h3>Puntos de fidelización (puntos-app)</h3>
      <p className="muted">
        Cada Factura de venta con cliente identificado (DNI cargado en el ABM de Clientes) suma
        puntos en puntos-app. El comercio se manda igual en todas las cargas — tiene que existir
        con ese nombre en el ABM de Comercios de puntos-app. Las Notas de Crédito todavía no restan
        puntos. La API key es el valor de <code>API_INTEGRATION_KEY</code> configurado en el
        Vercel de puntos-app (no un usuario/contraseña ni un token de sesión).
      </p>
      {error && <p className="error">{error}</p>}
      {ok && !error && <p className="muted">Guardado.</p>}
      <div className="field-row">
        <label>URL base<input value={urlBase} onChange={(e) => setUrlBase(e.target.value)} placeholder="https://puntos-app.vercel.app" /></label>
        <label>Comercio<input value={comercio} onChange={(e) => setComercio(e.target.value)} /></label>
        <label>
          API key de integración
          <input type="password" value={token} onChange={(e) => setToken(e.target.value)}
            placeholder={datos?.tieneToken ? "•••••••• (dejar vacío para no cambiarla)" : "Sin configurar"} />
        </label>
        <label className="check-box">
          <input type="checkbox" checked={habilitada} onChange={(e) => setHabilitada(e.target.checked)} />
          Habilitada
        </label>
      </div>
      <div className="row-actions">
        <button className="primary" disabled={!urlBase.trim() || !comercio.trim()} onClick={() => void guardar()}>
          Guardar
        </button>
        <button className="success-solid" disabled={probando || !urlBase.trim()}
          onClick={() => void probar()}>
          {probando ? "Probando…" : "Probar conexión"}
        </button>
      </div>
      {resultadoPrueba && (
        resultadoPrueba.ok
          ? <p className="muted">✔ Conexión exitosa.</p>
          : <p className="error">✘ No se pudo conectar: {resultadoPrueba.error}</p>
      )}
    </div>
  );
}
