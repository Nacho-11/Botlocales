$ErrorActionPreference = "Stop"

$agentExe = "C:\Botlocales\ParrillitaIA.Starter\publish\AgentProduction\ParrillitaIA.Agent.exe"
$agentDir = Split-Path -Parent $agentExe

if (-not (Test-Path $agentExe)) {
    throw "No existe el Agent productivo: $agentExe"
}

Set-Location $agentDir

# Mantiene el Agent dentro de esta tarea oculta.
# El proceso sigue en la sesión interactiva, por lo que Trainer puede operar SoftRestaurant.
& $agentExe

exit $LASTEXITCODE
