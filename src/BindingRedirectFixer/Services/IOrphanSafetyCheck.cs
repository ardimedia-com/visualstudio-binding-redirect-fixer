using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Inputs available to an orphan-safety check. Carries enough context for a check to decide
/// whether the assembly is still in use somewhere in the user's solution / on the user's
/// machine — without re-discovering project layout on every invocation.
/// </summary>
/// <param name="AssemblyName">
/// Simple assembly name (no extension, no version), e.g. <c>ICSharpCode.SharpZipLib</c>.
/// </param>
/// <param name="ProjectDirectory">
/// Absolute path to the project directory that owns the binding redirect. Used to locate
/// the project's <c>bin\</c> output for the transitive reference check.
/// </param>
/// <param name="SolutionDirectory">
/// Absolute path to the directory holding the solution file, or <c>null</c> when scanning
/// without a solution open. When null, the source-usage check degrades to scoping its
/// search to <see cref="ProjectDirectory"/> only.
/// </param>
public sealed record OrphanCheckContext(
    string AssemblyName,
    string ProjectDirectory,
    string? SolutionDirectory);

/// <summary>
/// Contract for one orphan-safety check. Each implementation answers a single question
/// about whether an assembly is still in use somewhere (source code, GAC, transitive
/// bin/ reference). The orchestrator runs all checks in parallel for a single row.
/// </summary>
/// <remarks>
/// Implementations must be stateless and safe to invoke concurrently across rows.
/// </remarks>
public interface IOrphanSafetyCheck
{
    /// <summary>
    /// Human-readable check title used as the row label in the UI checklist.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Runs the check. Must complete with a <see cref="SafetyCheckResult"/> regardless
    /// of internal failure — IO errors, missing folders, locked files all return an
    /// <see cref="SafetyCheckOutcome.Inconclusive"/> result rather than throwing.
    /// Only <see cref="OperationCanceledException"/> from cooperative cancellation
    /// should propagate.
    /// </summary>
    Task<SafetyCheckResult> RunAsync(OrphanCheckContext context, CancellationToken cancellationToken);
}
