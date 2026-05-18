using BindingRedirectFixer.Models;
using BindingRedirectFixer.ToolWindows;

namespace BindingRedirectFixer.Tests.ToolWindows;

/// <summary>
/// Tests for the wrapper view model's verification-related binding properties.
/// These cover the bindings the WPF guided-removal panel relies on.
/// </summary>
[TestClass]
public class AssemblyRedirectInfoViewModelTests
{
    private static AssemblyRedirectInfoViewModel CreateOrphanedFwWrapper()
    {
        var model = new AssemblyRedirectInfo
        {
            ProjectName = "TestProject",
            Name = "ICSharpCode.SharpZipLib",
            Status = RedirectStatus.OrphanedFramework,
            SuggestedAction = FixAction.VerifyBeforeRemoval,
            CurrentRedirectVersion = "1.4.2.13",
            ConfigPublicKeyToken = "1b03e6acf1164f73",
            IsNetFramework = true,
        };
        return new AssemblyRedirectInfoViewModel(model);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InitialState_HasNoVerification_ButtonVisible_RemoveBlocked()
    {
        var vm = CreateOrphanedFwWrapper();

        Assert.IsFalse(vm.HasVerification);
        Assert.AreEqual("Visible", vm.RunButtonVisibility);
        Assert.AreEqual("Collapsed", vm.VerificationResultVisibility);
        Assert.AreEqual("Visible", vm.VerificationVisibility);
        Assert.IsFalse(vm.CanRemove);
        Assert.IsNull(vm.BlockReason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SettingVerification_FlipsVisibilityAndKeepsRemoveBlockedUntilUserConfirms()
    {
        var vm = CreateOrphanedFwWrapper();

        vm.Verification = new OrphanVerificationReport
        {
            Auto =
            [
                new() { Title = "Source", Outcome = SafetyCheckOutcome.Pass, Detail = "0 matches" },
                new() { Title = "GAC", Outcome = SafetyCheckOutcome.Pass, Detail = "absent" },
                new() { Title = "Bin refs", Outcome = SafetyCheckOutcome.Pass, Detail = "no referrers" },
            ],
            PostBuildScript = "echo done",
        };

        Assert.IsTrue(vm.HasVerification);
        Assert.AreEqual("Collapsed", vm.RunButtonVisibility);
        Assert.AreEqual("Visible", vm.VerificationResultVisibility);
        Assert.IsFalse(vm.CanRemove, "Manual confirmation still missing.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UserConfirmsAfterAllPass_CanRemoveBecomesTrue()
    {
        var vm = CreateOrphanedFwWrapper();
        vm.Verification = new OrphanVerificationReport
        {
            Auto =
            [
                new() { Title = "Source", Outcome = SafetyCheckOutcome.Pass, Detail = "0 matches" },
            ],
        };
        Assert.IsFalse(vm.CanRemove);

        vm.UserConfirmedPostBuild = true;

        Assert.IsTrue(vm.CanRemove);
        Assert.IsTrue(vm.Verification!.UserConfirmedPostBuild,
            "Mirroring: report state should follow wrapper state.");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AutoCheckFail_BlockReasonExposed_RemoveBlockedEvenAfterConfirm()
    {
        var vm = CreateOrphanedFwWrapper();
        vm.Verification = new OrphanVerificationReport
        {
            Auto =
            [
                new() { Title = "Source", Outcome = SafetyCheckOutcome.Pass, Detail = "0 matches" },
                new() { Title = "Bin refs", Outcome = SafetyCheckOutcome.Fail, Detail = "Some.Other.dll references it" },
            ],
        };
        vm.UserConfirmedPostBuild = true;

        Assert.IsFalse(vm.CanRemove);
        Assert.IsTrue(vm.HasBlockReason);
        Assert.IsTrue(vm.BlockReason!.Contains("Bin refs"));
        Assert.IsTrue(vm.BlockReason.Contains("Some.Other.dll"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SettingFreshVerification_ResetsManualConfirmation()
    {
        var vm = CreateOrphanedFwWrapper();
        vm.Verification = new OrphanVerificationReport
        {
            Auto = [new() { Title = "X", Outcome = SafetyCheckOutcome.Pass, Detail = "ok" }],
        };
        vm.UserConfirmedPostBuild = true;
        Assert.IsTrue(vm.CanRemove);

        // Re-run: user must re-confirm because new auto results invalidate the prior consent.
        vm.Verification = new OrphanVerificationReport
        {
            Auto = [new() { Title = "X", Outcome = SafetyCheckOutcome.Pass, Detail = "ok again" }],
        };

        Assert.IsFalse(vm.UserConfirmedPostBuild,
            "Setting a fresh verification must clear stale manual consent.");
        Assert.IsFalse(vm.CanRemove);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OutcomeIcon_Pass_Fail_Inconclusive_AllRendered()
    {
        var pass = new SafetyCheckResult { Outcome = SafetyCheckOutcome.Pass };
        var fail = new SafetyCheckResult { Outcome = SafetyCheckOutcome.Fail };
        var inc = new SafetyCheckResult { Outcome = SafetyCheckOutcome.Inconclusive };

        Assert.AreEqual("✓", pass.OutcomeIcon);
        Assert.AreEqual("✗", fail.OutcomeIcon);
        Assert.AreEqual("?", inc.OutcomeIcon);
    }
}
