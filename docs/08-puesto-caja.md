# 08 — Puesto físico de Caja: impresión de tickets sin diálogo

La app de Caja es una SPA que corre en un navegador normal (no hay wrapper nativo tipo Electron).
Los tickets no fiscales (Retiro de efectivo, Presupuesto, comprobante de Vale, ficha del módulo
Clientes) se imprimen con `window.print()` contra la impresora "comandera" configurada en Windows —
ver `frontend/src/modules/caja/comprobante-print.css`. El comprobante fiscal (Factura A/B) sale por
el controlador Hasar, no por acá.

Por defecto, `window.print()` siempre abre el diálogo de impresión / vista previa del navegador:
no hay forma de saltearlo desde JavaScript. Para que la comandera imprima directo (sin que el
cajero tenga que confirmar nada) hay que lanzar el navegador en modo **kiosco de impresión**.

## Cómo configurarlo

1. En la PC del puesto de Caja, configurar la impresora de tickets como impresora **predeterminada**
   de Windows (kiosk-printing siempre imprime a la predeterminada, no deja elegir).
2. Usar `scripts/puesto-caja-kiosco.bat` (editar `URL_CAJA` con la URL real del puesto) en vez de
   abrir Chrome/Edge a mano. Ese script lanza el navegador con `--kiosk-printing`.
3. Crear un acceso directo a ese `.bat` en el escritorio / inicio del puesto, y usarlo para abrir
   la app en vez del ícono normal del navegador.
4. Probar con un Retiro de efectivo o un cobro con medio "VALE" (con "Imprime comprobante"
   tildado) y confirmar que el ticket sale solo, sin ningún diálogo en pantalla.

## Si el ticket sigue saliendo con diálogo ("no funciona el kiosco")

Causa más común (confirmada en la práctica, 2026-08-25): si Chrome **ya estaba corriendo**
(cualquier ventana, mismo usuario de Windows) en el momento de lanzar el acceso directo, Chrome no
arranca un proceso nuevo con `--kiosk-printing` — manda la ventana nueva al proceso viejo que ya
estaba abierto sin ese flag, y el diálogo de impresión sigue apareciendo aunque la ventana se vea
igual (modo kiosco visual, pero sin el efecto de impresión silenciosa). Por eso
`scripts/puesto-caja-kiosco.bat` ahora fuerza `--user-data-dir` con un perfil propio
(`%LOCALAPPDATA%\PosMayorista\ChromeKiosco`), que garantiza un proceso nuevo de verdad sin importar
qué otras ventanas de Chrome del perfil normal estén abiertas al mismo tiempo.

Si después de este cambio el problema persiste: cerrar TODOS los `chrome.exe` desde el
Administrador de tareas (no solo las ventanas) y volver a abrir con el `.bat`, y confirmar que la
impresora de tickets sea la predeterminada de Windows.

## Ojo con el ancho del ticket

El CSS de `comprobante-print.css` compensa hoy un desajuste de ancho que se detectó imprimiendo a
mano con Escala 130 % en el diálogo (ver el comentario ahí). Con `--kiosk-printing` no hay diálogo
ni escala manual: conviene volver a probar el ancho real en la impresora del puesto y, si sale
distinto, ajustar el `@page` de los tickets (`retiro-ticket` / `presupuesto-ticket` /
`vale-ticket`) en ese mismo archivo.

## Rendición de cajero (cierre de turno): PDF real, no `window.print()`

La rendición A4 (`ReporteCierreTurno.tsx`) es distinta de los tickets: se imprime para firmar y
presentar en Tesorería, así que conviene poder **elegir la impresora** (oficina/Tesorería) en vez
de que salga directo a la comandera. `--kiosk-printing` es un flag del **proceso completo** del
navegador, no de una página — con `window.print()` no hay forma de que muestre el diálogo en una
sola pestaña mientras el resto sigue imprimiendo silencioso.

**Solución actual (2026-08-25)**: el botón "Imprimir rendición" ya no llama a `window.print()` —
genera un PDF real con `@react-pdf/renderer` y lo abre en una pestaña nueva (ver `RendicionPdf.tsx`,
mismo mecanismo ya usado para las Etiquetas en `EtiquetaPdf.tsx`). Como no pasa por el pipeline de
impresión del navegador en absoluto, el flag `--kiosk-printing` no lo afecta: el cajero/Tesorería
abre ese PDF cuando quiera (inclusive en otra PC) e imprime eligiendo la impresora a mano, desde el
visor de PDF que sea.

Con esto, el acceso directo separado `scripts/puesto-caja-cierre.bat` (segundo proceso de Chrome sin
`--kiosk-printing`, pensado solo para esta pantalla) queda obsoleto — ya no hace falta abrir un
navegador aparte para cerrar el turno. Se deja el script en el repo por si en el futuro hiciera
falta el mismo truco para otra pantalla, pero no forma parte del flujo normal de cierre.
