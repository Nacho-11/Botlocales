PARRILLITA IA - PRODUCCION V1 / SAN PEDRO
================================================

ARCHIVOS:
1. Options\TrainerAutomationOptions.cs  -> NUEVO
2. Options\ScheduleOptions.cs           -> REEMPLAZAR
3. Services\Contracts.cs               -> REEMPLAZAR
4. Services\TrainerProcessRunner.cs     -> NUEVO
5. Services\AgentWorker.cs              -> REEMPLAZAR
6. Program.cs                           -> REEMPLAZAR
7. appsettings.json                     -> REEMPLAZAR

Antes de publicar:
- WorkflowRunner V6.18.80 debe estar como:
  src\ParrillitaIA.Trainer\WorkflowRunner.cs

Comportamiento:
- Agent permanece activo en sesión interactiva.
- 05:30 -> ejecuta:
  ParrillitaIA.Trainer.exe run SAN_PEDRO CIERRES
- 3 intentos máximo, cada 15 min si falla.
- Timeout del Trainer: 30 min.
- Tras éxito crea marcador:
  C:\ParrillitaIA\Data\cierres-SAN_PEDRO-YYYY-MM-DD.ok
- Guarda logs:
  C:\ParrillitaIA\Logs\Trainer
- Guarda historial:
  C:\ParrillitaIA\Data\execution-history.jsonl
- Delivery está deshabilitado.

IMPORTANTE:
No ejecutar como Windows Service. UI Automation necesita una
sesión de Windows interactiva. El usuario debe permanecer con
sesión iniciada y el escritorio disponible durante CIERRES.

Instalación:
- Copiar los archivos del paquete en las rutas indicadas.
- Ejecutar Install-ProductionTask.ps1 como administrador.
