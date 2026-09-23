#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name QuirrelBasic -ErrorAction Stop
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name QuirrelBasic
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}
& sc.exe delete QuirrelBasic
if ($LASTEXITCODE -ne 0) { throw 'Could not remove service.' }
Write-Host 'Service removed. Configuration, tokens and backups retained.'
