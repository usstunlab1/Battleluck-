-- ============================================================
-- BattleLuck Mod Debug Dashboard Schema
-- Run this in your Supabase SQL editor
-- ============================================================

begin;

-- ============================================================
-- 1. TABLES
-- ============================================================

-- Live BepInEx/BattleLuck log stream
create table if not exists public.mod_logs (
  id          bigserial primary key,
  level       text not null,           -- Info | Warning | Error | Message
  source      text not null,           -- BattleLuck | BepInEx | Il2CppInterop etc.
  message     text not null,
  raw         text,                    -- full raw log line
  created_at  timestamptz not null default now()
);
create index mod_logs_level_idx      on public.mod_logs (level);
create index mod_logs_created_at_idx on public.mod_logs (created_at desc);

-- Editable config store — mirrors .cfg + .json files
create table if not exists public.mod_configs (
  id          bigserial primary key,
  file_name   text not null,           -- e.g. gg.battleluck.cfg | ai_config.json
  section     text,                    -- e.g. AI | Events | Actions
  key         text not null,           -- e.g. Enabled | provider | model
  value       text not null,
  value_type  text not null default 'string', -- string | boolean | number | json
  description text,
  updated_at  timestamptz not null default now(),
  updated_by  uuid references auth.users(id),
  unique (file_name, section, key)
);

-- Game events from BattleLuck (actions, triggers, AI ops)
create table if not exists public.mod_events (
  id          bigserial primary key,
  event_type  text not null,           -- action | trigger | ai_op | mode_change
  mode_id     text,                    -- bloodbath | siege | trials | colosseum | aievent
  action      text,                    -- e.g. spawn.boss | announce | pvp.enable
  risk_level  text,                    -- safe | controlled | destructive | critical
  payload     jsonb,                   -- full action params
  status      text not null default 'pending', -- pending | approved | executed | rejected
  executed_by uuid references auth.users(id),
  created_at  timestamptz not null default now()
);
create index mod_events_mode_idx      on public.mod_events (mode_id);
create index mod_events_status_idx    on public.mod_events (status);
create index mod_events_created_at_idx on public.mod_events (created_at desc);

-- ============================================================
-- 2. RLS
-- ============================================================

alter table public.mod_logs    enable row level security;
alter table public.mod_configs enable row level security;
alter table public.mod_events  enable row level security;

-- Authenticated users can read all logs/configs/events
create policy "mod_logs_select"    on public.mod_logs    for select to authenticated using (true);
create policy "mod_configs_select" on public.mod_configs for select to authenticated using (true);
create policy "mod_events_select"  on public.mod_events  for select to authenticated using (true);

-- Authenticated users can insert logs and events (server agent writes these)
create policy "mod_logs_insert"   on public.mod_logs   for insert to authenticated with check (true);
create policy "mod_events_insert" on public.mod_events for insert to authenticated with check (true);

-- Authenticated users can update configs (dashboard edits)
create policy "mod_configs_update" on public.mod_configs
  for update to authenticated
  using (true)
  with check (true);

create policy "mod_configs_insert" on public.mod_configs
  for insert to authenticated
  with check (true);

-- Only allow update on mod_events (approve/reject)
create policy "mod_events_update" on public.mod_events
  for update to authenticated
  using (true)
  with check (true);

-- ============================================================
-- 3. REALTIME — private channel broadcast triggers
-- ============================================================

-- mod_logs → mod:battleluck:logs
create or replace function public.broadcast_mod_log()
returns trigger language plpgsql security definer as $$
begin
  perform realtime.broadcast_changes(
    'mod:battleluck:logs',
    tg_op, tg_op, tg_table_name, tg_table_schema, new, old
  );
  return new;
end;
$$;

drop trigger if exists on_mod_log on public.mod_logs;
create trigger on_mod_log
after insert on public.mod_logs
for each row execute function public.broadcast_mod_log();

-- mod_configs → mod:battleluck:configs
create or replace function public.broadcast_mod_config()
returns trigger language plpgsql security definer as $$
begin
  perform realtime.broadcast_changes(
    'mod:battleluck:configs',
    tg_op, tg_op, tg_table_name, tg_table_schema, new, old
  );
  return coalesce(new, old);
end;
$$;

