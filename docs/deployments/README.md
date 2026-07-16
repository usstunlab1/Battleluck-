# Deployments Guide

BattleLuck deployment is local-first and V Rising focused. The published package contains the BepInEx plugin, release metadata, and docs only. Cloud-hosted services are optional and not part of the standard release.

## Thunderstore Publishing

### Prerequisites

- A Thunderstore account
- A tested V Rising dedicated server build
- Package structure: `BattleLuck.dll`, `icon.png`, `README.md`, `manifest.json`, `LICENSE`, `THIRD_PARTY_NOTICES.md`

### Package Format

Thunderstore packages must contain files at the zip root:

```
BattleLuck.zip
├── BattleLuck.dll
├── icon.png
├── README.md
├── manifest.json
├── LICENSE
├── CHANGELOG.md
└── THIRD_PARTY_NOTICES.md
```

### Manifest

The package manifest must be valid JSON:

```json
{
  "name": "BattleLuck",
  "version_number": "1.0.0",
  "website_url": "https://github.com/usstunlab1/Battleluck-",
  "description": "Competitive arena game modes, event flow, and optional AI tooling for V Rising dedicated servers.",
  "dependencies": [
    "BepInEx-BepInExPack_V_Rising-1.733.2",
    "deca-VampireCommandFramework-0.10.4"
  ]
}
```

### Build

```powershell
dotnet restore .\BattleLuck.sln
dotnet build .\BattleLuck.sln --no-restore /p:GenerateReadme=false /p:DeployToServer=false
```

### Release Zip

Package from the `package/` folder:

```powershell
$manifest = Get-Content .\package\manifest.json -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path .\dist | Out-Null
$zip = ".\dist\$($manifest.name)-$($manifest.version_number).zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -LiteralPath (Get-ChildItem .\package -Force).FullName -DestinationPath $zip -CompressionLevel Optimal
```

## Local AI Defaults

Published configurations are safe by default:

```json
{
  "provider": "llama",
  "llama_api": {
    "enabled": false,
    "base_url": "http://127.0.0.1:11434"
  },
  "cloudflare_ai": { "enabled": false },
  "privacy": { "store_conversation_history": false }
}
```

## Secret Scan

Run before publishing:

```powershell
rg -n "cfat[_]" .
rg -n "cfut[_]" .
rg -n "discord[.]com/api/webhooks" .
git status --short
dotnet build .\BattleLuck.sln --no-restore /p:GenerateReadme=false /p:DeployToServer=false
```

Do not publish `.env`, logs, player snapshots, or AI operation logs.

## Manual Server Deployment

1. Stop the V Rising server
2. Copy `bin/Release/net6.0/BattleLuck.dll` to `BepInEx/plugins/`
3. Copy `config/BattleLuck/` to `BepInEx/config/BattleLuck/`
4. Start local AI (if enabled): `.\scripts\start_vllm.ps1`
5. Start the server
6. Run `.reload`, `.ai.status`, `.modelist`

## Related Documentation

- [Publishing Checklist](../PUBLISHING_CHECKLIST.md) — Full release checklist
- [LLM Guide](../LLM_GUIDE.md) — AI service setup
- [Thunderstore Upload Guide](https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html)
