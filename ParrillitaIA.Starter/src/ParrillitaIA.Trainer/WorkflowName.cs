using System.Globalization;
using System.Text;

namespace ParrillitaIA.Trainer;

public static class WorkflowName
{
    public static string Sanitize(string value)
    {
        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormD);

        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();

        return string.Join(
            '_',
            new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }
}
