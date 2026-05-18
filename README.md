# Binding Redirect Fixer

[![Build](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/actions/workflows/build.yml/badge.svg)](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/actions/workflows/build.yml)
[![Release](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/actions/workflows/release.yml/badge.svg)](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/actions/workflows/release.yml)
[![Visual Studio Marketplace](https://img.shields.io/visual-studio-marketplace/v/Ardimedia.BindingRedirectFixer.svg)](https://marketplace.visualstudio.com/items?itemName=Ardimedia.BindingRedirectFixer)
[![Visual Studio Marketplace Downloads](https://img.shields.io/visual-studio-marketplace/d/Ardimedia.BindingRedirectFixer.svg)](https://marketplace.visualstudio.com/items?itemName=Ardimedia.BindingRedirectFixer)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Visual Studio 2026 extension that automatically detects and repairs stale, missing, or orphaned assembly binding redirects in .NET Framework and .NET (Core) projects.

## The Problem

After NuGet updates or partial deployments, binding redirects in `web.config` / `app.config` often reference wrong assembly versions, causing runtime errors like:

> Could not load file or assembly 'Newtonsoft.Json, Version=12.0.0.0, ...' manifest definition does not match the assembly reference.

Tracking down which redirects are wrong and what versions they should point to is tedious and error-prone. This extension automates the entire process.

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

## Installation

### From Visual Studio Marketplace

1. Open Visual Studio 2026
2. Go to **Extensions** > **Manage Extensions**
3. Search for **"Binding Redirect Fixer"**
4. Click **Download** and restart Visual Studio

### From VSIX File

1. Download the `.vsix` file from [Releases](https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer/releases)
2. Double-click the file to install, or use **Extensions** > **Manage Extensions** > **Install from File**

## Usage

1. Open a solution containing .NET Framework projects with `web.config` or `app.config`
2. Go to **Tools** > **Binding Redirect Fixer**
3. The tool window opens and automatically scans your solution
4. Review the detected issues in the multi-source grid
5. Click **Fix All** to resolve all issues, or fix them individually

## How It Works

The extension reads assembly versions from multiple sources and compares them:

| Source | What It Represents | Trust Level |
|---|---|---|
| NuGet Resolved DLL | The actual DLL from the NuGet cache | Authoritative |
| Package Reference | What was requested in `.csproj` / `packages.config` | Cross-check |
| bin/ DLL | What is physically on disk | Can be stale |
| Config Redirect | What the runtime currently uses | Often wrong |

### Issue Types

| Status | Meaning | Auto-Fix |
|---|---|---|
| **STALE** | Config redirect points to an old assembly version | Updates `newVersion` to the resolved version |
| **MISSING** | Multiple packages need different versions but no redirect exists | Adds a new `<dependentAssembly>` element |
| **CONFLICT** | Config is correct but the bin/ DLL is outdated | Triggers a clean rebuild |
| **DUPLICATE** | Multiple redirects exist for the same assembly | Removes the duplicate entry |
| **MISMATCH** | Redirect targets a version not available on disk (bin/ DLL is older than NuGet resolved) | Removes the redirect so the runtime loads the bin/ DLL directly |
| **CRITICAL MISMATCH** | Redirect points FORWARD to a version greater than the highest DLL on disk -- runtime `FileLoadException` at startup is guaranteed | Updates `newVersion` to the highest version available on disk |
| **TOKEN LOST** | DLL exists but is unsigned while config expects a public key token | Preserves token, updates version if needed |
| **DEPRECATED** | Package is deprecated and should be replaced with a modern equivalent (e.g., `AppAuthentication` -> `Azure.Identity`) | Removes redirect (with warning to check NuGet refs) |
| **ORPHANED .NET (Core)** | No DLL found in a .NET (Core) project -- redirect is orphaned | Removes the redirect (safe) |
| **ORPHANED .NET Framework** | No DLL found in a .NET Framework project -- redirect is likely orphaned, but GAC / post-build / transitive bin refs could still depend on it | Opens an inline guided-removal panel: runs three automated safety checks (source-usage grep, GAC probe, transitive `bin/` references) and surfaces the `.csproj` post-build script for manual review. `Remove Redirect` is only enabled when every auto check passes AND the manual confirmation is ticked. |
| **UNUSED IN LIBRARY** | Binding redirect in a class library (DLL) project -- CLR never reads DLL config files | Removes all redirects (section or file deletion) |

### Package Version vs Assembly Version

Binding redirects operate on **assembly versions**, not NuGet package versions. These are often different:

```
Package version:  Newtonsoft.Json 13.0.3
Assembly version: Newtonsoft.Json 13.0.0.0
```

This extension resolves the actual assembly version by reading the DLL from the NuGet cache using `MetadataLoadContext` (no file locking).

## Requirements

- Visual Studio 2026 (18.0+) or Visual Studio 2022 17.14+
- .NET Framework projects with `web.config` or `app.config`
- NuGet packages restored (the extension will prompt you if `project.assets.json` is missing)

## Tech Stack

- [VisualStudio.Extensibility](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/) (out-of-proc model)
- Remote UI with XAML (WPF-compatible, theme-aware)
- NuGet.ProjectModel for `project.assets.json` parsing
- System.Reflection.MetadataLoadContext for non-locking DLL inspection
- System.Xml.Linq for config file manipulation

## Contributing

Contributions are welcome! Please open an issue first to discuss what you would like to change.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m 'Add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

## License

[MIT](LICENSE)

## Author

[Ardimedia](https://github.com/ardimedia)
