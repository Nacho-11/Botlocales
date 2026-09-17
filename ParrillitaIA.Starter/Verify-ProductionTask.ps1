$TaskName = "Parrillita IA Agent - SAN PEDRO"

$task = Get-ScheduledTask -TaskName $TaskName
$info = Get-ScheduledTaskInfo -TaskName $TaskName

"`n=== ESTADO ==="
$task | Select-Object TaskName, State

"`n=== PRINCIPAL ==="
$task.Principal | Format-List UserId, LogonType, RunLevel

"`n=== ACCION ==="
$task.Actions | Format-List Execute, Arguments, WorkingDirectory

"`n=== REINICIO / RECUPERACION ==="
$task.Settings | Format-List RestartCount, RestartInterval, MultipleInstances, StartWhenAvailable, ExecutionTimeLimit, DisallowStartIfOnBatteries, StopIfGoingOnBatteries

"`n=== ULTIMA EJECUCION ==="
$info | Format-List LastRunTime, LastTaskResult, NextRunTime, NumberOfMissedRuns

"`n=== PROCESO ==="
Get-Process ParrillitaIA.Agent -ErrorAction SilentlyContinue | Select-Object Id, SI, ProcessName, StartTime
