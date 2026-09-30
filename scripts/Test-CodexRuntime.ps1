[CmdletBinding()]
param(
    [string]$RuntimeRoot = (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\runtimes\cua_node')
)

$ErrorActionPreference = 'Stop'
$directories = if (Test-Path -LiteralPath $RuntimeRoot -PathType Container) {
    Get-ChildItem -LiteralPath $RuntimeRoot -Directory
} else { @() }
[pscustomobject]@{
    Id = 'RUNTIME_DIRECTORIES'
    Root = $RuntimeRoot
    Exists = Test-Path -LiteralPath $RuntimeRoot -PathType Container
    Staging = @($directories | Where-Object Name -like '.staging-*' | Select-Object -ExpandProperty FullName)
    Candidates = @($directories | Where-Object Name -notlike '.staging-*' | Where-Object Name -notlike '.selfcheck-*' | Select-Object -ExpandProperty FullName)
} | ConvertTo-Json -Depth 5
