# KindredExtract Unity system/tick research prompt

You are reviewing `kindredextract-systems.csv` and `kindredextract-ticks.csv`, generated from the
[Odjit/KindredExtract](https://github.com/Odjit/KindredExtract) reference list
for a V Rising Unity ECS server. The CSV contains 1534 system/type names and
name-based hints only; it is not proof of runtime scheduling. The tick CSV is a
classification list, not a measured frequency table.

## Tick semantics list

Use these labels as research categories only. An exact rate must come from Unity
group metadata, server configuration, or a live measurement:

| semantics | name hint | meaning | evidence required |
|---|---|---|---|
| `initialization` | initialization/baking hint | World/entity initialization, conversion, or baking | UpdateInGroup attributes, world bootstrap, or live trace |
| `simulation` | simulation/update hint | Regular simulation/update work; frequency is not implied by the name | UpdateInGroup/order plus measured server interval |
| `fixed_step` | fixed-step hint | Fixed-step group scheduling; timestep must be measured or read from configuration | FixedStepSimulationSystemGroup and runtime timestep |
| `presentation` | presentation hint | Presentation/render/UI work; usually not a server mutation boundary | Presentation group and server/client world check |
| `destroy_cleanup` | destroy/cleanup hint | Entity destruction, cleanup, or OnDestroy lifecycle | cleanup system/group and entity lifecycle trace |
| `spawn_lifecycle` | spawn lifecycle hint | Entity creation/spawn lifecycle work | spawn system/group and live entity creation trace |
| `server_world` | server-world hint | Name suggests server ownership; verify the actual world | WorldSystemFilter and live server-world lookup |
| `client_world` | client-world hint | Name suggests client ownership; do not use for server actions without proof | WorldSystemFilter and live client-world lookup |
| `group_barrier` | system-group boundary hint | System group or barrier boundary rather than an independently ticking system | group membership, ordering, and barrier type |
| `unknown` | unknown; inspect UpdateInGroup/runtime schedule | No safe timing classification from the name | assembly metadata, KindredExtract dump, or measured runtime trace |

For each system, research and verify:

1. The full Unity/ProjectM type and whether it exists in the server world.
2. The actual purpose and the components/queries it reads or writes.
3. The real update group (`InitializationSystemGroup`, simulation group,
   fixed-step group, presentation group, or a ProjectM group), ordering, and
   whether it runs server, client, or shared.
4. The tick semantics label from the tick list, the measured or configured
   interval (if known), and whether work is once-only, per-frame, fixed-step,
   event-driven, spawn/cleanup lifecycle, or unknown.
5. Whether it is safe to observe from a server plugin and the correct tick/main
   thread boundary for an approved BattleLuck action.

Do not infer an exact tick rate from a name. Mark unknown values as `unknown` and
cite the assembly/source or a live KindredExtract dump. Keep the output bounded:
return a corrected CSV with the original `system_name`, verified purpose,
`world`, `update_group`, `order`, `tick_semantics`, `tick_rate_hz`,
`tick_source`, `observed_interval_ms`, `evidence`, and `confidence` columns.
Never propose arbitrary reflection or direct mutation of an unverified native
system.

Timing for BattleLuck sequences must use validated `wait:<seconds>` and
`tick:<event-second>` markers; the server main-thread dispatcher remains the
mutation boundary.
