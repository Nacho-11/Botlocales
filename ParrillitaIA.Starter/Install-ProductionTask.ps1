$ErrorActionPreference = "Stop"

$TaskName   = "Parrillita IA Agent - SAN PEDRO"
$Root       = "C:\Botlocales\ParrillitaIA.Starter"
$TrainerCsproj = Join-Path $Root "src\ParrillitaIA.Trainer\ParrillitaIA.Trainer.csproj"
$AgentCsproj   = Join-Path $Root "src\ParrillitaIA.Agent\ParrillitaIA.Agent.csproj"
$TrainerPublish = Join-Path $Root "publish\TrainerProduction"
$AgentPublish   = Join-Path $Root "publish\AgentProduction"
$Launcher       = Join-Path $Root "Run-Agent-Hidden.ps1"

Write-Host "=== PARRILLITA IA - INSTALACION PRODUCCION V3 ==="
Write-Host ""

if (-not (Test-Path $Launcher)) {
    throw "Falta $Launcher. Copie Run-Agent-Hidden.ps1 a la raiz del proyecto antes de ejecutar este instalador."
}

Write-Host "=== DETENIENDO TAREA EXISTENTE SI ESTA ACTIVA ==="
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    try {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    } catch {
    }
}

Write-Host "=== PUBLICANDO TRAINER PRODUCTIVO ==="
dotnet publish $TrainerCsproj -c Release -o $TrainerPublish
if ($LASTEXITCODE -ne 0) {
    throw "Falló dotnet publish del Trainer."
}

Write-Host "=== PUBLICANDO AGENT PRODUCTIVO ==="
dotnet publish $AgentCsproj -c Release -o $AgentPublish
if ($LASTEXITCODE -ne 0) {
    throw "Falló dotnet publish del Agent."
}

$agentExe = Join-Path $AgentPublish "ParrillitaIA.Agent.exe"
if (-not (Test-Path $agentExe)) {
    throw "No se encontró el ejecutable publicado del Agent: $agentExe"
}

$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$powershellExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

# Se inicia PowerShell oculto, pero dentro de la sesión interactiva del usuario.
$actionArgs = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$Launcher`""

$action = New-ScheduledTaskAction `
    -Execute $powershellExe `
    -Argument $actionArgs `
    -WorkingDirectory $Root

# La automatización de UI necesita una sesión interactiva.
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser

$principal = New-ScheduledTaskPrincipal `
    -UserId $currentUser `
    -LogonType Interactive `
    -RunLevel Highest

# Reinicio automático si el proceso termina inesperadamente.
# StartWhenAvailable permite recuperar el arranque cuando Windows no pudo ejecutar el trigger en el momento previsto.
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "Parrillita IA Agent SAN PEDRO. Se ejecuta al iniciar sesion en modo oculto y mantiene la automatizacion diaria de CIERRES." `
    -Force | Out-Host

Write-Host ""
Write-Host "=== VERIFICACION ==="

$task = Get-ScheduledTask -TaskName $TaskName
$info = Get-ScheduledTaskInfo -TaskName $TaskName

Write-Host "TaskName:          $($task.TaskName)"
Write-Host "State:             $($task.State)"
Write-Host "UserId:            $($task.Principal.UserId)"
Write-Host "LogonType:         $($task.Principal.LogonType)"
Write-Host "RunLevel:          $($task.Principal.RunLevel)"
Write-Host "Execute:           $($task.Actions.Execute)"
Write-Host "Arguments:         $($task.Actions.Arguments)"
Write-Host "RestartCount:      $($task.Settings.RestartCount)"
Write-Host "RestartInterval:   $($task.Settings.RestartInterval)"
Write-Host "MultipleInstances: $($task.Settings.MultipleInstances)"
Write-Host "StartWhenAvailable:$($task.Settings.StartWhenAvailable)"
Write-Host "ExecutionTimeLimit:$($task.Settings.ExecutionTimeLimit)"
Write-Host "LastTaskResult:    $($info.LastTaskResult)"
Write-Host ""
Write-Host "Tarea instalada."
Write-Host "Prueba:"
Write-Host "Start-ScheduledTask -TaskName `"$TaskName`""
