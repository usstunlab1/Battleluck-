# Third-party notices

This inventory is generated from the locked dependency graph used to build
BattleLuck 1.1.2. It is an engineering record, not legal advice.

## Files distributed in the server package

| Component | Version | License declared by package | Project |
|---|---:|---|---|
| BouncyCastle.Cryptography | 2.6.2 | MIT | https://www.bouncycastle.org/ |
| HookDOTS.API | 1.1.1 | GPL-3.0-only | https://github.com/cheesasaurus/HookDOTS |

The package also contains `BattleLuck.dll`, which is distributed under the
repository's GNU Affero General Public License v3 (`LICENSE`).

## Runtime dependencies not bundled

| Component | Version | License metadata |
|---|---:|---|
| BepInEx Core / Unity Common / Unity IL2CPP | 6.0.0-be.733 | LGPL-2.1-only |
| VampireCommandFramework | runtime manifest 0.11.0; compile reference 0.10.4 | NuGet 0.10.4 does not declare a license expression |

## Build-time or optional dependencies not bundled

| Component | Version | License metadata |
|---|---:|---|
| Il2CppInterop.Runtime | 1.4.6-ci.426 | LGPL-3.0-only |
| VampireReferenceAssemblies | 1.1.11-r96495-b8 | NuGet package does not declare a license expression |
| VAutomationCore | 1.0.3 | MIT |

The complete resolved package inventory is in
`docs/audit/sbom.cdx.json`. Dependencies without declared license metadata
require a source-repository license review before a public release.
