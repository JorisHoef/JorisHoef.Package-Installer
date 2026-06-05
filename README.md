# JorisHoef Package Installer

JorisHoef Package Installer is a small editor-only Unity Package Manager package that adds a custom installer window for JorisHoef packages.

Open it from:

`Tools > JorisHoef > Package Installer`

The installer can install standalone packages and opt into package integrations without making this package a runtime dependency of any other package.

## Installation

Add the installer through Unity Package Manager with a Git URL:

```json
{
  "dependencies": {
    "com.jorishoef.package-installer": "https://github.com/JorisHoef/JorisHoef.Package-Installer.git#main"
  }
}
```

You can also use Unity's Package Manager window:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter the installer Git URL.
4. Open `Tools > JorisHoef > Package Installer`.

## Included Packages

The first version knows about these package entries:

- CoreState
- GenericUIItems
- APIHelper
- SessionHelper
- GenericUIItems + CoreState integration
- SessionHelper + APIHelper integration

`Install All` installs the standalone packages and enables all integration define symbols.

## Adding Package Definitions

Package entries are data-driven through `PackageRegistry`.

To add or change packages, edit:

`Editor/PackageRegistry.cs`

Each `PackageDefinition` contains:

- `displayName`: the name shown in the installer window.
- `packageId`: the Unity package name, such as `com.jorishoef.api-helper`.
- `stableUrl`: the stable Git URL or UPM identifier passed to `UnityEditor.PackageManager.Client.Add`.
- `developmentUrl`: optional development-channel Git URL or UPM identifier. If this is empty, Development falls back to Stable.
- `description`: the explanatory text shown in the UI.
- `displayVersion`: optional human-readable version text shown in the UI.
- `dependencies`: package IDs that should be installed before an integration is enabled.
- `scriptingDefineSymbols`: optional symbols added to the selected build target group.

For regular packages, set `stableUrl` and, when available, `developmentUrl` to the UPM identifier or Git URL. For integration entries that only compose other packages, leave the URL fields empty and list the required packages in `dependencies`.

## Update Checks

`Check for Updates` compares installed registry packages against the selected Stable or Development channel. Git packages are compared by installed revision and the latest revision returned by `git ls-remote`.

Unknown revisions, missing Git, network failures, local/file packages, and non-Git UPM identifiers are reported as check failures instead of blocking the installer.

`Update` and `Update All Installed Packages` reuse Unity Package Manager installation through `Client.Add` with the selected channel URL.

TODO: installer self-update is intentionally out of scope for this version.

## Integrations

Integrations keep packages standalone. The installer does not add compile-time references between packages by itself.

An integration definition does two things:

1. Installs its required package dependencies if they are not already installed.
2. Adds the integration's scripting define symbols to the active build target group.

The installer only adds symbols. It never removes user symbols.

Current integration symbols:

- `GENERIC_UI_ITEMS_CORE_STATE`
- `SESSION_HELPER_APIHELPER`

Packages that provide optional integration code should gate that code behind the same symbols in their own asmdefs or source.

## Why Editor-Only

This package exists only to help developers install and compose packages inside the Unity Editor. It creates no runtime assembly, has no `Runtime` folder, and should not be referenced by game code.

Keeping the installer editor-only ensures:

- CoreState, GenericUIItems, APIHelper, and SessionHelper remain standalone.
- Projects do not ship installer code in builds.
- No package gains a runtime dependency on this installer.

## Validation Notes

The installer uses:

- `UnityEditor.PackageManager.Client.Add` for package installation.
- `UnityEditor.PackageManager.Client.List` for installed-package detection.
- `PlayerSettings` scripting define APIs for integration symbols.

After installing a package, the installer refreshes installed-package state so installed entries show as installed.
