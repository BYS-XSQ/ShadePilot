namespace ShadePilot.DriverEnhancement;

/// <summary>
/// Architecture placeholder for the Intel provider. DriverLab owns all current
/// IGCL discovery. Every mutating method is deliberately blocked until a future
/// feature passes backup, readback, real-effect and restore validation.
/// </summary>
public sealed class IntelIgclProvider : IDriverEnhancementProvider
{
    public string Id => "intel.igcl";
    public DriverVendor Vendor => DriverVendor.Intel;

    public Task<DriverInitializationResult> InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new DriverInitializationResult(false, "Not initialized", "Phase boundary: run ShadePilot.DriverLab.exe probe; the shipping provider is not activated."));
    public Task<IReadOnlyList<DriverAdapterInfo>> GetAdapterInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DriverAdapterInfo>>(Array.Empty<DriverAdapterInfo>());
    public Task<IReadOnlyList<DriverFeatureCapability>> GetCapabilitiesAsync(DriverAdapterInfo adapter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DriverFeatureCapability>>(Array.Empty<DriverFeatureCapability>());
    public Task<DriverFeatureState> GetFeatureStateAsync(DriverAdapterInfo adapter, DriverFeatureId feature, CancellationToken cancellationToken = default) => Task.FromResult(new DriverFeatureState(feature, false, null, "Not executed", "Use the read-only DriverLab report in this phase."));
    public Task<DriverOperationResult<DriverFeatureState>> SetFeatureStateAsync(DriverAdapterInfo adapter, DriverFeatureId feature, DriverFeatureValue value, CancellationToken cancellationToken = default) => Task.FromResult(new DriverOperationResult<DriverFeatureState>(false, null, "Blocked by Phase 1-4 boundary: no driver Set operation is enabled."));
    public Task<DriverSnapshot> CreateSnapshotAsync(DriverAdapterInfo adapter, IReadOnlyCollection<DriverFeatureId> features, CancellationToken cancellationToken = default) => Task.FromResult(new DriverSnapshot(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, adapter, new Dictionary<DriverFeatureId, DriverFeatureState>(), false));
    public Task<DriverOperationResult> RestoreSnapshotAsync(DriverSnapshot snapshot, CancellationToken cancellationToken = default) => Task.FromResult(new DriverOperationResult(false, "Blocked by Phase 1-4 boundary: no driver restore operation is enabled."));
    public Task<DriverValidationResult> ValidateCurrentStateAsync(DriverSnapshot snapshot, CancellationToken cancellationToken = default) => Task.FromResult(new DriverValidationResult(false, "No applied driver state exists in this phase."));
    public Task ShutdownAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
