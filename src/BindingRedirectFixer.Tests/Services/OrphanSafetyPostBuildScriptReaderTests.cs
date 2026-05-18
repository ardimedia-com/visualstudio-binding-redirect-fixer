using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class OrphanSafetyPostBuildScriptReaderTests
{
    private static string CreateTempProjectDir(string csprojContent)
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRF_PostBuild_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "TestProject.csproj"), csprojContent);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LegacyPostBuildEventProperty_IsExtractedVerbatim()
    {
        string dir = CreateTempProjectDir("""
            <Project>
              <PropertyGroup>
                <PostBuildEvent>xcopy /Y "$(TargetDir)*.dll" "C:\Deploy\"</PostBuildEvent>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            string script = await OrphanSafetyPostBuildScriptReader.ReadAsync(dir, CancellationToken.None);

            Assert.IsTrue(script.Contains("xcopy /Y"));
            Assert.IsTrue(script.Contains("C:\\Deploy\\"));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ModernTargetWithExec_IsExtracted()
    {
        string dir = CreateTempProjectDir("""
            <Project>
              <Target Name="PostBuild" AfterTargets="PostBuildEvent">
                <Exec Command="copy /Y &quot;$(TargetPath)&quot; &quot;C:\Deploy\&quot;" />
              </Target>
            </Project>
            """);
        try
        {
            string script = await OrphanSafetyPostBuildScriptReader.ReadAsync(dir, CancellationToken.None);

            Assert.IsTrue(script.Contains("copy /Y"));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NoPostBuildSteps_ReturnsEmpty()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            string script = await OrphanSafetyPostBuildScriptReader.ReadAsync(dir, CancellationToken.None);

            Assert.AreEqual(string.Empty, script);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MalformedCsproj_ReturnsEmpty_DoesNotThrow()
    {
        string dir = CreateTempProjectDir("<<<not really xml");
        try
        {
            string script = await OrphanSafetyPostBuildScriptReader.ReadAsync(dir, CancellationToken.None);

            Assert.AreEqual(string.Empty, script);
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NoCsproj_ReturnsEmpty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRF_PostBuild_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string script = await OrphanSafetyPostBuildScriptReader.ReadAsync(dir, CancellationToken.None);

            Assert.AreEqual(string.Empty, script);
        }
        finally { Cleanup(dir); }
    }
}
