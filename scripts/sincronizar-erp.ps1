<#
.SYNOPSIS
  Corre una sincronización completa contra el ERP Central (Pos.ErpSync).

.DESCRIPTION
  Compila y ejecuta Pos.ErpSync una vez. Por defecto fuerza una corrida COMPLETA: ignora el gate
  de frecuencia (ErpSync:FrecuenciaMinutos) para que las 5 fuentes (Lookups, Articulos,
  Presentaciones, CodBarras, Clientes) corran ya mismo en vez de saltearse por "todavía no toca" —
  usar -RespetarFrecuencia si en cambio se quiere el comportamiento normal (saltear las fuentes que
  ya corrieron hace poco).

  Las cadenas de conexión (ConnectionStrings__Pos / ConnectionStrings__Erp) NUNCA se hardcodean acá
  — tienen que estar puestas como variables de entorno de la máquina (o de usuario) antes de correr
  este script, igual que en Pos.Api. Si falta alguna, el script corta con un mensaje claro en vez de
  dejar que Pos.ErpSync arranque contra una conexión vacía.

  Pensado tanto para correrlo a mano como para programarlo en el Task Scheduler de Windows: la
  acción del Task Scheduler sería "powershell.exe" con argumentos
  "-File C:\ruta\al\repo\scripts\sincronizar-erp.ps1", y las variables de entorno se configuran una
  sola vez a nivel de Sistema (Panel de Control > Variables de entorno), no en el script.

.PARAMETER RespetarFrecuencia
  No fuerza la corrida: cada fuente se saltea si no pasó ErpSync:FrecuenciaMinutos desde su última
  corrida (comportamiento normal de Pos.ErpSync, pensado para dispararse seguido desde una tarea
  programada). Sin este switch, el script siempre fuerza una corrida completa.

.PARAMETER LoteSize
  Filas por lote (ErpSync:LoteSize). Sin especificar, usa el default de appsettings.json (500).

.PARAMETER MaxLotesPorFuente
  Tope de lotes por fuente en esta corrida (ErpSync:MaxLotesPorFuente). Útil para acotar una prueba
  puntual; NO usar en el bootstrap inicial de una fuente dependiente (Presentaciones/CodBarras)
  antes de que su fuente padre (Articulos) haya terminado — puede saltear filas para siempre (ver
  memoria del proyecto pos-mayorista-erp-sync). Sin especificar, sin tope.

.PARAMETER SinBuild
  No compila antes de correr — asume que Pos.ErpSync ya está compilado en Debug. Más rápido para
  corridas repetidas mientras no cambió el código.

.EXAMPLE
  .\scripts\sincronizar-erp.ps1
  Corrida completa forzada, con build.

.EXAMPLE
  .\scripts\sincronizar-erp.ps1 -RespetarFrecuencia -SinBuild
  Modo "normal" (respeta el gate de frecuencia), sin recompilar — así es como lo dispararía una
  tarea programada frecuente.

.EXAMPLE
  .\scripts\sincronizar-erp.ps1 -MaxLotesPorFuente 5
  Corrida acotada de prueba (hasta 5 lotes por fuente).
#>
[CmdletBinding()]
param(
    [switch]$RespetarFrecuencia,
    [int]$LoteSize,
    [int]$MaxLotesPorFuente,
    [switch]$SinBuild
)

$ErrorActionPreference = "Stop"

# Raíz del repo: este script vive en scripts\, la raíz es un nivel arriba — así funciona sin
# importar desde qué carpeta se lo invoque, mientras no se lo mueva de scripts\ (mismo criterio que
# iniciar-dev.ps1).
$raiz = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $raiz "src\Pos.ErpSync"

if (-not (Test-Path $proyecto)) {
    throw "No se encontró '$proyecto'. ¿Se movió este script fuera de scripts\ del repo?"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "No se encontró 'dotnet' en el PATH de esta PC. Instalá el .NET SDK antes de correr este script."
}

# Fail-fast igual que Pos.Api/Program.cs: mejor cortar acá con un mensaje claro que dejar que
# Pos.ErpSync arranque contra una cadena de conexión vacía y falle recién adentro del primer query.
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Pos)) {
    throw "ConnectionStrings__Pos no está configurada como variable de entorno. Configurala (nivel Sistema o Usuario, nunca en este script) antes de correr la sincronización."
}
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Erp)) {
    throw "ConnectionStrings__Erp no está configurada como variable de entorno. Configurala (nivel Sistema o Usuario, nunca en este script) antes de correr la sincronización."
}

if (-not $SinBuild) {
    Write-Host "Compilando Pos.ErpSync..." -ForegroundColor Cyan
    dotnet build $proyecto --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Falló la compilación de Pos.ErpSync (código $LASTEXITCODE). Revisá la salida de arriba."
    }
}

# Se pasan como variables de entorno del proceso hijo (dotnet run), no como argumentos de línea de
# comando: es el mismo mecanismo de configuración que ya usa Pos.ErpSync (ver appsettings.json +
# ErpSyncDependencyInjection.AddErpSync), así no hace falta duplicar ninguna lógica de parseo acá.
if (-not $RespetarFrecuencia) {
    $env:ErpSync__FrecuenciaMinutos = "0"
    Write-Host "Forzando corrida completa (frecuencia ignorada)." -ForegroundColor Yellow
}
if ($PSBoundParameters.ContainsKey('LoteSize')) { $env:ErpSync__LoteSize = "$LoteSize" }
if ($PSBoundParameters.ContainsKey('MaxLotesPorFuente')) { $env:ErpSync__MaxLotesPorFuente = "$MaxLotesPorFuente" }

Write-Host "Iniciando sincronización con el ERP..." -ForegroundColor Cyan
Push-Location $proyecto
try {
    dotnet run --no-build -c Debug
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

# Limpieza: que estas variables no queden pisadas en la sesión de PowerShell del usuario para
# corridas manuales futuras que sí quieran el comportamiento normal.
Remove-Item Env:\ErpSync__FrecuenciaMinutos -ErrorAction SilentlyContinue
Remove-Item Env:\ErpSync__LoteSize -ErrorAction SilentlyContinue
Remove-Item Env:\ErpSync__MaxLotesPorFuente -ErrorAction SilentlyContinue

if ($exitCode -ne 0) {
    Write-Host "La sincronización terminó con error (código $exitCode). Revisá los logs en '$proyecto\logs\'." -ForegroundColor Red
    exit $exitCode
}

Write-Host "Sincronización terminada OK. Logs en '$proyecto\logs\'." -ForegroundColor Green
