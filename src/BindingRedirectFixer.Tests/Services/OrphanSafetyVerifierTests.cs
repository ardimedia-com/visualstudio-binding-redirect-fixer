using BindingRedirectFixer.Models;
using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class OrphanSafetyVerifierTests
{
    /// <summary>
    /// Stub check returning a pre-canned outcome; used to verify orchestration without
    /// hitting the file system or GAC.
    /// </summary>
    private sealed class StubCheck(string title, SafetyCheckOutcome outcome, string detail) : IOrphanSafetyCheck
    {
        public string Title { get; } = title;
        public Task<SafetyCheckResult> RunAsync(OrphanCheckContext _, CancellationToken __)
            => Task.FromResult(new SafetyCheckResult { Title = this.Title, Outcome = outcome, Detail = detail });
    }

    private static OrphanCheckContext FreshContext()
    {
        // ProjectDirectory points at a fresh empty dir so PostBuildScriptReader returns "".
        string dir = Path.Combine(Path.GetTempPath(), "BRF_Verifier_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new OrphanCheckContext("Whatever", dir, dir);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AllPass_ProducesReportWithUserConfirmationDefaultFalse()
    {
        var verifier = new OrphanSafetyVerifier(
        [
            new StubCheck("A", SafetyCheckOutcome.Pass, "a-ok"),
            new StubCheck("B", SafetyCheckOutcome.Pass, "b-ok"),
        ]);

        OrphanVerificationReport report = await verifier.VerifyAsync(FreshContext(), CancellationToken.None);

        Assert.AreEqual(2, report.Auto.Count);
        Assert.IsTrue(report.Auto.All(c => c.Outcome == SafetyCheckOutcome.Pass));
        Assert.IsFalse(report.UserConfirmedPostBuild,
            "User confirmation must default to false — the UI elicits it explicitly.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ChecksRunInParallel_ResultOrderPreserved()
    {
        // Order in the report should match the order of registered checks so the UI
        // can render a stable checklist regardless of which check finishes first.
        var verifier = new OrphanSafetyVerifier(
        [
            new StubCheck("First",  SafetyCheckOutcome.Pass, "1"),
            new StubCheck("Second", SafetyCheckOutcome.Fail, "2"),
            new StubCheck("Third",  SafetyCheckOutcome.Inconclusive, "3"),
        ]);

        OrphanVerificationReport report = await verifier.VerifyAsync(FreshContext(), CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "First", "Second", "Third" },
            report.Auto.Select(c => c.Title).ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PostBuildScriptIsRead_AndAttachedToReport()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRF_Verifier_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "T.csproj"), """
            <Project>
              <PropertyGroup>
                <PostBuildEvent>echo deploying</PostBuildEvent>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            var verifier = new OrphanSafetyVerifier([new StubCheck("X", SafetyCheckOutcome.Pass, "ok")]);
            var ctx = new OrphanCheckContext("Whatever", dir, dir);

            OrphanVerificationReport report = await verifier.VerifyAsync(ctx, CancellationToken.None);

            Assert.IsTrue(report.PostBuildScript.Contains("echo deploying"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_NullChecks_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => _ = new OrphanSafetyVerifier(null!));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateDefault_ProducesThreeRegisteredChecks()
    {
        // Indirect verification — Verify against a context where each default check will
        // run to completion (Pass / Inconclusive) and we just count the resulting list.
        OrphanSafetyVerifier verifier = OrphanSafetyVerifier.CreateDefault();

        // We can't directly inspect the list, but a verify call on a fresh empty context
        // should produce exactly 3 results — that's the count of default checks.
        string dir = Path.Combine(Path.GetTempPath(), "BRF_Verifier_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            OrphanVerificationReport report = verifier.VerifyAsync(
                new OrphanCheckContext("Nothing.Real_" + Guid.NewGuid().ToString("N"), dir, dir),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(3, report.Auto.Count);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
