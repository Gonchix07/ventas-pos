<#
.SYNOPSIS
  Levanta el backend (Pos.Api) y el frontend (Vite) del sistema POS Mayorista con un solo comando.

.DESCRIPTION
  Corre "dotnet run --project src/Pos.Api" y "npm run dev" (carpeta frontend) cada uno en su
  propia ventana de PowerShell — así cada uno muestra su log en vivo tal cual lo mostraría
  corriéndolo a mano, sin mezclar la salida de los dos en una sola consola. Este script solo los
  lanza; cerrar cada ventana (o Ctrl+C adentro) para el proceso correspondiente.

  Si un puerto ya está en uso (por una corrida anterior que quedó viva, algo del propio Visual
  Studio/`dotnet watch`, etc.) NO lanza un segundo proceso pisándolo: avisa y sigue con el otro,
  para no terminar con dos "Pos.Api" compitiendo por el mismo puerto.

.PARAMETER Instalar
  Si `frontend\node_modules` no existe todavía (primera vez que se clona el repo en esta PC),
  corre "npm install" antes de levantar el frontend.

.PARAMETER SinNavegador
  No abrir el navegador solo al final. Por defecto sí lo abre (apunta a la URL del frontend),
  dándole unos segundos a Vite para que arranque.

.EXAMPLE
  .\scripts\iniciar-dev.ps1

.EXAMPLE
  .\scripts\iniciar-dev.ps1 -Instalar
#>
[CmdletBinding()]
param(
    [switch]$Instalar,
    [switch]$SinNavegador
)

$ErrorActionPreference = "Stop"

# Raíz del repo: este script vive en scripts\, la raíz es un nivel arriba — así funciona sin
# importar desde qué carpeta se lo invoque, mientras no se lo mueva de scripts\.
$raiz = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $raiz "src\Pos.Api"
$frontendDir = Join-Path $raiz "frontend"
$puertoApi = 5038
$puertoWeb = 5173

if (-not (Test-Path $apiDir)) {
    throw "No se encontró '$apiDir'. ¿Se movió este script fuera de scripts\ del repo?"
}
if (-not (Test-Path $frontendDir)) {
    throw "No se encontró '$frontendDir'. ¿Se movió este script fuera de scripts\ del repo?"
}

foreach ($cmd in @("dotnet", "npm")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$cmd' en el PATH de esta PC. Instalalo (.NET SDK / Node.js) antes de correr este script."
    }
}

if ($Instalar -or -not (Test-Path (Join-Path $frontendDir "node_modules"))) {
    Write-Host "Instalando dependencias del frontend (npm install)..." -ForegroundColor Yellow
    Push-Location $frontendDir
    try { npm install } finally { Pop-Location }
}

# Evita levantar un segundo proceso si el puerto ya está escuchando — no todo "en uso" es una
# corrida vieja de ESTE mismo repo (podría ser cualquier otra cosa), pero lanzar igual solo
# terminaría en un error de bind más confuso más abajo; mejor avisar acá y no arrancarlo.
function Puerto-Ocupado([int]$puerto) {
    $conexiones = Get-NetTCPConnection -LocalPort $puerto -State Listen -ErrorAction SilentlyContinue
    return $null -ne $conexiones
}

function Levantar-Ventana([string]$titulo, [string]$directorio, [string]$comando, [int]$puerto) {
    if (Puerto-Ocupado $puerto) {
        Write-Host "Puerto $puerto ya está en uso — asumo que '$titulo' ya está corriendo, no lo relanzo." -ForegroundColor Yellow
        return
    }
    Write-Host "Levantando $titulo (puerto $puerto)..." -ForegroundColor Cyan
    # El título de la ventana (no solo el Write-Host de adentro) es lo que permite reconocerla de
    # un vistazo en la barra de tareas / Alt-Tab, sin tener que abrir cada una para ver qué es.
    Start-Process powershell -ArgumentList @(
        "-NoExit",
        "-Command", "`$host.UI.RawUI.WindowTitle = 'POS - $titulo'; Set-Location '$directorio'; $comando"
    ) | Out-Null
}

Levantar-Ventana "Pos.Api (backend)" $apiDir "dotnet run" $puertoApi
Levantar-Ventana "Frontend (Vite)" $frontendDir "npm run dev" $puertoWeb

if (-not $SinNavegador) {
    Write-Host "Esperando a que el frontend levante para abrir el navegador..." -ForegroundColor Yellow
    $listo = $false
    for ($i = 0; $i -lt 30 -and -not $listo; $i++) {
        Start-Sleep -Seconds 1
        $listo = Puerto-Ocupado $puertoWeb
    }
    if ($listo) {
        Start-Process "http://localhost:$puertoWeb"
    } else {
        Write-Host "El frontend no respondió en 30s — revisá su ventana por si hubo un error." -ForegroundColor Yellow
    }
}

Write-Host "`nListo. Backend y frontend quedaron corriendo cada uno en su propia ventana." -ForegroundColor Green
Write-Host "Para cortarlos: cerrá esa ventana o Ctrl+C adentro (esta consola se puede cerrar sin afectarlos)." -ForegroundColor Green
