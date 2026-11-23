# Downloads the FFmpeg build Klementina ships with into vendor\ffmpeg.
# ffmpeg.exe is not kept in git because it is larger than GitHub's 100 MB file limit.
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File tools\get-ffmpeg.ps1
$ErrorActionPreference = 'Stop'
$version = '9.0.2'
$url = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$version-essentials_build.zip"
$sha256 = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'vendor\ffmpeg'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("klementina-ffmpeg-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force $tmp, $dest | Out-Null
try {
    $zip = Join-Path $tmp 'ffmpeg.zip'
    Write-Host "Downloading FFmpeg $version ..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $sha256) { throw "Checksum mismatch. Expected $sha256, got $actual" }
    Expand-Archive $zip -DestinationPath $tmp
    $bin = Get-ChildItem $tmp -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    Copy-Item $bin.FullName $dest -Force
    $license = Get-ChildItem $tmp -Recurse -Filter LICENSE* | Select-Object -First 1
    if ($license) { Copy-Item $license.FullName (Join-Path $dest 'LICENSE.txt') -Force }
    Write-Host "Done: $dest\ffmpeg.exe"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
