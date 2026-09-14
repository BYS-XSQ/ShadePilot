namespace ShadePilot.DriverEnhancement;

public enum DriverVendor { Unknown, Intel, Amd, Nvidia }
public enum DriverFeatureId { Sharpening, AnisotropicFiltering, TextureFilteringQuality, AntiAliasing, LowLatency, FrameLimit, VSync, Upscaling, DriverFrameGeneration, FramePacing, GamingFlipModes, Cmaa, Msaa, AdaptiveSyncPlus, AppProfiles, GlobalOrPerApplication, LiveState, PrebuiltShaderDownload }
public enum DriverFeatureAvailability { Unsupported, ApiUnavailable, ApiExposed, Supported, DriverBlocked, KnownIneffective, KnownRemoved }
public enum DriverVerificationLevel { None, ApiExposed, ReadVerified, ReadWriteVerified, EffectVerified, ReadWriteVerifiedButNoEffect, KnownIneffective, KnownRemoved }
public enum DriverFeatureScope { Unknown, GlobalGpu, GlobalDisplay, PerApplication, TemporaryGlobal }

public sealed record DriverAdapterInfo(
    DriverVendor Vendor,
    string AdapterId,
    string Name,
    uint DeviceId,
    string DriverVersion,
    string ApiVersion);

public sealed record DriverFeatureCapability(
    DriverFeatureId FeatureId,
    DriverVendor Vendor,
    DriverFeatureAvailability Availability,
    bool CanRead,
    bool CanWrite,
    bool CanRestore,
    DriverFeatureScope Scope,
    bool RequiresRestart,
    bool SupportsPerApplication,
    bool SupportsLiveChange,
    double? Minimum,
    double? Maximum,
    double? Step,
    IReadOnlyList<string> Values,
    DriverVerificationLevel VerificationLevel,
    string Reason);

public sealed record DriverFeatureValue(string RawValue, bool? Enabled = null, double? NumericValue = null);

public sealed record DriverFeatureState(
    DriverFeatureId FeatureId,
    bool ReadSucceeded,
    DriverFeatureValue? Value,
    string ResultCode,
    string Reason);

public sealed record DriverSnapshot(
    string SnapshotId,
    DateTimeOffset CreatedAt,
    DriverAdapterInfo Adapter,
    IReadOnlyDictionary<DriverFeatureId, DriverFeatureState> Features,
    bool RestorePending);

public sealed record DriverInitializationResult(bool Succeeded, string ApiVersion, string Reason);
public sealed record DriverValidationResult(bool Succeeded, string Reason);
public sealed record DriverOperationResult(bool Succeeded, string Reason);
public sealed record DriverOperationResult<T>(bool Succeeded, T? Value, string Reason);
