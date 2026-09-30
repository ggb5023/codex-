[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$user = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'User')
$machine = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'Machine')
$current = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'Process')
$selected = if (-not [string]::IsNullOrWhiteSpace($user)) { $user } elseif (-not [string]::IsNullOrWhiteSpace($machine)) { $machine } else { $current }
$valid = [string]::IsNullOrWhiteSpace($selected) -or (Test-Path -LiteralPath $selected -PathType Leaf)
[pscustomobject]@{
    Id = 'CODEX_CLI_PATH'
    User = $user
    Machine = $machine
    Process = $current
    Selected = $selected
    Valid = $valid
    Status = if ($valid) { 'Healthy' } else { 'Repairable' }
} | ConvertTo-Json -Depth 5
