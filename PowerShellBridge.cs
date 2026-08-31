using System.Diagnostics;
using System.Text;

namespace CodexSelfCheck;

internal static class PowerShellBridge
{
    internal static async Task<string> RunAsync(string script, CancellationToken cancellationToken = default)
    {
        script = "$utf8 = New-Object System.Text.UTF8Encoding($false)\n" +
                 "[Console]::OutputEncoding = $utf8\n" +
                 "$OutputEncoding = $utf8\n" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"PowerShell 返回退出码 {process.ExitCode}。"
                : error.Trim());
        }

        return output.Trim().TrimStart('\uFEFF');
    }
}