drop trigger if exists on_mod_config on public.mod_configs;
create trigger on_mod_config
after insert or update on public.mod_configs
for each row execute function public.broadcast_mod_config();

-- mod_events → mod:battleluck:events
create or replace function public.broadcast_mod_event()
returns trigger language plpgsql security definer as $$
begin
  perform realtime.broadcast_changes(
    'mod:battleluck:events',
    tg_op, tg_op, tg_table_name, tg_table_schema, new, old
  );
  return coalesce(new, old);
end;
$$;

drop trigger if exists on_mod_event on public.mod_events;
create trigger on_mod_event
after insert or update on public.mod_events
for each row execute function public.broadcast_mod_event();

-- ============================================================
-- 4. RLS on realtime.messages (private channel auth)
-- ============================================================

create policy "mod_realtime_select"
on realtime.messages for select to authenticated
using (
  topic in (
    'mod:battleluck:logs',
    'mod:battleluck:configs',
    'mod:battleluck:events'
  )
);

create policy "mod_realtime_insert"
on realtime.messages for insert to authenticated
with check (
  topic in (
    'mod:battleluck:logs',
    'mod:battleluck:configs',
    'mod:battleluck:events'
  )
);

-- ============================================================
-- 5. SEED — import current config from gg.battleluck.cfg + ai_config.json
-- ============================================================

insert into public.mod_configs (file_name, section, key, value, value_type, description) values
  ('gg.battleluck.cfg', 'Actions',  'Enabled',              'true',        'boolean', 'Allow catalog-backed event, command, and AI actions to mutate game state.'),
  ('gg.battleluck.cfg', 'AI',       'Enabled',              'true',        'boolean', 'Master switch for the BattleLuck AI assistant.'),
  ('gg.battleluck.cfg', 'AI',       'Provider',             'config',      'string',  'AI provider override: config, auto, local, llama, cloudflare, or google.'),
  ('gg.battleluck.cfg', 'AI',       'EventAuthoringEnabled','true',        'boolean', 'Allow approval-gated AI event creation and editing.'),
  ('gg.battleluck.cfg', 'AI',       'AutoExecuteNpcActions','false',       'boolean', 'Allow ProjectM AI-group NPC actions to execute automatically.'),
  ('gg.battleluck.cfg', 'Events',   'Enabled',              'true',        'boolean', 'Enable declarative BattleLuck events and game modes.'),
  ('gg.battleluck.cfg', 'Events',   'EnabledModes',         '*',           'string',  'Comma-separated event IDs to load, or * for all.'),
  ('ai_config.json',    'root',     'enabled',              'true',        'boolean', 'Master AI enabled flag.'),
  ('ai_config.json',    'root',     'provider',             'llama',       'string',  'Active AI provider: llama | cloudflare | google_ai_studio.'),
  ('ai_config.json',    'llama_api','enabled',              'true',        'boolean', 'Enable local Llama API.'),
  ('ai_config.json',    'llama_api','base_url',             'http://127.0.0.1:11434', 'string', 'Llama API base URL.'),
  ('ai_config.json',    'llama_api','model',                'llama2:latest','string', 'Llama model name.'),
  ('ai_config.json',    'llama_api','temperature',          '0.5',         'number',  'Sampling temperature.'),
  ('ai_config.json',    'llama_api','max_tokens',           '512',         'number',  'Max tokens per response.'),
  ('ai_config.json',    'messaging','message_cooldown_seconds','30',       'number',  'Cooldown between AI messages.'),
  ('ai_config.json',    'messaging','auto_tips_enabled',    'true',        'boolean', 'Enable automatic AI tips.'),
  ('ai_config.json',    'messaging','welcome_messages_enabled','true',     'boolean', 'Enable AI welcome messages.'),
  ('ai_config.json',    'projectm_aigroup','enabled',       'true',        'boolean', 'Enable ProjectM AI group actions.'),
  ('ai_config.json',    'projectm_aigroup','auto_execute',  'false',       'boolean', 'Auto-execute AI NPC actions without approval.'),
  ('ai_config.json',    'projectm_aigroup','snapshot_interval_seconds','15','number', 'How often AI snapshots game state.'),
  ('ai_config.json',    'projectm_aigroup','min_confidence','0.65',        'number',  'Minimum AI confidence to act.')
on conflict (file_name, section, key) do nothing;

commit;
