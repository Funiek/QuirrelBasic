#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Config,
    [Parameter(Mandatory)][PSCredential]$Credential
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$configPath = (Resolve-Path -LiteralPath $Config).Path
if ($executablePath.Contains('"') -or $configPath.Contains('"')) { throw 'Invalid path.' }
if (Get-Service -Name QuirrelBasic -ErrorAction SilentlyContinue) { throw 'Service already exists. Uninstall it first.' }
& $executablePath validate $configPath
if ($LASTEXITCODE -ne 0) { throw 'Invalid configuration.' }
# Use the same Windows account as interactive OAuth; never default to LocalSystem.
New-Service -Name QuirrelBasic -DisplayName 'QuirrelBasic Google Drive Sync' -Description 'Two-way synchronization of configured files and folders.' -BinaryPathName ('"{0}" run "{1}"' -f $executablePath, $configPath) -StartupType Automatic -Credential $Credential
& sc.exe failure QuirrelBasic reset= 86400 actions= restart/60000/restart/60000/restart/60000
if ($LASTEXITCODE -ne 0) { throw 'Could not configure service recovery.' }
Write-Host 'Installed. Start-Service QuirrelBasic to start synchronization.'
