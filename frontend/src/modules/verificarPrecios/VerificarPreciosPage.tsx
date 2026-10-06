import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../../shared/auth/auth";
import { useLectorCodigo } from "../../shared/ui/useLectorCodigo";
import { formatearMoneda } from "../../shared/ui/moneda";
import { verificarPrecios, type ConsultaPrecio, type PrecioLista } from "../../shared/api/verificarPrecios";

// Listas que siempre se muestran, en este orden — mismos nombres que usa el backend
// (VerificarPreciosService.ListasMostradas) y que los badges de Caja (lista-azul/lista-roja en
// App.css). Placeholder antes de escanear nada: dos tarjetas vacías, como en la pantalla de espera.
const LISTAS_PLACEHOLDER: PrecioLista[] = [
  { codigoLista: "AZUL", precio: null },
  { codigoLista: "ROJA", precio: null },
];

function tituloLista(codigo: string): string {
  return `Tarjeta ${codigo.charAt(0)}${codigo.slice(1).toLowerCase()}`;
}

type Estado = "esperando" | "buscando" | "encontrado" | "error" | "video";

// Pasados los 20s de mostrado un escaneo, el kiosco reproduce en loop los videos institucionales
// de esta carpeta (listado de directorio IIS, con CORS abierto) hasta el próximo escaneo.
const SEGUNDOS_HASTA_VIDEO = 20;
const URL_VIDEOS = "https://portal.hergo.com.ar:8099/Imagenes/Videos/HERGO/";

async function listarVideos(): Promise<string[]> {
  const html = await (await fetch(URL_VIDEOS)).text();
  const doc = new DOMParser().parseFromString(html, "text/html");
  return Array.from(doc.querySelectorAll("a"))
    .map((a) => a.getAttribute("href") ?? "")
    .filter((h) => /\.(mp4|webm|ogg|mov)$/i.test(h))
    .map((h) => new URL(h, URL_VIDEOS).href);
}

/**
 * Módulo "Verificar Precios": kiosco de autoconsulta de cara al cliente, mismo patrón que el
 * módulo "Clientes" (ver ClientesPage.tsx) — pantalla de solo-escaneo, sin campo de búsqueda
 * manual, con `useLectorCodigo` captando la lectura a nivel de documento. Muestra imagen +
 * descripción + precio de las listas AZUL/ROJA en paralelo (no el precio "ganador" de una venta
 * real, ver VerificarPreciosService en el backend) y un sticker si el producto está en oferta o en
 * Lista Folder.
 *
 * Después de mostrar un resultado (o un error) vuelve solo a la pantalla de espera a los 20s pasa a reproducir videos —
 * es un kiosco sin nadie mirando la pantalla la mayor parte del tiempo, no debe quedar trabado
 * mostrando el último producto escaneado por el cliente anterior.
 */
