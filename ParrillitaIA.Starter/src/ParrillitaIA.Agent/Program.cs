using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Options;
using ParrillitaIA.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Parrillita IA Agent";
});

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

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IReportFileNameService, ReportFileNameService>();
builder.Services.AddSingleton<IDownloadValidator, DownloadValidator>();
builder.Services.AddSingleton<IFileOrganizer, FileOrganizer>();
builder.Services.AddSingleton<ICloudUploader, OneDriveSyncFolderUploader>();
builder.Services.AddSingleton<IExecutionHistory, JsonExecutionHistory>();

// Sustituir SimulatedSoftRestaurantBot por FlaUiSoftRestaurantBot
// cuando se hayan identificado los controles reales con FlaUInspect.
builder.Services.AddSingleton<ISoftRestaurantBot, FlaUiSoftRestaurantBot>();

builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
await host.RunAsync();
