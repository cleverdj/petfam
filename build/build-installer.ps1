<#
.SYNOPSIS
  Builds the Windows downloads: PetFam-Setup-<version>.exe (installer) and PetFam-<version>-portable.zip.

.DESCRIPTION
  1. Publishes PetFam as one self-contained PetFam.exe (no .NET install needed on the user's PC).
  2. Compiles installer\PetFam.iss with Inno Setup. ISCC.exe is taken from INNO_ISCC, an installed Inno Setup 6,
     or downloaded once into .tools\ from the Tools.InnoSetup NuGet package (nothing is installed on the machine).
  3. Zips the published app as a portable download.
  Everything lands in artifacts\.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
  powershell -ExecutionPolicy Bypass -File build\build-installer.ps1 -Version 1.2.0
#>
param(
    [string]$Version = "",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
Write-Host "PetFam $Version ($Runtime)"

$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish\$Runtime"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force $publish | Out-Null

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

& $dotnet publish (Join-Path $root "src\PetFam.App\PetFam.App.csproj") -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Inno Setup compiler.
$iscc = $env:INNO_ISCC
if (-not $iscc) {
    foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) {
    $tools = Join-Path $root ".tools\innosetup"
    $iscc = Join-Path $tools "tools\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        Write-Host "Downloading the Inno Setup compiler (Tools.InnoSetup 6.7.3 from nuget.org)..."
        New-Item -ItemType Directory -Force $tools | Out-Null
        $pkg = Join-Path $tools "innosetup.zip"
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.7.3/tools.innosetup.6.7.3.nupkg" -OutFile $pkg
        Expand-Archive $pkg -DestinationPath $tools -Force
    }
}

& $iscc "/DAppVersion=$Version" (Join-Path $root "installer\PetFam.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$zip = Join-Path $artifacts "PetFam-$Version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip

Write-Host ""
Write-Host "Done:"
Get-ChildItem $artifacts -File | ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
