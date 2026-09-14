using System.Diagnostics;
using System.Text.Json;

namespace ShadePilot.DriverEnhancement;

/// <summary>
/// Explicit Intel Sharpness write/restore boundary. No method is called automatically.
/// </summary>
public sealed class IntelSharpnessControlProvider
{
    private readonly string controlPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IntelSharpnessControlProvider(string controlPath)
    {
        this.controlPath = Path.GetFullPath(controlPath ?? throw new ArgumentNullException(nameof(controlPath)));
    }

    public Task<SharpnessWriteResult> SetAsync(SharpnessWriteRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new[] { "--set", request.OutputIndex.ToString(), request.Enabled ? "1" : "0", request.FilterType.ToString(), request.Intensity.ToString(System.Globalization.CultureInfo.InvariantCulture), request.BackupPath }, cancellationToken);

    public Task<SharpnessWriteResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new[] { "--restore", backupPath }, cancellationToken);

    private async Task<SharpnessWriteResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(controlPath)) return SharpnessWriteResult.Failed($"控制器不存在：{controlPath}");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = controlPath,
                WorkingDirectory = Path.GetDirectoryName(controlPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) return SharpnessWriteResult.Failed("无法启动 Sharpness 控制器。");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
                return SharpnessWriteResult.Failed(cancellationToken.IsCancellationRequested ? "写入已取消。" : "Sharpness 控制器超时。");
            }
            var output = await stdoutTask.ConfigureAwait(false);
            var errorOutput = await stderrTask.ConfigureAwait(false);
            SharpnessControlReport? report = null;
            try { report = JsonSerializer.Deserialize<SharpnessControlReport>(output, JsonOptions); } catch { }
            if (report is null)
                return new SharpnessWriteResult(false, process.ExitCode, "Invalid output", "Invalid output", null, null, null, null, string.IsNullOrWhiteSpace(errorOutput) ? $"控制器退出码：{process.ExitCode}。" : errorOutput.Trim());
            var success = process.ExitCode == 0 && report.SetResult == "0x0" && report.ReadbackResult == "0x0";
            return new SharpnessWriteResult(success, process.ExitCode, report.SetResult ?? "Not exposed by current IGCL version", report.ReadbackResult ?? "Not exposed by current IGCL version", report.Readback?.Enabled, report.Readback?.FilterType, report.Readback?.Intensity, report.Backup, success ? null : (string.IsNullOrWhiteSpace(errorOutput) ? $"控制器退出码：{process.ExitCode}。" : errorOutput.Trim()));
        }
        catch (OperationCanceledException) { return SharpnessWriteResult.Failed("写入已取消。"); }
        catch (Exception ex) { return SharpnessWriteResult.Failed(ex.Message); }
    }

    private sealed class SharpnessControlReport
    {
        public string? SetResult { get; set; }
        public string? ReadbackResult { get; set; }
        public SharpnessReadback? Readback { get; set; }
        public string? Backup { get; set; }
    }
    private sealed class SharpnessReadback { public bool Enabled { get; set; } public uint FilterType { get; set; } public float Intensity { get; set; } }
}
