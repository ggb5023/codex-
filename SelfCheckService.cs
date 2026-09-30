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
                AddCheck(report, "PACKAGE_NOT_FOUND", "商店包", CheckStatus.ManualAction, CheckSeverity.Error,
                    "未检测到 OpenAI.Codex Microsoft Store 软件包。", "请从 Microsoft Store 安装官方应用。", false);
                return Finish(report, HealthStatus.ManualAction,
                    "未检测到 OpenAI.Codex Microsoft Store 软件包。",
                    "请先从 Microsoft Store 安装官方应用。");
            }

            report.Findings.Add($"商店包：{report.Package.Name} {report.Package.Version} {report.Package.Architecture}，状态 {report.Package.Status}。");
            AddCheck(report, "PACKAGE_STATUS", "商店包", CheckStatus.Healthy, CheckSeverity.Info,
                $"{report.Package.Name} {report.Package.Version} 状态正常。", "无需处理。", false);
            if (!report.Package.Status.Equals("Ok", StringComparison.OrdinalIgnoreCase))
            {
                AddCheck(report, "PACKAGE_NOT_READY", "商店包", CheckStatus.ManualAction, CheckSeverity.Error,
                    $"商店包状态为 {report.Package.Status}。", "请在 Windows 设置中修复或重新安装应用。", false);
                return Finish(report, HealthStatus.ManualAction,
                    "商店包状态不是 Ok，自检启动器不会修改损坏的软件包。",
                    "请在 Windows 设置中修复应用或重新安装商店包。");
            }

            if (!Environment.Is64BitOperatingSystem ||
                !report.Package.Architecture.Equals("X64", StringComparison.OrdinalIgnoreCase))
            {
                AddCheck(report, "PACKAGE_ARCH_UNSUPPORTED", "商店包", CheckStatus.ManualAction, CheckSeverity.Error,
                    "系统或商店包不是受支持的 Windows x64 架构。", "请使用 Windows x64 和 x64 商店包。", false);
                return Finish(report, HealthStatus.ManualAction,
                    "首版自检启动器只支持 Windows x64 和 x64 商店包。",
                    "当前系统或软件包架构不在支持范围内。");
            }

            if (!File.Exists(report.Package.CodexExePath) || !Directory.Exists(report.Package.RuntimeSourcePath))
            {
                AddCheck(report, "RUNTIME_SOURCE_MISSING", "文件", CheckStatus.ManualAction, CheckSeverity.Error,
                    "商店包缺少 codex.exe 或 cua_node 运行时资源。", "请在 Windows 设置中修复或重新安装应用。", false);
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
                var signatureUnavailable = report.Signature.Status.Equals("Unavailable", StringComparison.OrdinalIgnoreCase);
                AddCheck(report, signatureUnavailable ? "CODEX_SIGNATURE_UNAVAILABLE" : "CODEX_SIGNATURE_INVALID", "签名",
                    CheckStatus.ManualAction, signatureUnavailable ? CheckSeverity.Warning : CheckSeverity.Critical,
                    signatureUnavailable
                        ? $"无法验证 codex.exe 数字签名：{report.Signature.StatusMessage}"
                        : $"codex.exe 数字签名无效：{report.Signature.StatusMessage}",
                    signatureUnavailable
                        ? "修复 Microsoft.PowerShell.Security 后重新检测。"
                        : "不要替换该文件，请修复或重新安装 Store 应用。", false);
                return Finish(report, HealthStatus.ManualAction,
                    signatureUnavailable ? "无法验证 codex.exe 数字签名，自检启动器已停止。" : "codex.exe 数字签名无效，自检启动器已停止。",
                    report.Signature.StatusMessage);
            }
            AddCheck(report, "CODEX_SIGNATURE", "签名", CheckStatus.Healthy, CheckSeverity.Info,
                $"codex.exe 已通过 OpenAI 数字签名验证。", "无需处理。", false);

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
                AddCheck(report, "CODEX_CLI_PATH_INVALID", "环境变量",
                    report.CodexCliPathIsUserLevel ? CheckStatus.Repairable : CheckStatus.ManualAction,
                    CheckSeverity.Warning,
                    $"CODEX_CLI_PATH 指向无效路径：{report.CodexCliPath}",
                    report.CodexCliPathIsUserLevel ? "确认后清除失效的用户级变量。" : "请在系统环境变量或父进程中清除该变量。",
                    report.CodexCliPathIsUserLevel);
                report.Findings.Add(report.CodexCliPathIsUserLevel
                    ? "用户级 CODEX_CLI_PATH 无效或指向 VS Code 扩展，建议在确认后清除。"
                    : "非用户级 CODEX_CLI_PATH 无效；自检启动器不会自动修改它。");
            }
            else
            {
                AddCheck(report, "CODEX_CLI_PATH", "环境变量", CheckStatus.Healthy, CheckSeverity.Info,
                    string.IsNullOrWhiteSpace(report.CodexCliPath) ? "未设置 CODEX_CLI_PATH。" : "CODEX_CLI_PATH 路径有效。",
                    "无需处理。", false);
            }

            report.ExistingWindow = _processes.FindVisibleWindow();
            report.ExistingWindowVisible = report.ExistingWindow?.IsOnScreen == true;
            if (report.ExistingWindowVisible)
            {
                AddCheck(report, "CODEX_PROCESS_VISIBLE", "进程", CheckStatus.Warning, CheckSeverity.Warning,
                    "检测到已有可见 Codex 窗口。", "关闭 Codex 后再执行运行时修复。", false);
                report.Findings.Add("检测到已有 Codex/ChatGPT 可见窗口，不会执行探测启动或终止该会话。");
            }

            report.Runtime = await _runtimes.InspectAsync(report.Package.RuntimeSourcePath, progress, cancellationToken);
            var diskSpaceLow = report.Runtime.AvailableBytes < report.Runtime.SourceBytes + 100L * 1024 * 1024;
            AddCheck(report, "DISK_SPACE", "系统", !diskSpaceLow
                ? CheckStatus.Healthy : CheckStatus.ManualAction, CheckSeverity.Warning,
                $"可用空间：{RuntimeManager.FormatBytes(report.Runtime.AvailableBytes)}",
                "释放运行时所在磁盘空间后重新检测。", false);
            report.ExpectedRuntimeId = report.Runtime.MatchingRuntimeId ?? report.Runtime.StagingRuntimeId;
            report.Findings.Add($"包内 cua_node：{report.Runtime.SourceFileCount} 个文件，{RuntimeManager.FormatBytes(report.Runtime.SourceBytes)}。");
            AddCheck(report, "RUNTIME_SOURCE_READABLE", "权限", report.Runtime.SourceReadable ? CheckStatus.Healthy : CheckStatus.ManualAction,
                CheckSeverity.Error,
                report.Runtime.SourceReadable ? "商店包运行时资源可读取。" : "商店包运行时资源为空或无法读取。",
                "请关闭 Codex 更新进程，或在 Windows 设置中修复 Store 应用。", false);
            AddCheck(report, "RUNTIME_ROOT_WRITABLE", "权限", report.Runtime.RuntimeRootWritable ? CheckStatus.Healthy : CheckStatus.ManualAction,
                CheckSeverity.Error,
                report.Runtime.RuntimeRootWritable ? "本地运行时目录可写。" : $"本地运行时目录不可写：{report.Runtime.AccessError}",
                "请关闭 Codex 后重试；若仍失败，请检查权限或安全软件拦截。", false);
            if (report.Runtime.MatchingRuntimeId is not null)
            {
                AddCheck(report, "RUNTIME_MATCHED", "运行时", CheckStatus.Healthy, CheckSeverity.Info,
                    $"已匹配本地运行时 {report.Runtime.MatchingRuntimeId}。", "无需修复。", false);
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

            if (diskSpaceLow && report.Runtime.MatchingRuntimeId is null)
            {
                return Finish(report, HealthStatus.ManualAction,
                    "运行时所在磁盘可用空间不足，无法安全执行修复。",
                    "释放磁盘空间后重新检测。 ");
            }

            if (!report.Runtime.RuntimeRootWritable && report.Runtime.MatchingRuntimeId is null)
            {
                return Finish(report, HealthStatus.ManualAction,
                    "本地运行时目录不可写，无法安全执行修复。",
                    report.Runtime.AccessError ?? "请检查权限或安全软件拦截。");
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
                AddCheck(report, "RUNTIME_STAGING_FOUND", "运行时", CheckStatus.Repairable, CheckSeverity.Warning,
                    $"发现未完成 staging，运行时 ID 为 {report.Runtime.StagingRuntimeId}。", "确认后重新部署运行时并验证启动。", true);
                report.RepairPlan.Add($"部署运行时 {report.Runtime.StagingRuntimeId}，并保留旧目录备份");
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
                result.Steps.Add("清除失效的用户级 CODEX_CLI_PATH");
                progress?.Report(new("环境", "正在清除失效的用户级 CODEX_CLI_PATH……", 1));
                Environment.SetEnvironmentVariable("CODEX_CLI_PATH", null, EnvironmentVariableTarget.User);
                NativeMethods.BroadcastEnvironmentChange();
            }

            var runtimeNeedsRepair = report.Runtime?.MatchingRuntimeId is null;
            var desktopProcesses = _processes.GetChatGptProcessIds();
            if (desktopProcesses.Count > 0)
            {
                result.Steps.Add($"关闭 {desktopProcesses.Count} 个 Codex 相关进程");
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
                result.Steps.Add($"部署并校验运行时 {report.ExpectedRuntimeId}");
            }

            progress?.Report(new("验证", "正在执行第一次冷启动验证……", 96));
            result.FirstLaunch = await _processes.VerifyLaunchAsync(
                report.Package,
                TimeSpan.FromSeconds(35),
                progress,
                cancellationToken);
            if (!result.FirstLaunch.Success)
            {
                result.Errors.Add(result.FirstLaunch.Message);
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
            if (!result.Success) result.Errors.Add(result.SecondLaunch.Message);
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
            result.Errors.Add(exception.Message);
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

    private static void AddCheck(
        DiagnosticReport report,
        string id,
        string category,
        CheckStatus status,
        CheckSeverity severity,
        string summary,
        string recommendation,
        bool autoFixable)
    {
        report.Checks.Add(new CheckResult
        {
            Id = id,
            Category = category,
            Status = status,
            Severity = severity,
            Summary = summary,
            Evidence = [summary],
            Recommendation = recommendation,
            AutoFixable = autoFixable
        });
    }

    private static bool NeedsCliPathAttention(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return normalized.Contains(".vscode\\extensions", StringComparison.OrdinalIgnoreCase) ||
               !File.Exists(normalized);
    }
}
