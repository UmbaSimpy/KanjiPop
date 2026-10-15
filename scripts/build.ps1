# Build a portable KanjiPop folder + zip in ./dist
# Usage: powershell -File scripts/build.ps1   (needs Python 3 and the .NET SDK)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (-not (Test-Path data/dict.db)) { python tools/build_dict.py }
$out = Join-Path $root "dist/KanjiPop"
if (Test-Path $out) { [System.IO.Directory]::Delete($out, $true) }
dotnet publish src/KanjiPop/KanjiPop.csproj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
New-Item -ItemType Directory -Force "$out/data" | Out-Null
Copy-Item data/dict.db "$out/data/dict.db" -Force
Copy-Item README.md, NOTICE.md $out -Force
Get-ChildItem "$out/*.pdb" -ErrorAction SilentlyContinue | ForEach-Object { [System.IO.File]::Delete($_.FullName) }
Compress-Archive -Path "$out/*" -DestinationPath (Join-Path $root "dist/KanjiPop-win-x64.zip") -Force
Write-Host "Done: dist/KanjiPop-win-x64.zip"
