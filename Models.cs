using System.Text.Json.Serialization;

namespace CodexSelfCheck;

internal enum HealthStatus
{
    Unknown,
    Healthy,
    Repairable,
    ManualAction,
    Repairing,
    VerificationFailed
}

internal sealed class PackageInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Status { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string AppUserModelId { get; set; } = "";

    [JsonIgnore]
    public string ResourcesPath => Path.Combine(InstallLocation, "app", "resources");

    [JsonIgnore]
    public string RuntimeSourcePath => Path.Combine(ResourcesPath, "cua_node");

    [JsonIgnore]
    public string CodexExePath => Path.Combine(ResourcesPath, "codex.exe");
}

internal sealed class SignatureInfo
{
    public string Status { get; set; } = "Unknown";
    public string StatusMessage { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Thumbprint { get; set; } = "";
    public bool IsValid => Status.Equals("Valid", StringComparison.OrdinalIgnoreCase);
}

internal sealed record FileEntry(string RelativePath, string FullPath, long Length);

internal sealed class RuntimeInspection
{
    public int SourceFileCount { get; set; }
    public long SourceBytes { get; set; }
    public string? MatchingRuntimeId { get; set; }
    public List<string> StagingDirectories { get; set; } = [];
    public string? StagingRuntimeId { get; set; }
    public long AvailableBytes { get; set; }
}

internal sealed class WindowInfo
{
    public int ProcessId { get; set; }

    [JsonIgnore]
    public nint Handle { get; set; }
    public string Title { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public bool IsOnScreen { get; set; }
}

internal sealed class ProbeResult
{
    public bool WindowVisible { get; set; }
    public WindowInfo? Window { get; set; }
    public string? RuntimeId { get; set; }
    public string? StagingDirectory { get; set; }
    public List<int> StartedProcessIds { get; set; } = [];
    public string Message { get; set; } = "";
}

internal sealed class LaunchVerification
{
    public bool Success { get; set; }
    public bool WindowVisible { get; set; }
    public bool RendererFound { get; set; }
    public bool AppServerFound { get; set; }
    public WindowInfo? Window { get; set; }
    public List<int> StartedProcessIds { get; set; } = [];
    public string Message { get; set; } = "";
}

internal sealed class DiagnosticReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public HealthStatus Status { get; set; }
    public string Summary { get; set; } = "";
    public PackageInfo? Package { get; set; }
    public SignatureInfo? Signature { get; set; }
    public RuntimeInspection? Runtime { get; set; }
    public string? ExpectedRuntimeId { get; set; }
    public string? CodexCliPath { get; set; }
    public string CodexCliPathScope { get; set; } = "未设置";
    public bool CodexCliPathIsUserLevel { get; set; }
    public bool CodexCliPathNeedsAttention { get; set; }
    public bool ExistingWindowVisible { get; set; }
    public WindowInfo? ExistingWindow { get; set; }
    public ProbeResult? Probe { get; set; }
    public List<string> Findings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

internal sealed class RepairResult
{
    public bool Success { get; set; }
    public bool Cancelled { get; set; }
    public string Message { get; set; } = "";
    public string? RuntimeId { get; set; }
    public List<string> BackupPaths { get; set; } = [];
    public LaunchVerification? FirstLaunch { get; set; }
    public LaunchVerification? SecondLaunch { get; set; }
}

internal sealed record OperationProgress(string Stage, string Message, int Percent = -1);

internal sealed class ProcessDetails
{
    public int ProcessId { get; set; }
    public string Name { get; set; } = "";
    public string CommandLine { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
}
