$ErrorActionPreference = 'Stop'
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../QuirrelBasic.exe'))
$running = @(Get-Process -Name QuirrelBasic -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable })
foreach ($process in $running) { Stop-Process -Id $process.Id -ErrorAction Stop }
Write-Host 'QuirrelBasic stopped. An interrupted transfer will be retried on the next start.'
