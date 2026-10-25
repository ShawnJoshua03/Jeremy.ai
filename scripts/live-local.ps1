<#
.SYNOPSIS
  One-command local live dashboard: SpacetimeDB + the jeremy-ai-live module + the dashboard dev server,
  plus `adb reverse` so a USB-connected Quest can publish telemetry without any firewall change.

.EXAMPLE
  .\scripts\live-local.ps1            # start everything, open http://localhost:5173/#live
  .\scripts\live-local.ps1 -Demo      # also run the fake headset (looping, with errors)
  .\scripts\live-local.ps1 -Lan       # listen on all interfaces so a Quest on Wi-Fi / other PCs can connect
  .\scripts\live-local.ps1 -Reset     # republish the module and wipe its live data (after schema changes)

  See dashboard/LOCAL.md.
#>
param(
  [switch]$Demo,
  [switch]$Lan,
  [switch]$Reset,
  [switch]$NoBrowser,
  [int]$Port = 3000,
  [string]$Database = "jeremy-ai-live"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dash = Join-Path $root "dashboard"
$base = "http://127.0.0.1:$Port"

function Say($text) { Write-Host "[live] $text" -ForegroundColor Cyan }
function Warn($text) { Write-Host "[live] $text" -ForegroundColor Yellow }

function Test-Url($url) {
  try { $null = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2; return $true } catch { return $false }
}

# ---- 1. SpacetimeDB CLI
$spacetime = Get-Command spacetime -ErrorAction SilentlyContinue
if (-not $spacetime) {
  $candidate = Join-Path $env:LOCALAPPDATA "SpacetimeDB\spacetime.exe"
  if (Test-Path $candidate) { $spacetime = Get-Item $candidate }
}
if (-not $spacetime) {
  Warn "SpacetimeDB CLI not found. Install it (one time), then rerun:"
  Write-Host "  iwr https://windows.spacetimedb.com -useb | iex"
  exit 1
}
$st = if ($spacetime -is [System.Management.Automation.CommandInfo]) { $spacetime.Source } else { $spacetime.FullName }

# ---- 2. SpacetimeDB server
if (Test-Url "$base/v1/ping") {
  Say "SpacetimeDB already running on $base"
} else {
  $listen = if ($Lan) { "0.0.0.0:$Port" } else { "127.0.0.1:$Port" }
  Say "Starting SpacetimeDB on $listen (minimised window; close it to stop)"
  Start-Process -FilePath $st -ArgumentList @("start", "--listen-addr", $listen, "--non-interactive") -WindowStyle Minimized
  $deadline = (Get-Date).AddSeconds(30)
  while (-not (Test-Url "$base/v1/ping")) {
    if ((Get-Date) -gt $deadline) { Warn "SpacetimeDB did not start on $base"; exit 1 }
    Start-Sleep -Milliseconds 500
  }
}

# ---- 3. Dependencies
foreach ($dir in @($dash, (Join-Path $dash "spacetimedb"))) {
  if (-not (Test-Path (Join-Path $dir "node_modules"))) {
    Say "npm install in $dir"
    Push-Location $dir; try { npm install --no-audit --no-fund | Out-Host } finally { Pop-Location }
  }
}

# ---- 4. Module (always against the LOCAL server, never maincloud)
Say "Publishing the module to $base as '$Database'"
$publishArgs = @("publish", "--server", $base, $Database, "--module-path", (Join-Path $dash "spacetimedb"), "--yes")
if ($Reset) { $publishArgs += "--delete-data" }
& $st @publishArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
  Warn "Publish failed. If the schema changed in a breaking way, rerun with -Reset (wipes live data only)."
  exit 1
}

# ---- 5. Headset over USB: adb reverse (no firewall change needed)
$adb = Get-Command adb -ErrorAction SilentlyContinue
if (-not $adb) {
  $sdk = Get-ChildItem -Path (Join-Path $env:USERPROFILE "UnityEditors") -Filter adb.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($sdk) { $adb = $sdk }
}
if ($adb) {
  $adbPath = if ($adb -is [System.Management.Automation.CommandInfo]) { $adb.Source } else { $adb.FullName }
  $devices = (& $adbPath devices) | Select-String -Pattern "\tdevice$"
  if ($devices) {
    & $adbPath reverse "tcp:$Port" "tcp:$Port" | Out-Null
    Say "Headset connected: adb reverse tcp:$Port -> this PC (rerun after replugging the cable)"
  } else {
    Warn "No headset on USB. Plug it in and run: adb reverse tcp:$Port tcp:$Port  (or use -Lan, see LOCAL.md)"
  }
} else {
  Warn "adb not found; skipping USB forwarding."
}

# ---- 6. Dashboard
if (Test-Url "http://localhost:5173/") {
  Say "Dashboard already running on http://localhost:5173"
} else {
  Say "Starting the dashboard (minimised window)"
  $npmArgs = if ($Lan) { "run live -- --host" } else { "run live" }
  Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "npm $npmArgs" -WorkingDirectory $dash -WindowStyle Minimized
  $deadline = (Get-Date).AddSeconds(40)
  while (-not (Test-Url "http://localhost:5173/")) {
    if ((Get-Date) -gt $deadline) { Warn "Dashboard did not start"; exit 1 }
    Start-Sleep -Milliseconds 500
  }
}

# ---- 7. Demo publisher
if ($Demo) {
  Say "Starting the fake headset (looping, with errors)"
  Start-Process -FilePath "node" -ArgumentList @("scripts/fake-headset.mjs", "--loop", "--sloppy", "--url", $base, "--db", $Database) -WorkingDirectory $dash -WindowStyle Minimized
}

$url = "http://localhost:5173/#live"
Say "Ready: $url"
if ($Lan) {
  $ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254*" } | Select-Object -First 1).IPAddress
  if ($ip) { Say "LAN: dashboard http://${ip}:5173/#live ; headset live-url.txt -> http://${ip}:$Port" }
}
if (-not $NoBrowser) { Start-Process $url }
