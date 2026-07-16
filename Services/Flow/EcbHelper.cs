using ProjectM;
using Unity.Entities;
using BattleLuck.Core;

namespace BattleLuck.Services.Flow
{
    public static class EcbHelper
    {
        static EntityCommandBuffer? _ecb;

        public static EntityCommandBuffer GetEcb()
        {
            if (_ecb.HasValue)
                return _ecb.Value;

            if (!VRisingCore.IsReady)
                return default;

            var server = VRisingCore.Server;
            
            // Use EndSimulationEntityCommandBufferSystem for stable sync-point playback
            var ecbSystem = server.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
            
            if (ecbSystem == null)
                return default;

            _ecb = ecbSystem.CreateCommandBuffer();
            return _ecb.Value;
        }

        public static bool TryGetEcb(out EntityCommandBuffer ecb)
        {
            ecb = GetEcb();
            return ecb.IsCreated;
        }

        public static void Reset()
        {
            _ecb = null;
        }
    }
}
