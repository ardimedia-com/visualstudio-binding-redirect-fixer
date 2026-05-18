using System.Runtime.Serialization;

namespace BindingRedirectFixer.Models;

/// <summary>
/// Status of a binding redirect evaluation.
/// </summary>
public enum RedirectStatus
{
    /// <summary>All version sources agree.</summary>
    OK,

    /// <summary>Config redirect version does not match the effective target version.</summary>
    Stale,

    /// <summary>No binding redirect exists but the assembly has version conflicts.</summary>
    Missing,

    /// <summary>Redirect matches effective target but bin/ DLL differs from NuGet resolved (build output mismatch).</summary>
    Conflict,

    /// <summary>Multiple binding redirect entries exist for the same assembly in the config file.</summary>
    Duplicate,

    /// <summary>Redirect targets a version not available on disk (bin/ DLL is older than NuGet resolved).</summary>
    Mismatch,

    /// <summary>Redirect targets a version that exists nowhere on disk (neither bin/ nor packages/). Runtime FileLoadException at startup is guaranteed.</summary>
    CriticalMismatch,

    /// <summary>The resolved assembly's public key token is empty but the config has a non-empty token (assembly may have lost strong naming).</summary>
    TokenLost,

    /// <summary>The package is deprecated and should be replaced with a modern equivalent.</summary>
    Deprecated,

    /// <summary>No DLL found for this assembly in a .NET (Core) project. The binding redirect is orphaned and safe to remove.</summary>
    Orphaned,

    /// <summary>No DLL found for this assembly in a .NET Framework project. The binding redirect is likely orphaned but GAC/post-build should be verified.</summary>
    OrphanedFramework,

    /// <summary>Binding redirect in a class library (DLL) project. The CLR never reads binding redirects from DLL config files.</summary>
    UnusedInLibrary
}

/// <summary>
/// Suggested fix action for a binding redirect issue.
/// </summary>
public enum FixAction
{
    /// <summary>No action needed.</summary>
    None,

    /// <summary>Update existing binding redirect to the effective target version.</summary>
    UpdateRedirect,

    /// <summary>Add a new binding redirect element.</summary>
    AddRedirect,

    /// <summary>Rebuild the project to replace a stale bin/ DLL.</summary>
    RebuildProject,

    /// <summary>Remove duplicate binding redirect entries, keeping the correct one.</summary>
    RemoveDuplicate,

    /// <summary>Remove the binding redirect entry because it targets a version not on disk.</summary>
    RemoveRedirect,

    /// <summary>Remove all binding redirects from the config file (entire assemblyBinding section or file deletion).</summary>
    RemoveAllRedirects,

    /// <summary>Open the guided verification flow before allowing removal. Used for OrphanedFramework where naive removal can break GAC / post-build / transitive loads.</summary>
    VerifyBeforeRemoval
}

/// <summary>
/// Outcome of a single orphan-safety check.
/// </summary>
public enum SafetyCheckOutcome
{
    /// <summary>Check ran and found no evidence the assembly is in use — safe signal.</summary>
    Pass,

    /// <summary>Check ran and found evidence the assembly IS in use — removing the redirect will likely break something.</summary>
    Fail,

    /// <summary>Check could not run conclusively (e.g. file lock, IO error). Treat with caution; do not gate removal as Pass.</summary>
    Inconclusive
}

/// <summary>
/// Result of one orphan-safety check (Source-usage / GAC / transitive bin/ refs).
/// Carries enough context for the UI to show actionable detail when the check fails.
/// </summary>
[DataContract]
public class SafetyCheckResult
{
    [DataMember] public string Title { get; set; } = string.Empty;
    [DataMember] public SafetyCheckOutcome Outcome { get; set; } = SafetyCheckOutcome.Inconclusive;
    [DataMember] public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// Unicode glyph for the per-result row in the guided-removal panel.
    /// Pass = check, Fail = cross, Inconclusive = question mark.
    /// </summary>
    [DataMember]
    public string OutcomeIcon => Outcome switch
    {
        SafetyCheckOutcome.Pass => "✓",
        SafetyCheckOutcome.Fail => "✗",
        SafetyCheckOutcome.Inconclusive => "?",
        _ => string.Empty,
    };
}

