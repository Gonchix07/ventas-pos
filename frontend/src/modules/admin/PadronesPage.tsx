import { useEffect, useRef, useState, type ReactNode } from "react";
import {
  padrones, type PadronIibb, type PadronExIva, type ImportacionPadron,
} from "../../shared/api/admin";

const miles = (n: number) => n.toLocaleString("es-AR");

export function PadronesPage() {
  const [iibb, setIibb] = useState<PadronIibb[]>([]);
  const [exiva, setExiva] = useState<PadronExIva[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [qi, setQi] = useState(""); const [qe, setQe] = useState("");

  const [nCuit, setNCuit] = useState(""); const [nPerc, setNPerc] = useState(0);
  const [eCuit, setECuit] = useState("");

  // Los dos padrones pueden tener decenas de miles de filas: la carga inicial y cada búsqueda por
  // CUIT suelen tardar unos segundos. Sin este bloqueo el admin ve las tablas "congeladas" y
  // reintenta el click.
  const [cargando, setCargando] = useState(false);

  const cargar = async () => {
    setError(null); setCargando(true);
    try { setIibb(await padrones.iibb(qi)); setExiva(await padrones.exIva(qe)); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
    finally { setCargando(false); }
  };
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  return (
    <div>
      {cargando && <PantallaBloqueada mensaje="Buscando en el padrón…" />}
      <h1>Padrones</h1>
      {error && <p className="error">{error}</p>}

      <ImportadorPadron
        titulo="Importar padrón de Ingresos Brutos"
        descripcion={
          <>
            Archivo TXT del régimen general (<span className="mono">PadronRGSPer…</span>) con los
            campos separados por <span className="mono">;</span>: el CUIT va en la columna 5 y la
            alícuota de percepción en la 9.
          </>
        }
        queSeBorra="el padrón de Ingresos Brutos"
        // Un CUIT con 0 % se comporta igual que uno que no está en el padrón, y son ~9 de cada 10
        // filas del archivo: por default no se guardan.
        opcion="Guardar también los CUIT con 0 % de percepción"
        onImportar={(archivo, opcion, onProgreso) => padrones.importarIibb(archivo, opcion, onProgreso)}
        onListo={cargar}
      />

      <ImportadorPadron
        titulo="Importar padrón de excepción de percepción de IVA"
        descripcion={
          <>
            Archivo TXT de ancho fijo, sin separadores: el CUIT son los{" "}
            <b>primeros 11 caracteres</b> de cada línea y el resto se ignora. No hay alícuota:
            figurar en el padrón <i>es</i> la excepción.
          </>
        }
        queSeBorra="el padrón de excepción de percepción de IVA"
        onImportar={(archivo, _opcion, onProgreso) => padrones.importarExIva(archivo, onProgreso)}
        onListo={cargar}
      />

      <div className="two-col">
        <div>
          <h3>Percepción Ingresos Brutos</h3>
          <div className="toolbar">
            <input placeholder="CUIT" value={nCuit} onChange={(e) => setNCuit(e.target.value)} style={{ width: 140 }} />
            <input type="number" step="0.01" placeholder="Percepción %" value={nPerc} onChange={(e) => setNPerc(Number(e.target.value))} style={{ width: 120 }} />
            <button className="primary" disabled={!nCuit.trim()}
              onClick={() => run(async () => { await padrones.upsertIibb(nCuit.trim(), nPerc); setNCuit(""); setNPerc(0); })}>Guardar</button>
          </div>
          <div className="toolbar">
            <input placeholder="Filtrar por CUIT" value={qi} onChange={(e) => setQi(e.target.value)} onKeyDown={(e) => e.key === "Enter" && cargar()} />
            <button onClick={cargar}>Buscar</button>
          </div>
          <table className="grid">
            <thead><tr><th>CUIT</th><th>Percepción</th><th></th></tr></thead>
            <tbody>
              {iibb.map((p) => (
                <tr key={p.cuit}>
                  <td className="mono">{p.cuit}</td><td className="mono">{p.percepcion}%</td>
                  <td><button className="danger" onClick={() => run(() => padrones.removeIibb(p.cuit))}>×</button></td>
                </tr>
              ))}
              {iibb.length === 0 && <tr><td colSpan={3} className="muted">Sin registros.</td></tr>}
            </tbody>
          </table>
        </div>

        <div>
          <h3>Excepción de percepción IVA</h3>
          <div className="toolbar">
            <input placeholder="CUIT" value={eCuit} onChange={(e) => setECuit(e.target.value)} style={{ width: 140 }} />
            <button className="primary" disabled={!eCuit.trim()}
              onClick={() => run(async () => { await padrones.addExIva(eCuit.trim()); setECuit(""); })}>Agregar</button>
          </div>
          <div className="toolbar">
            <input placeholder="Filtrar por CUIT" value={qe} onChange={(e) => setQe(e.target.value)} onKeyDown={(e) => e.key === "Enter" && cargar()} />
            <button onClick={cargar}>Buscar</button>
          </div>
          <table className="grid">
            <thead><tr><th>CUIT</th><th></th></tr></thead>
            <tbody>
              {exiva.map((p) => (
                <tr key={p.cuit}>
                  <td className="mono">{p.cuit}</td>
                  <td><button className="danger" onClick={() => run(() => padrones.removeExIva(p.cuit))}>×</button></td>
                </tr>
              ))}
              {exiva.length === 0 && <tr><td colSpan={2} className="muted">Sin registros.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

// Igual que en CajaPage: tapa la pantalla con blur + spinner mientras se espera una consulta lenta
// (los padrones tienen muchas filas y la búsqueda por CUIT puede tardar). Reutiliza las clases
// globales de App.css (pantalla-bloqueada / pantalla-bloqueada-caja / spinner).
function PantallaBloqueada({ mensaje }: { mensaje: string }) {
  return (
    <div className="pantalla-bloqueada" role="alert" aria-busy="true">
      <div className="pantalla-bloqueada-caja">
        <div className="spinner" aria-hidden="true" />
        <p>{mensaje}</p>
      </div>
    </div>
  );
}

/**
 * Subida de un padrón completo. Los dos padrones se cargan igual —archivo TXT, se borra todo y se
 * reemplaza— así que comparten pantalla: lo único distinto es el endpoint y, en el de IIBB, la
 * opción de guardar o no los CUIT con 0 %.
 */
function ImportadorPadron({ titulo, descripcion, queSeBorra, opcion, onImportar, onListo }: {
  titulo: string;
  descripcion: ReactNode;
  queSeBorra: string;
  opcion?: string;
  onImportar: (archivo: File, opcion: boolean, onProgreso: (pct: number, bytes: number) => void) => Promise<ImportacionPadron>;
  onListo: () => Promise<void> | void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [marcado, setMarcado] = useState(false);
  const [subiendo, setSubiendo] = useState(false);
  const [progreso, setProgreso] = useState(0);
  const [enviados, setEnviados] = useState(0);
  const [resultado, setResultado] = useState<ImportacionPadron | null>(null);
  const [error, setError] = useState<string | null>(null);

  const mb = (bytes: number) => (bytes / 1024 / 1024).toFixed(1);
  const mbEnviados = archivo ? mb(enviados) : null;
  const mbTotal = archivo ? mb(archivo.size) : "";

  const importar = async () => {
    if (!archivo) return;
    if (!confirm(`Se va a BORRAR todo ${queSeBorra} y reemplazarlo por el contenido de "${archivo.name}". ¿Continuar?`))
      return;

    setError(null); setResultado(null); setProgreso(0); setEnviados(0); setSubiendo(true);
    try {
      setResultado(await onImportar(archivo, marcado, (pct, bytes) => {
        setProgreso(pct); setEnviados(bytes);
      }));
      setArchivo(null); setEnviados(0);
      if (inputRef.current) inputRef.current.value = "";
      await onListo();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Error al importar el padrón");
    } finally {
      setSubiendo(false);
    }
  };

  return (
    <div className="card form">
      <h3>{titulo}</h3>
      <p className="muted" style={{ margin: 0 }}>
        {descripcion} <b>Reemplaza el padrón completo</b>: primero se borra todo lo que hay cargado y
        después se inserta el archivo. Si algo falla, no se toca nada.
      </p>
      <div className="import-row">
        <span className="file-field">
          <input ref={inputRef} type="file" accept=".txt,text/plain" disabled={subiendo}
            onChange={(e) => { setArchivo(e.target.files?.[0] ?? null); setResultado(null); setError(null); }} />
        </span>
        {opcion && (
          <label className="check-box">
            <input type="checkbox" checked={marcado} disabled={subiendo}
              onChange={(e) => setMarcado(e.target.checked)} />
            {opcion}
          </label>
        )}
        <button className="primary" disabled={!archivo || subiendo} onClick={importar}>
          {subiendo ? "Importando…" : "Importar y reemplazar"}
        </button>
      </div>
      {archivo && !subiendo && (
        <p className="muted">{archivo.name} · {(archivo.size / 1024 / 1024).toFixed(1)} MB</p>
      )}
      {subiendo && (
        <div>
          {/* El servidor procesa el archivo a medida que lo recibe (streaming), así que el avance
              de la subida es el avance real del import; el tramo final no reporta avance. */}
          <div className={`progress${progreso >= 100 ? " progress--indeterminada" : ""}`}>
            <div className="progress__barra" style={progreso < 100 ? { width: `${progreso}%` } : undefined} />
          </div>
          <p className="muted" style={{ margin: 0 }}>
            {progreso < 100
              ? `Procesando… ${progreso}% ${mbEnviados !== null ? `(${mbEnviados} de ${mbTotal} MB)` : ""}`
              : "Archivo leído — insertando en la base y confirmando…"}
          </p>
        </div>
      )}
      {error && <p className="error">{error}</p>}
      {resultado && (
        <p className="ok-msg">
          Padrón reemplazado: {miles(resultado.importadas)} CUIT cargados de{" "}
          {miles(resultado.filasLeidas)} líneas leídas
          {resultado.sinPercepcion > 0 && ` · ${miles(resultado.sinPercepcion)} con 0 % (omitidos)`}
          {resultado.invalidas > 0 && ` · ${miles(resultado.invalidas)} líneas inválidas`}
          {" · "}{miles(resultado.borradasPrevias)} filas anteriores borradas
          {" · "}{(resultado.milisegundosTotales / 1000).toFixed(1)} s
        </p>
      )}
    </div>
  );
}
