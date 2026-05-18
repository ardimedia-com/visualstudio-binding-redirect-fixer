namespace BindingRedirectFixer.Services;

/// <summary>
/// Scans physical DLLs in a project's bin/ output folder to determine
/// the actual assembly versions present on disk.
/// </summary>
public sealed class BinFolderScanner
{
    /// <summary>
    /// Default output subdirectories to scan when looking for compiled assemblies.
    /// </summary>
    private static readonly string[] OutputSubdirectories =
    [
        Path.Combine("bin", "Debug"),
        Path.Combine("bin", "Release"),
        "bin"
    ];

    /// <summary>
    /// Scans the project's bin/ output folder for .dll files and reads their assembly versions.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping assembly name to its four-part assembly version string.
    /// Only includes assemblies that could be successfully read.
    /// </returns>
    public Task<Dictionary<string, string>> ScanAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? binFolder = FindOutputFolder(projectDirectory);
        if (binFolder is null)
        {
            return Task.FromResult(results);
        }

        // Walk recursively so DLLs under bin/runtimes/<rid>/lib/<tfm>/ are also seen.
        // Microsoft.Data.SqlClient and EntityFramework.SqlServer (among others) ship key
        // assemblies in those subtrees; before recursion they were falsely reported as
        // ORPHANED because nothing matched the top-level lib folder.
        string[] dllFiles;
        try
        {
            dllFiles = Directory.GetFiles(binFolder, "*.dll", SearchOption.AllDirectories);
        }
        catch (Exception)
        {
            return Task.FromResult(results);
        }

        // Track the directory depth at which each assembly name was first recorded so a
        // shallower (top-level) DLL wins over a deeper (runtimes/*) one if both exist.
        // This keeps the reported PhysicalVersion aligned with what the CLR actually loads
        // at runtime for managed code (top-level wins).
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string dllPath in dllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip native runtime DLLs — they're not managed assemblies and would only
            // produce BadImageFormatException noise. Microsoft conventions place them
            // under "runtimes/<rid>/native/" so a path-segment check is sufficient.
            if (IsUnderNativeRuntimeFolder(dllPath))
            {
                continue;
            }

            try
            {
                var info = AssemblyMetadataReader.ReadAssemblyInfo(dllPath);
                if (info is null)
                {
                    // Non-.NET assembly or non-neutral culture — skip gracefully
                    continue;
                }

                string assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                int depth = ComputeDepth(binFolder, dllPath);

                if (!depths.TryGetValue(assemblyName, out int existing) || depth < existing)
                {
                    results[assemblyName] = info.AssemblyVersion.ToString();
                    depths[assemblyName] = depth;
                }
            }
            catch (Exception)
            {
                // Skip assemblies that cannot be read (native DLLs, corrupted files, etc.)
            }
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Returns true if <paramref name="dllPath"/> sits under a <c>runtimes\&lt;rid&gt;\native\</c>
    /// subtree. Used to avoid attempting managed metadata reads against native libraries.
    /// </summary>
    private static bool IsUnderNativeRuntimeFolder(string dllPath)
    {
        string normalized = dllPath.Replace('/', '\\');
        return normalized.IndexOf(@"\runtimes\", StringComparison.OrdinalIgnoreCase) >= 0
            && normalized.IndexOf(@"\native\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Counts directory-separator hops between <paramref name="root"/> and the parent
    /// folder of <paramref name="dllPath"/>. A DLL directly inside <paramref name="root"/>
    /// has depth 0; one folder deeper has depth 1; etc.
    /// </summary>
    private static int ComputeDepth(string root, string dllPath)
    {
        string? parent = Path.GetDirectoryName(dllPath);
        if (parent is null)
        {
            return 0;
        }
        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parent.Length <= normalizedRoot.Length)
        {
            return 0;
        }
        string rel = parent[(normalizedRoot.Length + 1)..];
        if (string.IsNullOrEmpty(rel))
        {
            return 0;
        }
        int hops = 0;
        foreach (char c in rel)
        {
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
            {
                hops++;
            }
        }
        return hops + 1;
    }

    /// <summary>
    /// Finds the first existing output folder under the project directory.
    /// Checks bin/Debug, bin/Release, and bin/ in that order.
    /// Prefers the folder with the most recently modified DLL.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <returns>Full path to the output folder, or <c>null</c> if none found.</returns>
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

                // Pick the folder with the most recently modified DLL
                DateTime newest = dlls
                    .Select(f => File.GetLastWriteTimeUtc(f))
                    .Max();

                if (newest > bestTimestamp)
                {
                    bestTimestamp = newest;
                    bestFolder = candidate;
                }
            }
            catch (Exception)
            {
                // Access denied or other I/O error — skip this candidate
            }
        }

        return bestFolder;
    }
}
