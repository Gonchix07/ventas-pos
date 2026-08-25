import { useEffect, useRef, useState } from "react";
import { clientesModulo, type ClienteTicket } from "../../shared/api/clientes";
import { parseDniQr } from "../../shared/ui/dni";
import { TicketCliente } from "./TicketCliente";

/**
 * Módulo "Clientes": pantalla de solo-escaneo. No hay campo de búsqueda manual — se espera
 * exclusivamente el QR del DNI (el lector escribe como si fuera un teclado, igual que en Caja). El
 * dato crudo escaneado nunca se muestra en pantalla: el input que lo recibe queda fuera de la vista
 * (mismo truco de posición que los tickets no visibles, ver .cbte--sinPantalla) y solo existe para
 * capturar el foco del teclado del lector.
 *
 * Al detectar una lectura se abre un popup con spinner ("Buscando…") y, al resolver, la lista de
 * cuentas encontradas: la propia del DNI (Titular) y toda cuenta donde ese DNI esté autorizado a
 * comprar (Autorizado) — un mismo documento puede traer más de una fila.
 */
export function ClientesPage() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [texto, setTexto] = useState("");
  const [popup, setPopup] = useState(false);
  const [buscando, setBuscando] = useState(false);
  const [resultados, setResultados] = useState<ClienteTicket[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [aImprimir, setAImprimir] = useState<ClienteTicket | null>(null);

  // Pantalla de cara al cliente: en vez de usuario/IP/Módulos/Salir (de uso interno del cajero),
  // el header muestra fecha y hora en vivo.
  const [ahora, setAhora] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setAhora(new Date()), 1000);
    return () => clearInterval(t);
  }, []);
  const fecha = ahora.toLocaleDateString("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" });
  const hora = ahora.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false });

  // El foco vuelve solo al campo de escaneo apenas queda libre (al abrir la pantalla, cerrar el
  // popup o terminar de imprimir) — el lector necesita que el foco esté siempre ahí, nadie hace
  // clic para "activarlo" como si fuera un campo de texto normal.
  useEffect(() => {
    if (!popup && !aImprimir) inputRef.current?.focus();
  }, [popup, aImprimir]);

  // Apenas llega el primer caracter (el lector empieza a "tipear" el QR) se abre el popup con el
  // spinner YA — no se espera a que termine de escanear ni a que arranque el pedido al backend.
  // Antes el spinner solo se prendía dentro de "procesar" (al presionar Enter), y como la búsqueda
  // en LAN es casi instantánea, esa ventana podía no llegar a pintarse en pantalla — a efectos
  // prácticos, "no se veía" el spinner.
  const onEntrada = (valor: string) => {
    setTexto(valor);
    if (valor.length > 0 && !popup) {
      setPopup(true);
      setBuscando(true);
      setResultados(null);
      setError(null);
    }
  };

  const procesar = async (crudo: string) => {
    setTexto("");
    const qr = parseDniQr(crudo);
    const dni = qr?.documento ?? (/^\d+$/.test(crudo.trim()) ? crudo.trim() : null);

    setPopup(true);
    setBuscando(true);
    setResultados(null);
    setError(null);

    if (!dni) {
      setBuscando(false);
      setError("No se pudo leer el DNI escaneado. Volvé a escanear el QR del documento.");
      return;
    }

    try {
      setResultados(await clientesModulo.buscarPorDni(dni));
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo buscar.");
    } finally {
      setBuscando(false);
    }
  };

  const cerrarPopup = () => {
    setPopup(false);
    setResultados(null);
    setError(null);
  };

  if (aImprimir) {
    return <TicketCliente cliente={aImprimir} onImpreso={() => { setAImprimir(null); cerrarPopup(); }} />;
  }

  return (
    <div className="app-shell" style={{ background: "#fff", minHeight: "100vh" }}>
      <header className="app-header">
        <div className="brand"><span className="brand-mark">POS</span><span className="brand-sub">Mayorista</span></div>
        <div className="user-box">
          <span className="mono">{fecha} · {hora}</span>
        </div>
      </header>
      <main className="app-main">
        <div className="card" style={{ textAlign: "center", padding: "16px 24px 48px" }}>
          <img src="/LogoHergo.png" alt="Hergo — El Mayorista de Mar del Plata" style={{ maxWidth: 473, marginBottom: 16 }} />
          <h1 style={{ marginTop: 0, fontSize: 34 }}>¡Bienvenido!</h1>
          <p className="muted" style={{ fontSize: 22 }}>Si te olvidaste la Tarjeta, escanea el QR de tu DNI aquí</p>
          <img src="/barcode-scan.gif" alt="Escaneando código de barras" style={{ maxWidth: 260, marginTop: 12 }} />
        </div>

        {/* Nunca visible: solo capta el teclado del lector. Ver nota de arriba. */}
        <input
          ref={inputRef}
          value={texto}
          style={{ position: "absolute", left: -9999, top: 0, opacity: 0 }}
          onChange={(e) => onEntrada(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && procesar(e.currentTarget.value)}
        />

        {popup && (
          // El fondo siempre cierra al hacer clic, incluso durante "buscando": el spinner ahora se
          // prende apenas empieza a llegar la lectura (ver onEntrada), así que puede quedar
          // esperando el Enter final del lector — si algo interrumpe el escaneo, no hay que dejar
          // al cajero sin forma de salir del popup.
          <div className="modal-fondo" onClick={cerrarPopup}>
            <div className="modal-caja" style={{ width: "min(728px, 100%)" }} onClick={(e) => e.stopPropagation()}>
              {buscando && (
                <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 14, padding: "24px 0" }}>
                  <div className="spinner" aria-hidden="true" />
                  <p style={{ margin: 0 }}>Buscando…</p>
                </div>
              )}
              {!buscando && error && (
                <>
                  <p className="error">{error}</p>
                  <div className="cbte__acciones"><button className="primary" onClick={cerrarPopup}>Cerrar</button></div>
                </>
              )}
              {!buscando && !error && resultados && (
                <>
                  <h3>Cuentas encontradas</h3>
                  <table className="grid">
                    <thead>
                      <tr><th>Código</th><th>Nombre</th><th>Tipo de Tarjeta</th><th>Cuenta</th><th /></tr>
                    </thead>
                    <tbody>
                      {resultados.map((c) => (
                        <tr key={`${c.idCliente}-${c.origen}`}>
                          <td className="mono">{c.codigoInt}</td>
                          <td>{c.descripcion}</td>
                          <td>{c.tipoTarjeta ?? "—"}</td>
                          <td>{c.origen === "Titular" ? "Titular" : "Autorizado"}</td>
                          <td><button onClick={() => setAImprimir(c)}>Imprimir ticket</button></td>
                        </tr>
                      ))}
                      {resultados.length === 0 && (
                        <tr><td colSpan={5} className="muted">No se encontró ningún cliente con ese DNI.</td></tr>
                      )}
                    </tbody>
                  </table>
                  <div className="cbte__acciones"><button onClick={cerrarPopup}>Cerrar</button></div>
                </>
              )}
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
