using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Orchestrates the orphan-safety verification flow for a single
/// <see cref="RedirectStatus.OrphanedFramework"/> row: runs every registered
/// <see cref="IOrphanSafetyCheck"/> in parallel and assembles the post-build script
/// text, returning an <see cref="OrphanVerificationReport"/> ready to bind to the UI.
/// </summary>
/// <remarks>
/// Designed to be constructed once and reused across rows. Each <see cref="VerifyAsync"/>
/// call is independent — checks are stateless, so no per-call disposal is needed.
/// </remarks>
public sealed class OrphanSafetyVerifier
{
    private readonly IReadOnlyList<IOrphanSafetyCheck> _checks;

    /// <summary>
    /// Creates a verifier with an explicit list of checks. Tests can inject mocks via
    /// this constructor; production code should prefer <see cref="CreateDefault"/>.
    /// </summary>
    public OrphanSafetyVerifier(IEnumerable<IOrphanSafetyCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks.ToList();
    }

    /// <summary>
    /// Convenience factory returning the production check set
    /// (source usage + GAC probe + transitive bin/ refs).
    /// </summary>
    public static OrphanSafetyVerifier CreateDefault() => new(
    [
        new OrphanSafetySourceUsageCheck(),
        new OrphanSafetyGacProbeCheck(),
        new OrphanSafetyBinReferenceCheck(),
    ]);

    /// <summary>
    /// Runs all checks in parallel for the given context and reads the project's
    /// post-build script. Returns a fresh <see cref="OrphanVerificationReport"/> with
    /// <see cref="OrphanVerificationReport.UserConfirmedPostBuild"/> defaulted to false
    /// — the UI must collect that confirmation before enabling removal.
    /// </summary>
    public async Task<OrphanVerificationReport> VerifyAsync(
        OrphanCheckContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Task<SafetyCheckResult>[] checkTasks = _checks
            .Select(c => c.RunAsync(context, cancellationToken))
            .ToArray();
        Task<string> postBuildTask = OrphanSafetyPostBuildScriptReader.ReadAsync(
            context.ProjectDirectory, cancellationToken);

        await Task.WhenAll([.. checkTasks.Cast<Task>(), postBuildTask]).ConfigureAwait(false);

        var results = checkTasks.Select(t => t.Result).ToList();
        string postBuild = postBuildTask.Result;

        return new OrphanVerificationReport
        {
            Auto = results,
            PostBuildScript = postBuild,
            UserConfirmedPostBuild = false,
        };
    }
}
