# Cambios de estructura OneDrive — Parrillita IA

Reemplaza en tu proyecto los archivos incluidos en este paquete.

## Archivos a reemplazar

Dentro de:

`src\ParrillitaIA.Agent`

reemplaza:

- `Services\Contracts.cs`
- `Services\ReportFileNameService.cs`
- `Services\FileOrganizer.cs`
- `Services\OneDriveSyncFolderUploader.cs`
- `Options\StorageOptions.cs`

Luego toma `appsettings.ejemplo.json` como referencia y actualiza tu `appsettings.json`.

## Nueva estructura

### Cierres

La raíz se configura mediante:

`OneDriveCashClosuresRoot`

Ejemplo:

```text
Cierres - Sabana
└── 2026
    └── 8.Agosto
        ├── 2026-08-08_SABANA_CALDERON_CIERRE_CAJA.pdf
        └── ...
```

### Delivery

La raíz se configura mediante:

`OneDriveDeliveryRoot`

Ejemplo:

```text
Reportes Delivery Sabana
└── 2026
    └── 8.Agosto
        ├── Didi
        │   └── SABANA DIDI 3-8 AL 9-8.xlsx
        ├── Pedidos ya
        │   └── SABANA PEDIDOS YA 3-8 AL 9-8.xlsx
        └── Uber
            └── SABANA UBER 3-8 AL 9-8.xlsx
```

Las carpetas de año, mes y plataforma se crean automáticamente cuando no existen.

## Después de reemplazar

Ejecuta:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet run --project .\src\ParrillitaIA.Agent
```

## Importante

Antes de probar, confirma las dos rutas:

```powershell
Test-Path -LiteralPath "RUTA_CIERRES"
Test-Path -LiteralPath "RUTA_DELIVERY"
```

Ambas deben devolver `True`.
