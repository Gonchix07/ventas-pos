import { useEffect, useState } from "react";
import { permisos, type MatrizPermisos } from "../../shared/api/admin";

/**
 * Permisos por rol: qué módulos del menú principal ve cada rol (tilde = accede, sin tilde = la
 * tarjeta le aparece deshabilitada en "Módulos"). Cada cambio se guarda solo, como el resto de los
 * toggles del ABM (sin botón "Guardar" aparte).
 *
 * Ojo — esto NO es un sistema de permisos a nivel de API: cada endpoint sigue protegido por su
 * propio `[Authorize(Roles=...)]` fijo en el backend. Sacarle acá el tilde de un módulo a un rol
 * oculta la tarjeta del menú, pero si ese rol de por sí no está en la lista de roles autorizados
 * del backend para esas pantallas, tampoco podría entrar aunque tuviera el tilde puesto.
 */
export function PermisosPage() {
  const [matriz, setMatriz] = useState<MatrizPermisos | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState<string | null>(null); // clave "idRol-idModulo" en curso

  const cargar = async () => {
    setError(null);
    try {
      setMatriz(await permisos.matriz());
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo cargar la matriz de permisos.");
    }
  };

  useEffect(() => { void cargar(); }, []);

  const toggle = async (idRol: number, idModulo: number, actual: boolean) => {
    const clave = `${idRol}-${idModulo}`;
    setError(null);
    setGuardando(clave);
    // Optimista: se refleja el cambio ya mismo y se revierte si el backend lo rechaza (ej. la
    // salvaguarda de no sacarle Administración al rol Administrador).
    setMatriz((m) => m && {
      ...m,
      roles: m.roles.map((r) => r.idRol !== idRol ? r : {
        ...r,
        celdas: r.celdas.map((c) => c.idModulo !== idModulo ? c : { ...c, puedeVer: !actual }),
      }),
    });
    try {
      await permisos.actualizar(idRol, idModulo, !actual);
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo guardar el cambio.");
      await cargar(); // revertir a lo que realmente quedó en el backend
    } finally {
      setGuardando(null);
    }
  };

  if (!matriz) {
    return (
      <div>
        <h1>Permisos por rol</h1>
        {error && <p className="error">{error}</p>}
        {!error && <p className="muted">Cargando…</p>}
      </div>
    );
  }

  return (
    <div>
      <h1>Permisos por rol</h1>
      <p className="muted">
        Qué módulos del menú principal ve cada rol. Un tilde habilita la tarjeta en "Módulos"; sin
        tilde queda deshabilitada para ese rol.
      </p>
      {error && <p className="error">{error}</p>}

      <table className="grid permisos-table">
        <thead>
          <tr>
            <th>Rol</th>
            {matriz.modulos.map((m) => <th key={m.idModulo}>{m.descripcion}</th>)}
          </tr>
        </thead>
        <tbody>
          {matriz.roles.map((r) => (
            <tr key={r.idRol}>
              <td><b>{r.rolDescripcion}</b></td>
              {r.celdas.map((c) => {
                const clave = `${r.idRol}-${c.idModulo}`;
                const bloqueado = r.rolDescripcion === "Administrador"
                  && matriz.modulos.find((m) => m.idModulo === c.idModulo)?.descripcion === "Administracion";
                return (
                  <td key={c.idModulo}>
                    <input
                      type="checkbox"
                      checked={c.puedeVer}
                      disabled={guardando === clave || bloqueado}
                      title={bloqueado ? "El rol Administrador siempre mantiene acceso a Administración." : undefined}
                      onChange={() => void toggle(r.idRol, c.idModulo, c.puedeVer)}
                    />
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
