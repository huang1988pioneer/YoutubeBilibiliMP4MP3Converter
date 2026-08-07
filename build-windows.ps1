param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$output = Join-Path $PSScriptRoot "publish\$Runtime"

dotnet publish `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Windows build created at: $output"
Write-Host "Run: $(Join-Path $output 'YoutubeOrBilibiliMP3Converter.exe')"
