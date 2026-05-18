using System.Xml.Linq;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Extracts the verbatim text of every <c>&lt;PostBuildEvent&gt;</c> element in a project's
/// <c>.csproj</c> file. Surfaced unchanged in the UI so the user can decide whether a
/// custom step copies the orphaned assembly — a check that cannot be reliably automated
/// because post-build scripts are arbitrary shell.
/// </summary>
public static class OrphanSafetyPostBuildScriptReader
{
    /// <summary>
    /// Reads the project file under <paramref name="projectDirectory"/> and concatenates
    /// every <c>PostBuildEvent</c> element. Returns the empty string when no project
    /// file is found, no post-build steps exist, or the file cannot be parsed.
    /// </summary>
    public static Task<string> ReadAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(projectDirectory))
        {
            return Task.FromResult(string.Empty);
        }

        string[] csprojFiles;
        try
        {
            csprojFiles = Directory.GetFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return Task.FromResult(string.Empty);
        }

        if (csprojFiles.Length == 0)
        {
            return Task.FromResult(string.Empty);
        }

        try
        {
            XDocument doc = XDocument.Load(csprojFiles[0]);
            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Modern MSBuild stores post-build steps as a property OR as a Target with an Exec.
            // Cover both forms by concatenating each match.
            var blocks = new List<string>();

            foreach (XElement el in doc.Descendants(ns + "PostBuildEvent"))
            {
                string text = el.Value;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    blocks.Add(text.Trim());
                }
            }

            // Also surface <Target Name="PostBuild" AfterTargets="PostBuildEvent"> ... <Exec Command="..."/> ... </Target>
            foreach (XElement target in doc.Descendants(ns + "Target"))
            {
                string? afterTargets = target.Attribute("AfterTargets")?.Value;
                if (!string.Equals(afterTargets, "PostBuildEvent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foreach (XElement exec in target.Descendants(ns + "Exec"))
                {
                    string? command = exec.Attribute("Command")?.Value;
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        blocks.Add(command.Trim());
                    }
                }
            }

            return Task.FromResult(blocks.Count == 0 ? string.Empty : string.Join("\n---\n", blocks));
        }
        catch (Exception)
        {
            return Task.FromResult(string.Empty);
        }
    }
}
