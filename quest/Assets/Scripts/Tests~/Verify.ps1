param(
    [string]$InstalledProject = 'C:/Users/shawn/Jeremy.ai/quest',
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe',
    [string]$BackendUrl = ''
)
$ErrorActionPreference = 'Stop'
$scriptsRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$verifyRoot = Join-Path $PSScriptRoot 'UnityProject~'
$assets = Join-Path $verifyRoot 'Assets/Scripts'
New-Item -ItemType Directory -Force -Path $assets, (Join-Path $verifyRoot 'Packages'), (Join-Path $verifyRoot 'ProjectSettings') | Out-Null
foreach ($folder in @('Core', 'Exercises', 'Interaction', 'Tracking', 'Networking', 'Feedback', 'Mentorship', 'Tests')) {
    Copy-Item -LiteralPath (Join-Path $scriptsRoot $folder) -Destination $assets -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $scriptsRoot 'Jeremy.Runtime.asmdef') -Destination $assets -Force
$cache = Join-Path $InstalledProject 'Library/PackageCache'
$inputPackage = Get-ChildItem -LiteralPath $cache -Directory -Filter 'com.unity.inputsystem@*' | Select-Object -First 1
$uiPackage = Get-ChildItem -LiteralPath $cache -Directory -Filter 'com.unity.ugui@*' | Select-Object -First 1
if (!$inputPackage -or !$uiPackage) { throw 'Installed Input System and uGUI package sources are required.' }
$dependencies = [ordered]@{
    'com.unity.inputsystem' = 'file:' + $inputPackage.FullName.Replace('\', '/')
    'com.unity.ugui' = 'file:' + $uiPackage.FullName.Replace('\', '/')
}
foreach ($module in @('audio','physics','physics2d','ui','imgui','uielements','jsonserialize','unitywebrequest','unitywebrequestaudio','animation','xr')) {
    $dependencies['com.unity.modules.' + $module] = '1.0.0'
}
$manifest = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $verifyRoot 'Packages/manifest.json'), $manifest, (New-Object System.Text.UTF8Encoding $false))
'm_EditorVersion: 6000.6.7f1' | Set-Content (Join-Path $verifyRoot 'ProjectSettings/ProjectVersion.txt')
$env:JEREMY_VERIFY_URL = $BackendUrl
$log = Join-Path $PSScriptRoot 'verification.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $verifyRoot + '"'), '-executeMethod', 'Jeremy.Tests.VerificationEntry.Run', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -WindowStyle Hidden
$process.WaitForExit()
Get-Content -LiteralPath $log | Select-String -Pattern 'error CS|error:|JEREMY|Compilation failed|Aborting batchmode|Exception:'
exit $process.ExitCode
