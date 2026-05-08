$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "TitanAILivePC.sln"
$appProj = Join-Path $root "App.Wpf\App.Wpf.csproj"
$serverProj = Join-Path $root "TitanCameraServer\TitanCameraServer.csproj"
$outRoot = Join-Path $root "_build\release"
$appOut = Join-Path $outRoot "App.Wpf"
$serverOut = Join-Path $outRoot "TitanCameraServer"

Write-Host "== Titan AI Live: single release build ==" -ForegroundColor Cyan
Write-Host "Output root: $outRoot" -ForegroundColor Yellow

if (Test-Path $outRoot) {
    Remove-Item -Path $outRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $appOut -Force | Out-Null
New-Item -ItemType Directory -Path $serverOut -Force | Out-Null

dotnet clean $solution -c Release
dotnet publish $appProj -c Release -o $appOut
dotnet publish $serverProj -c Release -o $serverOut

$exePath = Join-Path $appOut "App.Wpf.exe"
if (-not (Test-Path $exePath)) {
    throw "Missing App.Wpf.exe at: $exePath"
}

Write-Host ""
Write-Host "Build OK. Test ONLY from this folder:" -ForegroundColor Green
Write-Host "  $appOut" -ForegroundColor Green
Write-Host ""

Start-Process -FilePath $exePath
Write-Host "App launched: $exePath" -ForegroundColor Green
