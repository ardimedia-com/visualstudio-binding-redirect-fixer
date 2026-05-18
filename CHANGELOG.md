# Changelog

All notable changes to the Binding Redirect Fixer extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-05-18

### Added

- **Guided-removal flow for `ORPHANED .NET Framework` redirects** ([#8](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/issues/8)): replaces the one-click `Remove Redirect` button with an inline panel that runs three automated safety checks (solution-wide source-usage grep, GAC folder probe, transitive `bin/` reference scan via `MetadataLoadContext`) plus surfaces the verbatim `<PostBuildEvent>` from the project's `.csproj` for a manual review. Two-branch UX:
  - **All auto checks Pass** → manual confirmation checkbox + `Remove Redirect` button are shown. `Remove Redirect` is gated on the checkbox.
  - **One or more auto checks Fail / Inconclusive** → checkbox + Remove button are hidden; the panel shows a red "Automatic removal is blocked" hint explaining the cause and an `Open Config File for Manual Editing` button that launches the `web.config` / `app.config` in the OS default editor (Visual Studio when launched from inside it).
  Closes the classic "click button with warning to verify first" UX trap. The user no longer sees dead controls when removal is blocked.
- **Recursive `bin/` scan**: `BinFolderScanner` now walks `bin/Debug` / `bin/Release` recursively (was top-level-only), so DLLs that ship under `bin/runtimes/<rid>/lib/<tfm>/` are discovered. Fixes the false-orphan reports for `Microsoft.Data.SqlClient.Extensions.*`, `Microsoft.Data.SqlClient.Internal.Logging`, `Microsoft.EntityFramework.SqlServer`, and any other package using the same layout. On duplicate names the shallower (top-level) copy wins so the reported `PhysicalVersion` matches what the CLR actually loads. `bin/runtimes/<rid>/native/` subtrees are explicitly skipped to avoid `BadImageFormatException` noise.

### Changed

- **`OrphanedFramework` `SuggestedAction` is now `VerifyBeforeRemoval`** (was `RemoveRedirect`). The wrapper exposes new bindable properties (`Verification`, `HasVerification`, `CanRemove`, `BlockReason`, `RunButtonVisibility`, `VerificationResultVisibility`, `UserConfirmedPostBuild`, `PostBuildScript`, `VerificationAuto`) to support the new panel.
- **`Apply All` / bulk-fix no longer auto-removes orphaned .NET Framework redirects**. The allow-list at the bulk-fix path enumerates only the safe single-click actions (`UpdateRedirect` / `AddRedirect` / `RemoveDuplicate` / `RemoveRedirect` / `RemoveAllRedirects`), so `VerifyBeforeRemoval` is naturally excluded from bulk operations. Each row must now be reviewed individually.

### Fixed

- Microsoft.Data.SqlClient.Extensions.\*, Microsoft.Data.SqlClient.Internal.Logging, and Microsoft.EntityFramework.SqlServer no longer appear as `ORPHANED .NET Framework` in the Issues list — they were always present in `bin/runtimes/`, but the previous top-level-only scan missed them. See the recursive scan note under Added.

### Known limitations

- The source-usage check uses the assembly's simple name as a namespace heuristic. Assemblies whose exposed namespaces differ from the assembly name (e.g. `Microsoft.Bcl.AsyncInterfaces` exposes `System.Threading.Tasks`) may report `Pass` even when source code uses the assembly. The transitive `bin/` reference check (3) usually compensates because the consuming DLLs are visible there. A future iteration will harvest exported namespaces from the open `MetadataLoadContext` to close this gap.
- Post-build scripts with MSBuild property substitutions (e.g. `$(TargetDir)`) are displayed verbatim, not expanded. The user reviews them manually.

## [0.3.13] - 2026-05-17

### Added

- **`CRITICAL MISMATCH` status**: distinct red-severity classification for binding redirects whose `newVersion` is strictly greater than the highest version available on disk (`MAX(bin/, packages/)`). This catches the "site won't start" failure mode where `Web.config` was hand-edited or bulk-updated to a version that no installed package provides — the CLR throws `FileLoadException` at startup because the redirect demands a DLL that cannot be loaded from anywhere. Previously these would have been buried in generic `STALE` entries; now they surface separately at the top of the Issues list with `[Update Redirect]` as the suggested fix. New `Critical Mismatch` filter in the status dropdown.
- **Build-required banner**: when the scan completes but no on-disk DLLs were found for any of the binding redirects (project hasn't been built or NuGet packages not restored), the tool window now displays a red banner explaining that `MISMATCH` / `STALE` / `CRITICAL MISMATCH` detection is degraded and most entries will incorrectly surface as `ORPHANED`. Prompts the user to build and restore before relying on the analysis. Also fires (with a softer message) when more than half of all redirects have no matching DLL.

## [0.3.12] - 2026-05-15

### Fixed

- **False "UNUSED IN LIBRARY" flag on legacy ASP.NET Web Application Projects**: `DetectIsLibrary` previously recognized only SDK-style Web projects (`Microsoft.NET.Sdk.Web`) as host applications. Legacy ASP.NET MVC / WebForms / WebAPI projects use `OutputType=Library` but are hosted by IIS, which reads `web.config` binding redirects at runtime. They were wrongly flagged as DLL libraries whose redirects could be removed, surfacing "All binding redirects in this project can be safely removed" and a "Remove All Redirects" button that would have broken the running application. Detection now also returns false when the `.csproj` contains the WAP flavor GUID `{349c5851-65df-11da-9384-00065b846f21}` in `<ProjectTypeGuids>` or imports `Microsoft.WebApplication.targets`.

## [0.3.11] - 2026-04-08

### Fixed

- **Dark theme readability**: ListView text, column headers (including hover/pressed states), and selected item now use VS theme colors — readable in Dark, Blue, and High Contrast themes

## [0.3.4] - 2026-04-06

### Fixed

- **Project configuration**: Removed legacy VSSDK properties to match official VS Extensibility SDK samples, added `PrivateAssets="all"` on SDK packages

## [0.3.3] - 2026-04-05

### Changed

- **Dependency update**: Ardimedia.VsExtensions.Common 1.1.0

## [0.3.1] - 2026-04-04

### Changed

- **Feedback Tab**: Replaced link-only feedback with full GitHub issue form (Bug/Feature toggle, title with BUG:/FEATURE: prefix, description, "Open Browser for GitHub Issue" button)
- **DeployExtension**: Added debug deployment to experimental instance for development

## [0.3.0] - 2026-04-04

### Added

- **UNUSED IN LIBRARY** status: detects binding redirects in class library (DLL) projects where the CLR never reads the config file (issue #4)
- **Remove All Redirects** action: bulk-removes entire `<assemblyBinding>` section or deletes the config file if it contains only redirects
- Project type detection: `DetectIsLibrary`, `DetectIsTestProject`, `DetectHasAppConfigForCompiler` methods
- Modern .NET detection: flags all binding redirects as obsolete in .NET 5+ projects (regardless of project type)
- ConfigPatcher methods: `HasOnlyAssemblyBinding`, `RemoveAssemblyBindingSection`, `RemoveConfigFileAndCsprojReference`
- Blue info warning in detail panel explaining why DLL project redirects are unused
- "UNUSED IN LIBRARY" card in Learn tab
- 20 new unit tests for project type detection and config section operations (81 total)

### Exceptions (not flagged)

- .NET Framework test projects (test runner reads their config as host)
- EXE and Web projects (they are host applications)
- Projects using `AppConfigForCompiler` / `UseAppConfigForCompiler`

## [0.2.1] - 2026-03-30

### Added

- Parallel project scanning using `Parallel.ForEachAsync` with up to 5 concurrent projects (issue #3)
- Progress indicator shows "Analysing 3 of 12: ProjectName..." during parallel scan
- Better empty state message when no relevant projects found (issue #2)

### Changed

- Update project description and marketplace overview to mention .NET (Core) support
- Update marketplace tags to include dotnet, orphaned, deprecated
- Add `.claude/overview.md` to release sync checklist

## [0.2.0] - 2026-03-30

### Added

- **ORPHANED .NET (Core)** status: no DLL found in a .NET (Core) project, binding redirect is orphaned and safe to remove (green)
- **ORPHANED .NET Framework** status: no DLL found in a .NET Framework project, likely orphaned but verify GAC/post-build (amber)
- Remove Redirect action for DEPRECATED items with warning to check NuGet references first
- Remove Redirect action for ORPHANED items (both .NET and .NET Framework)
- Framework detection from `.csproj` (reads `TargetFramework`, `TargetFrameworks`, `TargetFrameworkVersion`)
- Framework-aware detail panel warnings: green for .NET (Core), amber for .NET Framework
- CONFLICT, DEPRECATED, ORPHANED .NET (Core), ORPHANED .NET Framework cards in Background tab
- Status filter entries for "Orphaned .NET (Core)" and "Orphaned .NET Framework" for targeted batch fixes
- Test project with 55 unit tests (EvaluateStatus rules, ConfigPatcher, DeprecatedPackageRegistry, DetectNetFramework)

### Changed

- TOKEN LOST now only applies when DLL is present but unsigned (redirect still needed); no-DLL cases are now ORPHANED
- Background tab cards sorted by severity (red > amber > green > blue), then alphabetically
- Info bar updated to mention ORPHANED status

### Fixed

- Empty Analyse button text after cancellation (was binding to a removed property)
- Column header click area: sorting now works on the full header cell, not just the text label

## [0.1.7] - 2026-03-25

### Added

- Resizable columns: switched from fixed-width Grid layout to native WPF GridView with drag-to-resize column headers
- Sortable columns: click any column header to sort ascending/descending, with sort indicator (▲/▼)
- Horizontal scrollbar for overflow when columns exceed available width

## [0.1.6] - 2026-03-25

### Added

- Deprecated package detection: flags packages like `Microsoft.Azure.Services.AppAuthentication` with migration guidance instead of fixing their binding redirects
- Built-in registry of deprecated Azure SDK packages with replacement recommendations and migration URLs
- New DEPRECATED status in the Issues grid with filtering support

## [0.1.5] - 2026-03-19

### Added

- TokenLost status detection for binding redirect token loss scenarios

## [0.1.4] - 2026-03-15

### Changed

- Fix minor UI enhancements

## [0.1.0] - 2026-03-13

### Added

- Initial release
- Scan command via Tools menu to detect binding redirect issues
- Multi-source version resolution (NuGet resolved, package reference, bin/ DLL, config redirect)
- Support for PackageReference and packages.config projects
- Issue detection: STALE, MISSING, CONFLICT, DUPLICATE, MISMATCH statuses
- One-click fix for individual issues
- Fix All to batch-resolve all detected issues
- Timestamped config file backups before modifications
- Detail panel with educational "What happened?" explanations
- Learn tab with binding redirect documentation
- Project and status filtering
- Assembly name search
- Theme-aware UI (Light, Dark, Blue, High Contrast)
- Persistent user settings (backup preference, panel layout)
