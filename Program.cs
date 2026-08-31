using System.Runtime.InteropServices;
using System.Text;

namespace CodexSelfCheck;

internal static class Program
{
    private const uint AttachParentProcess = 0xffffffff;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\CodexSelfCheckLauncher-v1", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Codex 自检启动器已经在运行。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        if (args.Length > 0)
        {
            NativeMethods.AttachConsole(AttachParentProcess);
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = Encoding.UTF8;
            var standardOutput = Console.OpenStandardOutput();
            var standardError = Console.OpenStandardError();
            if (standardOutput != Stream.Null)
                Console.SetOut(new StreamWriter(standardOutput, new UTF8Encoding(false)) { AutoFlush = true });
            if (standardError != Stream.Null)
                Console.SetError(new StreamWriter(standardError, new UTF8Encoding(false)) { AutoFlush = true });
            return await RunCommandLineAsync(args);
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            MessageBox.Show(eventArgs.Exception.Message, "未处理错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
        return 0;
    }

    private static async Task<int> RunCommandLineAsync(string[] args)
    {
        var reports = new ReportService();
        var service = new SelfCheckService(reports);
        var progress = new Progress<OperationProgress>(item =>
        {
            Console.WriteLine($"[{item.Stage}] {item.Message}");
        });

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--scan":
                {
                    var report = await service.ScanAsync(true, progress, CancellationToken.None);
                    Console.WriteLine(ReportService.BuildText(report));
                    return ExitCode(report.Status);
                }
                case "--repair":
                {
                    var report = await service.ScanAsync(true, progress, CancellationToken.None);
                    if (report.Status != HealthStatus.Repairable)
                    {
                        Console.WriteLine(report.Summary);
                        return ExitCode(report.Status);
                    }

                    var result = await service.RepairAsync(report, progress, CancellationToken.None);
                    Console.WriteLine(result.Message);
                    return result.Success ? 0 : result.Cancelled ? 5 : 4;
                }
                case "--launch":
                {
                    var report = await service.ScanAsync(false, progress, CancellationToken.None);
                    if (report.Package is null) return 3;
                    service.Launch(report.Package);
                    Console.WriteLine("已发送 Codex 启动请求。");
                    return 0;
                }
                case "--report" when args.Length >= 2:
                {
                    var report = await service.ScanAsync(true, progress, CancellationToken.None);
                    var paths = reports.Export(report, args[1]);
                    Console.WriteLine($"已生成：{paths.TextPath}");
                    Console.WriteLine($"已生成：{paths.JsonPath}");
                    return ExitCode(report.Status);
                }
                default:
                    Console.WriteLine("用法：CodexSelfCheck.exe --scan | --repair | --launch | --report <path>");
                    return 1;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 4;
        }
    }

    private static int ExitCode(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => 0,
        HealthStatus.Repairable => 2,
        HealthStatus.ManualAction => 3,
        _ => 4
    };
}
