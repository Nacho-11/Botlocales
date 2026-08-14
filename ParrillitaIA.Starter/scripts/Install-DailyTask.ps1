param(
    [Parameter(Mandatory=$true)]
    [string]$ExePath,

    [string]$Local = "SAN_PEDRO",
    [string]$Workflow = "CIERRES",
    [string]$At = "06:00"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ExePath)) {
    throw "No existe el ejecutable: $ExePath"
}

$taskName = "ParrillitaIA-$Local-$Workflow"

$action = New-ScheduledTaskAction `
    -Execute $ExePath `
    -Argument "run $Local $Workflow" `
    -WorkingDirectory (Split-Path -Parent $ExePath)

$trigger = New-ScheduledTaskTrigger `
    -Daily `
    -At $At

# Debe ejecutarse en la sesión interactiva del usuario:
# SoftRestaurant/SendInput no funcionan de forma fiable en Session 0.
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Highest

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Force | Out-Null

Write-Host ""
Write-Host "Tarea instalada: $taskName"
Write-Host "Hora diaria: $At"
Write-Host "Ejecutará con privilegios altos cuando el usuario esté conectado."
Write-Host ""
Write-Host "IMPORTANTE: la sesión de Windows debe permanecer iniciada y desbloqueada"
Write-Host "para que la automatización visual de SoftRestaurant pueda usar mouse/teclado."
