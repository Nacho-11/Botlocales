#if FLaui_ENABLED
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Extensions.Options;
using ParrillitaIA.Agent.Domain;
using ParrillitaIA.Agent.Options;

namespace ParrillitaIA.Agent.Services;

// EJEMPLO NO ACTIVADO.
// Requiere instalar FlaUI.Core y FlaUI.UIA3, inspeccionar SoftRestaurant
// con FlaUInspect y reemplazar todos los AutomationId/Name.
public sealed class FlaUiSoftRestaurantBot : ISoftRestaurantBot
{
    private readonly SoftRestaurantOptions _options;

    public FlaUiSoftRestaurantBot(IOptions<SoftRestaurantOptions> options) =>
        _options = options.Value;

    public async Task<BotResult> ExecuteAsync(
        ReportJob job,
        string downloadDirectory,
        CancellationToken cancellationToken)
    {
        using var app = Application.Launch(_options.ExecutablePath);
        using var automation = new UIA3Automation();

        var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))
            ?? throw new InvalidOperationException("No se encontró la ventana principal.");

        // EJEMPLOS: cambiar por los controles reales.
        // window.FindFirstDescendant(cf => cf.ByAutomationId("txtUsuario"))?.AsTextBox().Text = _options.Username;
        // window.FindFirstDescendant(cf => cf.ByName("Reportes"))?.AsButton().Invoke();
        // Seleccionar cierre o delivery, fechas, cajero/plataforma y exportar.

        await Task.CompletedTask;
        return BotResult.Fail(
            "SELECTORS_NOT_CONFIGURED",
            "Debes identificar los controles reales de SoftRestaurant con FlaUInspect.");
    }
}
#endif
