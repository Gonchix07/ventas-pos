import { useEffect, useState } from "react";
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

type Estado = "esperando" | "buscando" | "encontrado" | "error";

/**
 * Módulo "Verificar Precios": kiosco de autoconsulta de cara al cliente, mismo patrón que el
 * módulo "Clientes" (ver ClientesPage.tsx) — pantalla de solo-escaneo, sin campo de búsqueda
 * manual, con `useLectorCodigo` captando la lectura a nivel de documento. Muestra imagen +
 * descripción + precio de las listas AZUL/ROJA en paralelo (no el precio "ganador" de una venta
 * real, ver VerificarPreciosService en el backend) y un sticker si el producto está en oferta o en
 * Lista Folder.
 *
 * Después de mostrar un resultado (o un error) vuelve solo a la pantalla de espera a los 20s —
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
      setError(e instanceof Error ? e.message : "No se pudo consultar el precio.");
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
    if (estado !== "encontrado" && estado !== "error") return;
    const t = setTimeout(limpiar, 20000);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [estado, producto]);

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
            {estado === "esperando" && "INICIANDO…"}
            {estado === "buscando" && "BUSCANDO…"}
            {estado === "encontrado" && producto?.descripcion.toUpperCase()}
            {estado === "error" && "PRODUCTO NO ENCONTRADO"}
          </div>
          <div className="vp-panel-contenido">
            {estado === "esperando" && (
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
                    {tieneOferta && <span className="vp-sticker vp-sticker--oferta">OFERTA</span>}
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
