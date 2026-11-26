namespace CNCSync.Infrastructure.DesktopPrinting;

internal static class DesktopPrintActivity
{
    public static string PrinterName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Unnamed printer" :
            new string(name.Take(100).Select(character =>
                char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character).ToArray());
}

// Keep repeated failures from unverified requests from flooding the activity history.
internal sealed class PrintRequestActivityLimit
{
    private readonly Dictionary<string, long> _previous = new(StringComparer.Ordinal);

    public bool ShouldLog(string reason)
    {
        lock (_previous)
        {
            var now = Environment.TickCount64;
            if (_previous.TryGetValue(reason, out var last) && now - last < 60_000)
            {
                return false;
            }

            _previous[reason] = now;
            return true;
        }
    }
}
