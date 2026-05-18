using BindingRedirectFixer.Models;
using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class OrphanSafetyGacProbeCheckTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task NameThatCannotExistInGac_ReturnsPass()
    {
        // A name we know is not in any GAC folder — must Pass.
        var check = new OrphanSafetyGacProbeCheck();
        var ctx = new OrphanCheckContext(
            AssemblyName: "DefinitelyNotARealAssembly_" + Guid.NewGuid().ToString("N"),
            ProjectDirectory: Path.GetTempPath(),
            SolutionDirectory: Path.GetTempPath());

        SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

        Assert.AreEqual(SafetyCheckOutcome.Pass, r.Outcome);
        Assert.IsTrue(r.Detail.Contains("not present"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TitleIsStable()
    {
        var check = new OrphanSafetyGacProbeCheck();
        Assert.AreEqual("Global Assembly Cache", check.Title);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MissingWindir_ReturnsInconclusive()
    {
        // Temporarily clear WINDIR; the check must report Inconclusive rather than throw or
        // misclassify as Pass (we don't actually know without probing).
        string? original = Environment.GetEnvironmentVariable("WINDIR");
        try
        {
            Environment.SetEnvironmentVariable("WINDIR", string.Empty);

            var check = new OrphanSafetyGacProbeCheck();
            var ctx = new OrphanCheckContext("Anything", Path.GetTempPath(), null);

            SafetyCheckResult r = await check.RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(SafetyCheckOutcome.Inconclusive, r.Outcome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDIR", original);
        }
    }
}
