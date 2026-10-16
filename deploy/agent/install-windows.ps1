# Installerar ProjectTime Agent som en Windows-tjänst som startar automatiskt.
# Kör i en PowerShell med administratörsrättigheter, från katalogen med den publicerade agenten:
#   .\install-windows.ps1 -ServerUrl "https://tid.example.com" -ApiKey "pt_..." -DeviceName "Kontorsdator"
#
# OBS: En Windows-tjänst körs i session 0 och kan inte se användarens fönster.
# Därför registreras agenten istället som en schemalagd uppgift som startar vid inloggning
# för den inloggade användaren – det är så fönsterspårning måste fungera på Windows.
param(
    [Parameter(Mandatory=$true)][string]$ServerUrl,
    [Parameter(Mandatory=$true)][string]$ApiKey,
    [string]$DeviceName = $env:COMPUTERNAME,
    [string]$InstallDir = "$env:LOCALAPPDATA\ProjectTime\bin"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path "$PSScriptRoot\*" -Destination $InstallDir -Recurse -Force

$settings = Join-Path $InstallDir "appsettings.json"
$json = Get-Content $settings -Raw | ConvertFrom-Json
$json.Agent.ServerUrl = $ServerUrl
$json.Agent.ApiKey = $ApiKey
$json.Agent.DeviceName = $DeviceName
$json | ConvertTo-Json -Depth 5 | Set-Content $settings -Encoding UTF8

$exe = Join-Path $InstallDir "projecttime-agent.exe"
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settingsTask = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -Hidden
Register-ScheduledTask -TaskName "ProjectTime Agent" -Action $action -Trigger $trigger -Settings $settingsTask -RunLevel Limited -Force | Out-Null
Start-ScheduledTask -TaskName "ProjectTime Agent"

Write-Host "ProjectTime Agent installerad i $InstallDir och startad. Den startar automatiskt vid inloggning."
Write-Host "Ta bort med: Unregister-ScheduledTask -TaskName 'ProjectTime Agent' -Confirm:`$false"
