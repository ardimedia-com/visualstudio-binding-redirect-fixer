using BindingRedirectFixer.Models;
using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class OrphanSafetyBinReferenceCheckTests
{
    private static string CreateTempProject(bool withEmptyBin = false)
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRF_BinRef_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        if (withEmptyBin)
        {
            Directory.CreateDirectory(Path.Combine(dir, "bin", "Debug"));
        }
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NoBinFolder_ReturnsInconclusive()
    {
        string dir = CreateTempProject(withEmptyBin: false);
        try
        {
            var check = new OrphanSafetyBinReferenceCheck();
            var ctx = new OrphanCheckContext("Any.Assembly", dir, dir);

            SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(SafetyCheckOutcome.Inconclusive, r.Outcome);
            Assert.IsTrue(r.Detail.Contains("Build the project"));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task EmptyBinFolder_ReturnsInconclusive()
    {
        string dir = CreateTempProject(withEmptyBin: true);
        try
        {
            var check = new OrphanSafetyBinReferenceCheck();
            var ctx = new OrphanCheckContext("Any.Assembly", dir, dir);

            SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

            // No DLLs found at all → FindOutputFolder rejects the empty candidate.
            Assert.AreEqual(SafetyCheckOutcome.Inconclusive, r.Outcome);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BinWithKnownAssemblyRef_ReturnsFail()
    {
        // Use the test assembly's own bin output: every DLL there references core BCL
        // assemblies like System.Runtime, so a probe for "System.Runtime" must Fail.
        // This exercises the real MetadataLoadContext path end-to-end.
        string testDir = Path.GetDirectoryName(typeof(OrphanSafetyBinReferenceCheckTests).Assembly.Location)!;
        string fakeProject = Path.Combine(Path.GetTempPath(), "BRF_BinRef_" + Guid.NewGuid().ToString("N"));
        string fakeBin = Path.Combine(fakeProject, "bin", "Debug");
        Directory.CreateDirectory(fakeBin);

        // Copy one DLL from the test runtime into the fake bin so we have something to probe.
        string srcDll = Path.Combine(testDir, "BindingRedirectFixer.dll");
        string dstDll = Path.Combine(fakeBin, "BindingRedirectFixer.dll");
        try
        {
            if (!File.Exists(srcDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {srcDll}");
                return;
            }
            File.Copy(srcDll, dstDll, overwrite: true);

            var check = new OrphanSafetyBinReferenceCheck();
            var ctx = new OrphanCheckContext("System.Runtime", fakeProject, fakeProject);

            SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(SafetyCheckOutcome.Fail, r.Outcome,
                $"Expected Fail, got {r.Outcome} with detail: {r.Detail}");
            Assert.IsTrue(r.Detail.Contains("BindingRedirectFixer.dll"));
        }
        finally { Cleanup(fakeProject); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BinWithoutReferenceToTarget_ReturnsPass()
    {
        // Same fake bin, but probe for a name no real DLL references.
        string testDir = Path.GetDirectoryName(typeof(OrphanSafetyBinReferenceCheckTests).Assembly.Location)!;
        string fakeProject = Path.Combine(Path.GetTempPath(), "BRF_BinRef_" + Guid.NewGuid().ToString("N"));
        string fakeBin = Path.Combine(fakeProject, "bin", "Debug");
        Directory.CreateDirectory(fakeBin);

        string srcDll = Path.Combine(testDir, "BindingRedirectFixer.dll");
        string dstDll = Path.Combine(fakeBin, "BindingRedirectFixer.dll");
        try
        {
            if (!File.Exists(srcDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {srcDll}");
                return;
            }
            File.Copy(srcDll, dstDll, overwrite: true);

            var check = new OrphanSafetyBinReferenceCheck();
            var ctx = new OrphanCheckContext(
                AssemblyName: "Some.Assembly.That.Nothing.References." + Guid.NewGuid().ToString("N"),
                ProjectDirectory: fakeProject,
                SolutionDirectory: fakeProject);

            SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome,
                $"Expected Pass, got {r.Outcome} with detail: {r.Detail}");
        }
        finally { Cleanup(fakeProject); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TargetAssemblyItselfInBin_IsSkipped_NotCountedAsSelfReferrer()
    {
        // If a DLL with the target name happens to be in bin/, it must NOT be counted
        // as a transitive referrer to itself.
        string testDir = Path.GetDirectoryName(typeof(OrphanSafetyBinReferenceCheckTests).Assembly.Location)!;
        string fakeProject = Path.Combine(Path.GetTempPath(), "BRF_BinRef_" + Guid.NewGuid().ToString("N"));
        string fakeBin = Path.Combine(fakeProject, "bin", "Debug");
        Directory.CreateDirectory(fakeBin);

        string srcDll = Path.Combine(testDir, "BindingRedirectFixer.dll");
        try
        {
            if (!File.Exists(srcDll))
            {
                Assert.Inconclusive($"Expected fixture DLL not found at {srcDll}");
                return;
            }
            File.Copy(srcDll, Path.Combine(fakeBin, "BindingRedirectFixer.dll"), overwrite: true);

            var check = new OrphanSafetyBinReferenceCheck();
            var ctx = new OrphanCheckContext("BindingRedirectFixer", fakeProject, fakeProject);

            SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome,
                $"Expected Pass (self-ref excluded), got {r.Outcome}: {r.Detail}");
        }
        finally { Cleanup(fakeProject); }
    }
}
