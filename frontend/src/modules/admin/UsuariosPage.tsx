import { useEffect, useState } from "react";
import { usuarios, type Usuario, type Rol, type UsuarioCreateInput } from "../../shared/api/admin";

export function UsuariosPage() {
  const [items, setItems] = useState<Usuario[]>([]);
  const [roles, setRoles] = useState<Rol[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<(UsuarioCreateInput & { idUsuario?: number }) | null>(null);

  const cargar = async () => {
    setError(null);
    try { setItems(await usuarios.list()); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => {
    void cargar();
    usuarios.roles().then(setRoles).catch(() => {});
  }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const nuevo = () => setForm({ nombreUsuario: "", clave: "", idRol: roles[0]?.idRol ?? 1, activo: true, codigoSupervisor: "" });
  const editar = (u: Usuario) => setForm({
    idUsuario: u.idUsuario, nombreUsuario: u.nombreUsuario, clave: "", idRol: u.idRol, activo: u.activo,
    codigoSupervisor: u.codigoSupervisor ?? "",
  });

  const guardar = async () => {
    if (!form) return;
    // Vacío se manda como null: no todos los usuarios tienen código, y "" rompería la unicidad
    // (dos usuarios sin código no deberían chocar entre sí).
    const codigoSupervisor = form.codigoSupervisor?.trim() || null;
    await run(async () => {
      if (form.idUsuario) await usuarios.update(form.idUsuario, { nombreUsuario: form.nombreUsuario, idRol: form.idRol, activo: form.activo, codigoSupervisor });
      else await usuarios.create({ nombreUsuario: form.nombreUsuario, clave: form.clave, idRol: form.idRol, activo: form.activo, codigoSupervisor });
      setForm(null);
    });
  };

  const resetClave = async (u: Usuario) => {
    const nueva = prompt(`Nueva clave para ${u.nombreUsuario} (mín. 6 caracteres):`);
    if (!nueva) return;
    await run(() => usuarios.resetClave(u.idUsuario, nueva));
  };

  const set = (patch: Partial<UsuarioCreateInput>) => setForm((f) => (f ? { ...f, ...patch } : f));

  return (
    <div>
      <div className="page-head">
        <h1>Usuarios</h1>
        <button className="primary" onClick={nuevo}>Nuevo usuario</button>
      </div>
      {error && <p className="error">{error}</p>}

      {form && (
        <div className="card form">
          <h3>{form.idUsuario ? "Editar usuario" : "Nuevo usuario"}</h3>
          <div className="form-grid">
            <label>Usuario<input value={form.nombreUsuario} onChange={(e) => set({ nombreUsuario: e.target.value })} /></label>
            {!form.idUsuario && (
              <label>Clave<input type="password" value={form.clave} onChange={(e) => set({ clave: e.target.value })} /></label>
            )}
            <label>Rol
              <select value={form.idRol} onChange={(e) => set({ idRol: Number(e.target.value) })}>
                {roles.map((r) => <option key={r.idRol} value={r.idRol}>{r.descripcion}</option>)}
              </select>
            </label>
            <label className="check"><input type="checkbox" checked={form.activo} onChange={(e) => set({ activo: e.target.checked })} /> Activo</label>
            <label>Código de supervisor
              <input value={form.codigoSupervisor ?? ""} maxLength={8} inputMode="numeric" pattern="[0-9]*"
                placeholder="8 dígitos (opcional)"
                onChange={(e) => set({ codigoSupervisor: e.target.value.replace(/\D/g, "") })} />
            </label>
          </div>
          <div className="row-actions">
            <button className="primary" onClick={guardar}>Guardar</button>
            <button onClick={() => setForm(null)}>Cancelar</button>
          </div>
        </div>
      )}

      <table className="grid">
        <thead><tr><th>ID</th><th>Usuario</th><th>Rol</th><th>Estado</th><th></th></tr></thead>
        <tbody>
          {items.map((u) => (
            <tr key={u.idUsuario} className={u.activo ? "" : "inactive"}>
              <td className="mono">{u.idUsuario}</td>
              <td>{u.nombreUsuario}</td>
              <td>{u.rol}</td>
              <td>{u.activo ? <span className="badge on">Activo</span> : <span className="badge off">Inactivo</span>}</td>
              <td className="row-actions">
                <button onClick={() => editar(u)}>Editar</button>
                <button onClick={() => resetClave(u)}>Reset clave</button>
                <button className="danger" onClick={() => run(() => usuarios.remove(u.idUsuario))}>Eliminar</button>
              </td>
            </tr>
          ))}
          {items.length === 0 && <tr><td colSpan={5} className="muted">Sin usuarios.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
