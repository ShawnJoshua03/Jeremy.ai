param(
    [string]$InstalledProject = 'C:/Users/shawn/Jeremy.ai/quest',
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Unity -ErrorAction SilentlyContinue) {
    Write-Output 'Unity is busy; narrator compile and play-mode checks not verified.'
    exit 2
}
$scriptsRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$verifyRoot = Join-Path $PSScriptRoot 'NarratorUnityProject~'
$assets = Join-Path $verifyRoot 'Assets/Scripts'
New-Item -ItemType Directory -Force -Path $assets, (Join-Path $verifyRoot 'Packages'), (Join-Path $verifyRoot 'ProjectSettings') | Out-Null
foreach ($folder in Get-ChildItem -LiteralPath $scriptsRoot -Directory) {
    if (!$folder.Name.EndsWith('~')) { Copy-Item -LiteralPath $folder.FullName -Destination $assets -Recurse -Force }
}
Copy-Item -LiteralPath (Join-Path $scriptsRoot 'Jeremy.Runtime.asmdef') -Destination $assets -Force
$cache = Join-Path $InstalledProject 'Library/PackageCache'
$dependencies = [ordered]@{}
function Add-CachedPackage([string]$name) {
    if ($dependencies.Contains($name)) { return }
    if ($name.StartsWith('com.unity.modules.')) { $dependencies[$name] = '1.0.0'; return }
    $package = Get-ChildItem -LiteralPath $cache -Directory -Filter ($name + '@*') | Select-Object -First 1
    if (!$package) { throw "Missing cached package $name in $cache" }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
    $definition = Get-Content -LiteralPath (Join-Path $package.FullName 'package.json') -Raw | ConvertFrom-Json
    foreach ($dependency in $definition.dependencies.PSObject.Properties) { Add-CachedPackage $dependency.Name }
}
foreach ($name in @('com.unity.inputsystem', 'com.unity.ugui', 'com.meta.xr.sdk.core', 'com.unity.xr.openxr')) { Add-CachedPackage $name }
$projectManifest = Get-Content -LiteralPath (Join-Path $scriptsRoot '../../Packages/manifest.json') -Raw | ConvertFrom-Json
foreach ($dependency in $projectManifest.dependencies.PSObject.Properties) {
    if ($dependency.Name.StartsWith('com.unity.modules.')) { $dependencies[$dependency.Name] = $dependency.Value }
}
$manifest = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $verifyRoot 'Packages/manifest.json'), $manifest, (New-Object Text.UTF8Encoding $false))
'm_EditorVersion: 6000.6.7f1' | Set-Content (Join-Path $verifyRoot 'ProjectSettings/ProjectVersion.txt')
$log = Join-Path $PSScriptRoot 'narrator-verification.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $verifyRoot + '"'), '-executeMethod', 'Jeremy.Tests.RobotNarratorVerificationEntry.Run', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$process.WaitForExit(600000)) { $process.Kill(); throw 'Isolated narrator verification exceeded ten minutes.' }
Get-Content -LiteralPath $log | Select-String -Pattern 'error CS|warning CS|^JEREMY NARRATOR|Compilation failed|Aborting batchmode|Exception:'
exit $process.ExitCode
