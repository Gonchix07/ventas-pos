import { useEffect, useState } from "react";
import {
  estructura, type CertificadoCae, type Empresa, type EmpresaInput, type Sucursal, type SucursalInput,
} from "../../shared/api/admin";

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

  // Al editar una empresa existente se consulta el estado actual del certificado (nunca la clave).
  useEffect(() => {
    if (editEmpresa == null) { setCertificado(null); return; }
    setArchivoCert(null); setClaveCert(""); setArchivoClavePrivada(null); setArchivoCertificado(null); setPassphraseClave("");
    estructura.certificado(editEmpresa).then(setCertificado).catch(() => setCertificado(null));
  }, [editEmpresa]);

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
