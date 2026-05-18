using System.Text.RegularExpressions;
using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Orphan-safety check #1: greps every <c>.cs</c> / <c>.vb</c> file under the solution
/// (or project, if no solution is provided) for textual references to the assembly's
/// simple name. A match indicates source code still references the assembly and removing
/// the binding redirect would likely break a build or runtime load.
/// </summary>
/// <remarks>
/// <para>
/// Known limitation (v0.4.0): the check uses the assembly's simple name as a namespace
/// heuristic. For assemblies whose namespace differs from the assembly name (e.g.
/// <c>Microsoft.Bcl.AsyncInterfaces</c> exposes <c>System.Threading.Tasks</c>) the check
/// may return <see cref="SafetyCheckOutcome.Pass"/> even when source code uses the
/// assembly. This is documented in the user-facing diagnostic so the manual post-build
/// step can compensate.
/// </para>
/// <para>
/// False-positive direction (we say "in use" when it isn't — e.g. name appears inside
/// a comment) is acceptable: it merely blocks an unsafe removal. False-negative
/// direction would be dangerous, so the regex deliberately tolerates noise.
/// </para>
/// </remarks>
public sealed class OrphanSafetySourceUsageCheck : IOrphanSafetyCheck
{
    /// <summary>
    /// Directory names that are walked into but never grepped — pure build/cache output.
    /// </summary>
    private static readonly HashSet<string> SkipDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            "node_modules",
            ".git",
            ".vs",
            "packages",
        };

    /// <summary>
    /// Source extensions worth grepping. .vb included for legacy WAPs that mix VB.NET.
    /// </summary>
    private static readonly string[] SourceExtensions = [".cs", ".vb"];

    /// <inheritdoc />
    public string Title => "Source code references";

    /// <inheritdoc />
    public Task<SafetyCheckResult> RunAsync(OrphanCheckContext context, CancellationToken cancellationToken)
    {
        string searchRoot = context.SolutionDirectory ?? context.ProjectDirectory;
        if (!Directory.Exists(searchRoot))
        {
            return Task.FromResult(new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Inconclusive,
                Detail = $"Search root does not exist: {searchRoot}",
            });
        }

        var matcher = BuildMatcher(context.AssemblyName);
        var matches = new List<string>();

        try
        {
            EnumerateAndScan(searchRoot, matcher, matches, cancellationToken);
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
                Detail = $"Search failed: {ex.GetType().Name}: {ex.Message}",
            });
        }

        SafetyCheckResult result = matches.Count == 0
            ? new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Pass,
                Detail = $"0 source files reference '{context.AssemblyName}'.",
            }
            : new SafetyCheckResult
            {
                Title = this.Title,
                Outcome = SafetyCheckOutcome.Fail,
                Detail = BuildMatchDetail(context.AssemblyName, matches),
            };

        return Task.FromResult(result);
    }

    /// <summary>
    /// Recursively enumerates source files under <paramref name="root"/>, skipping
    /// build/cache directories, and records files containing a regex match.
    /// </summary>
    private static void EnumerateAndScan(
        string root,
        Regex matcher,
        List<string> matches,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = stack.Pop();

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (string sub in subdirs)
            {
                string name = Path.GetFileName(sub);
                if (SkipDirectoryNames.Contains(name))
                {
                    continue;
                }
                stack.Push(sub);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                if (Array.IndexOf(SourceExtensions, ext) < 0)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (FileContainsMatch(file, matcher))
                {
                    matches.Add(file);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if the file contains at least one match. Reads the file as text and
    /// applies the regex once. IO errors are treated as "no match" rather than thrown.
    /// </summary>
    private static bool FileContainsMatch(string filePath, Regex matcher)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            return matcher.IsMatch(content);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a regex that matches the assembly name either as a using directive
    /// (<c>using X.Y.Z;</c>) or as a qualified type reference (<c>X.Y.Z.Type</c>).
    /// </summary>
    private static Regex BuildMatcher(string assemblyName)
    {
        string escaped = Regex.Escape(assemblyName);
        // Either: "using <name>" (optional sub-namespace after) or "<name>." somewhere.
        // Word boundary on the left, dot or whitespace/semicolon on the right.
        string pattern = $@"(\busing\s+{escaped}\b)|(\b{escaped}\.)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Constructs a UI-friendly detail string summarising the first few matching files.
    /// Truncates aggressively so the tooltip / panel doesn't explode on big solutions.
    /// </summary>
    private static string BuildMatchDetail(string assemblyName, List<string> matches)
    {
        const int MaxFilesShown = 5;
        int total = matches.Count;
        var sample = matches.Take(MaxFilesShown).Select(Path.GetFileName);
        string list = string.Join(", ", sample);
        return total <= MaxFilesShown
            ? $"{total} source file(s) reference '{assemblyName}': {list}"
            : $"{total} source files reference '{assemblyName}'. First {MaxFilesShown}: {list}";
    }
}
