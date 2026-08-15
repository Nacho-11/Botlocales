using System.Runtime.InteropServices;

namespace ParrillitaIA.Trainer;

internal static class LegacyAccessibleReader
{
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private static readonly Guid IID_IAccessible =
        new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint dwId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppvObject);

    public sealed record AccessibleItem(
        string Name,
        string Role,
        int Depth,
        string Source);

    public static IReadOnlyList<AccessibleItem> ReadTree(
        IntPtr hwnd,
        string source,
        int maxDepth = 8,
        int maxNodes = 500)
    {
        var result = new List<AccessibleItem>();

        if (hwnd == IntPtr.Zero)
            return result;

        object? root = null;
        var iid = IID_IAccessible;

        var hr =
            AccessibleObjectFromWindow(
                hwnd,
                OBJID_CLIENT,
                ref iid,
                out root);

        if (hr < 0 || root is null)
            return result;

        var visited =
            new HashSet<nint>();

        Walk(
            root,
            childId: 0,
            depth: 0,
            source,
            result,
            visited,
            maxDepth,
            maxNodes);

        return result;
    }

    private static void Walk(
        object accessible,
        int childId,
        int depth,
        string source,
        List<AccessibleItem> result,
        HashSet<nint> visited,
        int maxDepth,
        int maxNodes)
    {
        if (depth > maxDepth ||
            result.Count >= maxNodes)
            return;

        dynamic acc = accessible;

        string name = string.Empty;
        string role = string.Empty;

        try
        {
            name =
                Convert.ToString(
                    acc.get_accName(
                        childId == 0
                            ? 0
                            : childId))
                ?? string.Empty;
        }
        catch
        {
        }

        try
        {
            var raw =
                acc.get_accRole(
                    childId == 0
                        ? 0
                        : childId);

            role =
                Convert.ToString(raw)
                ?? string.Empty;
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(name) ||
            !string.IsNullOrWhiteSpace(role))
        {
            result.Add(
                new AccessibleItem(
                    name.Trim(),
                    role.Trim(),
                    depth,
                    source));
        }

        if (childId != 0)
            return;

        int count = 0;

        try
        {
            count =
                Convert.ToInt32(
                    acc.accChildCount);
        }
        catch
        {
            return;
        }

        count =
            Math.Clamp(
                count,
                0,
                500);

        for (var i = 1;
             i <= count &&
             result.Count < maxNodes;
             i++)
        {
            object? child = null;

            try
            {
                child =
                    acc.get_accChild(i);
            }
            catch
            {
            }

            if (child is not null &&
                Marshal.IsComObject(child))
            {
                Walk(
                    child,
                    0,
                    depth + 1,
                    source,
                    result,
                    visited,
                    maxDepth,
                    maxNodes);

                continue;
            }

            // Muchos controles MSAA representan los hijos solamente
            // mediante CHILDID enteros. Leer su Name/Role desde el padre.
            Walk(
                accessible,
                i,
                depth + 1,
                source,
                result,
                visited,
                maxDepth,
                maxNodes);
        }
    }

    public static IReadOnlyList<string> ExtractCandidateUsers(
        IEnumerable<AccessibleItem> items)
    {
        var blocked =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "",
                "USUARIO",
                "USUARIOS",
                "CONTRASEÑA",
                "INICIAR",
                "CANCELAR",
                "REPORTES",
                "SOFT RESTAURANT",
                "FORMAS DE PAGO",
                "FORMAS DE PAGO POR TURNO",
                "FECHA",
                "DESDE",
                "HASTA",
                "EXCEL",
                "IMPRESORA",
                "VISTA PREVIA",
                "ACEPTAR",
                "SALIR"
            };

        return items
            .Select(x => Normalize(x.Name))
            .Where(x => IsPlausibleUser(x, blocked))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPlausibleUser(
        string value,
        HashSet<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (blocked.Contains(value))
            return false;

        if (value.Length < 2 ||
            value.Length > 40)
            return false;

        if (value.Any(char.IsDigit))
            return false;

        // Los usuarios observados son nombres/apellidos en mayúscula.
        // Permitimos espacios, guiones y acentos.
        foreach (var ch in value)
        {
            if (char.IsLetter(ch) ||
                ch == ' ' ||
                ch == '-' ||
                ch == '_' ||
                ch == '.')
                continue;

            return false;
        }

        return true;
    }

    private static string Normalize(
        string value) =>
        string.Join(
            ' ',
            value
                .Trim()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
}
