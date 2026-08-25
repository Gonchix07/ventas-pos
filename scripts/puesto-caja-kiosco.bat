@echo off
rem ---------------------------------------------------------------------------
rem Acceso directo para el puesto de Caja: abre la app en modo kiosco de
rem impresion. Con --kiosk-printing, window.print() imprime DIRECTO contra la
rem impresora predeterminada de Windows, sin mostrar el dialogo de impresion
rem ni la vista previa (asi se pueden imprimir solos los tickets de Retiro,
rem Presupuesto y el comprobante de Vale, sin que el cajero tenga que
rem confirmar nada).
rem
rem COMO USARLO:
rem   1. Editar la linea "set URL_CAJA=" de abajo con la URL real del puesto.
rem   2. Verificar que la impresora de tickets (comandera) este configurada
rem      como impresora PREDETERMINADA de Windows en esta PC (kiosk-printing
rem      imprime siempre a la predeterminada, no deja elegir).
rem   3. Crear un acceso directo a este .bat (o llamarlo desde el que ya se
rem      use para abrir la caja) y usar ESE acceso directo en vez de abrir
rem      Chrome/Edge a mano.
rem   4. Probar: hacer un Retiro de efectivo o un cobro con VALE y confirmar
rem      que el ticket sale sin ningun dialogo en pantalla.
rem
rem OJO - CAUSA MAS COMUN DE QUE "NO FUNCIONE": si Chrome YA estaba abierto
rem (cualquier ventana, mismo usuario de Windows) cuando se lanza este .bat,
rem Chrome NO arranca un proceso nuevo con estos flags: manda la ventana al
rem proceso viejo que ya estaba corriendo (sin --kiosk-printing), y el flag
rem queda sin efecto aunque la ventana se vea igual — sigue apareciendo el
rem dialogo de impresion. Por eso este .bat fuerza --user-data-dir con un
rem perfil PROPIO: asi Chrome arranca un proceso nuevo de verdad, sin importar
rem que otras ventanas de Chrome (del perfil normal del usuario) esten
rem abiertas al mismo tiempo. Si el ticket sigue sin salir directo, cerrar
rem TODOS los chrome.exe desde el Administrador de tareas y volver a probar.
rem
rem OJO CON EL ANCHO DEL TICKET: el CSS de comprobante-print.css compensa hoy
rem un desajuste de ancho que se detecto imprimiendo A MANO con Escala 130%
rem en el dialogo (ver comentario en ese archivo). Con --kiosk-printing ya no
rem hay dialogo ni escala manual: HAY QUE VOLVER A PROBAR el ancho real en
rem esta impresora y, si sale distinto, ajustar el @page de los tickets
rem (retiro-ticket / presupuesto-ticket / vale-ticket) en
rem frontend/src/modules/caja/comprobante-print.css.
rem ---------------------------------------------------------------------------

set URL_CAJA=http://localhost:5173

rem Usa Chrome si esta instalado; si no, Edge. Ajustar la ruta si la instalacion
rem esta en otro lado.
set CHROME="C:\Program Files\Google\Chrome\Application\chrome.exe"
set EDGE="C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

rem Perfil propio y aislado (no el default del usuario) — ver nota de arriba: es lo que
rem garantiza que --kiosk-printing arranque en un proceso nuevo de verdad.
set PERFIL_KIOSCO=%LOCALAPPDATA%\PosMayorista\ChromeKiosco

if exist %CHROME% (
    start "" %CHROME% --kiosk-printing --user-data-dir="%PERFIL_KIOSCO%" --app=%URL_CAJA%
) else (
    start "" %EDGE% --kiosk-printing --user-data-dir="%PERFIL_KIOSCO%" --app=%URL_CAJA%
)
