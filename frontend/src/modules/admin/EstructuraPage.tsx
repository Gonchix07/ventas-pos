import { useEffect, useState } from "react";
import {
  estructura, caea, type CaeaCargado, type CaeaCargadoInput, type CertificadoCae, type Empresa,
  type EmpresaInput, type ProbarConexionAfip, type Sucursal, type SucursalInput,
} from "../../shared/api/admin";

const caeaVacio = (idEmpresa: number): CaeaCargadoInput => ({
  idEmpresa, anio: new Date().getFullYear(), mes: new Date().getMonth() + 1, orden: 1,
  valor: "", vigenciaDesde: "", vigenciaHasta: "",
});

const empresaVacia = (): EmpresaInput => ({
  codigoInterno: "", descripcion: "", cuit: "", certificadoAlias: "",
  condicionIva: "", ingresosBrutos: "", inicioActividad: "",
  domicilio: "", localidad: "", provincia: "", codigoPostal: "",
});

const sucursalVacia = (idEmpresa: number): SucursalInput => ({
  idEmpresa, descripcion: "", domicilio: "", localidad: "", provincia: "", codigoPostal: "",
});

// El backend devuelve la fecha completa (ISO); el input date necesita solo yyyy-MM-dd.
const soloFecha = (iso?: string | null) => (iso ? iso.slice(0, 10) : "");

const desdeEmpresa = (e: Empresa): EmpresaInput => ({
  codigoInterno: e.codigoInterno, descripcion: e.descripcion, cuit: e.cuit ?? "",
  certificadoAlias: e.certificadoAlias ?? "", condicionIva: e.condicionIva ?? "",
  ingresosBrutos: e.ingresosBrutos ?? "", inicioActividad: soloFecha(e.inicioActividad),
  domicilio: e.domicilio ?? "", localidad: e.localidad ?? "", provincia: e.provincia ?? "",
  codigoPostal: e.codigoPostal ?? "",
});

const desdeSucursal = (s: Sucursal): SucursalInput => ({
  idEmpresa: s.idEmpresa, descripcion: s.descripcion, domicilio: s.domicilio ?? "",
  localidad: s.localidad ?? "", provincia: s.provincia ?? "", codigoPostal: s.codigoPostal ?? "",
});

// Los campos vacíos se mandan como null para no guardar cadenas en blanco.
const limpiar = <T extends Record<string, unknown>>(o: T): T => {
  const r: Record<string, unknown> = { ...o };
  for (const k of Object.keys(r)) if (r[k] === "") r[k] = null;
  return r as T;
};

/**
 * Empresas y sucursales. Los datos de la empresa (razón social, CUIT, condición frente al IVA,
 * Ing. Brutos, inicio de actividad) y el domicilio de la sucursal son los que salen impresos en
 * el encabezado de la factura A y B, así que se editan desde acá.
 */
