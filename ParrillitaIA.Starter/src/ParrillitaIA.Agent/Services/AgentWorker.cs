using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly LocalOptions _local;
    private readonly SoftRestaurantOptions _softRestaurant;
    private readonly ScheduleOptions _schedule;
    private readonly StorageOptions _storage;
    private readonly IClock _clock;
    private readonly ISoftRestaurantBot _bot;
    private readonly IDownloadValidator _validator;
    private readonly IFileOrganizer _organizer;
    private readonly ICloudUploader _uploader;
    private readonly IExecutionHistory _history;

    private DateOnly? _lastCashClosureRun;
    private DateOnly? _lastDeliveryRun;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<LocalOptions> local,
        IOptions<SoftRestaurantOptions> softRestaurant,
        IOptions<ScheduleOptions> schedule,
        IOptions<StorageOptions> storage,
        IClock clock,
        ISoftRestaurantBot bot,
        IDownloadValidator validator,
        IFileOrganizer organizer,
        ICloudUploader uploader,
        IExecutionHistory history)
    {
        _logger = logger;
        _local = local.Value;
        _softRestaurant = softRestaurant.Value;
        _schedule = schedule.Value;
        _storage = storage.Value;
        _clock = clock;
        _bot = bot;
        _validator = validator;
        _organizer = organizer;
        _uploader = uploader;
        _history = history;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Parrillita IA inició para {LocalCode} - {LocalName}",
            _local.Code,
            _local.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _clock.Now;
                var today = DateOnly.FromDateTime(now.LocalDateTime);

                if (ShouldRunCashClosures(now) && _lastCashClosureRun != today)
                {
                    await RunCashClosuresAsync(today.AddDays(-1), stoppingToken);
                    _lastCashClosureRun = today;
                }

                if (ShouldRunDelivery(now) && _lastDeliveryRun != today)
                {
                    var previousSunday = today.AddDays(-1);
                    var previousMonday = previousSunday.AddDays(-6);
                    await RunDeliveryAsync(previousMonday, previousSunday, stoppingToken);
                    _lastDeliveryRun = today;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no controlado en el ciclo del agente.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_schedule.PollSeconds), stoppingToken);
        }
    }

    private bool ShouldRunCashClosures(DateTimeOffset now) =>
        now.Hour > _schedule.CashClosuresHour ||
        (now.Hour == _schedule.CashClosuresHour && now.Minute >= _schedule.CashClosuresMinute);

    private bool ShouldRunDelivery(DateTimeOffset now) =>
        now.DayOfWeek == _schedule.DeliveryDayOfWeek &&
        (now.Hour > _schedule.DeliveryHour ||
         (now.Hour == _schedule.DeliveryHour && now.Minute >= _schedule.DeliveryMinute));

    private async Task RunCashClosuresAsync(
        DateOnly reportDate,
        CancellationToken cancellationToken)
    {
        foreach (var cashier in _softRestaurant.Cashiers)
            await ExecuteJobAsync(ReportJob.CashClosure(reportDate, cashier), cancellationToken);
    }

    private async Task RunDeliveryAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        foreach (var platform in _softRestaurant.DeliveryPlatforms)
            await ExecuteJobAsync(ReportJob.Delivery(startDate, endDate, platform), cancellationToken);
    }

    private async Task ExecuteJobAsync(
        ReportJob job,
        CancellationToken cancellationToken)
    {
        var executionId = Guid.NewGuid();
        var startedAt = _clock.Now;
        string? officialFileName = null;
        string status = "FAILED";
        string? errorCode = null;
        string? errorMessage = null;

        var workFolder = Path.Combine(
            _storage.WorkRoot,
            startedAt.ToString("yyyy-MM-dd"),
            job.Id.ToString("N"),
            "download");

        try
        {
            _logger.LogInformation(
                "Ejecutando {Kind}; Cajero={Cashier}; Plataforma={Platform}",
                job.Kind,
                job.Cashier,
                job.Platform);

            var result = await _bot.ExecuteAsync(job, workFolder, cancellationToken);
            if (!result.Success || string.IsNullOrWhiteSpace(result.DownloadedFile))
                throw new BotExecutionException(
                    result.ErrorCode ?? "BOT_FAILED",
                    result.ErrorMessage ?? "El bot no produjo un archivo.");

            var ready = await _validator.WaitUntilReadyAsync(
                result.DownloadedFile,
                TimeSpan.FromMinutes(3),
                cancellationToken);

            if (!ready)
                throw new BotExecutionException(
                    "DOWNLOAD_TIMEOUT",
                    "El archivo no terminó de descargarse o quedó bloqueado.");

            var archived = await _organizer.OrganizeAsync(
                job, result.DownloadedFile, cancellationToken);

            officialFileName = Path.GetFileName(archived);

            var oneDrivePath = await _uploader.UploadAsync(
                job, archived, cancellationToken);

            status = "COMPLETED";
            _logger.LogInformation(
                "Reporte completado: {File}; OneDrive={OneDrivePath}",
                officialFileName,
                oneDrivePath);
        }
        catch (BotExecutionException ex)
        {
            errorCode = ex.Code;
            errorMessage = ex.Message;
            _logger.LogError(ex, "Falló el trabajo {JobId}: {Code}", job.Id, ex.Code);
        }
        catch (Exception ex)
        {
            errorCode = "UNEXPECTED_ERROR";
            errorMessage = ex.Message;
            _logger.LogError(ex, "Falló el trabajo {JobId}", job.Id);
        }
        finally
        {
            await _history.AppendAsync(
                new ExecutionRecord(
                    executionId,
                    job.Id,
                    _local.Code,
                    job.Kind.ToString(),
                    status,
                    officialFileName,
                    errorCode,
                    errorMessage,
                    startedAt,
                    _clock.Now),
                cancellationToken);
        }
    }

    private sealed class BotExecutionException : Exception
    {
        public string Code { get; }

        public BotExecutionException(string code, string message)
            : base(message) => Code = code;
    }
}
