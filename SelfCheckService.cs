namespace CodexSelfCheck;

internal sealed class SelfCheckService
{
    private readonly PackageDiscovery _packages = new();
    private readonly RuntimeManager _runtimes = new();
    private readonly AppProcessService _processes = new();
    private readonly ReportService _reports;

    internal SelfCheckService(ReportService reports)
    {
        _reports = reports;
    }

    internal async Task<DiagnosticReport> ScanAsync(
        bool allowProbe,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var report = new DiagnosticReport { Status = HealthStatus.Unknown };
        try
        {
            progress?.Report(new("检测", "正在查找 OpenAI.Codex 商店包……", 2));
            report.Package = await _packages.FindAsync(cancellationToken);
            if (report.Package is null)
            {
                return Finish(report, HealthStatus.ManualAction,
                    "未检测到 OpenAI.Codex Microsoft Store 软件包。",
                    "请先从 Microsoft Store 安装官方应用。");
            }

            report.Findings.Add($"商店包：{report.Package.Name} {report.Package.Version} {report.Package.Architecture}，状态 {report.Package.Status}。");
            if (!report.Package.Status.Equals("Ok", StringComparison.OrdinalIgnoreCase))
            {
                return Finish(report, HealthStatus.ManualAction,
                    "商店包状态不是 Ok，自检启动器不会修改损坏的软件包。",
                    "请在 Windows 设置中修复应用或重新安装商店包。");
            }

            if (!Environment.Is64BitOperatingSystem ||
                !report.Package.Architecture.Equals("X64", StringComparison.OrdinalIgnoreCase))
            {
                return Finish(report, HealthStatus.ManualAction,
                    "首版自检启动器只支持 Windows x64 和 x64 商店包。",
                    "当前系统或软件包架构不在支持范围内。");
            }

            if (!File.Exists(report.Package.CodexExePath) || !Directory.Exists(report.Package.RuntimeSourcePath))
            {
                return Finish(report, HealthStatus.ManualAction,
                    "商店包缺少 codex.exe 或 cua_node 运行时资源。",
                    "不会对来源不完整的软件包执行修复。");
            }

            progress?.Report(new("检测", "正在验证 OpenAI 数字签名……", 5));
            report.Signature = await _packages.GetSignatureAsync(report.Package.CodexExePath, cancellationToken);
            report.Findings.Add($"codex.exe 数字签名状态：{report.Signature.Status}。");
            if (!report.Signature.IsValid ||
                !report.Signature.Subject.Contains("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return Finish(report, HealthStatus.ManualAction,
                    "codex.exe 数字签名无效，自检启动器已停止。",
                    report.Signature.StatusMessage);
            }

            var userCliPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.User);
            var machineCliPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH", EnvironmentVariableTarget.Machine);
            report.CodexCliPath = userCliPath ?? machineCliPath ??
                                  Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            report.CodexCliPathIsUserLevel = !string.IsNullOrWhiteSpace(userCliPath);
            report.CodexCliPathScope = report.CodexCliPath is null
                ? "未设置"
                : report.CodexCliPathIsUserLevel ? "用户级" : machineCliPath is not null ? "系统级" : "当前进程";
            report.CodexCliPathNeedsAttention = NeedsCliPathAttention(report.CodexCliPath);
            if (report.CodexCliPathNeedsAttention)
            {
                report.Findings.Add(report.CodexCliPathIsUserLevel
                    ? "用户级 CODEX_CLI_PATH 无效或指向 VS Code 扩展，建议在确认后清除。"
                    : "非用户级 CODEX_CLI_PATH 无效；自检启动器不会自动修改它。");
            }

            report.ExistingWindow = _processes.FindVisibleWindow();
            report.ExistingWindowVisible = report.ExistingWindow?.IsOnScreen == true;
            if (report.ExistingWindowVisible)
            {
                report.Findings.Add("检测到已有 Codex/ChatGPT 可见窗口，不会执行探测启动或终止该会话。");
            }

            report.Runtime = await _runtimes.InspectAsync(report.Package.RuntimeSourcePath, progress, cancellationToken);
            report.ExpectedRuntimeId = report.Runtime.MatchingRuntimeId ?? report.Runtime.StagingRuntimeId;
            report.Findings.Add($"包内 cua_node：{report.Runtime.SourceFileCount} 个文件，{RuntimeManager.FormatBytes(report.Runtime.SourceBytes)}。");
            if (report.Runtime.MatchingRuntimeId is not null)
            {
                report.Findings.Add($"完整匹配的本地运行时：{report.Runtime.MatchingRuntimeId}。");
            }
            if (report.Runtime.StagingDirectories.Count > 0)
            {
                report.Findings.Add($"发现 {report.Runtime.StagingDirectories.Count} 个未完成 staging 目录。");
            }

            if (report.CodexCliPathNeedsAttention && !report.CodexCliPathIsUserLevel)
            {
                return Finish(report, HealthStatus.ManualAction,
                    "检测到非用户级的失效 CODEX_CLI_PATH。",
                    "请先在系统环境或启动 Codex 的父进程中清除此变量，再重新检测。");
            }

            if (report.Runtime.MatchingRuntimeId is not null)
            {
                if (report.CodexCliPathNeedsAttention)
                {
                    return Finish(report, HealthStatus.Repairable,
                        "本地运行时完整，但 CODEX_CLI_PATH 需要清理。",
                        "确认修复后只会清除失效的用户级环境变量。");
                }

                return Finish(report, HealthStatus.Healthy,
                    "Codex 商店包、数字签名和本地运行时均正常。",
                    "未执行任何修复操作。");
            }

            if (report.Runtime.StagingRuntimeId is not null)
            {
                return Finish(report, HealthStatus.Repairable,
                    "检测到新版运行时部署不完整，可以自动修复。",
                    $"预期运行时 ID：{report.Runtime.StagingRuntimeId}");
            }

            if (report.ExistingWindowVisible)
            {
                return Finish(report, HealthStatus.Healthy,
                    "Codex 当前已有可见窗口；未找到与包资源匹配的缓存，但不会干扰正在运行的会话。",
                    "关闭应用后可重新运行深度检测。");
            }

            if (!allowProbe)
            {
                return Finish(report, HealthStatus.ManualAction,
                    "没有匹配运行时，需要执行受控探测才能确定新版运行时 ID。",
                    "请在图形界面点击“开始检测”。");
            }

            progress?.Report(new("探测", "运行时 ID 未知，准备受控启动一次 Codex……", 10));
            report.Probe = await _processes.ProbeAsync(
                report.Package.AppUserModelId,
                TimeSpan.FromSeconds(70),
                progress,
                cancellationToken);
            report.Findings.Add(report.Probe.Message);
            if (report.Probe.WindowVisible)
            {
                return Finish(report, HealthStatus.Healthy,
                    "Codex 受控探测启动成功，窗口可见。",
                    "不需要修复运行时。");
            }

            if (report.Probe.RuntimeId is not null)
            {
                report.ExpectedRuntimeId = report.Probe.RuntimeId;
                return Finish(report, HealthStatus.Repairable,
                    "受控探测确认新版运行时部署失败，可以自动修复。",
                    $"预期运行时 ID：{report.Probe.RuntimeId}");
            }

            return Finish(report, HealthStatus.ManualAction,
                "Codex 没有显示窗口，也没有生成可识别的 staging 目录。",
                "该故障不符合已知运行时复制问题，已停止自动修复。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _reports.Log($"检测失败：{exception}");
            report.Errors.Add(exception.Message);
            return Finish(report, HealthStatus.ManualAction, "自检过程中发生错误。", exception.Message);
        }
        finally
        {
            try
            {
                _reports.SaveLatest(report);
            }
            catch (Exception exception)
            {
                _reports.Log($"保存最新报告失败：{exception}");
            }
        }
    }

