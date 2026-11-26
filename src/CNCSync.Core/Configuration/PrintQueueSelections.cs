using System.Security.Cryptography;
using System.Text;

namespace CNCSync.Core.Configuration;

public enum PrintQueueConnection
{
    Unknown,
    Usb,
    Network,
    Bluetooth,
    Virtual
}

public sealed record InstalledPrintQueue(string Queue, string Name)
{
    public PrintQueueConnection Connection { get; init; }
    public string? Location { get; init; }
}

public sealed record PrintQueueSelection(
    string Id, string Queue, string InstalledName, string DisplayName, bool IsShared, bool IsAvailable)
{
    public PrintQueueConnection Connection { get; init; }
    public string? Location { get; init; }
}

public static class PrintQueueSelections
{
    public const int MaxDisplayNameLength = 100;

    public static IReadOnlyList<PrintQueueSelection> Merge(Guid stationId, IReadOnlyList<InstalledPrintQueue> installed,
        IReadOnlyList<DesktopPrintQueueMapping> saved, IReadOnlyList<PrintQueueSelection>? previous = null,
        bool ignoreQueueCase = false)
    {
        var comparer = ignoreQueueCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var existing = previous ?? saved.Select(mapping => new PrintQueueSelection(
            mapping.Id, mapping.Queue, mapping.Name, mapping.DisplayName ?? mapping.Name, true, false)).ToArray();
        var existingByQueue = existing.ToLookup(choice => choice.Queue, comparer);
        var savedIds = saved.Select(mapping => mapping.Id).ToHashSet(StringComparer.Ordinal);
        var seenQueues = new HashSet<string>(comparer);
        var choices = new List<PrintQueueSelection>();

        foreach (var printer in installed)
        {
            if (!seenQueues.Add(printer.Queue))
            {
                continue;
            }

            if (existingByQueue.Contains(printer.Queue))
            {
                // Previously configured aliases can share a physical queue and keep their own IDs.
                choices.AddRange(existingByQueue[printer.Queue].Select(choice => choice with
                {
                    Queue = printer.Queue, InstalledName = printer.Name, IsAvailable = true,
                    Connection = printer.Connection, Location = printer.Location
                }));
            }
            else
            {
                var identity = ignoreQueueCase ? printer.Queue.ToUpperInvariant() : printer.Queue;
                choices.Add(new(CreatePrinterId(stationId, identity), printer.Queue, printer.Name, string.Empty, false, true)
                {
                    Connection = printer.Connection,
                    Location = printer.Location
                });
            }
        }

        choices.AddRange(existing.Where(choice => !seenQueues.Contains(choice.Queue) &&
            (choice.IsShared || savedIds.Contains(choice.Id) || choice.DisplayName.Length > 0))
            .Select(choice => choice with { IsAvailable = false }));
        return choices;
    }

    public static DesktopPrintQueueMapping[] ToMappings(IEnumerable<PrintQueueSelection> choices) => choices
        .Where(choice => choice.IsShared)
        .Select(choice =>
        {
            var displayName = choice.DisplayName.Trim();
            var name = displayName.Length == 0 ? DefaultDisplayName(choice.InstalledName) : displayName;
            return new DesktopPrintQueueMapping(choice.Id, name, choice.Queue) { DisplayName = displayName };
        })
        .ToArray();

    private static string DefaultDisplayName(string installedName)
    {
        if (installedName.Length <= MaxDisplayNameLength)
        {
            return installedName;
        }

        var length = char.IsHighSurrogate(installedName[MaxDisplayNameLength - 1])
            ? MaxDisplayNameLength - 1
            : MaxDisplayNameLength;
        return installedName[..length].TrimEnd();
    }

    private static string CreatePrinterId(Guid stationId, string queue)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{stationId:N}:{queue}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true).ToString("N");
    }
}
