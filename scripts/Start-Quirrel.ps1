[CmdletBinding()]
param(
    [string]$Config = (Join-Path $PSScriptRoot '../drives_config.json')
)
$ErrorActionPreference = 'Stop'
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../QuirrelBasic.exe'))
$configPath = (Resolve-Path -LiteralPath $Config).Path
if (!(Test-Path -LiteralPath $executable)) { throw 'Run this script from the scripts folder in the published package.' }
$running = @(Get-Process -Name QuirrelBasic -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable })
if ($running.Count -gt 0) { Write-Host 'QuirrelBasic is already running.'; return }
$process = Start-Process -FilePath $executable -ArgumentList @('run', ('"{0}"' -f $configPath)) -WorkingDirectory (Split-Path $executable) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 2
if ($process.HasExited) { throw 'QuirrelBasic stopped. Run Start-Quirrel.cmd with the same config path to see the error.' }
Write-Host ('QuirrelBasic is running in the background. PID: ' + $process.Id)
