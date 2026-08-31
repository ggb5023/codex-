using System.Diagnostics;
using System.Text.Json;

namespace CodexSelfCheck;

internal sealed class AppProcessService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal IReadOnlySet<int> GetChatGptProcessIds()
    {
        return Process.GetProcessesByName("ChatGPT")
            .Select(process =>
            {
                using (process) return process.Id;
            })
            .ToHashSet();
    }

    internal WindowInfo? FindVisibleWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    if (handle == nint.Zero || !NativeMethods.IsWindowVisible(handle) ||
                        !NativeMethods.GetWindowRect(handle, out var rect))
                    {
                        continue;
                    }

                    var screen = SystemInformation.VirtualScreen;
                    var onScreen = rect.Right > screen.Left && rect.Left < screen.Right &&
                                   rect.Bottom > screen.Top && rect.Top < screen.Bottom;
                    return new WindowInfo
                    {
                        ProcessId = process.Id,
                        Handle = handle,
                        Title = process.MainWindowTitle,
                        Left = rect.Left,
                        Top = rect.Top,
                        Right = rect.Right,
                        Bottom = rect.Bottom,
                        IsOnScreen = onScreen
                    };
                }
                catch
                {
                    // 进程可能在枚举时退出。
                }
            }
        }

        return null;
    }

    internal void Start(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            throw new InvalidOperationException("未找到应用的 AppUserModelID。");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{appUserModelId}",
            UseShellExecute = true
        });
    }

    internal async Task<ProbeResult> ProbeAsync(
        string appUserModelId,
        TimeSpan timeout,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingWindow = FindVisibleWindow();
        if (existingWindow is not null)
        {
            return new ProbeResult
            {
                WindowVisible = true,
                Window = existingWindow,
                Message = "Codex 已有可见窗口，未执行探测启动。"
            };
        }

        var before = GetChatGptProcessIds();
        var knownStaging = Directory.Exists(RuntimeManager.RuntimeRoot)
            ? Directory.EnumerateDirectories(RuntimeManager.RuntimeRoot, ".staging-*").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Start(appUserModelId);
        var startedAt = DateTime.UtcNow;

        while (DateTime.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
            progress?.Report(new("探测", $"正在观察 Codex 启动状态……{elapsed}/{(int)timeout.TotalSeconds} 秒"));

            var window = FindVisibleWindow();
            var after = GetChatGptProcessIds();
            var started = after.Except(before).ToList();
            if (window is not null)
            {
                return new ProbeResult
                {
                    WindowVisible = true,
                    Window = window,
                    StartedProcessIds = started,
                    Message = "探测启动成功，图形窗口可见。"
                };
            }

            if (Directory.Exists(RuntimeManager.RuntimeRoot))
            {
                var staging = Directory.EnumerateDirectories(RuntimeManager.RuntimeRoot, ".staging-*")
                    .FirstOrDefault(path => !knownStaging.Contains(path));
                if (staging is not null)
                {
                    return new ProbeResult
                    {
                        RuntimeId = RuntimeManager.ExtractRuntimeId(staging),
                        StagingDirectory = staging,
                        StartedProcessIds = started,
                        Message = "检测到新版运行时的未完成 staging 目录。"
                    };
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        return new ProbeResult
        {
            StartedProcessIds = GetChatGptProcessIds().Except(before).ToList(),
            Message = "探测超时：没有可见窗口，也没有生成可识别的 staging 目录。"
        };
    }

    internal async Task<LaunchVerification> VerifyLaunchAsync(
        PackageInfo package,
        TimeSpan timeout,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var before = GetChatGptProcessIds();
        Start(package.AppUserModelId);
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new("验证", "正在等待窗口、renderer 和 app-server……"));
            var window = FindVisibleWindow();
            if (window is not null && window.IsOnScreen)
            {
                var details = await GetProcessDetailsAsync(cancellationToken);
                var renderer = details.Any(item => item.CommandLine.Contains("--type=renderer", StringComparison.OrdinalIgnoreCase));
                var appServer = IsPackagedAppServer(details, package);
                if (renderer && appServer)
                {
                    return new LaunchVerification
                    {
                        Success = true,
                        WindowVisible = true,
                        RendererFound = true,
                        AppServerFound = true,
                        Window = window,
                        StartedProcessIds = GetChatGptProcessIds().Except(before).ToList(),
                        Message = "窗口、renderer 和 app-server 均已验证。"
                    };
                }
            }

            await Task.Delay(750, cancellationToken);
        }

        var finalWindow = FindVisibleWindow();
        var finalDetails = await GetProcessDetailsAsync(cancellationToken);
        var finalRenderer = finalDetails.Any(item => item.CommandLine.Contains("--type=renderer", StringComparison.OrdinalIgnoreCase));
        var finalAppServer = IsPackagedAppServer(finalDetails, package);
        return new LaunchVerification
        {
            Success = false,
            WindowVisible = finalWindow?.IsOnScreen == true,
            RendererFound = finalRenderer,
            AppServerFound = finalAppServer,
            Window = finalWindow,
            StartedProcessIds = GetChatGptProcessIds().Except(before).ToList(),
            Message = "启动验证超时。"
        };
    }

    private static bool IsPackagedAppServer(IEnumerable<ProcessDetails> details, PackageInfo package)
    {
        return details.Any(process =>
            process.Name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase) &&
            process.CommandLine.Contains("app-server", StringComparison.OrdinalIgnoreCase) &&
            (process.CommandLine.Contains(package.InstallLocation, StringComparison.OrdinalIgnoreCase) ||
             process.ExecutablePath.Contains(package.InstallLocation, StringComparison.OrdinalIgnoreCase)));
    }

    internal void StopProcesses(IEnumerable<int> processIds)
    {
        foreach (var processId in processIds.Distinct())
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
            catch
            {
                // 已退出或拒绝访问时继续处理其他进程。
            }
        }
    }

    private static async Task<List<ProcessDetails>> GetProcessDetailsAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $items = Get-CimInstance Win32_Process | Where-Object {
                $_.Name -ieq 'ChatGPT.exe' -or $_.Name -ieq 'codex.exe'
            } | ForEach-Object {
                [pscustomobject]@{
                    ProcessId = [int]$_.ProcessId
                    Name = [string]$_.Name
                    CommandLine = [string]$_.CommandLine
                    ExecutablePath = [string]$_.ExecutablePath
                }
            }
            ConvertTo-Json -InputObject @($items) -Compress
            """;
        try
        {
            var json = await PowerShellBridge.RunAsync(script, cancellationToken);
            return JsonSerializer.Deserialize<List<ProcessDetails>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
