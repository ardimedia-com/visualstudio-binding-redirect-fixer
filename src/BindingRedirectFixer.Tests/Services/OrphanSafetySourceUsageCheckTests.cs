using BindingRedirectFixer.Models;
using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class OrphanSafetySourceUsageCheckTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRF_SrcUse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    private static async Task<SafetyCheckResult> RunAsync(string solutionRoot, string assemblyName)
    {
        var check = new OrphanSafetySourceUsageCheck();
        var ctx = new OrphanCheckContext(assemblyName, solutionRoot, solutionRoot);
        return await check.RunAsync(ctx, CancellationToken.None);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task UsingDirective_DetectedAsFail()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Foo.cs"),
                "using ICSharpCode.SharpZipLib.Zip;\nclass Foo {}");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            Assert.AreEqual(SafetyCheckOutcome.Fail, r.Outcome);
            Assert.IsTrue(r.Detail.Contains("Foo.cs"));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task QualifiedTypeReference_DetectedAsFail()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Bar.cs"),
                "class Bar { void M() { var x = new ICSharpCode.SharpZipLib.Zip.ZipFile(\"\"); } }");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            Assert.AreEqual(SafetyCheckOutcome.Fail, r.Outcome);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NoReferences_ReturnsPass()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Clean.cs"), "using System;\nclass Clean {}");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome);
            Assert.IsTrue(r.Detail.Contains("0 source files"));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BinAndObjFolders_AreSkipped()
    {
        // A bin/ DLL stub written as .cs (artificial) must NOT be scanned — those
        // folders are build output and would otherwise produce false-positives.
        string dir = CreateTempDir();
        try
        {
            string binDir = Path.Combine(dir, "bin", "Debug");
            string objDir = Path.Combine(dir, "obj");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(binDir, "Generated.cs"), "using ICSharpCode.SharpZipLib;");
            File.WriteAllText(Path.Combine(objDir, "Generated.cs"), "using ICSharpCode.SharpZipLib;");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NodeModulesAndDotGit_AreSkipped()
    {
        string dir = CreateTempDir();
        try
        {
            string nodeModules = Path.Combine(dir, "node_modules");
            string gitDir = Path.Combine(dir, ".git");
            Directory.CreateDirectory(nodeModules);
            Directory.CreateDirectory(gitDir);
            File.WriteAllText(Path.Combine(nodeModules, "Generated.cs"), "using ICSharpCode.SharpZipLib;");
            File.WriteAllText(Path.Combine(gitDir, "Generated.cs"), "using ICSharpCode.SharpZipLib;");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task VbFiles_AreAlsoScanned()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Legacy.vb"),
                "Imports ICSharpCode.SharpZipLib.Zip\nClass Legacy\nEnd Class");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            // The pattern uses "using" specifically, but the qualified-reference branch matches Imports too.
            Assert.AreEqual(SafetyCheckOutcome.Fail, r.Outcome);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NonexistentSearchRoot_ReturnsInconclusive()
    {
        string fakeDir = Path.Combine(Path.GetTempPath(), "BRF_NoExist_" + Guid.NewGuid().ToString("N"));

        SafetyCheckResult r = await RunAsync(fakeDir, "ICSharpCode.SharpZipLib");

        Assert.AreEqual(SafetyCheckOutcome.Inconclusive, r.Outcome);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SubstringMatch_NotTriggeredByWordBoundary()
    {
        // "ICSharpCode.SharpZipLib" should NOT match a different but textually-overlapping
        // identifier — relies on regex word boundary on the left.
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Other.cs"),
                "using MyICSharpCode.SharpZipLib;\nclass Other {}");

            SafetyCheckResult r = await RunAsync(dir, "ICSharpCode.SharpZipLib");

            Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome);
        }
        finally { Cleanup(dir); }
    }
}