export function VerificarPreciosPage() {
  const navigate = useNavigate();
  const { idSucursal: idSucursalAuth, isLoading: sesionCargando } = useAuth();

  const [estado, setEstado] = useState<Estado>("esperando");
  const [producto, setProducto] = useState<ConsultaPrecio | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [ahora, setAhora] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setAhora(new Date()), 1000);
    return () => clearInterval(t);
  }, []);
  const fecha = ahora.toLocaleDateString("es-AR", { weekday: "long", day: "2-digit", month: "long" });
  const fechaCapitalizada = fecha.charAt(0).toUpperCase() + fecha.slice(1);
  const hora = ahora.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit", hour12: false });

  const limpiar = () => { setEstado("esperando"); setProducto(null); setError(null); };

  const buscar = async (codigo: string) => {
    if (idSucursalAuth === null) return; // no debería poder dispararse — ver guard de abajo
    setEstado("buscando");
    setProducto(null);
    setError(null);
    try {
      setProducto(await verificarPrecios.consultar(idSucursalAuth, codigo));
      setEstado("encontrado");
    } catch (e) {
      setError(`${e instanceof Error ? e.message : "No se pudo consultar el precio."} (código leído: ${codigo})`);
      setEstado("error");
    }
  };

  // Activo solo con sucursal resuelta: los precios son por sucursal (listas AZUL/ROJA propias de
  // cada una), así que sin esto un escaneo en un equipo sin puesto vinculado — o disparado antes de
  // que /auth/me termine de rehidratar la sesión — mostraría precios de OTRA sucursal en vez de
  // frenar (ver guard de "Puesto no autorizado" más abajo, mismo criterio que CajaPage).
  useLectorCodigo({ activo: !sesionCargando && idSucursalAuth !== null, onCodigo: (c) => void buscar(c) });

  // Auto-reset: nadie "cierra" esta pantalla a mano, así que el kiosco tiene que volver solo a
  // esperar el próximo escaneo después de mostrarle el resultado al cliente un rato.
  useEffect(() => {
    if (estado !== "esperando" && estado !== "encontrado" && estado !== "error") return;
    const t = setTimeout(() => { setEstado("video"); setProducto(null); setError(null); }, SEGUNDOS_HASTA_VIDEO * 1000);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [estado, producto]);

  const [videos, setVideos] = useState<string[]>([]);
  const [videoIdx, setVideoIdx] = useState(0);
  const errores = useRef(0); // videos seguidos que fallaron: si fallan todos, se vuelve a la espera (no pantalla negra)
  useEffect(() => {
    if (estado !== "video" || videos.length > 0) return;
    listarVideos().then(setVideos).catch(() => setEstado("esperando"));
  }, [estado, videos.length]);
  useEffect(() => { if (estado === "video") setVideoIdx(0); }, [estado]);

  const listas = producto?.precios ?? LISTAS_PLACEHOLDER;
  const tieneOferta = !!producto && producto.ofertas.length > 0;

  // Esta PC no está vinculada a ningún puesto (ver ABM Estructura de caja > Puestos): sin sucursal
  // resuelta no hay forma correcta de saber qué listas de precio mostrar — mismo criterio que
  // CajaPage.tsx, no se ofrece ningún fallback "a ciegas".
  if (!sesionCargando && idSucursalAuth === null) {
    return (
      <div className="vp-shell">
        <header className="vp-header">
          <div className="vp-header-left">
            <button className="vp-icon-btn" onClick={() => navigate("/")} aria-label="Volver al menú">‹</button>
            <div>
              <span className="vp-brand-badge">HERGO</span>
              <h1>Consulta de precios</h1>
            </div>
          </div>
        </header>
        <main className="vp-body vp-body--centrado">
          <section className="vp-panel-principal">
            <div className="vp-panel-titulo vp-panel-titulo--error">PUESTO NO AUTORIZADO</div>
            <div className="vp-panel-contenido">
              <p className="vp-mensaje">
                Esta PC todavía no está vinculada a ningún puesto de caja. Andá a Administración &gt;
                Asignación de cajas y usá "Vincular este equipo" parado frente a esta PC.
              </p>
            </div>
          </section>
        </main>
      </div>
    );
  }

  if (estado === "video" && videos.length > 0) {
    return (
      <div className="vp-shell" onClick={limpiar} style={{ background: "#000" }}>
        <video
          key={videos[videoIdx % videos.length]}
          src={videos[videoIdx % videos.length]}
          autoPlay muted playsInline
          onEnded={() => { errores.current = 0; setVideoIdx((i) => (i + 1) % videos.length); }}
          onError={() => { if (errores.current + 1 >= videos.length) { errores.current = 0; setVideos([]); limpiar(); } else { errores.current += 1; setVideoIdx((i) => (i + 1) % videos.length); } }}
          style={{ width: "100%", height: "100vh", objectFit: "contain" }}
        />
      </div>
    );
  }

  return (
    <div className="vp-shell">
      <header className="vp-header">
        <div className="vp-header-left">
          <button className="vp-icon-btn" onClick={() => navigate("/")} aria-label="Volver al menú">‹</button>
          <div>
            <span className="vp-brand-badge">HERGO</span>
            <h1>Consulta de precios</h1>
          </div>
        </div>
        <div className="vp-header-right">
          <button className="vp-icon-btn" onClick={limpiar} title="Reiniciar" aria-label="Reiniciar">↻</button>
          <span className="vp-fecha">{fechaCapitalizada} {hora}</span>
        </div>
      </header>

      <main className="vp-body">
        <section className="vp-panel-principal">
          <div className={`vp-panel-titulo${estado === "error" ? " vp-panel-titulo--error" : ""}`}>
            {(estado === "esperando" || estado === "video") && "INICIANDO…"}
            {estado === "buscando" && "BUSCANDO…"}
            {estado === "encontrado" && producto?.descripcion.toUpperCase()}
            {estado === "error" && "PRODUCTO NO ENCONTRADO"}
          </div>
          <div className="vp-panel-contenido">
            {(estado === "esperando" || estado === "video") && (
              <>
                <img src="/barcode-scan.gif" alt="Escaneando código de barras" className="vp-gif-espera" />
                <p className="vp-mensaje">Escaneé un producto para ver la imagen</p>
              </>
            )}
            {estado === "buscando" && <div className="spinner" aria-hidden="true" />}
            {estado === "error" && <p className="vp-mensaje">{error}</p>}
            {estado === "encontrado" && producto && (
              <>
                <img src={producto.imagenUrl} alt={producto.descripcion} className="vp-imagen" />
                {(producto.esListaFolder || tieneOferta) && (
                  <div className="vp-stickers">
                    {producto.esListaFolder && <span className="vp-sticker vp-sticker--folder">LISTA FOLDER</span>}
                    {/* Una por oferta: puede tener más de una vigente a la vez (ver AplicarOfertasAsync,
                        Acumula) — cada una con su nombre real, no un genérico "OFERTA" sin identificar cuál. */}
                    {producto.ofertas.map((o) => (
                      <span key={o.idOferta} className="vp-sticker vp-sticker--oferta">OFERTA: {o.descripcion}</span>
                    ))}
                  </div>
                )}
              </>
            )}
          </div>
        </section>

        <aside className="vp-panel-listas">
          {listas.map((l) => (
            <div key={l.codigoLista} className={`vp-panel-lista vp-panel-lista--${l.codigoLista.toLowerCase()}`}>
              <div className="vp-panel-lista-titulo">{tituloLista(l.codigoLista)}</div>
              <div className="vp-panel-lista-precio">
                {l.precio === null ? <span className="vp-precio-vacio">—</span> : formatearMoneda(l.precio)}
              </div>
            </div>
          ))}
        </aside>
      </main>
    </div>
  );
}
