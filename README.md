# Deucarian Package Installer

## Overview

Deucarian Package Installer is a small editor-only Unity Package Manager package that adds a custom installer window for Deucarian packages.

Open it from:

```text
Tools > Deucarian > Package Installer
```

The installer can install standalone packages, bridge packages, and explicitly declared package samples without making this package a runtime dependency of any other package.

Package ID: `com.deucarian.package-installer`

## Installation

Add the installer through Unity Package Manager with a Git URL:

```json
{
  "dependencies": {
    "com.deucarian.package-installer": "https://github.com/Deucarian/Package-Installer.git#main"
  }
}
```

For development builds, use:

```json
"com.deucarian.package-installer": "https://github.com/Deucarian/Package-Installer.git#develop"
```

You can also use Unity's Package Manager window:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter the installer Git URL.
4. Open `Tools > Deucarian > Package Installer`.

The package requires Unity `2021.3` or newer and has no package dependencies.

## Package Registry

Package entries are loaded from a registry instead of being hardcoded in the installer window.

The installer loads the bundled `PackageRegistry.json` first so it works offline, then tries to refresh from:

`https://raw.githubusercontent.com/Deucarian/Package-Registry/main/packages.json`

If the remote registry succeeds and validates, the window uses it. If it fails, the bundled registry stays active and the header shows that the remote registry failed.

Remote registry validation also checks each package entry against the target package's `package.json` name so installed-package detection uses Unity's exact package IDs.

The current registry includes these package entries:

- Core: Core State, API, Object Loading, Session
- UI: UI Binding
- World: Object Selection
- Bridge: UI Binding + Core State Bridge, Object Loading API Bridge, ObjectSelection + CoreState Bridge, Session + API Bridge
- Suites: Selection Suite

Registered packages are first-class UPM packages with their own package IDs:

- `com.deucarian.core-state`
- `com.deucarian.api`
- `com.deucarian.object-loading`
- `com.deucarian.session`
- `com.deucarian.ui-binding`
- `com.deucarian.object-selection`
- `com.deucarian.ui-binding.core-state-bridge`
- `com.deucarian.object-loading.api-bridge`
- `com.deucarian.object-selection.core-state-bridge`
- `com.deucarian.session.api-bridge`
- `com.deucarian.selection-suite`

`Install All` installs all missing registered packages in dependency order. Installing one bridge package automatically installs its missing dependencies first, then installs the bridge.

Package IDs remain branded as `com.deucarian.*`. Display names are supplied by the registry and used by the installer UI.
Technical details such as package IDs, selected references, installed references, revisions, and raw update messages are available from each row's Advanced foldout.

## Adding Package Definitions

Package entries are data-driven through registry JSON.

To add or change packages, update the remote registry repository and keep the bundled fallback in sync:

- Remote: `https://github.com/Deucarian/Package-Registry`
- Bundled fallback: `PackageRegistry.json`

The registry schema uses `schemaVersion` 1 and contains:

- `id`: the Unity package name, such as `com.deucarian.api`. This must exactly match the target package's `package.json` `name` value.
- `displayName`: the name shown in the installer window.
- `category`: grouping shown in the sidebar. Core, UI, World, Bridge, and Suites are ordered first; unknown categories are shown alphabetically after them.
- `description`: explanatory text shown in the detail pane.
- `stableUrl`: the stable Git URL or UPM identifier passed to `UnityEditor.PackageManager.Client.Add`.
- `developmentUrl`: optional development-channel Git URL or UPM identifier. If this is empty, the Development channel is disabled for that package.
- `dependencies`: package IDs that should be installed before this package is installed. Bridge packages are just packages in the `Bridge` category with dependencies.

Set `stableUrl` and, when available, `developmentUrl` to the UPM identifier or Git URL. Bridge packages should also list their dependency package IDs in `dependencies`.

When an installed Git package can be matched to `#main` or `#develop`, the installer infers the visible channel from the installed package reference. If the installed reference does not match a known channel, the row shows a Custom channel until the user selects Stable or Development.

## Samples and Extras

UPM packages can include `Samples~` folders, but Unity does not import those samples automatically. The installer keeps package installation clean and only imports samples when a sample's `Import` button is clicked.

