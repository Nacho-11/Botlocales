# Parrillita IA — Starter

Agente local para instalar en cada servidor/local. El mismo ejecutable se configura con el nombre del local y guarda los reportes en la carpeta correspondiente de OneDrive.

## Qué incluye

- Worker Service de .NET 8.
- Configuración independiente por local.
- Cierres diarios del día anterior, uno por cajero.
- Delivery semanal, un archivo por plataforma.
- Carpeta temporal exclusiva por trabajo.
- Validación de descarga estable.
- Nombres oficiales.
- Archivo local de historial JSONL.
- Copia a una carpeta sincronizada por OneDrive.
- Bot simulado para probar el flujo sin tocar SoftRestaurant.
- Clase de ejemplo para integrar FlaUI.

## Primer arranque

1. Instala .NET 8 SDK.
2. Edita `src/ParrillitaIA.Agent/appsettings.json`.
3. Mantén `"SimulationMode": true`.
4. Para probar inmediatamente, coloca los horarios anteriores a la hora actual.
5. Ejecuta:

```powershell
dotnet restore
dotnet run --project .\src\ParrillitaIA.Agent
```

## Configuración por local

Cambia únicamente:

```json
"Local": {
  "Code": "NORTE",
  "Name": "Parrillita Norte"
}
```

y las rutas del servidor.

## Resultado esperado

Cierres:

```text
OneDriveRoot/
  Parrillita Centro/
    2026/
      08_Agosto/
        Cierres/
          2026-08-04_CENTRO_CAJA01_CIERRE_CAJA.pdf
```

Delivery:

```text
OneDriveRoot/
  Parrillita Centro/
    2026/
      08_Agosto/
        Delivery/
          UBER/
            2026-07-27_2026-08-02_CENTRO_DELIVERY_UBER.xlsx
```

## Activar SoftRestaurant real

1. Instala FlaUInspect y abre SoftRestaurant.
2. Identifica `AutomationId`, `Name` y tipo de control de:
   - usuario y contraseña;
   - botón de ingreso;
   - menú de reportes;
   - cierre de caja;
   - fecha;
   - cajero;
   - reporte de delivery;
   - plataforma;
   - exportar PDF/Excel.
3. Agrega al `.csproj`:

```xml
<PackageReference Include="FlaUI.Core" Version="5.*" />
<PackageReference Include="FlaUI.UIA3" Version="5.*" />
```

4. Convierte `FlaUiSoftRestaurantBot.example.cs` en una clase activa.
5. Registra `FlaUiSoftRestaurantBot` en `Program.cs`.
6. No uses coordenadas hasta comprobar que el control no está disponible por UI Automation.

## OneDrive

La primera versión copia a una carpeta local ya sincronizada por OneDrive. Esto permite comenzar sin implementar autenticación de Microsoft Graph. Para una instalación centralizada posterior, sustituye `OneDriveSyncFolderUploader` por una implementación de `ICloudUploader` con Microsoft Graph.

## Importante

El bot simulado escribe texto en archivos con extensión PDF/XLSX para probar el flujo de carpetas. No genera documentos reales. Al conectar SoftRestaurant, los archivos serán los exportados por el programa.