/// <summary>
/// Per-row state for the OrphanedFramework guided-removal flow:
/// auto-check results, the post-build script text (verbatim from .csproj), and
/// the user's manual confirmation that the post-build script does not copy the assembly.
/// Populated lazily when the user clicks "Verify..." — null until then.
/// </summary>
[DataContract]
public class OrphanVerificationReport
{
    [DataMember] public List<SafetyCheckResult> Auto { get; set; } = new();
    [DataMember] public string PostBuildScript { get; set; } = string.Empty;
    [DataMember] public bool UserConfirmedPostBuild { get; set; }
}

/// <summary>
/// Holds all version information for a single assembly in a single project,
/// aggregated from all resolution sources.
/// </summary>
[DataContract]
public class AssemblyRedirectInfo
{
    /// <summary>Project name this assembly belongs to.</summary>
    [DataMember]
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Assembly name (e.g., "Newtonsoft.Json").</summary>
    [DataMember]
    public string Name { get; set; } = string.Empty;

    /// <summary>Public key token from assembly metadata.</summary>
    [DataMember]
    public string PublicKeyToken { get; set; } = string.Empty;

    /// <summary>Culture from assembly metadata (typically "neutral").</summary>
    [DataMember]
    public string Culture { get; set; } = "neutral";

    /// <summary>Public key token from the existing config file entry (used to detect token loss).</summary>
    [DataMember]
    public string ConfigPublicKeyToken { get; set; } = string.Empty;

    /// <summary>Assembly version from the NuGet cache/packages folder DLL.</summary>
    [DataMember]
    public string? ResolvedAssemblyVersion { get; set; }

    /// <summary>NuGet package version (informational only).</summary>
    [DataMember]
    public string? ResolvedPackageVersion { get; set; }

    /// <summary>Version from PackageReference or packages.config.</summary>
    [DataMember]
    public string? RequestedVersion { get; set; }

    /// <summary>Assembly version from the physical DLL in bin/.</summary>
    [DataMember]
    public string? PhysicalVersion { get; set; }

    /// <summary>Current binding redirect version from web.config/app.config.</summary>
    [DataMember]
    public string? CurrentRedirectVersion { get; set; }

    /// <summary>Whether the project targets .NET Framework (net48, etc.) as opposed to modern .NET.</summary>
    [DataMember]
    public bool IsNetFramework { get; set; }

    /// <summary>Evaluated status of this binding redirect.</summary>
    [DataMember]
    public RedirectStatus Status { get; set; } = RedirectStatus.OK;

    /// <summary>Plain-English explanation of the issue.</summary>
    [DataMember]
    public string DiagnosticMessage { get; set; } = string.Empty;

    /// <summary>Suggested fix action.</summary>
    [DataMember]
    public FixAction SuggestedAction { get; set; } = FixAction.None;

    /// <summary>
    /// Orphan verification report — populated lazily when the user opens the guided-removal
    /// flow for an OrphanedFramework entry. Null until the user clicks "Verify...".
    /// </summary>
    [DataMember]
    public OrphanVerificationReport? Verification { get; set; }

    /// <summary>
    /// True only when every auto-check in <see cref="Verification"/> has Outcome=Pass AND
    /// <see cref="OrphanVerificationReport.UserConfirmedPostBuild"/> is true. The Remove
    /// button is bound to this; if false, removal is blocked.
    /// </summary>
    [DataMember]
    public bool CanRemove => Verification is { } v
        && v.Auto.Count > 0
        && v.Auto.TrueForAll(c => c.Outcome == SafetyCheckOutcome.Pass)
        && v.UserConfirmedPostBuild;

