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

    // Note: the "WINDIR missing / not-existing" path is intentionally not unit-tested.
    // Mutating Environment.GetEnvironmentVariable("WINDIR") races with other tests when
    // MSTest runs methods in parallel, and the defensive code path is trivially inspectable.
    // Production callers on Windows always have WINDIR set.
}
