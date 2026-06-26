<#
.SYNOPSIS
    Registers ServerRemote.Service.exe (located in the same folder as this script) as a Windows service.
.DESCRIPTION
    Must be run from an ELEVATED PowerShell (as Administrator).
    Compiles NOTHING — ServerRemote.Service.exe must already sit next to this script.
.EXAMPLE
    .\install-service.ps1
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "ServerRemoteService"
)

$ErrorActionPreference = "Stop"

# Check for administrator rights
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) { throw "Please run as Administrator." }

# The exe lives in the same directory as this script
$exePath = Join-Path $PSScriptRoot "ServerRemote.Service.exe"
if (-not (Test-Path $exePath)) { throw "ServerRemote.Service.exe not found in: $PSScriptRoot" }

# Stop/remove an existing service
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping and removing existing service ..."
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Registering service $ServiceName ..."
sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto obj= LocalSystem DisplayName= "ServerRemote Service" | Out-Null
sc.exe description $ServiceName "ServerRemote — server remote management (API + metrics + service control)" | Out-Null

Write-Host "Starting service ..."
sc.exe start $ServiceName | Out-Null

Write-Host "Done. Status:" -ForegroundColor Green
Get-Service -Name $ServiceName
