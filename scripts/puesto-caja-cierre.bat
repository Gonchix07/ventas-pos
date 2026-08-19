@echo off
rem ---------------------------------------------------------------------------
rem Acceso directo para CERRAR TURNO / imprimir la rendicion de cajero en la
rem MISMA PC de Caja, pero en un navegador NORMAL (sin --kiosk-printing): asi
rem window.print() vuelve a mostrar el dialogo de impresion, con seleccion de
rem impresora, en vez de imprimir directo a la predeterminada.
rem
rem Por que hace falta un .bat aparte (y no alcanza con abrir una pestana nueva
rem en el navegador de ventas): --kiosk-printing es un flag del PROCESO del
rem navegador, no de la pestana/pagina. Si Chrome/Edge ya esta corriendo en
rem modo kiosco (ver puesto-caja-kiosco.bat) y se lo vuelve a abrir con esta
rem misma instalacion, el navegador solo abre una pestana nueva DENTRO del
rem proceso que ya esta corriendo (con el flag puesto), ignorando cualquier
rem opcion nueva de la linea de comandos. Por eso --user-data-dir de abajo
rem apunta a un PERFIL DISTINTO: eso obliga a Chrome/Edge a levantar un
rem proceso nuevo y separado, con sus propios flags (sin --kiosk-printing).
rem
rem COMO USARLO:
rem   1. Editar la linea "set URL_CAJA=" con la URL real del puesto (misma que
rem      en puesto-caja-kiosco.bat).
rem   2. Crear un acceso directo a este .bat (ej. "Cerrar caja / Rendicion") en
rem      el escritorio del puesto, aparte del acceso directo normal de venta.
rem   3. Al llegar el momento de cerrar el turno, el cajero/supervisor usa ESTE
rem      acceso directo (no el de venta) para entrar a la caja y hacer el
rem      cierre: al confirmar y apretar "Imprimir rendicion" va a aparecer el
rem      dialogo de Windows para elegir la impresora de Tesoreria/oficina.
rem   4. Terminada la impresion se puede cerrar esta ventana; el navegador de
rem      venta (kiosco) sigue intacto en su propio proceso, sin verse afectado.
rem ---------------------------------------------------------------------------

set URL_CAJA=http://localhost:5173

rem Perfil separado (no el de "Usuario" normal ni el del kiosco) SOLO para que
rem el sistema operativo lo trate como un proceso nuevo. No comparte sesion ni
rem historial con el navegador de venta.
set PERFIL_CIERRE=%LOCALAPPDATA%\PosCierrePerfil

set CHROME="C:\Program Files\Google\Chrome\Application\chrome.exe"
set EDGE="C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

if exist %CHROME% (
    start "" %CHROME% --user-data-dir="%PERFIL_CIERRE%" --new-window %URL_CAJA%
) else (
    start "" %EDGE% --user-data-dir="%PERFIL_CIERRE%" --new-window %URL_CAJA%
)
