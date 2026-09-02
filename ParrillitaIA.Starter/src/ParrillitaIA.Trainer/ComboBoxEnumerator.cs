using System.Text;

namespace ParrillitaIA.Trainer;

internal static class ComboBoxEnumerator
{
    internal readonly record struct ControlCandidate(
        IntPtr Handle,
        string ClassName,
        string Text,
        int Left,
        int Top,
        int Width,
        int Height,
        double Distance);

    internal static List<ControlCandidate> FindCandidatesNearPoint(
        IntPtr parent,
        int anchorX,
        int anchorY)
    {
        return FindAllControls(parent, anchorX, anchorY)
            .Where(x =>
                x.ClassName.Contains(
                    "Combo",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Distance)
            .ToList();
    }

    internal static List<ControlCandidate> FindNearbyControls(
        IntPtr parent,
        int anchorX,
        int anchorY,
        int max)
    {
        return FindAllControls(parent, anchorX, anchorY)
            .OrderBy(x => x.Distance)
            .Take(Math.Max(1, max))
            .ToList();
    }

    private static List<ControlCandidate> FindAllControls(
        IntPtr parent,
        int anchorX,
        int anchorY)
    {
        var result =
            new List<ControlCandidate>();

        NativeMethods.EnumChildWindows(
            parent,
            (child, _) =>
            {
                if (!NativeMethods.IsWindowVisible(child))
                    return true;

                if (!NativeMethods.GetWindowRect(
                        child,
                        out var rect))
                {
                    return true;
                }

                var className =
                    GetClassName(child);

                var text =
                    GetWindowText(child);

                var centerX =
                    rect.Left +
                    Math.Max(1, rect.Width) / 2.0;

                var centerY =
                    rect.Top +
                    Math.Max(1, rect.Height) / 2.0;

                var dx =
                    centerX - anchorX;

                var dy =
                    centerY - anchorY;

                var distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy);

                result.Add(
                    new ControlCandidate(
                        child,
                        className,
                        text,
                        rect.Left,
                        rect.Top,
                        rect.Width,
                        rect.Height,
                        distance));

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    internal static int TryGetCount(
        IntPtr combo)
    {
        if (combo == IntPtr.Zero)
            return NativeMethods.CB_ERR;

        try
        {
            return NativeMethods
                .SendMessage(
                    combo,
                    NativeMethods.CB_GETCOUNT,
                    IntPtr.Zero,
                    IntPtr.Zero)
                .ToInt32();
        }
        catch
        {
            return NativeMethods.CB_ERR;
        }
    }

    internal static int TryGetCurrentIndex(
        IntPtr combo)
    {
        if (combo == IntPtr.Zero)
            return NativeMethods.CB_ERR;

        try
        {
            return NativeMethods
                .SendMessage(
                    combo,
                    NativeMethods.CB_GETCURSEL,
                    IntPtr.Zero,
                    IntPtr.Zero)
                .ToInt32();
        }
        catch
        {
            return NativeMethods.CB_ERR;
        }
    }

    internal static string TryGetItemText(
        IntPtr combo,
        int index)
    {
        if (combo == IntPtr.Zero ||
            index < 0)
        {
            return string.Empty;
        }

        try
        {
            var length =
                NativeMethods
                    .SendMessage(
                        combo,
                        NativeMethods.CB_GETLBTEXTLEN,
                        (IntPtr)index,
                        IntPtr.Zero)
                    .ToInt32();

            if (length < 0)
                return string.Empty;

            var buffer =
                new StringBuilder(
                    length + 2);

            var copied =
                NativeMethods
                    .SendMessage(
                        combo,
                        NativeMethods.CB_GETLBTEXT,
                        (IntPtr)index,
                        buffer)
                    .ToInt32();

            return copied < 0
                ? string.Empty
                : buffer.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static bool TrySelectIndex(
        IntPtr combo,
        int index)
    {
        var count =
            TryGetCount(combo);

        if (count <= 0 ||
            index < 0 ||
            index >= count)
        {
            return false;
        }

        var selected =
            NativeMethods
                .SendMessage(
                    combo,
                    NativeMethods.CB_SETCURSEL,
                    (IntPtr)index,
                    IntPtr.Zero)
                .ToInt32();

        if (selected == NativeMethods.CB_ERR)
            return false;

        var parent =
            NativeMethods.GetParent(
                combo);

        if (parent != IntPtr.Zero)
        {
            var controlId =
                NativeMethods.GetDlgCtrlID(
                    combo);

            var wParam =
                (IntPtr)(
                    (NativeMethods.CBN_SELCHANGE << 16) |
                    (controlId & 0xFFFF));

            NativeMethods.SendMessage(
                parent,
                NativeMethods.WM_COMMAND,
                wParam,
                combo);
        }

        return TryGetCurrentIndex(combo) == index;
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

    private static string GetWindowText(
        IntPtr hwnd)
    {
        var sb =
            new StringBuilder(512);

        NativeMethods.GetWindowText(
            hwnd,
            sb,
            sb.Capacity);

        return sb.ToString();
    }
}
