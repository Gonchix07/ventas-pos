import { useEffect, useState } from "react";
import { version, type VersionInfo } from "../../shared/api/admin";

const fecha = (iso?: string | null) => {
  if (!iso) return null;
  const d = new Date(iso);
  return isNaN(d.getTime()) ? iso : d.toLocaleString("es-AR");
};

export function VersionPage() {
  const [datos, setDatos] = useState<VersionInfo | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    version.get().then(setDatos).catch((e) => setError(e instanceof Error ? e.message : "Error"));
  }, []);

  return (
    <div>
      <h1>Versión</h1>
      {error && <p className="error">{error}</p>}
      {!datos && !error && <p className="muted">Cargando…</p>}

      {datos && (
        <>
          <div className="card form">
            <h3>Aplicación</h3>
            <div className="form-grid" style={{ alignItems: "start" }}>
              <div><span className="muted">Versión</span><div className="mono">{datos.versionApp}</div></div>
              <div><span className="muted">Entorno</span><div className="mono">{datos.entorno}</div></div>
              <div><span className="muted">Servidor</span><div className="mono">{datos.servidor}</div></div>
              <div><span className="muted">.NET</span><div className="mono">{datos.runtimeDotnet}</div></div>
              <div><span className="muted">Entity Framework Core</span><div className="mono">{datos.efCoreVersion}</div></div>
            </div>
          </div>

          <div className="card form">
            <h3>Último commit</h3>
            {datos.gitCommit ? (
              <div className="form-grid" style={{ alignItems: "start" }}>
                <div><span className="muted">Commit</span><div className="mono">{datos.gitCommitCorto}</div></div>
                <div><span className="muted">Rama</span><div className="mono">{datos.gitRama ?? "—"}</div></div>
                <div><span className="muted">Fecha</span><div className="mono">{fecha(datos.gitFecha) ?? "—"}</div></div>
                <div style={{ gridColumn: "1 / -1" }}>
                  <span className="muted">Mensaje</span>
                  <div>{datos.gitMensaje ?? "—"}</div>
                </div>
                <div style={{ gridColumn: "1 / -1" }}>
                  <span className="muted">Hash completo</span>
                  <div className="mono" style={{ wordBreak: "break-all" }}>{datos.gitCommit}</div>
                </div>
              </div>
            ) : (
              <p className="muted">
                No se pudo leer el commit — el servidor no tiene git instalado o no corre desde un
                checkout con historial (ej. un deploy que solo copió los binarios).
              </p>
            )}
          </div>
        </>
      )}
    </div>
  );
}
