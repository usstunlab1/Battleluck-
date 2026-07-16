CI & Secrets: Quick Guide

1) Revoke any exposed token immediately.

2) Add repository secrets (Settings → Secrets → Actions):
   - CLOUDFLARE_API_TOKEN
   - CLOUDFLARE_ACCOUNT_ID

3) Workflow usage: the provided .github/workflows/ci.yml injects secrets into the Test step via env.

4) Local development options:
   - dotnet user-secrets: `dotnet user-secrets init` then `dotnet user-secrets set "Cloudflare:ApiToken" "<token>"`
   - or set environment variable (Windows PowerShell):
     [Environment]::SetEnvironmentVariable('CLOUDFLARE_API_TOKEN','<token>','User')

5) Recommended: rotate tokens, give least privilege, and use cloud KMS (GitHub Actions secrets are fine for CI).

6) Example C# snippet (see Utilities/CloudflareClientExample.cs) shows secure runtime retrieval.
