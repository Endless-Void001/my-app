$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'src\VoidsMacroProject\VoidsMacroProject.csproj'
$Output = Join-Path $Root 'dist'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun this script from Windows PowerShell.'
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
Write-Host 'Publishing Voids Macro Project for Windows x64...' -ForegroundColor Cyan
dotnet publish $Project --configuration Release --runtime win-x64 --self-contained true --output $Output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Build complete: $(Join-Path $Output 'VoidsMacroProject.exe')" -ForegroundColor Green
