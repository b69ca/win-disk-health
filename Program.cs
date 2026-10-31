using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDiskHealth;

public sealed record Disk(
    [property: JsonRequired] int Index,
    [property: JsonRequired] string? ProviderDeviceId,
    [property: JsonRequired] ushort? HealthStatus,
    [property: JsonRequired] ushort[]? OperationalStatus);
public sealed record Snapshot(
    [property: JsonRequired] string Tool,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] DateTimeOffset CapturedAt,
    [property: JsonRequired] string Source,
    [property: JsonRequired] Disk[] Disks,
    [property: JsonRequired] string[] Issues);
public sealed record Finding(int DiskIndex, string Code, string Detail);
public sealed record Report(
    [property: JsonRequired] Snapshot Snapshot,
    [property: JsonRequired] List<Finding> Findings,
    [property: JsonRequired] string Status);

public static class App
{
    public const string ToolName = "win-disk-health";
    public const int MaxBytes = 1_048_576;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    public static int Main(string[] args)
    {
        try
        {
            var (json, from, help) = Parse(args);
            if (help)
            {
                Console.WriteLine("win-disk-health [--json] [--from FILE]\nRead-only Windows physical-disk provider health. --from replays a snapshot or JSON report.\nExit: 0 observed; 1 invalid input; 2 review; 3 incomplete evidence; 4 live requires Windows.");
                return 0;
            }
            if (from is null && !OperatingSystem.IsWindows())
            { Console.Error.WriteLine("Live collection requires Windows; use --from for offline analysis."); return 4; }
            var report = Analyze(from is null ? Collect() : Load(from));
            if (json) Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            else Print(report);
            return report.Status switch { "partial" => 3, "review" => 2, _ => 0 };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            // Avoid leaking user-supplied paths, report contents, or parser excerpts.
            Console.Error.WriteLine("win-disk-health: invalid arguments or unreadable/invalid snapshot. Use --help.");
            return 1;
        }
    }
    public static (bool Json, string? From, bool Help) Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help") return (false, null, true);
        bool json = false; string? from = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json" when !json: json = true; break;
                case "--from" when from is null:
                    if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("file required");
                    from = args[i]; break;
                default: throw new ArgumentException("unknown or duplicate option");
            }
        }
        return (json, from, false);
    }
    public static Snapshot Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new IOException("too large");
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192]; int read;
        while ((read = stream.Read(chunk)) != 0)
        {
            if (buffer.Length + read > MaxBytes) throw new IOException("too large");
            buffer.Write(chunk, 0, read);
        }
        byte[] bytes = buffer.ToArray();
        Encoding encoding = new UTF8Encoding(false, true); int offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) offset = 3;
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, true, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, true, true); offset = 2; }
        return Decode(encoding.GetString(bytes, offset, bytes.Length - offset));
    }
    public static Snapshot Decode(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("object required");
        RejectDuplicates(doc.RootElement);
        Snapshot snapshot;
        if (doc.RootElement.TryGetProperty("snapshot", out _))
        {
            var report = doc.RootElement.Deserialize<Report>(JsonOptions) ?? throw new JsonException("null report");
            snapshot = report.Snapshot;
        }
        else snapshot = doc.RootElement.Deserialize<Snapshot>(JsonOptions) ?? throw new JsonException("null snapshot");
        Validate(snapshot);
        return snapshot;
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("duplicate property"); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    public static void Validate(Snapshot? s)
    {
        if (s is null || s.Tool != ToolName || s.SchemaVersion != 1 || s.CapturedAt == default ||
            s.CapturedAt.Offset != TimeSpan.Zero || s.Source is not ("windows" or "fixture") ||
            s.Disks is null || s.Disks.Length > 256 || s.Issues is null || s.Issues.Length > 2 ||
            s.Issues.Distinct().Count() != s.Issues.Length ||
            s.Issues.Any(x => x is not ("collector-unavailable" or "truncated"))) throw new JsonException("invalid snapshot");
        for (int i = 0; i < s.Disks.Length; i++)
        {
            var d = s.Disks[i];
            if (d is null || d.Index != i + 1 || d.ProviderDeviceId is { } id &&
                (id.Length is < 1 or > 10 || id.Any(c => c is < '0' or > '9')) ||
                d.OperationalStatus is { Length: > 32 }) throw new JsonException("invalid disk");
        }
    }
    public static Report Analyze(Snapshot s)
    {
        Validate(s);
        var findings = new List<Finding>();
        bool partial = s.Issues.Length > 0 || s.Disks.Length == 0;
        foreach (var d in s.Disks)
        {
            if (d.ProviderDeviceId is null || d.HealthStatus is not (0 or 1 or 2) ||
                d.OperationalStatus is null or { Length: 0 }) partial = true;
            if (d.HealthStatus is 1 or 2)
                findings.Add(new(d.Index, "health-attention", $"Provider health is {HealthLabel(d.HealthStatus)}; verify backup coverage and investigate using local storage/vendor diagnostics."));
            foreach (ushort code in (d.OperationalStatus ?? []).Distinct())
            {
                if (!KnownOperational(code) || code is 0 or 1) partial = true;
                // OK, In Service and Completed alone are observations, not failure claims.
                if (code is not (0 or 1 or 2 or 11 or 17))
                    findings.Add(new(d.Index, "operational-attention", $"Provider operational status: {OperationalLabel(code)} ({code}). Review the provider state; this does not prove a failed drive."));
            }
        }
        return new(s, findings, partial ? "partial" : findings.Count > 0 ? "review" : "observed");
    }
    public static string HealthLabel(ushort? code) => code switch
    { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", 5 => "Unknown", null => "Unavailable", _ => $"Unrecognized ({code})" };
    private static readonly string[] OperationalNames = ["Unknown", "Other", "OK", "Degraded", "Stressed", "Predictive Failure", "Error", "Non-Recoverable Error", "Starting", "Stopping", "Stopped", "In Service", "No Contact", "Lost Communication", "Aborted", "Dormant", "Supporting Entity in Error", "Completed", "Power Mode", "Relocating"];
    public static bool KnownOperational(ushort code) => code < OperationalNames.Length || code is >= 0xD004 and <= 0xD008;
    public static string OperationalLabel(ushort code) => code < OperationalNames.Length ? OperationalNames[code] : code switch
    { 0xD004 => "Failed Media", 0xD005 => "Split", 0xD006 => "Stale Metadata", 0xD007 => "IO Error", 0xD008 => "Unrecognized Metadata", _ => "Unrecognized" };
    private static void Print(Report r)
    {
        Console.WriteLine($"Windows physical-disk health: {r.Status} ({r.Snapshot.CapturedAt:O})");
        foreach (var d in r.Snapshot.Disks)
            Console.WriteLine($"Row {d.Index}, provider DeviceId={d.ProviderDeviceId ?? "unavailable"}: health={HealthLabel(d.HealthStatus)}; operational={string.Join(", ", (d.OperationalStatus ?? []).Select(c => $"{OperationalLabel(c)} ({c})"))}");
        foreach (var f in r.Findings) Console.WriteLine($"REVIEW row {f.DiskIndex}: {f.Detail}");
        foreach (var issue in r.Snapshot.Issues) Console.WriteLine($"Coverage: {issue}");
        if (r.Status == "partial") Console.WriteLine("Incomplete evidence: missing/unknown status or no visible disks is not a healthy result.");
        Console.WriteLine("Provider state is a snapshot, not a hardware test or prediction. No changes made.");
    }
    private static Snapshot Unavailable() => new(ToolName, 1, DateTimeOffset.UtcNow, "windows", [], ["collector-unavailable"]);
    private static Snapshot Collect()
    {
        using var resource = typeof(App).Assembly.GetManifestResourceStream("WinDiskHealth.Collector.ps1")!;
        using var reader = new StreamReader(resource);
        string script = reader.ReadToEnd();
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = new Process { StartInfo = new ProcessStartInfo(powershell)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("-NoProfile"); process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-EncodedCommand");
        process.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        try
        {
            process.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            async Task<string> ReadBounded(StreamReader stream)
            {
                var output = new StringBuilder(); var block = new char[4096]; int count;
                while ((count = await stream.ReadAsync(block.AsMemory(), timeout.Token)) > 0)
                { if (output.Length + count > MaxBytes) throw new IOException("collector output too large"); output.Append(block, 0, count); }
                return output.ToString();
            }
            var output = ReadBounded(process.StandardOutput); var error = ReadBounded(process.StandardError);
            Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token)).GetAwaiter().GetResult();
            if (process.ExitCode != 0) return Unavailable();
            return Decode(output.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        { return Unavailable(); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
