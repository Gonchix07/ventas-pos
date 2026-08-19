import { useEffect, useState } from "react";
import { lookups, type Lookup } from "../../shared/api/admin";

interface Props {
  resource: string;
  title: string;
}

export function LookupPage({ resource, title }: Props) {
  const [items, setItems] = useState<Lookup[]>([]);
  const [nuevo, setNuevo] = useState("");
  const [editId, setEditId] = useState<number | null>(null);
  const [editText, setEditText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const cargar = async () => {
    setLoading(true);
    setError(null);
    try {
      setItems(await lookups.list(resource));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void cargar();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resource]);

  const agregar = async () => {
    if (!nuevo.trim()) return;
    try {
      await lookups.create(resource, nuevo.trim());
      setNuevo("");
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  const guardar = async (id: number) => {
    try {
      await lookups.update(resource, id, editText.trim());
      setEditId(null);
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  const eliminar = async (id: number) => {
    if (!confirm("¿Eliminar el registro?")) return;
    try {
      await lookups.remove(resource, id);
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error");
    }
  };

  return (
    <div>
      <h1>{title}</h1>
      <div className="toolbar">
        <input
          placeholder={`Nuevo ${title.toLowerCase()}`}
          value={nuevo}
          onChange={(e) => setNuevo(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && agregar()}
        />
        <button className="primary" onClick={agregar}>Agregar</button>
      </div>
      {error && <p className="error">{error}</p>}
      {loading ? (
        <p className="muted">Cargando…</p>
      ) : (
        <table className="grid">
          <thead>
            <tr><th style={{ width: 80 }}>ID</th><th>Descripción</th><th style={{ width: 160 }}></th></tr>
          </thead>
          <tbody>
            {items.map((it) => (
              <tr key={it.id}>
                <td className="mono">{it.id}</td>
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
                      <button onClick={() => { setEditId(it.id); setEditText(it.descripcion); }}>Editar</button>
                      <button className="danger" onClick={() => eliminar(it.id)}>Eliminar</button>
                    </>
                  )}
                </td>
              </tr>
            ))}
            {items.length === 0 && (
              <tr><td colSpan={3} className="muted">Sin registros.</td></tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
