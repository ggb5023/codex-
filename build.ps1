param(
    [string]$Configuration = 'Release',
    [string]$Output = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
$localDotnet = Join-Path $env:LOCALAPPDATA 'Programs\dotnet-sdk-8\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

& $dotnet publish (Join-Path $PSScriptRoot 'CodexSelfCheck.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $Output

$exe = Join-Path $Output 'CodexSelfCheck.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "发布失败：没有生成 $exe"
}

$hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  CodexSelfCheck.exe" |
    Set-Content -LiteralPath (Join-Path $Output 'CodexSelfCheck.exe.sha256') -Encoding ascii
Write-Host "Published: $exe"
