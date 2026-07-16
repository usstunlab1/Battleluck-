# BattleLuck publishing checklist

Use this checklist before publishing a GitHub release, Thunderstore package, or release zip.

## Package root

The package zip must contain these files at its root:

- `BattleLuck.dll`
- `manifest.json`
- `README.md`
- `icon.png` (exactly 256x256 PNG)
- `CHANGELOG.md`
- `LICENSE`
- `THIRD_PARTY_NOTICES.md`

Never package secrets or server state: `.env`, credentials, model weights, logs, snapshots, backups, `.bak` files, or build folders.

## Release defaults

- AI provider: local `llama` endpoint (`http://127.0.0.1:11434`).
- Cloud providers and Discord AI logging: disabled unless the server owner opts in.
- Conversation history: disabled by default.
- Event authoring: enabled; risky live actions require approval.
- Keep package configuration free of tokens, webhooks, and personal data.

## Validate

```powershell
dotnet restore .\BattleLuck.sln
dotnet build .\BattleLuck.sln --no-restore /p:GenerateReadme=false /p:DeployToServer=false
Get-Content .\manifest.json | ConvertFrom-Json | Out-Null
```

Run a secret scan before upload:

```powershell
rg -n "cfat[_]|cfut[_]|discord[.]com/api/webhooks|GOOGLE_AI_API_KEY\\s*=\\s*[^\\s#]+|CLOUDFLARE_AI_API_TOKEN\\s*=\\s*[^\\s#]+" .
```

On a clean server, smoke-test `.bl.help`, `.aistatus`, `.modelist`, `.event.status`, and one private event before publishing.

## Versioning

Increment `manifest.json` `version_number`, update `CHANGELOG.md`, and keep the package name stable for updates.

## V Rising references

- [V Rising Mod Wiki](https://wiki.vrisingmods.com/)
- [Mod licensing and attribution](https://wiki.vrisingmods.com/dev/licensing.html)
- [Thunderstore upload](https://wiki.vrisingmods.com/dev/upload_to_thunderstore.html)
