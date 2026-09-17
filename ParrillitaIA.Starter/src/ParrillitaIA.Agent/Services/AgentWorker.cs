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
    private readonly ITrainerProcessRunner _trainer;
    private readonly IDesktopProcessCleanup _cleanup;
    private readonly ISoftRestaurantBot _bot;
    private readonly IDownloadValidator _validator;
    private readonly IFileOrganizer _organizer;
    private readonly ICloudUploader _uploader;
    private readonly IExecutionHistory _history;

    private DateOnly? _lastCashClosureRun;
    private DateOnly? _lastDeliveryRun;

    private DateOnly? _cashAttemptDay;
    private int _cashAttempts;
    private DateTimeOffset? _nextCashAttemptAt;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<LocalOptions> local,
        IOptions<SoftRestaurantOptions> softRestaurant,
        IOptions<ScheduleOptions> schedule,
        IOptions<StorageOptions> storage,
        IClock clock,
        ITrainerProcessRunner trainer,
        IDesktopProcessCleanup cleanup,
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
        _trainer = trainer;
        _cleanup = cleanup;
        _bot = bot;
        _validator = validator;
        _organizer = organizer;
        _uploader = uploader;
        _history = history;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Parrillita IA inició para {LocalCode} - {LocalName}",
            _local.Code,
            _local.Name);

        _logger.LogInformation(
            "CIERRES: Enabled={Enabled}; Hora={Hour:00}:{Minute:00}; MaxAttempts={MaxAttempts}; RetryMinutes={RetryMinutes}",
            _schedule.EnableCashClosures,
            _schedule.CashClosuresHour,
            _schedule.CashClosuresMinute,
            _schedule.CashClosuresMaxAttempts,
            _schedule.CashClosuresRetryMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now =
                    _clock.Now;

                var today =
                    DateOnly.FromDateTime(
                        now.LocalDateTime);

                ResetCashAttemptsIfNewDay(
                    today);

                if (_schedule.EnableCashClosures &&
                    ShouldRunCashClosures(now) &&
                    _lastCashClosureRun != today)
                {
                    var reportDate =
                        today.AddDays(-1);

                    if (HasSuccessfulMarker(
                            reportDate))
                    {
                        _logger.LogInformation(
                            "CIERRES {ReportDate}: ya existe marcador OK. No se repite.",
                            reportDate);

                        // V2A:
                        // Si el cierre ya terminó anteriormente, todavía puede haber
                        // quedado Excel o SoftRestaurant abierto de esa ejecución.
                        // Hacemos una limpieza única al reconocer el marcador diario.
                        try
                        {
                            await _cleanup.CleanupAsync(
                                "MARCADOR_OK_LIMPIEZA_RESIDUAL",
                                stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "[CLEANUP] Falló la limpieza residual asociada al marcador OK.");
                        }

                        _lastCashClosureRun =
                            today;
                    }
                    else if (CanAttemptCashClosures(
                                 now))
                    {
                        _cashAttempts++;

                        var success =
                            await RunCashClosuresViaTrainerAsync(
                                reportDate,
                                _cashAttempts,
                                stoppingToken);

                        if (success)
                        {
                            WriteSuccessfulMarker(
                                reportDate);

                            _lastCashClosureRun =
                                today;
                        }
                        else if (_cashAttempts >=
                                 _schedule.CashClosuresMaxAttempts)
                        {
                            _logger.LogError(
                                "CIERRES agotó {Attempts} intentos para {ReportDate}. No habrá más reintentos automáticos hoy.",
                                _cashAttempts,
                                reportDate);

                            _lastCashClosureRun =
                                today;
                        }
                        else
                        {
                            _nextCashAttemptAt =
                                now.AddMinutes(
                                    _schedule.CashClosuresRetryMinutes);

                            _logger.LogWarning(
                                "CIERRES falló. Próximo intento {Attempt}/{MaxAttempts} a las {NextAttempt}.",
                                _cashAttempts + 1,
                                _schedule.CashClosuresMaxAttempts,
                                _nextCashAttemptAt);
                        }
                    }
                }

                if (_schedule.EnableDelivery &&
                    ShouldRunDelivery(now) &&
                    _lastDeliveryRun != today)
                {
                    var previousSunday =
                        today.AddDays(-1);

                    var previousMonday =
                        previousSunday.AddDays(-6);

                    await RunDeliveryAsync(
                        previousMonday,
                        previousSunday,
                        stoppingToken);

                    _lastDeliveryRun =
                        today;
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error no controlado en el ciclo del agente.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        _schedule.PollSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void ResetCashAttemptsIfNewDay(
        DateOnly today)
    {
        if (_cashAttemptDay == today)
            return;

        _cashAttemptDay =
            today;

        _cashAttempts =
            0;

        _nextCashAttemptAt =
            null;
    }

    private bool CanAttemptCashClosures(
        DateTimeOffset now)
    {
        if (_cashAttempts >=
            _schedule.CashClosuresMaxAttempts)
        {
            return false;
        }

        return _nextCashAttemptAt is null ||
               now >= _nextCashAttemptAt.Value;
    }

    private bool ShouldRunCashClosures(
        DateTimeOffset now) =>
        now.Hour > _schedule.CashClosuresHour ||
        (now.Hour ==
             _schedule.CashClosuresHour &&
         now.Minute >=
             _schedule.CashClosuresMinute);

    private bool ShouldRunDelivery(
        DateTimeOffset now) =>
        now.DayOfWeek ==
            _schedule.DeliveryDayOfWeek &&
        (now.Hour >
             _schedule.DeliveryHour ||
         (now.Hour ==
              _schedule.DeliveryHour &&
          now.Minute >=
              _schedule.DeliveryMinute));

    private async Task<bool> RunCashClosuresViaTrainerAsync(
        DateOnly reportDate,
        int attempt,
        CancellationToken cancellationToken)
    {
        var executionId =
            Guid.NewGuid();

        var jobId =
            Guid.NewGuid();

        var startedAt =
            _clock.Now;

        var status =
            "FAILED";

        string? errorCode =
            null;

        string? errorMessage =
            null;

        string? logFile =
            null;

        try
        {
            // Limpiar cualquier proceso residual antes de iniciar.
            await _cleanup.CleanupAsync(
                $"ANTES_CIERRES_INTENTO_{attempt}",
                cancellationToken);

            _logger.LogInformation(
                "CIERRES iniciando intento {Attempt}/{MaxAttempts}; FechaReporte={ReportDate}",
                attempt,
                _schedule.CashClosuresMaxAttempts,
                reportDate);

            var result =
                await _trainer.RunClosuresAsync(
                    reportDate,
                    cancellationToken);

            logFile =
                result.LogFile;

            if (!result.Success)
            {
                errorCode =
                    result.ErrorCode ??
                    "TRAINER_FAILED";

                errorMessage =
                    result.ErrorMessage ??
                    "El Trainer no confirmó una ejecución productiva correcta.";

                _logger.LogError(
                    "CIERRES falló. Code={Code}; Message={Message}; Log={Log}",
                    errorCode,
                    errorMessage,
                    logFile);

                return false;
            }

            status =
                "COMPLETED";

            _logger.LogInformation(
                "CIERRES completado correctamente para {ReportDate}. Log={Log}",
                reportDate,
                logFile);

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            errorCode =
                "CANCELLED";

            errorMessage =
                "La ejecución fue cancelada.";

            throw;
        }
        catch (Exception ex)
        {
            errorCode =
                "UNEXPECTED_ERROR";

            errorMessage =
                ex.Message;

            _logger.LogError(
                ex,
                "CIERRES falló con error inesperado para {ReportDate}.",
                reportDate);

            return false;
        }
        finally
        {
            // Siempre cerrar Excel y SoftRestaurant:
            // éxito, error, timeout o antes de un reintento.
            try
            {
                await _cleanup.CleanupAsync(
                    $"DESPUES_CIERRES_INTENTO_{attempt}_{status}",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[CLEANUP] La limpieza final produjo un error, pero el estado del cierre se conserva.");
            }

            await _history.AppendAsync(
                new ExecutionRecord(
                    executionId,
                    jobId,
                    _local.Code,
                    "CashClosures",
                    status,
                    logFile is null
                        ? null
                        : Path.GetFileName(
                            logFile),
                    errorCode,
                    errorMessage,
                    startedAt,
                    _clock.Now),
                CancellationToken.None);
        }
    }

    private string GetMarkerPath(
        DateOnly reportDate)
    {
        var dataDirectory =
            Path.GetDirectoryName(
                _storage.HistoryFile);

        if (string.IsNullOrWhiteSpace(
                dataDirectory))
        {
            dataDirectory =
                @"C:\ParrillitaIA\Data";
        }

        Directory.CreateDirectory(
            dataDirectory);

        var safeLocal =
            string.Concat(
                _local.Code.Select(
                    c =>
                        Path.GetInvalidFileNameChars()
                            .Contains(c)
                            ? '_'
                            : c));

        return Path.Combine(
            dataDirectory,
            $"cierres-{safeLocal}-{reportDate:yyyy-MM-dd}.ok");
    }

    private bool HasSuccessfulMarker(
        DateOnly reportDate) =>
        File.Exists(
            GetMarkerPath(
                reportDate));

    private void WriteSuccessfulMarker(
        DateOnly reportDate)
    {
        var marker =
            GetMarkerPath(
                reportDate);

        File.WriteAllText(
            marker,
            $"COMPLETED {DateTimeOffset.Now:O}{Environment.NewLine}");

        _logger.LogInformation(
            "Marcador OK creado: {Marker}",
            marker);
    }

    // Delivery se conserva para una etapa posterior.
    private async Task RunDeliveryAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        foreach (var platform in
                 _softRestaurant.DeliveryPlatforms)
        {
            await ExecuteLegacyJobAsync(
                ReportJob.Delivery(
                    startDate,
                    endDate,
                    platform),
                cancellationToken);
        }
    }

    private async Task ExecuteLegacyJobAsync(
        ReportJob job,
        CancellationToken cancellationToken)
    {
        var executionId =
            Guid.NewGuid();

        var startedAt =
            _clock.Now;

        string? officialFileName =
            null;

        var status =
            "FAILED";

        string? errorCode =
            null;

        string? errorMessage =
            null;

        var workFolder =
            Path.Combine(
                _storage.WorkRoot,
                startedAt.ToString(
                    "yyyy-MM-dd"),
                job.Id.ToString("N"),
                "download");

        try
        {
            var result =
                await _bot.ExecuteAsync(
                    job,
                    workFolder,
                    cancellationToken);

            if (!result.Success ||
                string.IsNullOrWhiteSpace(
                    result.DownloadedFile))
            {
                throw new InvalidOperationException(
                    result.ErrorMessage ??
                    "El bot no produjo un archivo.");
            }

            var ready =
                await _validator.WaitUntilReadyAsync(
                    result.DownloadedFile,
                    TimeSpan.FromMinutes(3),
                    cancellationToken);

            if (!ready)
                throw new TimeoutException(
                    "El archivo no terminó de descargarse.");

            var archived =
                await _organizer.OrganizeAsync(
                    job,
                    result.DownloadedFile,
                    cancellationToken);

            officialFileName =
                Path.GetFileName(
                    archived);

            await _uploader.UploadAsync(
                job,
                archived,
                cancellationToken);

            status =
                "COMPLETED";
        }
        catch (Exception ex)
        {
            errorCode =
                "DELIVERY_ERROR";

            errorMessage =
                ex.Message;

            _logger.LogError(
                ex,
                "Falló Delivery {JobId}.",
                job.Id);
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
}
