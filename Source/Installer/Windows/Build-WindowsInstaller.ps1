param(
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ProgramDirDefault = 'C:\twxproxy'
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Build-WindowsInstaller.ps1 must be run on Windows with the .NET MAUI Windows workload installed.'
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$payloadRoot = Join-Path $scriptRoot "artifacts\$Architecture\payload"
$programDirPayload = Join-Path $payloadRoot 'ProgramDir'
$mtcPayload = Join-Path $payloadRoot 'MTC'
$twxpPayload = Join-Path $payloadRoot 'TWXP'
$outputRoot = Join-Path $scriptRoot "artifacts\$Architecture"
$rid = "win-$Architecture"
$twxpRid = if ($Architecture -eq 'x64') { 'win10-x64' } else { 'win10-arm64' }
$wixProject = Join-Path $scriptRoot 'TWXWindowsInstaller.wixproj'

Write-Host "==> Cleaning payload for $Architecture"
Remove-Item $payloadRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $programDirPayload, $mtcPayload, $twxpPayload | Out-Null

Write-Host "==> Publishing MTC ($rid)"
dotnet publish (Join-Path $sourceRoot 'MTC\MTC.csproj') `
    -c $Configuration `
    -r $rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $mtcPayload

Write-Host "==> Publishing TWXC ($rid)"
dotnet publish (Join-Path $sourceRoot 'TWXC\TWXC.csproj') `
    -c $Configuration `
    -r $rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $programDirPayload

Write-Host "==> Publishing TWXD ($rid)"
dotnet publish (Join-Path $sourceRoot 'TWXD\TWXD.csproj') `
    -c $Configuration `
    -r $rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $programDirPayload

Write-Host "==> Publishing TWXP ($twxpRid)"
dotnet publish (Join-Path $sourceRoot 'TWXP\TWXP.csproj') `
    -f 'net10.0-windows10.0.19041.0' `
    -c $Configuration `
    -p:IncludeWindowsTarget=true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:RuntimeIdentifierOverride=$twxpRid `
    -o $twxpPayload

Write-Host "==> Building MSI ($Architecture)"
dotnet build $wixProject `
    -c $Configuration `
    -p:InstallerPlatform=$Architecture `
    -p:ProgramDirDefault="$ProgramDirDefault" `
    -p:PayloadRoot="$payloadRoot"

$builtMsi = Join-Path $scriptRoot "bin\$Configuration\$Architecture\TWXProxy-$Architecture.msi"
$finalMsi = Join-Path $outputRoot "TWXProxy-$Architecture.msi"

if (-not (Test-Path $builtMsi)) {
    throw "Installer build completed but MSI was not found at $builtMsi"
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
Copy-Item $builtMsi $finalMsi -Force

Write-Host ''
Write-Host "==> Done: $finalMsi"