    /// <summary>
    /// Human-readable explanation when <see cref="CanRemove"/> is false because an auto-check
    /// failed. Used as the tooltip on the disabled Remove button. Null when no blocker exists
    /// (e.g. user simply hasn't ticked the manual checkbox yet).
    /// </summary>
    [DataMember]
    public string? BlockReason
    {
        get
        {
            if (Verification is not { } v)
            {
                return null;
            }

            var firstFail = v.Auto.Find(c => c.Outcome == SafetyCheckOutcome.Fail);
            if (firstFail is not null)
            {
                return $"{firstFail.Title}: {firstFail.Detail}";
            }

            var firstInconclusive = v.Auto.Find(c => c.Outcome == SafetyCheckOutcome.Inconclusive);
            if (firstInconclusive is not null)
            {
                return $"{firstInconclusive.Title} (inconclusive): {firstInconclusive.Detail}";
            }

            return null;
        }
    }

    /// <summary>
    /// The effective version the binding redirect should target.
    /// Uses the highest of NuGet-resolved and bin/ DLL versions, because the redirect
    /// oldVersion range must cover all referenced versions and newVersion must match
    /// what the runtime will actually load.
    /// </summary>
    [DataMember]
    public string? EffectiveTargetVersion
    {
        get
        {
            string? resolved = ResolvedAssemblyVersion;
            string? physical = PhysicalVersion;

            if (string.IsNullOrEmpty(resolved)) return physical;
            if (string.IsNullOrEmpty(physical)) return resolved;

            try
            {
                var resolvedVer = new Version(resolved);
                var physicalVer = new Version(physical);
                return physicalVer > resolvedVer ? physical : resolved;
            }
            catch
            {
                return resolved;
            }
        }
    }

    /// <summary>Display string for the status icon.</summary>
    [DataMember]
    public string StatusIcon => Status switch
    {
        RedirectStatus.OK => "\u2713",
        RedirectStatus.Stale => "\u26A0",
        RedirectStatus.Missing => "\u2717",
        RedirectStatus.Conflict => "\u2717",
        RedirectStatus.Duplicate => "\u2717",
        RedirectStatus.Mismatch => "\u26A0",
        RedirectStatus.CriticalMismatch => "\u2717",
        RedirectStatus.TokenLost => "\u26A0",
        RedirectStatus.Deprecated => "\u26D4",
        RedirectStatus.Orphaned => "\u26A0",
        RedirectStatus.OrphanedFramework => "\u26A0",
        RedirectStatus.UnusedInLibrary => "\u2297",
        _ => ""
    };

    /// <summary>Display string for the action button label.</summary>
    [DataMember]
    public string ActionLabel => SuggestedAction switch
    {
        FixAction.UpdateRedirect => "Update Redirect",
        FixAction.AddRedirect => "Add Redirect",
        FixAction.RebuildProject => "Rebuild Project",
        FixAction.RemoveDuplicate => "Remove Duplicate",
        FixAction.RemoveRedirect => "Remove Redirect",
        FixAction.RemoveAllRedirects => "Remove All Redirects",
        FixAction.VerifyBeforeRemoval => "Verify & Remove…",
        _ => ""
    };

    /// <summary>CSS-style color indicator for the resolved version cell.</summary>
    [DataMember]
    public string ResolvedCellStatus => "ok";

    /// <summary>CSS-style color indicator for the physical version cell.</summary>
    [DataMember]
    public string PhysicalCellStatus => GetCellStatus(PhysicalVersion, ResolvedAssemblyVersion);

    /// <summary>CSS-style color indicator for the config version cell.</summary>
    [DataMember]
    public string ConfigCellStatus => GetCellStatus(CurrentRedirectVersion, ResolvedAssemblyVersion);

    private static string GetCellStatus(string? actual, string? expected)
    {
        if (string.IsNullOrEmpty(actual)) return "unavailable";
        if (string.IsNullOrEmpty(expected)) return "unavailable";
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ? "ok" : "diverge";
    }
}
