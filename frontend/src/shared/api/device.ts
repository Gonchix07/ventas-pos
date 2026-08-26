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

export function getPuestoId(): string {
  let id = localStorage.getItem(PUESTO_ID_KEY);
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem(PUESTO_ID_KEY, id);
  }
  return id;
}
