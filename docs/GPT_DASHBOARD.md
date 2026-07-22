# BattleLuck GPT Dashboard

The BattleLuck GPT Dashboard is a separate web application and repository:

- Repository: `https://github.com/usstunlab1/battleluck-gpt-dashboard`
- Responsibility: authenticated team discussions, operational log/event visibility, and GPT-powered summaries and diagnostics.
- This repository: the authoritative V Rising plugin, game rules, and all in-game mutations.

## Integration boundary

The dashboard uses Supabase tables named `mod_logs`, `mod_events`, and `mod_configs` as its external integration boundary. A deployment relay or future server telemetry adapter may write status records to those tables with a Supabase service-role credential held outside both repositories.

The dashboard does not receive direct game-server access. A configuration change in the dashboard is not an in-game command: BattleLuck must validate, authorize, and apply every requested change through its existing server-side command/config mechanisms.

## Deploy independently

1. Deploy BattleLuck from this repository to the V Rising server.
2. Deploy the dashboard from its own repository, configure Supabase GitHub OAuth, and set its frontend `VITE_SUPABASE_URL` and `VITE_SUPABASE_PUBLISHABLE_KEY` values.
3. Set `OPENAI_API_KEY` only as a Supabase Edge Function secret for the dashboard. Never place it in BattleLuck configuration, chat commands, logs, or source control.

Keeping the systems separate avoids making the game plugin depend on browser dependencies or web-hosting credentials while preserving a stable operational-data contract.
