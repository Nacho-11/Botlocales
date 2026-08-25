using System.Runtime.InteropServices;
using System.Text;

namespace ParrillitaIA.Trainer;

internal static class ClipboardHelper
{
    public static void SetText(string text)
    {
        text ??= string.Empty;

        var bytes =
            Encoding.Unicode.GetBytes(text + "\0");

        IntPtr globalMemory = IntPtr.Zero;
        var opened = false;

        try
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                if (NativeMethods.OpenClipboard(IntPtr.Zero))
                {
                    opened = true;
                    break;
                }

                Thread.Sleep(50 * attempt);
            }

            if (!opened)
                throw new InvalidOperationException(
                    "No se pudo abrir el portapapeles.");

            if (!NativeMethods.EmptyClipboard())
                throw new InvalidOperationException(
                    "No se pudo limpiar el portapapeles.");

            globalMemory =
                NativeMethods.GlobalAlloc(
                    NativeMethods.GMEM_MOVEABLE,
                    (UIntPtr)bytes.Length);

            if (globalMemory == IntPtr.Zero)
                throw new InvalidOperationException(
                    "No se pudo reservar memoria para el portapapeles.");

            var target =
                NativeMethods.GlobalLock(globalMemory);

            if (target == IntPtr.Zero)
                throw new InvalidOperationException(
                    "No se pudo bloquear memoria del portapapeles.");

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(globalMemory);
            }

            var result =
                NativeMethods.SetClipboardData(
                    NativeMethods.CF_UNICODETEXT,
                    globalMemory);

            if (result == IntPtr.Zero)
                throw new InvalidOperationException(
                    "No se pudo escribir en el portapapeles.");

            globalMemory = IntPtr.Zero;
        }
        finally
        {
            if (opened)
                NativeMethods.CloseClipboard();

            if (globalMemory != IntPtr.Zero)
                NativeMethods.GlobalFree(globalMemory);
        }
    }



    public static string GetText()
    {
        var opened = false;

        try
        {
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                if (NativeMethods.OpenClipboard(IntPtr.Zero))
                {
                    opened = true;
                    break;
                }

                Thread.Sleep(30 * attempt);
            }

            if (!opened)
                return string.Empty;

            var handle =
                NativeMethods.GetClipboardData(
                    NativeMethods.CF_UNICODETEXT);

            if (handle == IntPtr.Zero)
                return string.Empty;

            var pointer =
                NativeMethods.GlobalLock(handle);

            if (pointer == IntPtr.Zero)
                return string.Empty;

            try
            {
                return Marshal.PtrToStringUni(pointer)
                    ?? string.Empty;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            if (opened)
                NativeMethods.CloseClipboard();
        }
    }
    public static void TryClear()
    {
        try
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                NativeMethods.EmptyClipboard();
                NativeMethods.CloseClipboard();
            }
        }
        catch
        {
        }
    }
}