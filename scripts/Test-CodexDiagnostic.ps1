[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Get-CodexDiagnostic.ps1'
$raw = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath
$report = $raw | ConvertFrom-Json

if ($report.SchemaVersion -ne 1) { throw 'SchemaVersion must be 1.' }
if ($null -eq $report.Checks) { throw 'Checks must be present.' }
foreach ($check in $report.Checks) {
    foreach ($property in @('Id', 'Category', 'Status', 'Severity', 'Summary', 'Recommendation', 'AutoFixable')) {
        if ($null -eq $check.$property) { throw "Check property missing: $property" }
    }
}
Write-Output ("PASS: {0} checks, status {1}." -f $report.Checks.Count, $report.Status)
