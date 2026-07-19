-- Realtime setup for messaging app

-- 1. Enable RLS on realtime.messages for private channel auth
alter table realtime.messages enable row level security;

-- 2. Policy: Allow authenticated users to join private thread topics
-- Users can only join topics for threads they are members of
create policy "thread_messages_select" on realtime.messages
for select to authenticated
using (
  topic like 'thread:%:messages'
  and (
    select 1 from public.thread_members tm
    where tm.thread_id = split_part(topic, ':', 2)::uuid
    and tm.user_id = auth.uid()
  )
);

-- 3. Policy: Allow authenticated users to broadcast to thread topics
create policy "thread_messages_insert" on realtime.messages
for insert to authenticated
with check (
  topic like 'thread:%:messages'
  and (
    select 1 from public.thread_members tm
    where tm.thread_id = split_part(topic, ':', 2)::uuid
    and tm.user_id = auth.uid()
  )
);

-- 4. Trigger function to broadcast message changes
create or replace function broadcast_message_changes()
returns trigger
language plpgsql
security definer
as $$
begin
  if TG_OP = 'INSERT' then
    perform realtime.broadcast_changes(
      'thread:' || NEW.thread_id::text || ':messages',
      'INSERT',
      row_to_json(NEW)::jsonb
    );
    return NEW;
  elsif TG_OP = 'DELETE' then
    perform realtime.broadcast_changes(
      'thread:' || OLD.thread_id::text || ':messages',
      'DELETE',
      row_to_json(OLD)::jsonb
    );
    return OLD;
  elsif TG_OP = 'UPDATE' then
    perform realtime.broadcast_changes(
      'thread:' || NEW.thread_id::text || ':messages',
      'UPDATE',
      row_to_json(NEW)::jsonb
    );
    return NEW;
  end if;
  return null;
end;
$$;

-- 5. Create trigger on messages table
drop trigger if exists messages_broadcast on public.messages;
create trigger messages_broadcast
after insert or update or delete on public.messages
for each row execute function broadcast_message_changes();

-- 6. Also add trigger for threads (for thread list updates)
create or replace function broadcast_thread_changes()
returns trigger
language plpgsql
security definer
as $$
begin
  if TG_OP = 'INSERT' then
    perform realtime.broadcast_changes(
      'threads',
      'INSERT',
      row_to_json(NEW)::jsonb
    );
    return NEW;
  end if;
  return null;
end;
$$;

drop trigger if exists threads_broadcast on public.threads;
create trigger threads_broadcast
after insert on public.threads
for each row execute function broadcast_thread_changes();

-- 7. RLS for threads broadcast topic
create policy "threads_broadcast_select" on realtime.messages
for select to authenticated
using (topic = 'threads');

create policy "threads_broadcast_insert" on realtime.messages
for insert to authenticated
with check (topic = 'threads');