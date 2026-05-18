using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Orphan-safety check #2: looks for the assembly under any of the well-known
/// .NET Framework Global Assembly Cache (GAC) folders. A presence in the GAC means
/// the CLR can satisfy the binding redirect at runtime without a DLL in <c>bin\</c>,
/// so removing the redirect could break loading paths that rely on the GAC copy.
/// </summary>
/// <remarks>
/// Probed folders (all under <c>%windir%</c>):
/// <list type="bullet">
///   <item><c>Microsoft.NET\assembly\GAC_MSIL\&lt;name&gt;</c> (.NET 4.x, architecture-neutral)</item>
///   <item><c>Microsoft.NET\assembly\GAC_32\&lt;name&gt;</c> (.NET 4.x, x86-specific)</item>
///   <item><c>Microsoft.NET\assembly\GAC_64\&lt;name&gt;</c> (.NET 4.x, x64-specific)</item>
///   <item><c>assembly\GAC_MSIL\&lt;name&gt;</c> (legacy .NET 2.0 GAC, still valid for some assemblies)</item>
/// </list>
/// We do NOT match on a specific version — any version is enough to indicate the GAC
/// can satisfy the redirect.
/// </remarks>
public sealed class OrphanSafetyGacProbeCheck : IOrphanSafetyCheck
{
    /// <summary>
    /// Subpaths under <c>%windir%</c> that may contain a per-assembly folder.
    /// </summary>
    private static readonly string[] GacRoots =
    [
        Path.Combine("Microsoft.NET", "assembly", "GAC_MSIL"),
        Path.Combine("Microsoft.NET", "assembly", "GAC_32"),
        Path.Combine("Microsoft.NET", "assembly", "GAC_64"),
        Path.Combine("assembly", "GAC_MSIL"),
    ];

    /// <inheritdoc />
    public string Title => "Global Assembly Cache";

    /// <inheritdoc />
    public Task<SafetyCheckResult> RunAsync(OrphanCheckContext context, CancellationToken cancellationToken)
    {
        string? windir = Environment.GetEnvironmentVariable("WINDIR");
        if (string.IsNullOrEmpty(windir) || !Directory.Exists(windir))
        {
            return Task.FromResult(new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Inconclusive,
                Detail = "Could not resolve %WINDIR%; cannot probe GAC folders.",
            });
        }

        var hits = new List<string>();

        foreach (string gacRoot in GacRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string assemblyFolder = Path.Combine(windir, gacRoot, context.AssemblyName);
            try
            {
                if (Directory.Exists(assemblyFolder))
                {
                    hits.Add(gacRoot);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip — common for restricted GAC folders. Other GAC roots may still answer.
            }
        }

        SafetyCheckResult result = hits.Count == 0
            ? new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Pass,
                Detail = $"'{context.AssemblyName}' is not present in any probed GAC folder.",
            }
            : new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Fail,
                Detail = $"'{context.AssemblyName}' exists in GAC ({string.Join(", ", hits)}). " +
                         "The CLR can satisfy the redirect from there; removing it may break runtime loads.",
            };

        return Task.FromResult(result);
    }
}
