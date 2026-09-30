[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function New-Check {
    param(
        [string]$Id,
        [string]$Category,
        [string]$Status,
        [string]$Severity,
        [string]$Summary,
        [string]$Recommendation,
        [bool]$AutoFixable = $false
    )
    [pscustomobject]@{
        Id = $Id
        Category = $Category
        Status = $Status
        Severity = $Severity
        Summary = $Summary
        Evidence = @($Summary)
        Recommendation = $Recommendation
        AutoFixable = $AutoFixable
    }
}

$checks = [System.Collections.Generic.List[object]]::new()
$package = Get-AppxPackage -Name 'OpenAI.Codex' | Sort-Object Version -Descending | Select-Object -First 1

if ($null -eq $package) {
    $checks.Add((New-Check 'PACKAGE_NOT_FOUND' 'package' 'ManualAction' 'Error' `
        'OpenAI.Codex Microsoft Store package was not found.' 'Install the official app from Microsoft Store.'))
    [pscustomobject]@{ SchemaVersion = 1; Status = 'ManualAction'; Checks = $checks } | ConvertTo-Json -Depth 8
    exit 3
}

$packageOk = $package.Status.ToString() -eq 'Ok'
$checks.Add((New-Check 'PACKAGE_STATUS' 'package' $(if ($packageOk) { 'Healthy' } else { 'ManualAction' }) `
    $(if ($packageOk) { 'Info' } else { 'Error' }) `
    ("{0} {1}, status {2}." -f $package.Name, $package.Version, $package.Status) `
    'Repair or reinstall the Store app if its status is not Ok.'))

$codexPath = Join-Path $package.InstallLocation 'app\resources\codex.exe'
$runtimePath = Join-Path $package.InstallLocation 'app\resources\cua_node'
$codexExists = Test-Path -LiteralPath $codexPath -PathType Leaf
$runtimeExists = Test-Path -LiteralPath $runtimePath -PathType Container
$checks.Add((New-Check 'CODEX_EXE' 'files' $(if ($codexExists) { 'Healthy' } else { 'ManualAction' }) `
    $(if ($codexExists) { 'Info' } else { 'Error' }) $codexPath 'Repair or reinstall the Store app.'))
$checks.Add((New-Check 'RUNTIME_SOURCE' 'runtime' $(if ($runtimeExists) { 'Healthy' } else { 'ManualAction' }) `
    $(if ($runtimeExists) { 'Info' } else { 'Error' }) $runtimePath 'Repair or reinstall the Store app.'))

$userCli = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'User')
$machineCli = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH', 'Machine')
$cli = if ([string]::IsNullOrWhiteSpace($userCli)) { $machineCli } else { $userCli }
$cliOk = [string]::IsNullOrWhiteSpace($cli) -or (Test-Path -LiteralPath $cli -PathType Leaf)
$cliSummary = if ([string]::IsNullOrWhiteSpace($cli)) { 'CODEX_CLI_PATH is not set.' } else { "CODEX_CLI_PATH=$cli" }
$cliAdvice = if ($cliOk) { 'No action is required.' } else { 'Confirm before clearing the invalid user-level variable.' }
$checks.Add((New-Check 'CODEX_CLI_PATH' 'environment' $(if ($cliOk) { 'Healthy' } else { 'Repairable' }) `
    'Warning' $cliSummary $cliAdvice (-not $cliOk)))

$status = if ($checks.Status -contains 'ManualAction') { 'ManualAction' } elseif ($checks.Status -contains 'Repairable') { 'Repairable' } else { 'Healthy' }
[pscustomobject]@{
    SchemaVersion = 1
    Timestamp = [DateTimeOffset]::Now
    Status = $status
    Package = [pscustomobject]@{ Name = $package.Name; Version = $package.Version; InstallLocation = $package.InstallLocation; PackageFamilyName = $package.PackageFamilyName }
    Checks = $checks
} | ConvertTo-Json -Depth 8
if ($status -eq 'Healthy') { exit 0 } elseif ($status -eq 'Repairable') { exit 2 } else { exit 3 }
