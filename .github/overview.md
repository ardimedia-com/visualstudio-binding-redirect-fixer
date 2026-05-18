# Binding Redirect Fixer

Automatically detects and repairs stale, missing, or orphaned assembly binding redirects in .NET Framework and .NET (Core) projects.

## The Problem

After NuGet updates or partial deployments, binding redirects in `web.config` / `app.config` often reference wrong assembly versions, causing runtime errors like:

> Could not load file or assembly 'Newtonsoft.Json, Version=12.0.0.0, ...' manifest definition does not match the assembly reference.

Tracking down which redirects are wrong and what versions they should point to is tedious and error-prone. **This extension automates the entire process.**

## Features

- **Automatic Detection** -- scans all projects in your solution for stale, missing, orphaned, conflicting, or critically mismatched binding redirects
- **Multi-Source Version Resolution** -- cross-references four independent sources to pinpoint exactly where versions diverge:
  1. NuGet resolved DLL (authoritative)
  2. Package reference version (cross-check)
  3. Physical DLL in `bin/` -- walks `bin/runtimes/<rid>/lib/<tfm>/` too, top-level DLL wins on duplicate names
  4. Config redirect (what the runtime uses)
- **Smart Fix Actions** -- updates stale redirects, adds missing ones, removes duplicates, or rebuilds projects with conflicting bin/ output. High-risk removals (orphaned .NET Framework) route through a guided flow with safety checks instead of a single click.
- **Fix Shown Items / Fix All** -- batch-fix the currently visible issues. The guided orphan-.NET-Framework flow is excluded from bulk operations -- each must be reviewed individually.
- **Critical Mismatch Detection** -- flags redirects pointing forward to a version that exists nowhere on disk; runtime `FileLoadException` at startup is guaranteed. Auto-updates to the highest version available on disk.
- **Guided Orphan Removal (.NET Framework)** -- runs three automated safety checks (solution-wide source-usage grep, GAC folder probe, transitive `bin/` reference scan via `MetadataLoadContext`) plus surfaces the project's `<PostBuildEvent>` for manual review. `Remove Redirect` only enables when every auto check passes AND the manual confirmation is ticked. If a check fails, the panel routes you to `Open Config File for Manual Editing` instead.
- **Build-Required Banner** -- when the scan finds no on-disk DLLs for any redirect (project not built, NuGet not restored), a banner explains that detection is degraded and prompts to build + restore first.
- **Deprecated Package Detection** -- flags packages like `Microsoft.Azure.Services.AppAuthentication` with migration guidance and offers removal with a warning
- **DLL Project Redirect Cleanup** -- detects class library projects where binding redirects have no effect (CLR only reads host app config) and offers bulk removal of the entire section or file
- **Framework Detection** -- reads target framework from `.csproj` to provide framework-specific guidance, including detection of legacy ASP.NET Web Application Projects (WAPs)
- **Supports Both Project Types** -- works with PackageReference and `packages.config` projects
- **Parallel Scanning** -- analyses up to 5 projects concurrently with real-time progress ("Analysing 3 of 12: ProjectName...")
- **Resizable & Sortable Columns** -- drag column borders to resize, click headers to sort ascending/descending
- **Educational UI** -- a built-in Background tab explains what binding redirects are, why they break, how this tool resolves them, and how the orphan-safety checks work
- **Theme-Aware** -- fully adapts to Light, Dark, Blue, and High Contrast themes
- **Non-Destructive** -- creates timestamped backups before modifying any config file

## Usage

1. Open a solution containing .NET Framework or .NET (Core) projects with `web.config` or `app.config`
2. Go to **Tools** > **Binding Redirect Fixer**
3. The tool window opens and automatically scans your solution
4. Review the detected issues in the multi-source grid
5. Filter by status (e.g. "Orphaned .NET (Core)") to focus on one group
6. Click **Fix Shown Items** to resolve all visible issues, or fix them individually

## How It Works

The extension reads assembly versions from multiple sources and compares them:

| Source | What It Represents | Trust Level |
|---|---|---|
| NuGet Resolved DLL | The actual DLL from the NuGet cache | Authoritative |
| Package Reference | What was requested in `.csproj` / `packages.config` | Cross-check |
| bin/ DLL | What is physically on disk | Can be stale |
| Config Redirect | What the runtime currently uses | Often wrong |

## Issue Types

| Status | Meaning | Auto-Fix |
|---|---|---|
| **STALE** | Config redirect points to an old assembly version | Updates `newVersion` |
| **MISSING** | Redirect needed but does not exist | Adds `dependentAssembly` element |
| **CONFLICT** | Analysis error (corrupted DLL, inaccessible path) | Manual resolution |
| **DUPLICATE** | Multiple redirects for the same assembly | Removes duplicate |
| **MISMATCH** | Redirect targets a version not on disk (bin/ DLL older than NuGet resolved) | Removes the redirect |
| **CRITICAL MISMATCH** | Redirect points forward to a version greater than the highest DLL on disk -- runtime `FileLoadException` at startup is guaranteed | Updates `newVersion` to the highest version on disk |
| **TOKEN LOST** | DLL exists but is unsigned while config expects a public key token | Preserves token, updates version if needed |
| **DEPRECATED** | Package replaced by a modern equivalent | Removes redirect (with warning) |
| **ORPHANED .NET (Core)** | No DLL found in a .NET (Core) project | Removes redirect (safe) |
| **ORPHANED .NET Framework** | No DLL found in a .NET Framework project, but assembly may still load from GAC / post-build copy / transitive bin ref | Opens an inline guided-removal panel with 3 auto safety checks (source usage, GAC probe, transitive bin refs) + manual post-build confirmation; Remove only enables when all pass |
| **UNUSED IN LIBRARY** | Binding redirect in a class library (DLL) project | Removes all redirects (section or file deletion) |

## Requirements

- Visual Studio 2022 (17.14+) or Visual Studio 2026
- .NET Framework or .NET (Core) projects with `web.config` or `app.config`
- NuGet packages restored

## Links

- [Source Code](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer)
- [Report Issues](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/issues)
- [License (MIT)](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/blob/main/LICENSE)
