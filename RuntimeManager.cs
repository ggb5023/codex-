using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CodexSelfCheck;

internal sealed partial class RuntimeManager
{
    internal static string RuntimeRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenAI", "Codex", "runtimes", "cua_node");

    internal static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexSelfCheckLauncher");

    internal static string BackupRoot => Path.Combine(DataRoot, "backups");

    [GeneratedRegex("^\\.staging-([0-9a-fA-F]{16})-", RegexOptions.CultureInvariant)]
    private static partial Regex StagingRegex();

    internal async Task<RuntimeInspection> InspectAsync(
        string sourcePath,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            progress?.Report(new("检测", "正在统计商店包运行时文件……"));
            var source = GetManifest(sourcePath, cancellationToken);
            var inspection = new RuntimeInspection
            {
                SourceFileCount = source.Count,
                SourceBytes = source.Sum(file => file.Length),
                AvailableBytes = GetAvailableBytes(RuntimeRoot)
            };

            var directories = Directory.Exists(RuntimeRoot)
                ? Directory.EnumerateDirectories(RuntimeRoot)
                    .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path))
                    .ToList()
                : [];
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                var match = StagingRegex().Match(name);
                if (match.Success)
                {
                    inspection.StagingDirectories.Add(directory);
                    inspection.StagingRuntimeId ??= match.Groups[1].Value.ToLowerInvariant();
                    continue;
                }

                if (name.StartsWith(".selfcheck-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = GetManifest(directory, cancellationToken);
                if (candidate.Count != source.Count || candidate.Sum(file => file.Length) != inspection.SourceBytes)
                {
                    continue;
                }

                if (SamplesMatch(source, candidate, cancellationToken))
                {
                    inspection.MatchingRuntimeId = name;
                    break;
                }
            }

            return inspection;
        }, cancellationToken);
    }

    internal static string? ExtractRuntimeId(string directoryName)
    {
        var match = StagingRegex().Match(Path.GetFileName(directoryName));
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    internal async Task<(string FinalPath, List<string> Backups)> InstallAsync(
        string sourcePath,
        string runtimeId,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(BackupRoot);
            var source = GetManifest(sourcePath, cancellationToken);
            var sourceBytes = source.Sum(file => file.Length);
            var required = sourceBytes + 100L * 1024 * 1024;
            if (GetAvailableBytes(RuntimeRoot) < required)
            {
                throw new IOException($"磁盘可用空间不足，需要至少 {FormatBytes(required)}。 ");
            }

            var backups = new List<string>();
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var finalPath = Path.Combine(RuntimeRoot, runtimeId);
            var tempPath = Path.Combine(RuntimeRoot, $".selfcheck-staging-{runtimeId}-{Guid.NewGuid():N}");
            string? originalRuntimeBackup = null;

            try
            {
                if (Directory.Exists(finalPath))
                {
                    originalRuntimeBackup = MoveToBackup(finalPath, $"runtime-{runtimeId}-{timestamp}");
                    backups.Add(originalRuntimeBackup);
                }

                foreach (var staging in Directory.EnumerateDirectories(RuntimeRoot, $".staging-{runtimeId}-*"))
                {
                    backups.Add(MoveToBackup(staging, $"failed-{Path.GetFileName(staging)}-{timestamp}"));
                }

                Directory.CreateDirectory(tempPath);
                long completedBytes = 0;
                var completedFiles = 0;
                foreach (var file in source)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = Path.Combine(tempPath, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    CopyProtectedFile(file.FullPath, destination, cancellationToken);
                    completedBytes += file.Length;
                    completedFiles++;
                    var percent = sourceBytes == 0 ? 100 : (int)(completedBytes * 65 / sourceBytes);
                    progress?.Report(new("复制", $"正在复制 {completedFiles}/{source.Count}：{file.RelativePath}", percent));
                }

                var destinationManifest = GetManifest(tempPath, cancellationToken);
                if (destinationManifest.Count != source.Count || destinationManifest.Sum(file => file.Length) != sourceBytes)
                {
                    throw new InvalidDataException("复制后的文件数量或总大小与源目录不一致。");
                }

                var destinationByPath = destinationManifest.ToDictionary(
                    file => file.RelativePath,
                    StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < source.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceFile = source[index];
                    if (!destinationByPath.TryGetValue(sourceFile.RelativePath, out var destinationFile))
                    {
                        throw new InvalidDataException($"目标目录缺少文件：{sourceFile.RelativePath}");
                    }

                    var sourceHash = HashFile(sourceFile.FullPath, cancellationToken);
                    var destinationHash = HashFile(destinationFile.FullPath, cancellationToken);
                    if (!sourceHash.AsSpan().SequenceEqual(destinationHash))
                    {
                        throw new InvalidDataException($"SHA-256 校验失败：{sourceFile.RelativePath}");
                    }

                    var percent = 65 + (int)((index + 1L) * 30 / Math.Max(1, source.Count));
                    progress?.Report(new("校验", $"正在校验 {index + 1}/{source.Count}：{sourceFile.RelativePath}", percent));
                }

                Directory.Move(tempPath, finalPath);
                progress?.Report(new("切换", $"运行时已原子切换为 {runtimeId}。", 95));
                return (finalPath, backups);
            }
            catch
            {
                TryDeleteDirectory(tempPath);
                if (!Directory.Exists(finalPath) &&
                    originalRuntimeBackup is not null &&
                    Directory.Exists(originalRuntimeBackup))
                {
                    Directory.Move(originalRuntimeBackup, finalPath);
                    backups.Remove(originalRuntimeBackup);
                }
                throw;
            }
        }, cancellationToken);
    }

    internal static void DeleteBackups(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(BackupRoot), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(path);
        }
    }

    private static List<FileEntry> GetManifest(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var files = new List<FileEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            files.Add(new FileEntry(Path.GetRelativePath(root, path), path, info.Length));
        }

        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        return files;
    }

    private static bool SamplesMatch(
        IReadOnlyList<FileEntry> source,
        IReadOnlyList<FileEntry> candidate,
        CancellationToken cancellationToken)
    {
        var byPath = candidate.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var sampleCount = Math.Min(16, source.Count);
        if (sampleCount == 0)
        {
            return false;
        }

        for (var sample = 0; sample < sampleCount; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = sampleCount == 1 ? 0 : sample * (source.Count - 1) / (sampleCount - 1);
            var sourceFile = source[index];
            if (!byPath.TryGetValue(sourceFile.RelativePath, out var candidateFile) ||
                candidateFile.Length != sourceFile.Length ||
                !HashFile(sourceFile.FullPath, cancellationToken).AsSpan()
                    .SequenceEqual(HashFile(candidateFile.FullPath, cancellationToken)))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return sha.Hash!;
    }

    private static void CopyProtectedFile(string source, string destination, CancellationToken cancellationToken)
    {
        var cancel = 0;
        NativeMethods.CopyProgressRoutine callback = (_, _, _, _, _, _, _, _, _) =>
            cancellationToken.IsCancellationRequested
                ? NativeMethods.CopyProgressResult.Cancel
                : NativeMethods.CopyProgressResult.Continue;
        if (!NativeMethods.CopyFileEx(
                NativeMethods.LongPath(source),
                NativeMethods.LongPath(destination),
                callback,
                nint.Zero,
                ref cancel,
                NativeMethods.CopyFileAllowDecryptedDestination))
        {
            if (cancellationToken.IsCancellationRequested || cancel != 0)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new Win32Exception(Marshal.GetLastWin32Error(), $"复制失败：{source}");
        }
    }

    private static string MoveToBackup(string source, string preferredName)
    {
        var destination = Path.Combine(BackupRoot, preferredName);
        var suffix = 1;
        while (Directory.Exists(destination))
        {
            destination = Path.Combine(BackupRoot, $"{preferredName}-{suffix++}");
        }

        Directory.Move(source, destination);
        return destination;
    }

    private static long GetAvailableBytes(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("无法确定运行时磁盘。 ");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // 保留无法清理的目录，供后续人工检查。
        }
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
