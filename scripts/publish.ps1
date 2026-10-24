<#
배포 파일 만들기: dist\HotzoneConverter\ 폴더와 dist\HotzoneConverter-win-x64.zip
- self-contained 폴더 배포 (단일 파일로 묶지 않음: LGPL DLL과 FFmpeg를 사용자가 교체할 수 있어야 한다)
- FFmpeg LGPL 빌드를 tools\ffmpeg\ 에, 라이선스 고지를 licenses\ 에 함께 넣는다
사용: powershell -File scripts\publish.ps1 [-FfmpegDir <ffmpeg.exe가 있는 폴더>]
#>
param(
    [string]$FfmpegDir = "$env:LOCALAPPDATA\HotzoneConverter\ffmpeg",
    [string]$Out = "dist"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$ffmpeg = Join-Path $FfmpegDir "ffmpeg.exe"
if (-not (Test-Path $ffmpeg)) { throw "FFmpeg를 찾을 수 없습니다: $ffmpeg (LGPL 빌드를 받아 -FfmpegDir로 지정하세요)" }
# GPL 빌드가 섞여 들어가지 않게 확인
$conf = & $ffmpeg -hide_banner -buildconf
if ($conf -match "enable-gpl|enable-nonfree") { throw "GPL/nonfree FFmpeg 빌드입니다. LGPL 빌드만 배포할 수 있습니다." }

dotnet test -c Release
if ($LASTEXITCODE -ne 0) { throw "테스트 실패" }

$app = Join-Path $Out "HotzoneConverter"
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
dotnet publish src/Hotzone.App -c Release -r win-x64 --self-contained true -o $app
if ($LASTEXITCODE -ne 0) { throw "publish 실패" }

$tools = New-Item -ItemType Directory -Force (Join-Path $app "tools\ffmpeg")
Copy-Item $ffmpeg $tools
$licenses = New-Item -ItemType Directory -Force (Join-Path $app "licenses")
Copy-Item THIRD_PARTY_NOTICES.md $app
if (Test-Path (Join-Path $FfmpegDir "LICENSE.txt")) { Copy-Item (Join-Path $FfmpegDir "LICENSE.txt") (Join-Path $licenses "FFmpeg-LICENSE.txt") }
$vipsNotice = Get-ChildItem "$env:USERPROFILE\.nuget\packages\netvips.native.win-x64" -Recurse -Filter THIRD-PARTY-NOTICES.md | Select-Object -First 1
if ($vipsNotice) { Copy-Item $vipsNotice.FullName (Join-Path $licenses "libvips-THIRD-PARTY-NOTICES.md") }
# win-x64 외의 네이티브 런타임은 필요 없다
Get-ChildItem (Join-Path $app "runtimes") -Directory -ErrorAction SilentlyContinue | Where-Object Name -ne "win-x64" | Remove-Item -Recurse -Force

$zip = Join-Path $Out "HotzoneConverter-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$app\*" -DestinationPath $zip
"완료: $zip ({0:N1} MB)" -f ((Get-Item $zip).Length / 1MB)
