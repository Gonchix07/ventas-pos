import { useEffect, useMemo, useState } from "react";
import { familias as api, lookups, type Familia, type Lookup } from "../../shared/api/admin";

/**
 * ABM de familias. No usa LookupPage porque la familia cuelga de un sector: el nombre se repite
 * entre sectores (DESODORANTES está en PERFUMERIA y en LIMPIEZA) y sin el sector la lista no se
 * entiende.
 */
export function FamiliasPage() {
  const [items, setItems] = useState<Familia[]>([]);
  const [sectores, setSectores] = useState<Lookup[]>([]);
  const [filtroSector, setFiltroSector] = useState<number | "">("");

  const [nuevo, setNuevo] = useState("");
  const [nuevoSector, setNuevoSector] = useState<number | "">("");

  const [editId, setEditId] = useState<number | null>(null);
  const [editText, setEditText] = useState("");
  const [editSector, setEditSector] = useState<number | "">("");

  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const cargar = async (idSector: number | "" = filtroSector) => {
    setLoading(true);
    setError(null);
    try {
      setItems(await api.list(idSector === "" ? undefined : idSector));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void (async () => {
      try {
        setSectores(await lookups.list("sectores"));
      } catch {
        /* el listado de familias igual funciona sin el combo */
      }
      await cargar("");
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const nombreSector = useMemo(() => {
    const m = new Map<number, string>();
    sectores.forEach((s) => m.set(s.id, s.descripcion));
    return m;
  }, [sectores]);

  const cambiarFiltro = (v: string) => {
    const id = v === "" ? "" : Number(v);
    setFiltroSector(id);
    void cargar(id);
  };

  const agregar = async () => {
    if (!nuevo.trim() || nuevoSector === "") return;
    setError(null);
    try {
      await api.create(nuevo.trim(), nuevoSector);
      setNuevo("");
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  const guardar = async (id: number) => {
    setError(null);
    try {
      await api.update(id, editText.trim(), editSector === "" ? null : editSector);
      setEditId(null);
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  const eliminar = async (id: number) => {
    if (!confirm("¿Eliminar la familia?")) return;
    setError(null);
    try {
      await api.remove(id);
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  return (
    <div>
      <h1>Familias</h1>

      <div className="field-row">
        <label>
          <span>Sector</span>
          <select
            className={nuevoSector === "" ? "sin-valor" : ""}
            value={nuevoSector}
            onChange={(e) => setNuevoSector(e.target.value === "" ? "" : Number(e.target.value))}
          >
            <option value="">(elegí un sector)</option>
            {sectores.map((s) => (
              <option key={s.id} value={s.id}>{s.descripcion}</option>
            ))}
          </select>
        </label>
        <label>
          <span>Nueva familia</span>
          <input
            placeholder="Nombre de la familia"
            value={nuevo}
            onChange={(e) => setNuevo(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && agregar()}
          />
        </label>
        <button className="primary" onClick={agregar} disabled={!nuevo.trim() || nuevoSector === ""}>
          Agregar
        </button>
      </div>

      <div className="filter-bar">
        <label className="inline-label">
          Ver sector
          <select
            className={filtroSector === "" ? "sin-valor" : ""}
            value={filtroSector}
            onChange={(e) => cambiarFiltro(e.target.value)}
          >
            <option value="">Todos</option>
            {sectores.map((s) => (
              <option key={s.id} value={s.id}>{s.descripcion}</option>
            ))}
          </select>
        </label>
        <span className="muted">{items.length} familia(s)</span>
      </div>

      {error && <p className="error">{error}</p>}
      {loading ? (
        <p className="muted">Cargando…</p>
      ) : (
        <table className="grid">
          <thead>
            <tr>
              <th style={{ width: 80 }}>ID</th>
              <th style={{ width: 240 }}>Sector</th>
              <th>Familia</th>
              <th style={{ width: 160 }}></th>
            </tr>
          </thead>
          <tbody>
            {items.map((it) => (
              <tr key={it.id}>
                <td className="mono">{it.id}</td>
                <td>
                  {editId === it.id ? (
                    <select
                      className={editSector === "" ? "sin-valor" : ""}
                      value={editSector}
                      onChange={(e) => setEditSector(e.target.value === "" ? "" : Number(e.target.value))}
                    >
                      <option value="">(sin sector)</option>
                      {sectores.map((s) => (
                        <option key={s.id} value={s.id}>{s.descripcion}</option>
                      ))}
                    </select>
                  ) : it.idSector == null ? (
                    <span className="muted">(sin sector)</span>
                  ) : (
                    it.sectorDescripcion ?? nombreSector.get(it.idSector) ?? it.idSector
                  )}
                </td>
                <td>
                  {editId === it.id ? (
                    <input value={editText} onChange={(e) => setEditText(e.target.value)} />
                  ) : (
                    it.descripcion
                  )}
                </td>
                <td className="row-actions">
                  {editId === it.id ? (
                    <>
                      <button className="primary" onClick={() => guardar(it.id)}>Guardar</button>
                      <button onClick={() => setEditId(null)}>Cancelar</button>
                    </>
                  ) : (
                    <>
                      <button
                        onClick={() => {
                          setEditId(it.id);
                          setEditText(it.descripcion);
                          setEditSector(it.idSector ?? "");
                        }}
                      >
                        Editar
                      </button>
                      <button className="danger" onClick={() => eliminar(it.id)}>Eliminar</button>
                    </>
                  )}
                </td>
              </tr>
            ))}
            {items.length === 0 && (
              <tr><td colSpan={4} className="muted">Sin registros.</td></tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
