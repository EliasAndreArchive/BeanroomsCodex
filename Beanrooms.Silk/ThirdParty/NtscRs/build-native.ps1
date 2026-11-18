$ErrorActionPreference = 'Stop'
$previousTarget = $env:CARGO_TARGET_DIR
# Keep linker temporary paths below Windows' legacy path-length limit.
if (-not $env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR = Join-Path ([System.IO.Path]::GetTempPath()) 'beanrooms-ntsc-native' }
Push-Location (Join-Path $PSScriptRoot 'native')
try {
    & cargo build --release --locked --target x86_64-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw 'Native NTSC build failed.' }
    Copy-Item -LiteralPath (Join-Path $env:CARGO_TARGET_DIR 'x86_64-pc-windows-msvc/release/beanrooms_ntsc.dll') -Destination (Join-Path $PSScriptRoot 'beanrooms_ntsc.dll') -Force
}
finally { Pop-Location; $env:CARGO_TARGET_DIR = $previousTarget }
