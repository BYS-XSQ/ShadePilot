using System.Diagnostics;
using System.Text.Json;

namespace ShadePilot.DriverEnhancement;

/// <summary>
/// Intel read-only provider backed by the independently compiled official-IGCL Probe.
/// The Probe loads ControlLib.dll from the system driver installation and performs no Set call.
/// </summary>
public sealed class IntelSharpnessReadProvider : IDriverEnhancementProvider
{
    private readonly string probePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public IntelSharpnessReadProvider(string probePath)
    {
        this.probePath = Path.GetFullPath(probePath ?? throw new ArgumentNullException(nameof(probePath)));
    }

    public string Id => "intel.igcl.sharpness.read";
    public string DisplayName => "Intel IGCL Sharpness（只读）";

    public async Task<DriverEnhancementReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(probePath))
            return DriverEnhancementReadResult.Failed($"Probe 不存在：{probePath}");

        var workDirectory = Path.Combine(Path.GetTempPath(), "ShadePilot", "driver-read", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var probeCopy = Path.Combine(workDirectory, Path.GetFileName(probePath));
        try
        {
            File.Copy(probePath, probeCopy, overwrite: true);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = probeCopy,
                    WorkingDirectory = workDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start())
                return DriverEnhancementReadResult.Failed("无法启动 Intel DriverProbe。");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
                return DriverEnhancementReadResult.Failed(cancellationToken.IsCancellationRequested ? "读取已取消。" : "Intel DriverProbe 超时。");
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

            var reportPath = Path.Combine(workDirectory, "driver_probe_report.json");
            if (!File.Exists(reportPath))
                return DriverEnhancementReadResult.Failed($"Probe 未生成报告，退出码：{process.ExitCode}。");

            var json = await File.ReadAllTextAsync(reportPath, cancellationToken).ConfigureAwait(false);
            var report = JsonSerializer.Deserialize<ProbeReport>(json, JsonOptions);
            if (report is null)
                return DriverEnhancementReadResult.Failed("Probe 报告为空或格式无效。");

            var adapters = report.Adapters.Select(ToAdapter).ToArray();
            var supported = report.Igcl?.InitSuccess == true && adapters.Length > 0;
            var error = process.ExitCode == 0 ? null : $"Probe 退出码：{process.ExitCode}。";
            return new DriverEnhancementReadResult(process.ExitCode == 0, supported, report.Status ?? "Unknown", error, adapters);
        }
        catch (OperationCanceledException)
        {
            return DriverEnhancementReadResult.Failed("读取已取消。");
        }
        catch (Exception ex)
        {
            return DriverEnhancementReadResult.Failed(ex.Message);
        }
        finally
        {
            try { Directory.Delete(workDirectory, recursive: true); } catch { }
        }
    }

    private static DriverAdapterSnapshot ToAdapter(ProbeAdapter adapter) => new(
        adapter.Name ?? "Not exposed by current IGCL version",
        adapter.VendorId, adapter.DeviceId, adapter.RevisionId, adapter.DriverVersion, adapter.SupportedSubfunctions,
        adapter.DisplayOutputs.Select((output, index) => ToDisplay(output, index)).ToArray(),
        adapter.Features3D?.Features.Select(ToFeature).ToArray() ?? Array.Empty<Driver3DFeatureSnapshot>());

    private static DriverDisplaySnapshot ToDisplay(ProbeDisplayOutput output, int index) => new(
        index, output.DisplayPropertiesResult ?? "Not exposed by current IGCL version", output.DisplayType,
        output.AttachedDisplayMuxType, output.ProtocolConverterOutput, output.DisplayConfigFlags,
        output.FeatureEnabledFlags, output.FeatureSupportedFlags,
        output.EncoderPropertiesResult ?? "Not exposed by current IGCL version", output.EncoderConfigFlags, output.EncoderType,
        output.CapsResult ?? "Not exposed by current IGCL version",
        output.SupportedFilterFlags, output.Filters?.Select(f => new SharpnessFilterRange(f.Type, f.Min, f.Max, f.Step)).ToArray() ?? Array.Empty<SharpnessFilterRange>(),
        output.CurrentResult ?? "Not exposed by current IGCL version", output.Current?.Enabled ?? false,
        output.Current?.FilterType ?? 0, output.Current?.Intensity ?? 0);

    private static Driver3DFeatureSnapshot ToFeature(ProbeFeature feature) => new(
        feature.Feature, feature.Name ?? "Unknown Feature", feature.ValueType, feature.PerAppSupport,
        feature.LiveChange, feature.ConflictingFeatures, feature.FeatureMiscSupport);

    private sealed class ProbeReport { public ProbeIgcl? Igcl { get; set; } public List<ProbeAdapter> Adapters { get; set; } = new(); public string? Status { get; set; } }
    private sealed class ProbeIgcl { public bool InitSuccess { get; set; } }
    private sealed class ProbeAdapter
    {
        public string? Name { get; set; } public uint VendorId { get; set; } public uint DeviceId { get; set; }
        public uint RevisionId { get; set; } public ulong DriverVersion { get; set; } public uint SupportedSubfunctions { get; set; }
        public List<ProbeDisplayOutput> DisplayOutputs { get; set; } = new(); public Probe3DCapabilities? Features3D { get; set; }
    }
    private sealed class ProbeDisplayOutput
    {
        public string? DisplayPropertiesResult { get; set; } public int DisplayType { get; set; } public int AttachedDisplayMuxType { get; set; }
        public int ProtocolConverterOutput { get; set; } public uint DisplayConfigFlags { get; set; } public uint FeatureEnabledFlags { get; set; }
        public uint FeatureSupportedFlags { get; set; } public string? EncoderPropertiesResult { get; set; } public uint EncoderConfigFlags { get; set; }
        public int EncoderType { get; set; } public string? CapsResult { get; set; } public uint SupportedFilterFlags { get; set; }
        public List<ProbeFilter>? Filters { get; set; } public string? CurrentResult { get; set; } public ProbeCurrent? Current { get; set; }
    }
    private sealed class ProbeFilter { public uint Type { get; set; } public float Min { get; set; } public float Max { get; set; } public float Step { get; set; } }
    private sealed class ProbeCurrent { public bool Enabled { get; set; } public uint FilterType { get; set; } public float Intensity { get; set; } }
    private sealed class Probe3DCapabilities { public List<ProbeFeature> Features { get; set; } = new(); }
    private sealed class ProbeFeature
    {
        public int Feature { get; set; } public string? Name { get; set; } public int ValueType { get; set; }
        public bool PerAppSupport { get; set; } public bool LiveChange { get; set; } public long ConflictingFeatures { get; set; }
        public short FeatureMiscSupport { get; set; }
    }
}
