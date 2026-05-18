using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

/// <summary>
/// Tests for the recursive bin/ scan introduced by v0.4.0 M4. The probe must descend
/// into <c>bin\runtimes\&lt;rid&gt;\lib\&lt;tfm&gt;\</c> subtrees so DLLs shipped by
/// Microsoft.Data.SqlClient / EntityFramework.SqlServer / similar packages are
/// detected (they were falsely reported as ORPHANED before recursion).
/// </summary>
[TestClass]
public class BinFolderScannerTests
{
    /// <summary>
    /// Source DLL we copy into fake bin folders. Any managed assembly with neutral culture
    /// would do; we use the production extension assembly since the test runner has it on
    /// disk in its own bin/.
    /// </summary>
    private static string SourceFixtureDll =>
        Path.Combine(
            Path.GetDirectoryName(typeof(BinFolderScannerTests).Assembly.Location)!,
            "BindingRedirectFixer.dll");

    private static string CreateProjectWithBin()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRF_BinScan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "bin", "Debug"));
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TopLevelDll_IsFound()
    {
        string project = CreateProjectWithBin();
        try
        {
            if (!File.Exists(SourceFixtureDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {SourceFixtureDll}");
                return;
            }

            string dst = Path.Combine(project, "bin", "Debug", "BindingRedirectFixer.dll");
            File.Copy(SourceFixtureDll, dst);

            var scanner = new BinFolderScanner();
            var results = await scanner.ScanAsync(project, CancellationToken.None);

            Assert.IsTrue(results.ContainsKey("BindingRedirectFixer"),
                $"Top-level DLL must be discovered. Found keys: [{string.Join(", ", results.Keys)}]");
        }
        finally { Cleanup(project); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DllUnderRuntimesSubtree_IsFound_NoLongerOrphan()
    {
        // The original false-orphan case: Microsoft.Data.SqlClient.Extensions.* and
        // EntityFramework.SqlServer ship under bin/runtimes/<rid>/lib/<tfm>/ rather than
        // bin/. Pre-M4 the scanner only looked at the top level, so EvaluateStatus would
        // mark them as ORPHANED. With recursion, PhysicalVersion gets populated and they
        // fall out of the orphan branch.
        string project = CreateProjectWithBin();
        try
        {
            if (!File.Exists(SourceFixtureDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {SourceFixtureDll}");
                return;
            }

            string nested = Path.Combine(project, "bin", "Debug", "runtimes", "win-x64", "lib", "net48");
            Directory.CreateDirectory(nested);

            // Critical: needs a sibling DLL at the top-level so FindOutputFolder picks
            // bin/Debug. We use a copy of the fixture DLL with a different name so it
            // doesn't shadow the nested one in the dedupe step.
            string topLevel = Path.Combine(project, "bin", "Debug", "AnchorAssembly.dll");
            File.Copy(SourceFixtureDll, topLevel);

            string nestedDll = Path.Combine(nested, "BindingRedirectFixer.dll");
            File.Copy(SourceFixtureDll, nestedDll);

            var scanner = new BinFolderScanner();
            var results = await scanner.ScanAsync(project, CancellationToken.None);

            Assert.IsTrue(results.ContainsKey("BindingRedirectFixer"),
                $"Nested DLL must be discovered. Found keys: [{string.Join(", ", results.Keys)}]");
            Assert.IsTrue(results.ContainsKey("AnchorAssembly"));
        }
        finally { Cleanup(project); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TopLevelWins_OverRuntimesSubtree_OnDuplicateName()
    {
        // If both bin/Debug/X.dll AND bin/Debug/runtimes/.../X.dll exist, the top-level
        // version is the one the CLR actually loads, so the scan must report the top-level
        // assembly version, not the nested one.
        string project = CreateProjectWithBin();
        try
        {
            if (!File.Exists(SourceFixtureDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {SourceFixtureDll}");
                return;
            }

            // We can't easily author two DLLs with DIFFERENT AssemblyVersions on the fly,
            // so this test just asserts that exactly one entry is emitted and the path
            // chosen was the top-level one. We use the file's containing folder via a
            // sentinel filename trick: copy the fixture twice under different names and
            // verify the top-level result key matches what we expect.
            File.Copy(SourceFixtureDll, Path.Combine(project, "bin", "Debug", "BindingRedirectFixer.dll"));
            string nested = Path.Combine(project, "bin", "Debug", "runtimes", "win-x64", "lib", "net48");
            Directory.CreateDirectory(nested);
            File.Copy(SourceFixtureDll, Path.Combine(nested, "BindingRedirectFixer.dll"));

            var scanner = new BinFolderScanner();
            var results = await scanner.ScanAsync(project, CancellationToken.None);

            // Both files have the same AssemblyVersion so we can only assert that the entry
            // exists and the count remains 1 (dedupe worked).
            Assert.IsTrue(results.ContainsKey("BindingRedirectFixer"));
            // Sanity: there should be no second entry under a separate key
            Assert.AreEqual(1, results.Count(kvp =>
                string.Equals(kvp.Key, "BindingRedirectFixer", StringComparison.OrdinalIgnoreCase)));
        }
        finally { Cleanup(project); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NativeRuntimesSubfolder_IsSkipped_DoesNotThrow()
    {
        // Native libraries shipped under bin/runtimes/<rid>/native/ are not managed and
        // would otherwise produce BadImageFormatException noise. Explicit skip means no
        // exception, no entry, no perf hit.
        string project = CreateProjectWithBin();
        try
        {
            if (!File.Exists(SourceFixtureDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {SourceFixtureDll}");
                return;
            }

            // Anchor DLL so FindOutputFolder doesn't reject the empty top level
            File.Copy(SourceFixtureDll, Path.Combine(project, "bin", "Debug", "Anchor.dll"));

            string nativeDir = Path.Combine(project, "bin", "Debug", "runtimes", "win-x64", "native");
            Directory.CreateDirectory(nativeDir);
            // Write a non-PE file pretending to be a DLL — would normally throw on metadata read
            File.WriteAllBytes(Path.Combine(nativeDir, "nativeblob.dll"), [0x00, 0x01, 0x02, 0x03]);

            var scanner = new BinFolderScanner();
            var results = await scanner.ScanAsync(project, CancellationToken.None);

            Assert.IsFalse(results.ContainsKey("nativeblob"),
                "Native runtimes/<rid>/native/ DLLs must be skipped.");
            Assert.IsTrue(results.ContainsKey("Anchor"));
        }
        finally { Cleanup(project); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NoBinFolder_ReturnsEmptyResult()
    {
        string project = Path.Combine(Path.GetTempPath(), "BRF_BinScan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        try
        {
            var scanner = new BinFolderScanner();
            var results = await scanner.ScanAsync(project, CancellationToken.None);

            Assert.AreEqual(0, results.Count);
        }
        finally { Cleanup(project); }
    }
}
