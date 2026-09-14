namespace ShadePilot.DriverEnhancement;

public sealed record DriverEnhancementReadResult(
    bool Succeeded,
    bool IsSupported,
    string Status,
    string? Error,
    IReadOnlyList<DriverAdapterSnapshot> Adapters)
{
    public static DriverEnhancementReadResult Failed(string error) =>
        new(false, false, "Driver Enhancement Unsupported", error, Array.Empty<DriverAdapterSnapshot>());
}

public sealed record DriverAdapterSnapshot(
    string Name,
    uint VendorId,
    uint DeviceId,
    uint RevisionId,
    ulong DriverVersionRaw,
    uint SupportedSubfunctions,
    IReadOnlyList<DriverDisplaySnapshot> DisplayOutputs,
    IReadOnlyList<Driver3DFeatureSnapshot> Features3D);

public sealed record DriverDisplaySnapshot(
    int Index,
    string DisplayPropertiesResult,
    int DisplayType,
    int AttachedDisplayMuxType,
    int ProtocolConverterOutput,
    uint DisplayConfigFlags,
    uint FeatureEnabledFlags,
    uint FeatureSupportedFlags,
    string EncoderPropertiesResult,
    uint EncoderConfigFlags,
    int EncoderType,
    string SharpnessCapsResult,
    uint SupportedFilterFlags,
    IReadOnlyList<SharpnessFilterRange> Filters,
    string CurrentSharpnessResult,
    bool CurrentEnabled,
    uint CurrentFilterType,
    float CurrentIntensity);

public sealed record SharpnessFilterRange(uint Type, float Minimum, float Maximum, float Step);

public sealed record Driver3DFeatureSnapshot(
    int Feature,
    string Name,
    int ValueType,
    bool PerAppSupport,
    bool LiveChange,
    long ConflictingFeatures,
    short FeatureMiscSupport);

public sealed record SharpnessWriteRequest(
    int OutputIndex,
    bool Enabled,
    uint FilterType,
    float Intensity,
    string BackupPath);

public sealed record SharpnessWriteResult(
    bool Succeeded,
    int ExitCode,
    string SetResult,
    string ReadbackResult,
    bool? ReadbackEnabled,
    uint? ReadbackFilterType,
    float? ReadbackIntensity,
    string? BackupPath,
    string? Error)
{
    public static SharpnessWriteResult Failed(string error) =>
        new(false, -1, "Not executed", "Not executed", null, null, null, null, error);
}
