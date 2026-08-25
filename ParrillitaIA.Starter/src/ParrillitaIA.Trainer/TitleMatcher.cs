namespace ParrillitaIA.Trainer;

internal static class TitleMatcher
{
    public static string BuildStableTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // El título principal cambia por fecha, empresa y usuario:
        // SOFT RESTAURANT 11.0 (01/08/2025) - ... - USUARIO: CALDERON
        // Para reproducir solo necesitamos una parte estable.
        if (title.Contains(
                "SOFT RESTAURANT",
                StringComparison.OrdinalIgnoreCase))
        {
            return "SOFT RESTAURANT";
        }

        // Para diálogos con título corto y estático conservamos el título.
        var trimmed = title.Trim();

        if (trimmed.Length <= 80)
            return trimmed;

        // Para otros títulos largos usamos el primer segmento.
        var separators = new[] { " - ", " | ", " — " };

        foreach (var separator in separators)
        {
            var index = trimmed.IndexOf(
                separator,
                StringComparison.OrdinalIgnoreCase);

            if (index > 3)
                return trimmed[..index].Trim();
        }

        return trimmed[..80].Trim();
    }
}
