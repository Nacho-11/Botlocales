using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Options;
using ParrillitaIA.Agent.Services;

using var singleInstanceMutex =
    new Mutex(
        initiallyOwned: true,
        name: "ParrillitaIA.Agent.SingleInstance",
        createdNew: out var createdNew);

if (!createdNew)
{
    Console.WriteLine(
        "Parrillita IA Agent ya está ejecutándose en esta sesión.");

    return;
}

var builder =
    Host.CreateApplicationBuilder(args);

// No usar AddWindowsService:
// la automatización requiere una sesión interactiva de Windows.

builder.Services
    .AddOptions<LocalOptions>()
    .Bind(builder.Configuration.GetSection(LocalOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<SoftRestaurantOptions>()
    .Bind(builder.Configuration.GetSection(SoftRestaurantOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ScheduleOptions>()
    .Bind(builder.Configuration.GetSection(ScheduleOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<TrainerAutomationOptions>()
    .Bind(builder.Configuration.GetSection(TrainerAutomationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IReportFileNameService, ReportFileNameService>();
builder.Services.AddSingleton<IDownloadValidator, DownloadValidator>();
builder.Services.AddSingleton<IFileOrganizer, FileOrganizer>();
builder.Services.AddSingleton<ICloudUploader, OneDriveSyncFolderUploader>();
builder.Services.AddSingleton<IExecutionHistory, JsonExecutionHistory>();
builder.Services.AddSingleton<ITrainerProcessRunner, TrainerProcessRunner>();
builder.Services.AddSingleton<IDesktopProcessCleanup, DesktopProcessCleanup>();

// Delivery todavía no es productivo y queda deshabilitado en appsettings.
builder.Services.AddSingleton<ISoftRestaurantBot, SimulatedSoftRestaurantBot>();

builder.Services.AddHostedService<AgentWorker>();

var host =
    builder.Build();

await host.RunAsync();
