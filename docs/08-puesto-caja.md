# 08 — Puesto físico de Caja: impresión de tickets sin diálogo

La app de Caja es una SPA que corre en un navegador normal (no hay wrapper nativo tipo Electron).
Los tickets no fiscales (Retiro de efectivo, Presupuesto, comprobante de Vale) se imprimen con
`window.print()` contra la impresora "comandera" configurada en Windows — ver
`frontend/src/modules/caja/comprobante-print.css`. El comprobante fiscal (Factura A/B) sale por el
controlador Hasar, no por acá.

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

## Ojo con el ancho del ticket

El CSS de `comprobante-print.css` compensa hoy un desajuste de ancho que se detectó imprimiendo a
mano con Escala 130 % en el diálogo (ver el comentario ahí). Con `--kiosk-printing` no hay diálogo
ni escala manual: conviene volver a probar el ancho real en la impresora del puesto y, si sale
distinto, ajustar el `@page` de los tickets (`retiro-ticket` / `presupuesto-ticket` /
`vale-ticket`) en ese mismo archivo.

## Rendición de cajero (cierre de turno): necesita el diálogo de impresión

La rendición A4 (`ReporteCierreTurno.tsx` / `cierre-print.css`) es distinta de los tickets: se
imprime para firmar y presentar en Tesorería, así que conviene poder **elegir la impresora**
(oficina/Tesorería) en vez de que salga directo a la comandera. Pero `--kiosk-printing` es un flag
del **proceso completo** del navegador, no de una página — no hay forma de que `window.print()`
muestre el diálogo en una sola pestaña mientras el resto sigue imprimiendo silencioso.

Solución: un segundo acceso directo, `scripts/puesto-caja-cierre.bat`, que abre la misma app en un
proceso de navegador **separado** (perfil distinto, sin `--kiosk-printing`). Ahí `window.print()`
vuelve a comportarse normal: aparece el diálogo de Windows con selección de impresora. El cajero
usa el acceso directo normal (kiosco) para vender, y este otro solo para cerrar el turno e imprimir
la rendición — no afecta ni interfiere con el navegador de venta, que sigue corriendo en su propio
proceso.
