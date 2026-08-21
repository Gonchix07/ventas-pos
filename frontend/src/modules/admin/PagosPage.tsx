import { useEffect, useState } from "react";
import {
  pagos, clusters as clustersApi, FUENTES_PAGO, CANALES_COBRO,
  type TipoPago, type MedioPago, type Cluster,
} from "../../shared/api/admin";
import { PlanesCuotaModal } from "./PlanesCuotaModal";

// Familia "Tarjetas" en FUENTES_PAGO (ver PosEnums.FuentePago en el backend).
const FUENTE_TARJETA = 2;

/**
 * Tipos y medios de pago.
 *
 * Un TIPO es el genérico (Efectivo, Transferencia, Billetera virtual, Tarjetas) y define POR DÓNDE
 * se cobra: Manual (lo registra el cajero) o iCARD (sale por el wrapper local). Un MEDIO es cada
 * forma concreta (Visa, Mastercard, MODO…) y siempre cuelga de un tipo — un mismo tipo puede tener
 * todos los medios que haga falta, y todos heredan su canal.
 */
export function PagosPage() {
  const [tipos, setTipos] = useState<TipoPago[]>([]);
  const [medios, setMedios] = useState<MedioPago[]>([]);
  const [error, setError] = useState<string | null>(null);

  // alta de tipo
  const [tDesc, setTDesc] = useState("");
  const [tFuente, setTFuente] = useState(1);
  const [tCanal, setTCanal] = useState(1);
  const [editTipo, setEditTipo] = useState<number | null>(null);

  // alta de medio
  const [mDesc, setMDesc] = useState("");
  const [mTipo, setMTipo] = useState(0);
  const [mCluster, setMCluster] = useState<number | 0>(0);
  const [mImprime, setMImprime] = useState(true);
  const [cls, setCls] = useState<Cluster[]>([]);
  const [planesMedio, setPlanesMedio] = useState<MedioPago | null>(null);

  const cargar = async () => {
    setError(null);
    try {
      const [t, m, c] = await Promise.all([pagos.tipos(), pagos.medios(), clustersApi.list()]);
      setTipos(t); setMedios(m); setCls(c);
      if (t.length && !t.some((x) => x.idTipoPago === mTipo)) setMTipo(t[0].idTipoPago);
    } catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const guardarTipo = () =>
    run(async () => {
      const input = { descripcion: tDesc.trim(), fuente: tFuente, canal: tCanal };
      if (editTipo) await pagos.updateTipo(editTipo, input);
      else await pagos.createTipo(input);
      setTDesc(""); setTFuente(1); setTCanal(1); setEditTipo(null);
    });

  const editar = (t: TipoPago) => {
    setEditTipo(t.idTipoPago); setTDesc(t.descripcion);
    setTFuente(t.fuente); setTCanal(t.canal);
  };

  const mediosDe = (idTipo: number) => medios.filter((m) => m.idTipoPago === idTipo);

  // Solo activos: un medio desactivado no se ofrece en el cobro, así que tampoco puede ser el default.
  const mediosActivos = medios.filter((m) => m.activo);
  const idPredeterminado = medios.find((m) => m.esPredeterminado)?.idMedioPago ?? 0;

  const agregarMedio = async () => {
    await pagos.createMedio({
      descripcion: mDesc.trim(), idTipoPago: mTipo, esPredeterminado: false, activo: true,
      imprimeComprobante: mImprime, idCluster: mCluster || null,
    });
    setMDesc(""); setMCluster(0); setMImprime(true);
  };

  // Base común de todo update parcial de un medio: parte de sus valores actuales para no pisar
  // campos que esa acción puntual no toca (ej. cambiar el cluster no debe borrar el código de
  // tarjeta ya cargado, y viceversa — bug real: al no mandar codigoTarjetaInterfase explícito en
  // CADA llamada, el backend lo pisaba con null porque el DTO lo trata como "no vino = null").
  const inputDesdeMedio = (m: MedioPago) => ({
    descripcion: m.descripcion, idTipoPago: m.idTipoPago, esPredeterminado: m.esPredeterminado,
    activo: m.activo, imprimeComprobante: m.imprimeComprobante, idCluster: m.idCluster ?? null,
    codigoTarjetaInterfase: m.codigoTarjetaInterfase ?? null,
  });

  // El cluster limita el medio a un grupo de clientes; se cambia desde la propia fila.
  const cambiarCluster = (m: MedioPago, idCluster: number) =>
    run(() => pagos.updateMedio(m.idMedioPago, { ...inputDesdeMedio(m), idCluster: idCluster || null }));

  // Todavía no se usa en Caja: se define más adelante qué hacer al cobrar con un medio marcado así.
  const cambiarImprimeComprobante = (m: MedioPago, imprimeComprobante: boolean) =>
    run(() => pagos.updateMedio(m.idMedioPago, { ...inputDesdeMedio(m), imprimeComprobante }));

  // Código de tarjeta para la interfase contable externa (cupones.tarjeta) — solo tiene sentido
  // para medios de Tarjeta, ver columna condicional más abajo.
  const cambiarCodigoTarjeta = (m: MedioPago, codigoTarjetaInterfase: string) =>
    run(() => pagos.updateMedio(m.idMedioPago,
      { ...inputDesdeMedio(m), codigoTarjetaInterfase: codigoTarjetaInterfase.trim() || null }));

  const cambiarPredeterminado = (id: number) => {
    const m = medios.find((x) => x.idMedioPago === id);
    if (!m) return; // "(ninguno)" no hace nada: siempre tiene que haber uno elegible.
    void run(() => pagos.updateMedio(m.idMedioPago, { ...inputDesdeMedio(m), esPredeterminado: true }));
  };

  return (
    <div>
      <h1>Tipos y medios de pago</h1>
      {error && <p className="error">{error}</p>}

      {/* Uno debajo del otro (antes iban en dos columnas): el medio se define contra un tipo, así
          que se lee mejor de arriba hacia abajo, y las tablas de medios necesitan el ancho completo. */}
      <div>
        <div>
          <h3>Tipos de pago</h3>
          <p className="muted" style={{ margin: "0 0 4px" }}>
            Los genéricos (Efectivo, Transferencia, Billetera virtual, Tarjetas). Cada uno define
            por dónde se efectúa el cobro; los medios que cuelguen de él heredan ese canal.
          </p>
          <div className="field-row">
            <label>Nombre
              <input placeholder="Ej. Tarjetas" value={tDesc} onChange={(e) => setTDesc(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && tDesc.trim() && guardarTipo()} />
            </label>
            <label>Familia
              <select value={tFuente} onChange={(e) => setTFuente(Number(e.target.value))}>
                {FUENTES_PAGO.map((f) => <option key={f.v} value={f.v}>{f.l}</option>)}
              </select>
            </label>
            <label>Se cobra por
              <select value={tCanal} onChange={(e) => setTCanal(Number(e.target.value))}>
                {CANALES_COBRO.map((c) => <option key={c.v} value={c.v}>{c.l}</option>)}
              </select>
            </label>
            <button className="primary" disabled={!tDesc.trim()} onClick={guardarTipo}>
              {editTipo ? "Guardar" : "Agregar"}
            </button>
            {editTipo && (
              <button onClick={() => { setEditTipo(null); setTDesc(""); setTFuente(1); setTCanal(1); }}>
                Cancelar
              </button>
            )}
          </div>

          <table className="grid">
            <thead>
              <tr><th>Nombre</th><th>Familia</th><th>Se cobra por</th><th>Medios</th><th></th></tr>
            </thead>
            <tbody>
              {tipos.map((t) => (
                <tr key={t.idTipoPago} className={editTipo === t.idTipoPago ? "sel" : ""}>
                  <td>{t.descripcion}</td>
                  <td className="muted">{t.fuenteDescripcion}</td>
                  <td>
                    <span className={`badge ${t.canal === 2 ? "on" : "off"}`}>{t.canalDescripcion}</span>
                  </td>
                  <td className="mono">{t.cantidadMedios}</td>
                  <td className="row-actions">
                    <button onClick={() => editar(t)}>Editar</button>
                    <button className="danger" onClick={() => run(() => pagos.removeTipo(t.idTipoPago))}>Eliminar</button>
                  </td>
                </tr>
              ))}
              {tipos.length === 0 && <tr><td colSpan={5} className="muted">Sin tipos de pago.</td></tr>}
            </tbody>
          </table>
        </div>

        <div style={{ marginTop: 24 }}>
          <h3>Medios de pago</h3>
          <p className="muted" style={{ margin: "0 0 4px" }}>
            Las formas concretas (Visa, Mastercard, MODO, una cuenta bancaria…). Podés cargar
            todos los que quieras sobre un mismo tipo.
          </p>
          <div className="field-row">
            <label>Nombre
              <input placeholder="Ej. Visa crédito" value={mDesc} onChange={(e) => setMDesc(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && mDesc.trim() && mTipo &&
                  run(agregarMedio)} />
            </label>
            <label>Tipo de pago
              <select value={mTipo} onChange={(e) => setMTipo(Number(e.target.value))}>
                {tipos.map((t) => (
                  <option key={t.idTipoPago} value={t.idTipoPago}>
                    {t.descripcion} · {t.canalDescripcion}
                  </option>
                ))}
              </select>
            </label>
            <label>Limitar a un grupo de clientes
              <select className={mCluster ? "" : "sin-valor"} value={mCluster}
                onChange={(e) => setMCluster(Number(e.target.value))}>
                <option value={0}>(todos los clientes)</option>
                {cls.map((c) => <option key={c.idCluster} value={c.idCluster}>{c.descripcion}</option>)}
              </select>
            </label>
            <label className="check-box">
              <input type="checkbox" checked={mImprime} onChange={(e) => setMImprime(e.target.checked)} />
              Imprime comprobante
            </label>
            <button className="primary" disabled={!mDesc.trim() || !mTipo}
              onClick={() => run(agregarMedio)}>
              Agregar
            </button>
          </div>

          {/* El predeterminado se elige acá, en un solo lugar: es una opción del sistema, no un
              atributo que se edite medio por medio. Al cambiarlo, el backend destilda al anterior. */}
          <div className="field-row">
            <label>Medio predeterminado en caja
              <select
                className={idPredeterminado ? "" : "sin-valor"}
                value={idPredeterminado}
                onChange={(e) => cambiarPredeterminado(Number(e.target.value))}
                disabled={mediosActivos.length === 0}
              >
                <option value={0}>(ninguno)</option>
                {mediosActivos.map((m) => (
                  <option key={m.idMedioPago} value={m.idMedioPago}>
                    {m.descripcion}{m.tipoPagoDescripcion ? ` · ${m.tipoPagoDescripcion}` : ""}
                  </option>
                ))}
              </select>
            </label>
            <span className="muted" style={{ alignSelf: "center" }}>
              Es el que viene elegido al abrir el cobro. Solo se ofrecen los medios activos.
            </span>
          </div>

          {tipos.length === 0 && <p className="muted">Primero creá un tipo de pago.</p>}

          {/* Agrupado por tipo: deja a la vista que un tipo puede tener muchos medios. */}
          {tipos.map((t) => {
            const suyos = mediosDe(t.idTipoPago);
            return (
              <div key={t.idTipoPago} style={{ marginBottom: 14 }}>
                <h4 style={{ margin: "10px 0 2px" }}>
                  {t.descripcion}{" "}
                  <span className={`badge ${t.canal === 2 ? "on" : "off"}`}>{t.canalDescripcion}</span>
                </h4>
                <table className="grid" style={{ marginTop: 4 }}>
                  <thead>
                    <tr>
                      <th>Medio</th><th>Habilitado para</th><th>Imprime comprobante</th>
                      {t.fuente === FUENTE_TARJETA && <th>Código interfase</th>}
                      <th>Estado</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {suyos.map((m) => (
                      <tr key={m.idMedioPago} className={m.activo ? "" : "inactive"}>
                        <td className="stack">
                          {m.descripcion}
                          {m.esPredeterminado && <small>predeterminado en caja</small>}
                        </td>
                        {/* Con un cluster asignado, el medio solo se ofrece en caja a los clientes
                            que pertenecen a él (el backend además lo revalida al facturar). */}
                        <td>
                          <select className={m.idCluster ? "" : "sin-valor"} value={m.idCluster ?? 0}
                            onChange={(e) => cambiarCluster(m, Number(e.target.value))}>
                            <option value={0}>(todos los clientes)</option>
                            {cls.map((c) => <option key={c.idCluster} value={c.idCluster}>{c.descripcion}</option>)}
                          </select>
                        </td>
                        <td>
                          <label className="check-box">
                            <input type="checkbox" checked={m.imprimeComprobante}
                              onChange={(e) => cambiarImprimeComprobante(m, e.target.checked)} />
                          </label>
                        </td>
                        {/* Código de tarjeta del sistema contable externo (ej. "00003" Visa
                            Crédito) — alimenta cupones.tarjeta en la interfase MySQL. Solo se
                            pide para medios de Tarjeta, el resto no lo necesita. */}
                        {t.fuente === FUENTE_TARJETA && (
                          <td>
                            <input className="mono" style={{ width: 60 }} maxLength={5}
                              defaultValue={m.codigoTarjetaInterfase ?? ""}
                              placeholder="00000"
                              onBlur={(e) => {
                                if (e.target.value.trim() !== (m.codigoTarjetaInterfase ?? "")) {
                                  cambiarCodigoTarjeta(m, e.target.value);
                                }
                              }} />
                          </td>
                        )}
                        <td>{m.activo
                          ? <span className="badge on">Activo</span>
                          : <span className="badge off">Inactivo</span>}</td>
                        <td className="row-actions">
                          {t.fuente === FUENTE_TARJETA && (
                            <button onClick={() => setPlanesMedio(m)}>Planes</button>
                          )}
                          <button onClick={() => run(() => pagos.updateMedio(m.idMedioPago,
                            { ...inputDesdeMedio(m), activo: !m.activo }))}>
                            {m.activo ? "Desactivar" : "Activar"}
                          </button>
                          <button className="danger" onClick={() => run(() => pagos.removeMedio(m.idMedioPago))}>
                            Eliminar
                          </button>
                        </td>
                      </tr>
                    ))}
                    {suyos.length === 0 && (
                      <tr><td colSpan={t.fuente === FUENTE_TARJETA ? 6 : 5} className="muted">Sin medios en este tipo.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            );
          })}
        </div>
      </div>
      {planesMedio && <PlanesCuotaModal medio={planesMedio} onCerrar={() => setPlanesMedio(null)} />}
    </div>
  );
}
