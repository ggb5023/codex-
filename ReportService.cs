using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CodexSelfCheck;

internal sealed class ReportService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal string LogPath { get; }

    internal ReportService()
    {
        Directory.CreateDirectory(RuntimeManager.DataRoot);
        LogPath = Path.Combine(RuntimeManager.DataRoot, "CodexSelfCheck.log");
    }

    internal void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // 日志失败不阻断诊断与修复。
        }
    }

    internal (string TextPath, string JsonPath) SaveLatest(DiagnosticReport report)
    {
        return Export(report, Path.Combine(RuntimeManager.DataRoot, "latest-report"));
    }

    internal (string TextPath, string JsonPath) Export(DiagnosticReport report, string requestedPath)
    {
        var extension = Path.GetExtension(requestedPath);
        var basePath = string.IsNullOrEmpty(extension)
            ? requestedPath
            : requestedPath[..^extension.Length];
        var directory = Path.GetDirectoryName(Path.GetFullPath(basePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var textPath = basePath + ".txt";
        var jsonPath = basePath + ".json";
        File.WriteAllText(textPath, Redact(BuildText(report)), Encoding.UTF8);
        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(jsonPath, RedactJson(json), Encoding.UTF8);
        return (textPath, jsonPath);
    }

    internal static string BuildText(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Codex 桌面版自检报告");
        builder.AppendLine($"时间：{report.Timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"状态：{StatusText(report.Status)}");
        builder.AppendLine($"结论：{report.Summary}");
        if (report.Package is not null)
        {
            builder.AppendLine($"商店包：{report.Package.Name} {report.Package.Version} {report.Package.Architecture}");
            builder.AppendLine($"包状态：{report.Package.Status}");
            builder.AppendLine($"安装目录：{report.Package.InstallLocation}");
            builder.AppendLine($"应用 ID：{report.Package.AppUserModelId}");
        }

        if (report.Signature is not null)
        {
            builder.AppendLine($"数字签名：{report.Signature.Status} {report.Signature.Subject}");
        }

        if (report.Runtime is not null)
        {
            builder.AppendLine($"源运行时：{report.Runtime.SourceFileCount} 个文件，{RuntimeManager.FormatBytes(report.Runtime.SourceBytes)}");
            builder.AppendLine($"匹配运行时：{report.Runtime.MatchingRuntimeId ?? "无"}");
            builder.AppendLine($"预期运行时：{report.ExpectedRuntimeId ?? "未知"}");
            builder.AppendLine($"未完成 staging：{report.Runtime.StagingDirectories.Count} 个");
            builder.AppendLine($"磁盘可用：{RuntimeManager.FormatBytes(report.Runtime.AvailableBytes)}");
        }

        builder.AppendLine($"CODEX_CLI_PATH：{report.CodexCliPath ?? "未设置"}（{report.CodexCliPathScope}）");
        builder.AppendLine();
        builder.AppendLine("发现：");
        foreach (var finding in report.Findings) builder.AppendLine($"- {finding}");
        if (report.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("错误：");
            foreach (var error in report.Errors) builder.AppendLine($"- {error}");
        }

        return builder.ToString();
    }

    internal static string StatusText(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "正常",
        HealthStatus.Repairable => "可自动修复",
        HealthStatus.ManualAction => "需要人工处理",
        HealthStatus.Repairing => "正在修复",
        HealthStatus.VerificationFailed => "验证失败",
        _ => "未知"
    };

    private static string Redact(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile)
            ? value
            : value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactJson(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return value;
        return value
            .Replace(profile.Replace("\\", "\\\\"), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
}
