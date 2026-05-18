using System.Reflection;
using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Orphan-safety check #3: opens every DLL in the project's <c>bin\</c> output and
/// inspects its <c>AssemblyRef</c> table for references to the target assembly.
/// A transitive reference means another DLL in <c>bin\</c> still loads the assembly
/// at runtime — removing the binding redirect would likely cause a
/// <c>FileLoadException</c> at the point of that transitive load.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="MetadataLoadContext"/> with a <see cref="PathAssemblyResolver"/>
/// over a snapshot of bin/ + runtime DLLs to avoid locking files while a build may
/// be running.
/// </para>
/// <para>
/// Walks recursively so DLLs under <c>bin\runtimes\&lt;rid&gt;\lib\&lt;tfm&gt;\</c>
/// are also examined — Microsoft.Data.SqlClient and friends ship important parts there.
/// </para>
/// </remarks>
public sealed class OrphanSafetyBinReferenceCheck : IOrphanSafetyCheck
{
    /// <summary>
    /// Bin output subdirectories probed in order. Matches <see cref="BinFolderScanner"/>
    /// so the two services see the same view of disk.
    /// </summary>
    private static readonly string[] OutputSubdirectories =
    [
        Path.Combine("bin", "Debug"),
        Path.Combine("bin", "Release"),
        "bin",
    ];

    /// <inheritdoc />
    public string Title => "Transitive bin/ references";

    /// <inheritdoc />
    public Task<SafetyCheckResult> RunAsync(OrphanCheckContext context, CancellationToken cancellationToken)
    {
        string? binFolder = FindOutputFolder(context.ProjectDirectory);
        if (binFolder is null)
        {
            return Task.FromResult(new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Inconclusive,
                Detail = $"No bin/ output found under {context.ProjectDirectory}. Build the project, then re-run the check.",
            });
        }

        string[] dlls;
        try
        {
            dlls = Directory.GetFiles(binFolder, "*.dll", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Inconclusive,
                Detail = $"Could not enumerate {binFolder}: {ex.GetType().Name}: {ex.Message}",
            });
        }

        if (dlls.Length == 0)
        {
            return Task.FromResult(new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Inconclusive,
                Detail = $"{binFolder} contains no DLLs. Build the project, then re-run the check.",
            });
        }

        var referringDlls = new List<string>();
        try
        {
            referringDlls.AddRange(FindReferrers(dlls, context.AssemblyName, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Inconclusive,
                Detail = $"MetadataLoadContext failure: {ex.GetType().Name}: {ex.Message}",
            });
        }

        SafetyCheckResult result = referringDlls.Count == 0
            ? new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Pass,
                Detail = $"No DLL under {Path.GetFileName(binFolder)} references '{context.AssemblyName}'.",
            }
            : new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Fail,
                Detail = BuildReferrerDetail(context.AssemblyName, referringDlls),
            };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Picks the most-recently-built output folder under the project directory.
    /// Mirrors <see cref="BinFolderScanner.FindOutputFolder"/> behaviour.
    /// </summary>
    private static string? FindOutputFolder(string projectDirectory)
    {
        string? bestFolder = null;
        DateTime bestTimestamp = DateTime.MinValue;

        foreach (string subDir in OutputSubdirectories)
        {
            string candidate = Path.Combine(projectDirectory, subDir);
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                string[] dlls = Directory.GetFiles(candidate, "*.dll", SearchOption.TopDirectoryOnly);
                if (dlls.Length == 0)
                {
                    continue;
                }

                DateTime newest = dlls.Select(f => File.GetLastWriteTimeUtc(f)).Max();
                if (newest > bestTimestamp)
                {
                    bestTimestamp = newest;
                    bestFolder = candidate;
                }
            }
            catch (Exception)
            {
                // Access denied / IO — skip candidate, others may still answer
            }
        }

        return bestFolder;
    }

    /// <summary>
    /// Opens each DLL via <see cref="MetadataLoadContext"/> and yields the file name of
    /// any DLL whose <c>AssemblyRef</c> table contains <paramref name="assemblyName"/>.
    /// The target DLL itself (if present in bin/ under a different folder) is excluded
    /// so a self-reference doesn't masquerade as a transitive one.
    /// </summary>
    private static IEnumerable<string> FindReferrers(
        string[] dlls,
        string assemblyName,
        CancellationToken cancellationToken)
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] runtimeAssemblies = Directory.GetFiles(runtimeDirectory, "*.dll");

        var resolverPaths = new HashSet<string>(runtimeAssemblies, StringComparer.OrdinalIgnoreCase);
        foreach (string dll in dlls)
        {
            resolverPaths.Add(dll);
        }

        var resolver = new PathAssemblyResolver(resolverPaths);
        using var context = new MetadataLoadContext(resolver);

        var referrers = new List<string>();
        foreach (string dll in dlls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip the target itself — finding 'X' inside 'X.dll' is not a transitive ref.
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(dll),
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AssemblyName[] refs;
            try
            {
                Assembly probe = context.LoadFromAssemblyPath(dll);
                refs = probe.GetReferencedAssemblies();
            }
            catch (BadImageFormatException)
            {
                continue; // Native DLL or unmanaged file under bin/
            }
            catch (FileLoadException)
            {
                continue; // File in use or otherwise inaccessible
            }
            catch (IOException)
            {
                continue;
            }

            foreach (AssemblyName r in refs)
            {
                if (string.Equals(r.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    referrers.Add(Path.GetFileName(dll));
                    break;
                }
            }
        }

        return referrers;
    }

    /// <summary>
    /// UI-friendly summary of the referrer set. Truncates to keep the panel readable.
    /// </summary>
    private static string BuildReferrerDetail(string assemblyName, List<string> referrers)
    {
        const int MaxShown = 5;
        int total = referrers.Count;
        string list = string.Join(", ", referrers.Take(MaxShown));
        return total <= MaxShown
            ? $"{total} bin/ DLL(s) reference '{assemblyName}': {list}"
            : $"{total} bin/ DLLs reference '{assemblyName}'. First {MaxShown}: {list}";
    }
}