    internal async Task<RepairResult> RepairAsync(
        DiagnosticReport report,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new RepairResult { RuntimeId = report.ExpectedRuntimeId };
        try
        {
            if (report.Status != HealthStatus.Repairable || report.Package is null)
            {
                throw new InvalidOperationException("当前检测结果不允许自动修复。");
            }

            if (report.ExistingWindowVisible)
            {
                throw new InvalidOperationException("Codex 已有可见窗口。请关闭窗口后再修复。");
            }

            if (report.CodexCliPathNeedsAttention && report.CodexCliPathIsUserLevel)
            {
                progress?.Report(new("环境", "正在清除失效的用户级 CODEX_CLI_PATH……", 1));
                Environment.SetEnvironmentVariable("CODEX_CLI_PATH", null, EnvironmentVariableTarget.User);
                NativeMethods.BroadcastEnvironmentChange();
            }

            var runtimeNeedsRepair = report.Runtime?.MatchingRuntimeId is null;
            var desktopProcesses = _processes.GetChatGptProcessIds();
            if (desktopProcesses.Count > 0)
            {
                progress?.Report(new("准备", "正在关闭 ChatGPT/Codex 桌面进程……", 2));
                _processes.StopProcesses(desktopProcesses);
            }
            if (runtimeNeedsRepair)
            {
                if (string.IsNullOrWhiteSpace(report.ExpectedRuntimeId))
                {
                    throw new InvalidOperationException("无法确定新版运行时 ID，禁止猜测目标目录。");
                }

                if (report.Probe?.StartedProcessIds.Count > 0)
                {
                    progress?.Report(new("准备", "正在关闭本次探测产生的桌面进程……", 2));
                    _processes.StopProcesses(report.Probe.StartedProcessIds);
                }

                var installed = await _runtimes.InstallAsync(
                    report.Package.RuntimeSourcePath,
                    report.ExpectedRuntimeId,
                    progress,
                    cancellationToken);
                result.BackupPaths.AddRange(installed.Backups);
            }

            progress?.Report(new("验证", "正在执行第一次冷启动验证……", 96));
            result.FirstLaunch = await _processes.VerifyLaunchAsync(
                report.Package,
                TimeSpan.FromSeconds(35),
                progress,
                cancellationToken);
            if (!result.FirstLaunch.Success)
            {
                result.Message = "运行时已处理，但第一次启动验证失败。";
                return result;
            }

            _processes.StopProcesses(result.FirstLaunch.StartedProcessIds);
            await Task.Delay(1500, cancellationToken);
            progress?.Report(new("验证", "正在执行第二次冷启动验证……", 98));
            result.SecondLaunch = await _processes.VerifyLaunchAsync(
                report.Package,
                TimeSpan.FromSeconds(35),
                progress,
                cancellationToken);
            result.Success = result.SecondLaunch.Success;
            result.Message = result.Success
                ? "修复成功：连续两次启动均验证了可见窗口、renderer 和 app-server。"
                : "第一次启动成功，但第二次启动验证失败。";
            progress?.Report(new("完成", result.Message, 100));
            _reports.Log(result.Message);
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            result.Message = "操作已取消，未完成的自检临时目录已清理，原运行时未被覆盖。";
            _reports.Log(result.Message);
            return result;
        }
        catch (Exception exception)
        {
            result.Message = $"修复失败：{exception.Message}";
            _reports.Log($"修复失败：{exception}");
            return result;
        }
    }

    internal void Launch(PackageInfo package) => _processes.Start(package.AppUserModelId);

    private DiagnosticReport Finish(DiagnosticReport report, HealthStatus status, string summary, string finding)
    {
        report.Status = status;
        report.Summary = summary;
        if (!string.IsNullOrWhiteSpace(finding)) report.Findings.Add(finding);
        _reports.Log($"{ReportService.StatusText(status)}：{summary}");
        return report;
    }

    private static bool NeedsCliPathAttention(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return normalized.Contains(".vscode\\extensions", StringComparison.OrdinalIgnoreCase) ||
               !File.Exists(normalized);
    }
}
