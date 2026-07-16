using ProjectM;
using Unity.Entities;
namespace BattleLuck.Utilities;

/// <summary>
/// Queues VFX sequence requests to be played on any world entity (objects, structures, NPCs, etc.).
/// Consumed each server tick by a system that applies the sequence to the target entity.
/// </summary>
internal static class Sequences
{
    public struct SequenceRequest
    {
        public string Label;
        public SequenceGUID SequenceGuid;
        public Entity Target;
        public Entity Secondary;
        public float Scale;
    }

    static readonly Queue<SequenceRequest> _sequenceQueue = new();

    public static int QueuedCount => _sequenceQueue.Count;

    static void Enqueue(SequenceRequest request) => _sequenceQueue.Enqueue(request);

    public static bool TryDequeue(out SequenceRequest request) => _sequenceQueue.TryDequeue(out request);

    /// <summary>
    /// Queue a VFX sequence on any entity (object, structure, NPC, etc.).
    /// </summary>
    public static void PlaySequence(this Entity target, SequenceGUID sequenceGuid, Entity secondary = default, float scale = 1f, string label = "")
    {
        Enqueue(new SequenceRequest
        {
            Label = label,
            Target = target,
            SequenceGuid = sequenceGuid,
            Scale = scale,
            Secondary = secondary
        });
    }
}
