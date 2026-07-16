# Continuous Integration (CI)

This document describes the CI build-verification setup for BattleLuck and the
**stage-gating** rule that governs the in-progress architecture refactor.

## Why CI must come first

BattleLuck is a V Rising ECS plugin. Refactoring an ECS monolith without a
build gate is how you end up with orphaned references and a server that crashes
on boot. Every refactor stage is therefore gated on a **green CI build** — you
do not advance to the next stage until the runner is green.

## Workflow

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on pushes to
`main`, on pull requests, and on manual dispatch. It:

1. Checks out the repository.
2. Installs the .NET 6 SDK (the project targets `net6.0`).
3. Authenticates the private GitHub Packages NuGet feed.
4. Restores `BattleLuck.sln`.
5. Builds in `Release`.

## Copilot cloud agent setup

BattleLuck now includes a repository-specific Copilot setup workflow at
[`/.github/workflows/copilot-setup-steps.yml`](../../.github/workflows/copilot-setup-steps.yml).
It preinstalls the .NET 6 SDK, authenticates the private GitHub Packages feed
when `NUGET_GITHUB_PAT` is available, and warms the solution restore for Copilot
sessions.

The setup workflow intentionally stays on `ubuntu-latest`, because Copilot's
default environment is Ubuntu-compatible. That is enough to prepare the shared
.NET/NuGet prerequisites, but it is **not** enough to guarantee a full
BattleLuck plugin build: the project still needs the V Rising / BepInEx
reference-assembly layout described below. If you want Copilot sessions to run
full plugin builds, move the setup workflow to a Windows large runner or a
Windows self-hosted runner after the required runner/firewall setup is in
place.

## Required repository configuration

These prerequisites live outside the repository and must be configured by a
maintainer before CI can go green.

### 1. `NUGET_GITHUB_PAT` secret

The `NuGet.Config` source `GitHub-Coyoteq1`
(`https://nuget.pkg.github.com/Coyoteq1/index.json`) is private. Add a repository
(or organization) Actions secret named **`NUGET_GITHUB_PAT`** containing a
Personal Access Token with at least the `read:packages` scope for that feed.

The workflow injects this token into the existing NuGet source at runtime via
`dotnet nuget update source` using an environment variable, so the token is
never committed to the repository and is masked in logs.

### 2. V Rising reference assemblies

`BattleLuck.csproj` resolves a large set of references from a local V Rising
server layout rather than from NuGet:

- `$(CorePath)` → `BepInEx.Core.dll`, `BepInEx.Unity.IL2CPP.dll`, `0Harmony.dll`, `Iced.dll`
- `$(InteropPath)` → `Stunlock.Core.dll`, `ProjectM*.dll`, `Unity*.dll`
- `$(UnityLibsPath)` → the Unity managed libraries

`$(VRisingServerRoot)` (and the derived paths above) must point at a location on
the runner that actually contains these assemblies. Options:

- Provide them through the V Rising reference-assembly NuGet packages already
  declared in `Directory.Packages.props` (`VampireReferenceAssemblies`,
  `Il2CppInterop.Runtime`) and repoint `CorePath` / `InteropPath` /
  `UnityLibsPath` at the restored package contents, **or**
- Restore a V Rising dedicated-server install on the runner and set
  `VRISING_SERVER_ROOT` accordingly.

Until one of these is wired up, the **Build** step will fail to resolve
`ProjectM`/`Unity`/`BepInEx` types even after a successful package restore.

## Package lock file

`Directory.Packages.props` enables `RestoreLockedMode` when `GITHUB_ACTIONS` is
`true`, but no `packages.lock.json` is committed yet. The baseline workflow
therefore restores with `-p:RestoreLockedMode=false
-p:RestorePackagesWithLockFile=true`, which generates a lock file without
enforcing locked mode. Once `packages.lock.json` is committed to the repository,
remove the `RestoreLockedMode=false` override from the **Restore** step so
locked restores are enforced for reproducibility.

## Refactor stage gates

The architecture refactor proceeds in stages, each ending in a CI gate:

| Stage | Scope | Gate |
| --- | --- | --- |
| 0 | CI environment + baseline build (this doc) | CI builds the current, un-refactored codebase |
| 1 | New data models + serialization tests | CI compiles + serialization tests pass |
| 2 | Validators (action/zone/kit/prefab/schematic) | CI compiles + validator tests pass |
| 3 | Side-by-side loaders + registry | CI compiles (old loader still intact) |
| 4 | Engine wire-up to new architecture | CI compiles |
| 5 | Purge of deprecated loaders/models/config | CI compiles with no orphaned references |
| 6 | Orchestrator points at `config/BattleLuck/` modes | n/a (Python/Node) |

Do not begin a stage until the previous stage's gate is green.
