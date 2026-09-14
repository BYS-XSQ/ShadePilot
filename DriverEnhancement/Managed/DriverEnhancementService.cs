namespace ShadePilot.DriverEnhancement;

/// <summary>
/// Composition root for vendor providers. It deliberately has no WPF dependency.
/// The shipping application does not instantiate it in this phase.
/// </summary>
public sealed class DriverEnhancementService
{
    private readonly IReadOnlyList<IDriverEnhancementProvider> providers;

    public DriverEnhancementService(IEnumerable<IDriverEnhancementProvider> providers) =>
        this.providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));

    public async Task<IReadOnlyList<(IDriverEnhancementProvider Provider, DriverInitializationResult Initialization)>> InitializeAvailableAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<(IDriverEnhancementProvider, DriverInitializationResult)>();
        foreach (var provider in providers)
            results.Add((provider, await provider.InitializeAsync(cancellationToken).ConfigureAwait(false)));
        return results;
    }
}

/// <summary>
/// Future profile orchestration contract. No process detection or driver writes are
/// wired in this phase; it only makes the intended boundaries explicit.
/// </summary>
public sealed class DriverProfileManager
{
    public Task<DriverSnapshot> CaptureBeforeApplyAsync(IDriverEnhancementProvider provider, DriverAdapterInfo adapter, IReadOnlyCollection<DriverFeatureId> features, CancellationToken cancellationToken = default) =>
        provider.CreateSnapshotAsync(adapter, features, cancellationToken);
}

/// <summary>
/// A future restore must first validate that the current value is still ShadePilot's
/// applied value. This phase stores no pending restore state.
/// </summary>
public sealed class DriverRestoreManager
{
    public Task<DriverValidationResult> ValidateBeforeRestoreAsync(IDriverEnhancementProvider provider, DriverSnapshot snapshot, CancellationToken cancellationToken = default) =>
        provider.ValidateCurrentStateAsync(snapshot, cancellationToken);
}
