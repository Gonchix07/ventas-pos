// Identificador de esta PC de caja — reemplaza a la IP de origen como forma de resolver qué
// puesto/caja es al loguear (ver AuthController.ObtenerIdEquipo / PuestoCaja.IdentificadorEquipo).
// La IP deja de ser confiable en cuanto hay NAT/VPN/proxy entre la PC y el servidor (sucursales
// remotas, o saltos entre VLANs en la propia LAN); este GUID no depende de la red en absoluto.
//
// Se genera UNA sola vez y se persiste en localStorage. Como cada PC de caja abre la app con un
// perfil de Chrome dedicado y exclusivo (--user-data-dir propio, ver scripts/puesto-caja-kiosco.bat
// y docs/08-puesto-caja.md), ese localStorage es 1 a 1 con la PC física — no se mezcla con el
// Chrome "normal" que se use en esa misma máquina para otra cosa.
const PUESTO_ID_KEY = "pos.puestoId";

// crypto.randomUUID() exige "contexto seguro" (HTTPS o localhost) — las cajas reales entran por
// http://192.168.4.x:5173 (IP de LAN, HTTP plano, ver client.ts), así que ahí esa función ni
// existe (TypeError: crypto.randomUUID is not a function). crypto.getRandomValues() sí funciona
// sin HTTPS: se arma el UUID v4 a mano con eso. No hace falta que sea criptográficamente
// perfecto — solo tiene que no repetirse entre PCs, así que ni el detalle de versión/variant
// importa demasiado, pero se respeta el formato para que se vea como un GUID normal en el ABM.
function generarUuid(): string {
  if (typeof crypto !== "undefined" && crypto.getRandomValues) {
    const b = crypto.getRandomValues(new Uint8Array(16));
    b[6] = (b[6] & 0x0f) | 0x40; // versión 4
    b[8] = (b[8] & 0x3f) | 0x80; // variante RFC 4122
    const hex = Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }
  // Último recurso (navegador sin Web Crypto en absoluto): Math.random no es criptográfico, pero
  // alcanza para este uso — un identificador de PC, no una credencial.
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}

export function getPuestoId(): string {
  let id = localStorage.getItem(PUESTO_ID_KEY);
  if (!id) {
    id = generarUuid();
    localStorage.setItem(PUESTO_ID_KEY, id);
  }
  return id;
}