For installed packages, the installer resolves the package through Unity Package Manager metadata, reads its `package.json`, and displays entries from the `samples` array under the package detail view. Each row shows the sample `displayName`, `description`, import status, and an explicit import action.

Sample imports are explicit. The installer first tries Unity's Package Manager sample import API, then falls back to a bounded copy from the installed package's `Samples~` folder into `Assets/Samples/<Package Display Name>/<Version>/<Sample Name>`.

If a sample destination already exists, the installer shows it as already imported and does not overwrite it silently.

## Update Checks

`Check for Updates` compares installed registry packages against the selected Stable or Development channel. Git packages are compared by installed revision and the latest revision returned by `git ls-remote`.

Unknown revisions, missing Git, network failures, local/file packages, and non-Git UPM identifiers are reported as check failures instead of blocking the installer.

`Update` and `Update All Installed Packages` reuse Unity Package Manager installation through `Client.Add` with the selected channel URL.

TODO: installer self-update is intentionally out of scope for this version.

## Progress Display

The installer shows step-based progress for package install, bridge install, install-all, single update, update-all, and remove operations.

Progress is counted by package steps because Unity Package Manager does not provide reliable download-byte progress for these Git package operations.

Progress summaries list succeeded, failed, and skipped package steps so multi-package operations do not rely only on console logs.

## Bridge Packages

Bridge packages keep the core packages standalone while providing explicit composition packages for projects that want the combined behavior.

Current bridge package dependencies:

- UIBinding CoreState Bridge depends on UI Binding and Core State.
- Object Loading API Bridge depends on Object Loading and API.
- ObjectSelection CoreState Bridge depends on Object Selection and Core State.
- Session API Bridge depends on Session and API.

Installing a bridge only requires one click. The installer computes the dependency-first install plan from `PackageDefinition.Dependencies` and sends that ordered package list to Unity Package Manager.

Bridge packages are regular UPM packages, so no scripting define symbols are required for these bridge installs.

When removing a package, the installer warns and disables removal if another installed registered package depends on it. Remove the dependent bridge package first to avoid silently breaking the project.

## Public API

This package is editor-only and exposes no runtime API for game code.

The user-facing entry point is the Unity menu item:

```text
Tools/Deucarian/Package Installer
```

The implementation is split into internal editor classes:

- `PackageInstallerWindow`: IMGUI window and coordination.
- `PackageRegistryProvider`, `PackageRegistryLoader`, and `PackageRegistryValidator`: bundled and remote registry loading.
- `PackageDefinition`, `PackageChannel`, and `PackageExtraDefinition`: installer data models.
- `PackageInstallService`: Unity Package Manager install, update, and remove operations.
- `PackageDependencyInstaller`: dependency-first package install sequencing.
- `PackageDetectionService`: installed package detection through `Client.List`.
- `PackageUpdateCheckService`: Git revision comparison for installed Git packages.
- `PackageSampleImportService`: explicit sample import through Unity sample APIs or a safe copy fallback.

## Why Editor-Only

This package exists only to help developers install and compose packages inside the Unity Editor. It creates no runtime assembly, has no `Runtime` folder, and should not be referenced by game code.

Keeping the installer editor-only ensures:

- Core State, UI Binding, API, Object Loading, and Session remain standalone.
- Projects do not ship installer code in builds.
- No package gains a runtime dependency on this installer.

## Versioning

Current package version: `1.0.0`.

Branch strategy:

- `main`: stable installer branch.
- `develop`: development installer branch.

Use branch refs for active development and stable release tags when tags are available.

## Validation Notes

The installer uses:

- `UnityEditor.PackageManager.Client.Add` for package installation and update.
- `UnityEditor.PackageManager.Client.Remove` for package removal.
- `UnityEditor.PackageManager.Client.List` for installed-package detection.

After installing, updating, or removing a package, the installer refreshes installed-package state so entries show their current status.

## Limitations

- This package is editor-only. It has no `Runtime` folder and should not be referenced by game code.
- The installer uses a bundled registry first and a remote registry refresh when available; it does not auto-discover GitHub repositories.
- Only Git branch update checks are supported today.
- The installer cannot know download-byte progress for Git packages.
- Sample import avoids silent overwrite; there is no overwrite UI in this version.