export function EstructuraPage() {
  const [empresas, setEmpresas] = useState<Empresa[]>([]);
  const [sucursales, setSucursales] = useState<Sucursal[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [formEmpresa, setFormEmpresa] = useState<EmpresaInput | null>(null);
  const [editEmpresa, setEditEmpresa] = useState<number | null>(null);
  const [formSucursal, setFormSucursal] = useState<SucursalInput | null>(null);
  const [editSucursal, setEditSucursal] = useState<number | null>(null);

  // Certificado CAE: solo tiene sentido una vez que la empresa existe (necesita su id), por eso
  // vive aparte del alta/edición de datos generales y solo se muestra al editar. Dos modos porque
  // ARCA solo entrega el certificado (.crt/.cer) suelto — la clave privada la genera quien tramita
  // el certificado y hay que combinarla; no todos tienen ya armado el .pfx.
  const [modoCert, setModoCert] = useState<"pfx" | "clave-cert">("pfx");
  const [certificado, setCertificado] = useState<CertificadoCae | null>(null);
  const [archivoCert, setArchivoCert] = useState<File | null>(null);
  const [claveCert, setClaveCert] = useState("");
  const [archivoClavePrivada, setArchivoClavePrivada] = useState<File | null>(null);
  const [archivoCertificado, setArchivoCertificado] = useState<File | null>(null);
  const [passphraseClave, setPassphraseClave] = useState("");
  const [certBusy, setCertBusy] = useState(false);

  // Prueba de conexión con ARCA: solo lectura (login WSAA + FEDummy + último autorizado), nunca
  // emite ni autoriza nada. Requiere el punto de venta a chequear (número ARCA, no el id interno).
  const [ptoVtaProbar, setPtoVtaProbar] = useState("");
  const [cbteTipoProbar, setCbteTipoProbar] = useState(6);
  const [resultadoProbar, setResultadoProbar] = useState<ProbarConexionAfip | null>(null);
  const [probando, setProbando] = useState(false);

  // CAEA precargado (contingencia): se consigue con conexión (FECAEASolicitar, con antelación por
  // quincena) y se carga acá a mano para poder seguir facturando si ARCA está inaccesible en el
  // momento de la venta.
  const [caeas, setCaeas] = useState<CaeaCargado[]>([]);
  const [formCaea, setFormCaea] = useState<CaeaCargadoInput | null>(null);
  const [editCaea, setEditCaea] = useState<number | null>(null);

  const cargar = async () => {
    setError(null);
    try {
      const [e, s] = await Promise.all([estructura.empresas(), estructura.sucursales()]);
      setEmpresas(e); setSucursales(s);
    } catch (err) { setError(err instanceof Error ? err.message : "Error"); }
  };
  useEffect(() => { void cargar(); /* eslint-disable-next-line */ }, []);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); await cargar(); }
    catch (e) { setError(e instanceof Error ? e.message : "Error"); }
  };

  const setE = (patch: Partial<EmpresaInput>) => setFormEmpresa((f) => (f ? { ...f, ...patch } : f));
  const setS = (patch: Partial<SucursalInput>) => setFormSucursal((f) => (f ? { ...f, ...patch } : f));

  // Al editar una empresa existente se consulta el estado actual del certificado (nunca la clave)
  // y la lista de CAEA precargados; la prueba de conexión se limpia porque quedaría desactualizada.
  useEffect(() => {
    if (editEmpresa == null) { setCertificado(null); setCaeas([]); setResultadoProbar(null); setFormCaea(null); return; }
    setArchivoCert(null); setClaveCert(""); setArchivoClavePrivada(null); setArchivoCertificado(null); setPassphraseClave("");
    setResultadoProbar(null);
    estructura.certificado(editEmpresa).then(setCertificado).catch(() => setCertificado(null));
    caea.list(editEmpresa).then(setCaeas).catch(() => setCaeas([]));
  }, [editEmpresa]);

  const probarConexion = () => run(async () => {
    if (editEmpresa == null || !ptoVtaProbar.trim()) return;
    setProbando(true); setResultadoProbar(null);
    try {
      setResultadoProbar(await estructura.probarConexionAfip(editEmpresa, Number(ptoVtaProbar), cbteTipoProbar));
    } finally { setProbando(false); }
  });

  const cargarCaeas = () => editEmpresa != null && caea.list(editEmpresa).then(setCaeas).catch(() => {});

  const guardarCaea = () => run(async () => {
    if (!formCaea) return;
    if (editCaea) await caea.update(editCaea, formCaea);
    else await caea.create(formCaea.idEmpresa, formCaea);
    setFormCaea(null); setEditCaea(null);
    await cargarCaeas();
  });

  const eliminarCaea = (id: number) => run(async () => {
    if (!confirm("¿Eliminar este CAEA cargado?")) return;
    await caea.remove(id);
    await cargarCaeas();
  });

  const subirCertificado = () => run(async () => {
    if (editEmpresa == null || !archivoCert || !claveCert) return;
    setCertBusy(true);
    try {
      setCertificado(await estructura.subirCertificado(editEmpresa, archivoCert, claveCert));
      setArchivoCert(null); setClaveCert("");
    } finally { setCertBusy(false); }
  });

  const subirCertificadoDesdeClaveYCert = () => run(async () => {
    if (editEmpresa == null || !archivoClavePrivada || !archivoCertificado) return;
    setCertBusy(true);
    try {
      setCertificado(await estructura.subirCertificadoDesdeClaveYCert(
        editEmpresa, archivoClavePrivada, archivoCertificado, passphraseClave));
      setArchivoClavePrivada(null); setArchivoCertificado(null); setPassphraseClave("");
    } finally { setCertBusy(false); }
  });

  const eliminarCertificado = () => run(async () => {
    if (editEmpresa == null) return;
    await estructura.removeCertificado(editEmpresa);
    setCertificado({ presente: false });
  });

  const certVencido = !!certificado?.vencimiento && new Date(certificado.vencimiento) < new Date();

  const guardarEmpresa = () => run(async () => {
    if (!formEmpresa) return;
    const input = limpiar({ ...formEmpresa, codigoInterno: formEmpresa.codigoInterno.trim(), descripcion: formEmpresa.descripcion.trim() });
    if (editEmpresa) await estructura.updateEmpresa(editEmpresa, input);
    else await estructura.createEmpresa(input);
    setFormEmpresa(null); setEditEmpresa(null);
  });

  const guardarSucursal = () => run(async () => {
    if (!formSucursal) return;
    const input = limpiar({ ...formSucursal, descripcion: formSucursal.descripcion.trim() });
    if (editSucursal) await estructura.updateSucursal(editSucursal, input);
    else await estructura.createSucursal(input);
    setFormSucursal(null); setEditSucursal(null);
  });

  return (
    <div>
      {probando && <PantallaBloqueada mensaje="Probando conexión con ARCA…" />}
      <div className="page-head">
        <h1>Empresas y sucursales</h1>
        <div className="row-actions">
          <button className="primary" onClick={() => { setEditEmpresa(null); setFormEmpresa(empresaVacia()); }}>
            Nueva empresa
          </button>
          <button
            disabled={empresas.length === 0}
            onClick={() => { setEditSucursal(null); setFormSucursal(sucursalVacia(empresas[0]?.idEmpresa ?? 0)); }}
          >
            Nueva sucursal
          </button>
        </div>
      </div>
      <p className="muted">
        Estos datos encabezan la factura: la <b>empresa</b> aporta razón social, CUIT, condición
        frente al IVA, Ingresos Brutos e inicio de actividad; el domicilio impreso es el de la{" "}
        <b>sucursal</b> que emite (si no tiene, se usa el de la empresa).
      </p>
      {error && <p className="error">{error}</p>}

      {formEmpresa && (
        <div className="card form">
          <h3>{editEmpresa ? "Editar empresa" : "Nueva empresa"}</h3>
          <div className="form-grid">
            <label>Código<input value={formEmpresa.codigoInterno} onChange={(e) => setE({ codigoInterno: e.target.value })} /></label>
            <label>Razón social<input value={formEmpresa.descripcion} onChange={(e) => setE({ descripcion: e.target.value })} /></label>
            <label>CUIT<input value={formEmpresa.cuit ?? ""} onChange={(e) => setE({ cuit: e.target.value })} maxLength={13} /></label>
            <label>Condición frente al IVA
              <input value={formEmpresa.condicionIva ?? ""} onChange={(e) => setE({ condicionIva: e.target.value })}
                placeholder="Resp. Inscripto" maxLength={60} />
            </label>
            <label>Ingresos Brutos
              <input value={formEmpresa.ingresosBrutos ?? ""} onChange={(e) => setE({ ingresosBrutos: e.target.value })} maxLength={40} />
            </label>
            <label>Inicio de actividad
              <input type="date" value={formEmpresa.inicioActividad ?? ""} onChange={(e) => setE({ inicioActividad: e.target.value })} />
            </label>
            <label>Domicilio<input value={formEmpresa.domicilio ?? ""} onChange={(e) => setE({ domicilio: e.target.value })} maxLength={120} /></label>
            <label>Localidad<input value={formEmpresa.localidad ?? ""} onChange={(e) => setE({ localidad: e.target.value })} maxLength={60} /></label>
            <label>Provincia<input value={formEmpresa.provincia ?? ""} onChange={(e) => setE({ provincia: e.target.value })} maxLength={60} /></label>
            <label>Código postal<input value={formEmpresa.codigoPostal ?? ""} onChange={(e) => setE({ codigoPostal: e.target.value })} maxLength={8} /></label>
            <label>Alias del certificado (CAE)
              <input value={formEmpresa.certificadoAlias ?? ""} onChange={(e) => setE({ certificadoAlias: e.target.value })} maxLength={60} />
            </label>
          </div>
          <div className="row-actions">
            <button className="primary" disabled={!formEmpresa.codigoInterno.trim() || !formEmpresa.descripcion.trim()}
              onClick={guardarEmpresa}>Guardar</button>
            <button onClick={() => { setFormEmpresa(null); setEditEmpresa(null); }}>Cancelar</button>
          </div>

          {editEmpresa != null && (
            <>
            <div className="cert-cae">
              <div className="cert-cae-head">
                <span className="cert-cae-icon" aria-hidden="true">
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                    strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12 2 20 6v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V6l8-4Z" />
                    <path d="m9 12 2 2 4-4" />
                  </svg>
                </span>
                <h3>Certificado CAE</h3>
              </div>
              <p className="cert-cae-desc">
                Certificado .pfx/.p12 emitido por ARCA para firmar los pedidos de CAE de esta
                empresa. Se guarda en el servidor; la contraseña se cifra y no se puede volver a ver.
              </p>
              <div className="cert-cae-estado">
                {certificado?.presente ? (
                  <>
                    <span className={`badge ${certVencido ? "off" : "on"}`}>{certVencido ? "Vencido" : "Vigente"}</span>
                    <span>
                      {certificado.nombreArchivo}
                      {certificado.vencimiento && (
                        <> · {certVencido ? "venció" : "vence"} {soloFecha(certificado.vencimiento)}</>
                      )}
                    </span>
                  </>
                ) : (
                  <>
                    <span className="badge muted">Sin certificado</span>
                    <span className="muted">Todavía no se cargó ninguno.</span>
                  </>
                )}
              </div>
              <div className="cert-cae-modo">
                <button type="button" className={modoCert === "pfx" ? "activo" : ""} onClick={() => setModoCert("pfx")}>
                  Archivo .pfx/.p12
                </button>
                <button type="button" className={modoCert === "clave-cert" ? "activo" : ""} onClick={() => setModoCert("clave-cert")}>
                  Clave privada + certificado
                </button>
              </div>

              {modoCert === "pfx" ? (
                <div className="import-row">
                  <label className="file-field">
                    <input type="file" accept=".pfx,.p12" onChange={(e) => setArchivoCert(e.target.files?.[0] ?? null)} />
                  </label>
                  <input type="password" placeholder="Contraseña del certificado" value={claveCert}
                    onChange={(e) => setClaveCert(e.target.value)} />
                  <button className="primary" disabled={!archivoCert || !claveCert || certBusy} onClick={subirCertificado}>
                    {certBusy ? "Subiendo…" : certificado?.presente ? "Reemplazar" : "Subir"}
                  </button>
                  {certificado?.presente && (
                    <button className="danger" onClick={eliminarCertificado}>Eliminar</button>
                  )}
                </div>
              ) : (
                <>
                  <p className="cert-cae-desc" style={{ marginTop: -6 }}>
                    Para cuando ARCA solo entregó el certificado (.crt/.cer): subilo junto con la
                    clave privada (.key) generada al tramitarlo — el servidor los combina.
                  </p>
                  <div className="import-row">
                    <label className="file-field">
                      <span className="muted" style={{ display: "block", fontSize: 12, marginBottom: 4 }}>Clave privada (.key)</span>
                      <input type="file" accept=".key,.pem" onChange={(e) => setArchivoClavePrivada(e.target.files?.[0] ?? null)} />
                    </label>
                    <label className="file-field">
                      <span className="muted" style={{ display: "block", fontSize: 12, marginBottom: 4 }}>Certificado (.crt/.cer)</span>
                      <input type="file" accept=".crt,.cer,.pem" onChange={(e) => setArchivoCertificado(e.target.files?.[0] ?? null)} />
                    </label>
                  </div>
                  <div className="import-row" style={{ marginTop: 10 }}>
                    <input type="password" placeholder="Passphrase de la clave privada (si tiene)" value={passphraseClave}
                      onChange={(e) => setPassphraseClave(e.target.value)} />
                    <button className="primary" disabled={!archivoClavePrivada || !archivoCertificado || certBusy}
                      onClick={subirCertificadoDesdeClaveYCert}>
                      {certBusy ? "Subiendo…" : certificado?.presente ? "Reemplazar" : "Subir"}
                    </button>
                    {certificado?.presente && (
                      <button className="danger" onClick={eliminarCertificado}>Eliminar</button>
                    )}
                  </div>
                </>
              )}
            </div>

            <div className="cert-cae">
              <h3>Probar conexión con ARCA</h3>
              <p className="cert-cae-desc">
                Solo lectura: login WSAA con el certificado cargado + FEDummy + último comprobante
                autorizado en el punto de venta indicado. Nunca emite ni autoriza nada.
              </p>
              <div className="form-grid">
                <label>Punto de venta ARCA
                  <input className="mono" placeholder="Ej. 34" value={ptoVtaProbar}
                    onChange={(e) => setPtoVtaProbar(e.target.value)} />
                </label>
                <label>Tipo de comprobante
                  <select value={cbteTipoProbar} onChange={(e) => setCbteTipoProbar(Number(e.target.value))}>
                    <option value={1}>001 · Factura A</option>
                    <option value={6}>006 · Factura B</option>
                    <option value={11}>011 · Factura C</option>
                    <option value={3}>003 · Nota de Crédito A</option>
                    <option value={8}>008 · Nota de Crédito B</option>
                    <option value={13}>013 · Nota de Crédito C</option>
                  </select>
                </label>
                <button className="primary" disabled={!ptoVtaProbar.trim() || probando} onClick={probarConexion}>
                  Probar conexión
                </button>
              </div>
              {resultadoProbar && (
                <table className="grid" style={{ marginTop: 10 }}>
                  <tbody>
                    <tr>
                      <td>Login WSAA</td>
                      <td><span className={`badge ${resultadoProbar.wsaaOk ? "on" : "off"}`}>{resultadoProbar.wsaaOk ? "OK" : "Falló"}</span></td>
                      <td className="muted">{resultadoProbar.wsaaError}</td>
                    </tr>
                    <tr>
                      <td>WSFEv1 (FEDummy)</td>
                      <td><span className={`badge ${resultadoProbar.dummyOk ? "on" : "off"}`}>{resultadoProbar.dummyOk ? "OK" : "Falló"}</span></td>
                      <td className="muted">{resultadoProbar.dummyError}</td>
                    </tr>
                    <tr>
                      <td>Último autorizado</td>
                      <td className="mono">{resultadoProbar.ultimoAutorizado ?? "—"}</td>
                      <td className="muted">{resultadoProbar.ultimoAutorizadoError}</td>
                    </tr>
                    {resultadoProbar.certificadoSubject && (
                      <tr>
                        <td>Certificado</td>
                        <td colSpan={2} className="mono" style={{ fontSize: 12 }}>
                          {resultadoProbar.certificadoSubject}<br />
                          <span className="muted">Emitido por: {resultadoProbar.certificadoIssuer}</span>
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              )}
            </div>

            <div className="cert-cae">
              <h3>CAEA precargado (contingencia)</h3>
              <p className="cert-cae-desc">
                Se usa automáticamente al facturar si ARCA no responde (CAE inaccesible). El valor
                se consigue CON conexión (FECAEASolicitar, con antelación por quincena) y se carga
                acá a mano — esta pantalla no le pide nada a ARCA.
              </p>
              <table className="grid">
                <thead><tr><th>Período</th><th>Quincena</th><th>Valor</th><th>Vigencia</th><th>Estado</th><th></th></tr></thead>
                <tbody>
                  {caeas.map((c) => (
                    <tr key={c.idCaea}>
                      <td className="mono">{c.anio}-{String(c.mes).padStart(2, "0")}</td>
                      <td>{c.orden === 1 ? "1 al 15" : "16 a fin de mes"}</td>
                      <td className="mono">{c.valor}</td>
                      <td className="mono">{c.vigenciaDesde.slice(0, 10)} a {c.vigenciaHasta.slice(0, 10)}</td>
                      <td><span className={`badge ${c.vigenteHoy ? "on" : "muted"}`}>{c.vigenteHoy ? "Vigente hoy" : "—"}</span></td>
                      <td>
                        <button onClick={() => { setEditCaea(c.idCaea); setFormCaea({
                          idEmpresa: c.idEmpresa, anio: c.anio, mes: c.mes, orden: c.orden,
                          valor: c.valor, vigenciaDesde: c.vigenciaDesde.slice(0, 10), vigenciaHasta: c.vigenciaHasta.slice(0, 10),
                        }); }}>Editar</button>
                        <button className="danger" onClick={() => eliminarCaea(c.idCaea)}>×</button>
                      </td>
                    </tr>
                  ))}
                  {caeas.length === 0 && <tr><td colSpan={6} className="muted">Sin CAEA cargados.</td></tr>}
                </tbody>
              </table>

              {formCaea ? (
                <div className="form-grid" style={{ marginTop: 10 }}>
                  <label>Año<input type="number" value={formCaea.anio}
                    onChange={(e) => setFormCaea({ ...formCaea, anio: Number(e.target.value) })} /></label>
                  <label>Mes<input type="number" min={1} max={12} value={formCaea.mes}
                    onChange={(e) => setFormCaea({ ...formCaea, mes: Number(e.target.value) })} /></label>
                  <label>Quincena
                    <select value={formCaea.orden} onChange={(e) => setFormCaea({ ...formCaea, orden: Number(e.target.value) })}>
                      <option value={1}>1 al 15</option>
                      <option value={2}>16 a fin de mes</option>
                    </select>
                  </label>
                  <label>Valor (CAEA)<input value={formCaea.valor}
                    onChange={(e) => setFormCaea({ ...formCaea, valor: e.target.value })} maxLength={14} /></label>
                  <label>Vigencia desde<input type="date" value={formCaea.vigenciaDesde}
                    onChange={(e) => setFormCaea({ ...formCaea, vigenciaDesde: e.target.value })} /></label>
                  <label>Vigencia hasta<input type="date" value={formCaea.vigenciaHasta}
                    onChange={(e) => setFormCaea({ ...formCaea, vigenciaHasta: e.target.value })} /></label>
                  <button className="primary"
                    disabled={!formCaea.valor.trim() || !formCaea.vigenciaDesde || !formCaea.vigenciaHasta}
                    onClick={guardarCaea}>Guardar</button>
                  <button onClick={() => { setFormCaea(null); setEditCaea(null); }}>Cancelar</button>
                </div>
              ) : (
                <button style={{ marginTop: 10 }} onClick={() => setFormCaea(caeaVacio(editEmpresa))}>+ Cargar CAEA</button>
              )}
            </div>
            </>
          )}
        </div>
      )}

      {formSucursal && (
        <div className="card form">
          <h3>{editSucursal ? "Editar sucursal" : "Nueva sucursal"}</h3>
          <div className="form-grid">
            <label>Empresa
              <select value={formSucursal.idEmpresa} onChange={(e) => setS({ idEmpresa: Number(e.target.value) })}>
                {empresas.map((e) => <option key={e.idEmpresa} value={e.idEmpresa}>{e.descripcion}</option>)}
              </select>
            </label>
            <label>Descripción<input value={formSucursal.descripcion} onChange={(e) => setS({ descripcion: e.target.value })} /></label>
            <label>Domicilio<input value={formSucursal.domicilio ?? ""} onChange={(e) => setS({ domicilio: e.target.value })} maxLength={120} /></label>
            <label>Localidad<input value={formSucursal.localidad ?? ""} onChange={(e) => setS({ localidad: e.target.value })} maxLength={60} /></label>
            <label>Provincia<input value={formSucursal.provincia ?? ""} onChange={(e) => setS({ provincia: e.target.value })} maxLength={60} /></label>
            <label>Código postal<input value={formSucursal.codigoPostal ?? ""} onChange={(e) => setS({ codigoPostal: e.target.value })} maxLength={8} /></label>
          </div>
          <div className="row-actions">
            <button className="primary" disabled={!formSucursal.descripcion.trim() || !formSucursal.idEmpresa}
              onClick={guardarSucursal}>Guardar</button>
            <button onClick={() => { setFormSucursal(null); setEditSucursal(null); }}>Cancelar</button>
          </div>
        </div>
      )}

      <h3>Empresas</h3>
      <table className="grid">
        <thead>
          <tr><th>Código</th><th>Razón social</th><th>CUIT</th><th>Datos fiscales</th><th>Domicilio</th><th></th></tr>
        </thead>
        <tbody>
          {empresas.map((e) => (
            <tr key={e.idEmpresa}>
              <td className="mono">{e.codigoInterno}</td>
              <td>{e.descripcion}</td>
              <td className="mono">{e.cuit ?? <span className="muted">—</span>}</td>
              <td className="stack">
                {e.condicionIva ?? <span className="muted">(sin condición IVA)</span>}
                <small>
                  {[e.ingresosBrutos ? `IIBB ${e.ingresosBrutos}` : null,
                    e.inicioActividad ? `Inicio ${soloFecha(e.inicioActividad)}` : null]
                    .filter(Boolean).join(" · ") || "—"}
                </small>
              </td>
              <td className="stack">
                {e.domicilio ?? <span className="muted">—</span>}
                <small>{[e.localidad, e.provincia].filter(Boolean).join(" - ")}</small>
              </td>
              <td className="row-actions">
                <button onClick={() => { setEditEmpresa(e.idEmpresa); setFormEmpresa(desdeEmpresa(e)); }}>Editar</button>
                <button className="danger" onClick={() => run(() => estructura.removeEmpresa(e.idEmpresa))}>Eliminar</button>
              </td>
            </tr>
          ))}
          {empresas.length === 0 && <tr><td colSpan={6} className="muted">Sin empresas.</td></tr>}
        </tbody>
      </table>

      <h3 style={{ marginTop: 18 }}>Sucursales</h3>
      <table className="grid">
        <thead>
          <tr><th>ID</th><th>Descripción</th><th>Empresa</th><th>Domicilio</th><th></th></tr>
        </thead>
        <tbody>
          {sucursales.map((s) => (
            <tr key={s.idSucursal}>
              <td className="mono">{s.idSucursal}</td>
              <td>{s.descripcion}</td>
              <td>{s.empresaDescripcion}</td>
              <td className="stack">
                {s.domicilio ?? <span className="muted">(usa el de la empresa)</span>}
                <small>{[s.localidad, s.provincia].filter(Boolean).join(" - ")}</small>
              </td>
              <td className="row-actions">
                <button onClick={() => { setEditSucursal(s.idSucursal); setFormSucursal(desdeSucursal(s)); }}>Editar</button>
                <button className="danger" onClick={() => run(() => estructura.removeSucursal(s.idSucursal))}>Eliminar</button>
              </td>
            </tr>
          ))}
          {sucursales.length === 0 && <tr><td colSpan={5} className="muted">Sin sucursales.</td></tr>}
        </tbody>
      </table>
    </div>
  );
}

// Igual que en Padrones/Caja: tapa la pantalla con blur + spinner mientras se espera una consulta
// lenta (acá, la prueba de conexión real contra ARCA — login WSAA + FEDummy puede tardar unos
// segundos). Reutiliza las clases globales de App.css (pantalla-bloqueada / …-caja / spinner).
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
