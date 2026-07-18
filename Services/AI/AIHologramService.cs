using BattleLuck;

namespace BattleLuck.Services.AI
{
    /// <summary>
    /// Spawns world-space hologram entities to display AI responses visually.
    /// Note: Requires client-side mod to render BattleAiHologramDisplay component.
    /// Without the client mod, this logs the hologram intent only.
    /// </summary>
    public class AiHologramService
    {
        private readonly EntityManager _entityManager;
        private readonly List<HologramEntry> _activeHolograms = new();
        private readonly float _lifetimeSeconds;

        private struct HologramEntry
        {
            public Entity Entity;
            public float ExpiryTime;
        }

        public AiHologramService(EntityManager em, float lifetimeSeconds = 15f)
        {
            _entityManager = em;
            _lifetimeSeconds = lifetimeSeconds;
        }

        public Entity SpawnHologram(float3 position, string message)
        {
            try
            {
                // Create an entity with Translation component to mark a position
                var entity = _entityManager.CreateEntity();
                _entityManager.AddComponent<Translation>(entity);
                _entityManager.SetComponentData(entity, new Translation { Value = position });

                // Log the hologram content for debugging
                BattleLuckLogger.Log($"[Hologram] Would spawn at ({position.x:F1}, {position.y:F1}, {position.z:F1}): {TruncateMessage(message, 127)}");

                var expiryTime = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds + _lifetimeSeconds;
                _activeHolograms.Add(new HologramEntry { Entity = entity, ExpiryTime = expiryTime });

                return entity;
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"Failed to spawn hologram: {ex.Message}");
                return Entity.Null;
            }
        }

        private static string TruncateMessage(string message, int maxLength)
        {
            if (string.IsNullOrEmpty(message)) return "";
            return message.Length <= maxLength ? message : message[..maxLength];
        }

        public void UpdateHolograms()
        {
            try
            {
                var now = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;
                for (int i = _activeHolograms.Count - 1; i >= 0; i--)
                {
                    var entry = _activeHolograms[i];
                    if (!entry.Entity.Exists())
                    {
                        _activeHolograms.RemoveAt(i);
                        continue;
                    }

                    if (now >= entry.ExpiryTime)
                    {
                        _entityManager.DestroyEntity(entry.Entity);
                        _activeHolograms.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                BattleLuckLogger.Warning($"Hologram update error: {ex.Message}");
            }
        }
    }
}