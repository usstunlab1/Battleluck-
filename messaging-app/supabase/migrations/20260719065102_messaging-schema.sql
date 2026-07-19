-- Messaging App Schema for Supabase

begin;
create extension if not exists pgcrypto;

create table if not exists "public"."threads" (
  "id" uuid primary key default gen_random_uuid(),
  "created_by" uuid not null references auth.users(id) on delete restrict,
  "title" text,
  "created_at" timestamptz not null default now()
);

create table if not exists "public"."thread_members" (
  "thread_id" uuid not null references "public"."threads"(id) on delete cascade,
  "user_id" uuid not null references auth.users(id) on delete cascade,
  "role" text not null default 'member',
  "created_at" timestamptz not null default now(),
  primary key ("thread_id","user_id")
);

create table if not exists "public"."messages" (
  "id" uuid primary key default gen_random_uuid(),
  "thread_id" uuid not null references "public"."threads"(id) on delete cascade,
  "sender_id" uuid not null references auth.users(id) on delete restrict,
  "parent_message_id" uuid references "public"."messages"(id) on delete cascade,
  "body" text not null,
  "created_at" timestamptz not null default now()
);

create index if not exists "messages_thread_created_at_idx" on "public"."messages" ("thread_id","created_at");
create index if not exists "messages_sender_idx" on "public"."messages" ("sender_id");

create table if not exists "public"."thread_summaries" (
  "thread_id" uuid primary key references "public"."threads"(id) on delete cascade,
  "latest_message_id" uuid references "public"."messages"(id) on delete set null,
  "summary" text,
  "key_points" text[],
  "model" text,
  "created_at" timestamptz not null default now()
);

alter table "public"."threads" enable row level security;
alter table "public"."thread_members" enable row level security;
alter table "public"."messages" enable row level security;
alter table "public"."thread_summaries" enable row level security;

-- Drop existing policies to avoid conflicts
drop policy if exists "threads_select_members" on "public"."threads";
drop policy if exists "threads_insert_creator" on "public"."threads";
drop policy if exists "thread_members_select" on "public"."thread_members";
drop policy if exists "thread_members_insert_if_member" on "public"."thread_members";
drop policy if exists "thread_members_delete_if_member" on "public"."thread_members";
drop policy if exists "messages_select_members" on "public"."messages";
drop policy if exists "messages_insert_sender" on "public"."messages";
drop policy if exists "summaries_select_members" on "public"."thread_summaries";
drop policy if exists "summaries_upsert_members" on "public"."thread_summaries";
drop policy if exists "summaries_update_members" on "public"."thread_summaries";

-- Threads: members can read
create policy "threads_select_members" on "public"."threads"
for select to authenticated
using (
  exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "threads"."id"
    and tm."user_id" = (select auth.uid())
  )
);

-- Threads: allow creator to insert a thread
create policy "threads_insert_creator" on "public"."threads"
for insert to authenticated
with check ("created_by" = (select auth.uid()));

-- Thread members: members can see membership; allow creator to add members (simple model)
create policy "thread_members_select" on "public"."thread_members"
for select to authenticated
using (
  exists (
    select 1 from "public"."thread_members" tm2
    where tm2."thread_id" = "thread_members"."thread_id"
    and tm2."user_id" = (select auth.uid())
  )
);

create policy "thread_members_insert_if_member" on "public"."thread_members"
for insert to authenticated
with check (
  exists (
    select 1 from "public"."thread_members" tm2
    where tm2."thread_id" = "thread_members"."thread_id"
    and tm2."user_id" = (select auth.uid())
  )
);

create policy "thread_members_delete_if_member" on "public"."thread_members"
for delete to authenticated
using (
  exists (
    select 1 from "public"."thread_members" tm2
    where tm2."thread_id" = "thread_members"."thread_id"
    and tm2."user_id" = (select auth.uid())
  )
);

-- Messages: members can read
create policy "messages_select_members" on "public"."messages"
for select to authenticated
using (
  exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "messages"."thread_id"
    and tm."user_id" = (select auth.uid())
  )
);

-- Messages: members can insert (sender must be auth.uid)
create policy "messages_insert_sender" on "public"."messages"
for insert to authenticated
with check (
  "sender_id" = (select auth.uid())
  and exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "messages"."thread_id"
    and tm."user_id" = (select auth.uid())
  )
);

-- Summaries: members can read; any member can write latest summary (idempotency handled by edge function)
create policy "summaries_select_members" on "public"."thread_summaries"
for select to authenticated
using (
  exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "thread_summaries"."thread_id"
    and tm."user_id" = (select auth.uid())
  )
);

create policy "summaries_upsert_members" on "public"."thread_summaries"
for insert to authenticated
with check (
  exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "thread_summaries"."thread_id"
    and tm."user_id" = (select auth.uid())
  )
);

create policy "summaries_update_members" on "public"."thread_summaries"
for update to authenticated
using (
  exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "thread_summaries"."thread_id"
    and tm."user_id" = (select auth.uid())
  )
)
with check (
  exists (
    select 1 from "public"."thread_members" tm
    where tm."thread_id" = "thread_summaries"."thread_id"
    and tm."user_id" = (select auth.uid())
  )
);

commit;