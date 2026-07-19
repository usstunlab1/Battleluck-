# Changelog

## 1.0.0

- Consolidated server event, NPC, action, progression, teleport, schematic, and catalog services.
- Added roadmap and local LLM prompt runtime support.
- Added Thunderstore package metadata, icon, license, and release documentation.

## 1.0.1

- Added README visuals and a verified command quick reference.
- Clarified player, admin, NPC, schematic, roadmap, and AI workflows.

## 1.0.2

- Clarified native ECS event actions, rollback snapshots, and main-thread-safe execution.
- Documented asynchronous LLM I/O and approval-gated native-world mutations.

## 1.1.0

- Added `.event.create <eventId> [templateId]` for Bloodbath-style custom events.
- Custom events clone their flow, zones, kit, and prompt, receive a unique zone hash, and register without a server restart.
- Declarative event discovery now loads both top-level event files and `events/<id>/flow.json` folders.

## 1.1.1

- Registered newly-created event zones with live zone detection immediately, so `.toggleenter <eventId>` works without restarting.
