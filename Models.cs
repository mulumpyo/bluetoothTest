using System.Collections.Concurrent;

namespace BluetoothDiagnostics;

public record Capability(string State, bool? Value = null, string? Detail = null);
public sealed class PnpDevice
{
    public string Name { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string Status { get; set; } = "";
    public bool? Present { get; set; }
    public string? Provider { get; set; }
    public string? Version { get; set; }
    public string? Parent { get; set; }
    public string PresenceSource { get; set; } = "PowerShell DEVPKEY_Device_IsPresent";
    public string? PresenceDetail { get; set; }
}
public sealed class AdapterInfo
{
    public string Name { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string? InstanceId { get; set; }
    public PnpDevice? Pnp { get; set; }
    public bool IsDefault { get; set; }
    public string? Error { get; set; }
    public string? BluetoothAddress { get; set; }
    public string BluetoothAddressState { get; set; } = "미확인";
    public string? BluetoothAddressError { get; set; }
    public Dictionary<string, Capability> Capabilities { get; set; } = [];
    public bool IsBarrot => InstanceId?.Contains("VID_33FA&PID_0012", StringComparison.OrdinalIgnoreCase) == true;
}
public static class BluetoothAddressFormatter
{
    public static string? Format(ulong address)
    {
        if (address == 0 || address > 0xFFFFFFFFFFFFUL) return null;
        var hex = address.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }
}
public record LogEntry(DateTimeOffset Time, string Event, string Detail, string? RunId = null);
public sealed class DiagnosticLog
{
    public event Action<LogEntry>? Added;
    public string? CurrentRunId { get; set; }
    private readonly ConcurrentQueue<LogEntry> entries = new();
    public LogEntry[] Entries => entries.ToArray();
    public void Add(string kind, string detail, string? runId = null)
    {
        var item = new LogEntry(DateTimeOffset.Now, kind, detail, runId ?? CurrentRunId);
        entries.Enqueue(item);
        Console.WriteLine($"[{item.Time:HH:mm:ss}] {kind}: {detail}");
        Added?.Invoke(item);
    }
    public void Error(string operation, Exception ex) => Add("오류", $"{operation}: {ex.GetType().Name}, HRESULT=0x{ex.HResult:X8}, {ex.Message}");
}
