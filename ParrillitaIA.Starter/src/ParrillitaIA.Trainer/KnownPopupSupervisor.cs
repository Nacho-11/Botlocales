using System.Diagnostics;
using System.Text;

namespace ParrillitaIA.Trainer;

/// <summary>
/// Supervisa popups modales conocidos de SoftRestaurant que pueden robar
/// el foco durante una automatización. La detección y el clic se hacen por
/// HWND/texto del control, nunca por coordenadas.
/// </summary>
internal static class KnownPopupSupervisor
{
    private const string CommercialVersionTitle = "Versión Comercial";
    private const string ContinueButtonText = "Continuar";

    internal static async Task HandleKnownSoftRestaurantPopupsAsync(
        string processName,
        CancellationToken cancellationToken)
    {
        // Varias llamadas son intencionales: el popup puede aparecer unos
        // milisegundos después de que la ventana principal toma foco.
        var deadline =
            DateTimeOffset.UtcNow.AddSeconds(4);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var popup =
                FindCommercialLicensePopup(
                    processName);

            if (popup == IntPtr.Zero)
            {
                await Task.Delay(
                    150,
                    cancellationToken);

                // Una segunda observación evita declarar "libre" justo antes
                // de que SoftRestaurant termine de crear la ventana modal.
                if (FindCommercialLicensePopup(processName) == IntPtr.Zero)
                    return;

                continue;
            }

            Console.WriteLine(
                "[POPUP] Ventana modal con botón Continuar detectada.");

            var continueButton =
                FindVisibleChildButton(
                    popup,
                    ContinueButtonText);

            if (continueButton == IntPtr.Zero)
            {
                Console.WriteLine(
                    "[POPUP] Se detectó la ventana, pero no el botón Continuar.");

                // No enviar Enter a ciegas: podría activar Registrar ahora
                // u otra acción si cambió el foco.
                return;
            }

            NativeMethods.SetForegroundWindow(
                popup);

            await Task.Delay(
                150,
                cancellationToken);

            NativeMethods.SendMessage(
                continueButton,
                NativeMethods.BM_CLICK,
                IntPtr.Zero,
                IntPtr.Zero);

            Console.WriteLine(
                "[POPUP] Botón Continuar pulsado por HWND.");

            // Esperar confirmación positiva: la ventana debe desaparecer.
            var closed =
                await WaitUntilClosedAsync(
                    popup,
                    4000,
                    cancellationToken);

            if (closed)
            {
                Console.WriteLine(
                    "[POPUP] Aviso cerrado correctamente.");
                return;
            }

            throw new InvalidOperationException(
                "Se detectó un popup modal de SoftRestaurant, se pulsó Continuar, " +
                "pero la ventana no se cerró. Se detiene la automatización para evitar acciones fuera de contexto.");
        }
    }

    private static IntPtr FindCommercialLicensePopup(
        string processName)
    {
        var processIds =
            GetProcessIds(
                processName);

        if (processIds.Count == 0)
            return IntPtr.Zero;

        IntPtr found =
            IntPtr.Zero;

        NativeMethods.EnumWindows(
            (window, _) =>
            {
                if (!NativeMethods.IsWindowVisible(window))
                    return true;

                NativeMethods.GetWindowThreadProcessId(
                    window,
                    out var pid);

                if (!processIds.Contains(pid))
                    return true;

                // La señal determinista es el botón visible "Continuar"
                // dentro de una ventana secundaria del propio proceso.
                // No dependemos del título del popup ni de coordenadas.
                var continueButton =
                    FindVisibleChildButton(
                        window,
                        ContinueButtonText);

                if (continueButton == IntPtr.Zero)
                    return true;

                // Evita confundir la ventana principal con un popup:
                // exigimos que la ventana candidata tenga título distinto
                // al título principal habitual o clase de diálogo.
                var title =
                    GetWindowText(window);

                var className =
                    GetClassName(window);

                var looksLikeDialog =
                    className.Equals(
                        "#32770",
                        StringComparison.OrdinalIgnoreCase) ||
                    !title.Equals(
                        "SOFT RESTAURANT",
                        StringComparison.OrdinalIgnoreCase);

                if (!looksLikeDialog)
                    return true;

                found =
                    window;

                return false;
            },
            IntPtr.Zero);

        return found;
    }

    private static IntPtr FindVisibleChildButton(
        IntPtr parent,
        string buttonText)
    {
        IntPtr found =
            IntPtr.Zero;

        NativeMethods.EnumChildWindows(
            parent,
            (child, _) =>
            {
                if (!NativeMethods.IsWindowVisible(child) ||
                    !NativeMethods.IsWindowEnabled(child))
                {
                    return true;
                }

                var text =
                    GetWindowText(child);

                if (!text.Equals(
                        buttonText,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var className =
                    GetClassName(child);

                // VB6 puede usar clases distintas a "Button", por lo que el
                // texto exacto es la señal principal. Si la clase contiene
                // Button/Command, mejor; si no, conservamos el control por texto.
                if (className.Contains(
                        "Button",
                        StringComparison.OrdinalIgnoreCase) ||
                    className.Contains(
                        "Command",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(text))
                {
                    found =
                        child;

                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        return found;
    }

    private static async Task<bool> WaitUntilClosedAsync(
        IntPtr window,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline =
            DateTimeOffset.UtcNow.AddMilliseconds(
                timeoutMs);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!NativeMethods.IsWindowVisible(window))
            {
                return true;
            }

            await Task.Delay(
                100,
                cancellationToken);
        }

        return !NativeMethods.IsWindowVisible(window);
    }

    private static HashSet<uint> GetProcessIds(
        string processName)
    {
        var normalized =
            Path.GetFileNameWithoutExtension(
                processName);

        if (string.IsNullOrWhiteSpace(normalized))
            return new HashSet<uint>();

        try
        {
            return Process
                .GetProcessesByName(normalized)
                .Select(x => (uint)x.Id)
                .ToHashSet();
        }
        catch
        {
            return new HashSet<uint>();
        }
    }

    private static string GetWindowText(
        IntPtr hwnd)
    {
        var sb =
            new StringBuilder(512);

        NativeMethods.GetWindowText(
            hwnd,
            sb,
            sb.Capacity);

        return sb.ToString().Trim();
    }

    private static string GetClassName(
        IntPtr hwnd)
    {
        var sb =
            new StringBuilder(256);

        NativeMethods.GetClassName(
            hwnd,
            sb,
            sb.Capacity);

        return sb.ToString();
    }
}
