using System.Text.Json;

namespace CodexSelfCheck;

internal sealed class PackageDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal async Task<PackageInfo?> FindAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $p = Get-AppxPackage OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1
            if ($null -eq $p) { return }
            [xml]$manifest = Get-Content -LiteralPath (Join-Path $p.InstallLocation 'AppxManifest.xml') -Raw
            $app = @($manifest.Package.Applications.Application)[0]
            [pscustomobject]@{
                Name = $p.Name
                Version = $p.Version.ToString()
                Architecture = $p.Architecture.ToString()
                Status = $p.Status.ToString()
                InstallLocation = $p.InstallLocation
                PackageFamilyName = $p.PackageFamilyName
                ApplicationId = $app.Id
                AppUserModelId = "$($p.PackageFamilyName)!$($app.Id)"
            } | ConvertTo-Json -Compress
            """;
        var json = await PowerShellBridge.RunAsync(script, cancellationToken);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<PackageInfo>(json, JsonOptions);
    }

    internal async Task<SignatureInfo> GetSignatureAsync(string path, CancellationToken cancellationToken)
    {
        var escaped = path.Replace("'", "''");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            if ($null -eq (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
                [pscustomobject]@{
                    Status = 'Unavailable'
                    StatusMessage = '当前 Windows PowerShell 无法加载 Microsoft.PowerShell.Security，无法验证数字签名。'
                    Subject = ''
                    Thumbprint = ''
                } | ConvertTo-Json -Compress
                return
            }
            $s = Get-AuthenticodeSignature -LiteralPath '{{escaped}}'
            [pscustomobject]@{
                Status = $s.Status.ToString()
                StatusMessage = $s.StatusMessage
                Subject = if ($s.SignerCertificate) { $s.SignerCertificate.Subject } else { '' }
                Thumbprint = if ($s.SignerCertificate) { $s.SignerCertificate.Thumbprint } else { '' }
            } | ConvertTo-Json -Compress
            """;
        var json = await PowerShellBridge.RunAsync(script, cancellationToken);
        return JsonSerializer.Deserialize<SignatureInfo>(json, JsonOptions) ?? new SignatureInfo();
    }
}
