namespace ShadePilot.DriverEnhancement;

/// <summary>
/// Vendor-neutral boundary for future driver enhancement providers.
/// DriverLab and providers are separate from the shipping WPF UI until a feature
/// has both hardware verification and an explicit product decision.
/// </summary>
public interface IDriverEnhancementProvider : IAsyncDisposable
{
    string Id { get; }
    DriverVendor Vendor { get; }

    Task<DriverInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DriverAdapterInfo>> GetAdapterInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DriverFeatureCapability>> GetCapabilitiesAsync(DriverAdapterInfo adapter, CancellationToken cancellationToken = default);
    Task<DriverFeatureState> GetFeatureStateAsync(DriverAdapterInfo adapter, DriverFeatureId feature, CancellationToken cancellationToken = default);
    Task<DriverOperationResult<DriverFeatureState>> SetFeatureStateAsync(DriverAdapterInfo adapter, DriverFeatureId feature, DriverFeatureValue value, CancellationToken cancellationToken = default);
    Task<DriverSnapshot> CreateSnapshotAsync(DriverAdapterInfo adapter, IReadOnlyCollection<DriverFeatureId> features, CancellationToken cancellationToken = default);
    Task<DriverOperationResult> RestoreSnapshotAsync(DriverSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<DriverValidationResult> ValidateCurrentStateAsync(DriverSnapshot snapshot, CancellationToken cancellationToken = default);
    Task ShutdownAsync();
}
