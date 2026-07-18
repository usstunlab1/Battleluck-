using ServantCommand = BattleLuck.Models.ServantCommand;
using ServantFaction = BattleLuck.Models.ServantFaction;
using ServantFormation = BattleLuck.Models.ServantFormation;
using ServantType = BattleLuck.Models.ServantType;

namespace BattleLuck.Services.Flow
{
    public static class ServantEnumParsers
    {
        public static ServantType ParseServantType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ServantType.Guard;
            return Enum.TryParse<ServantType>(value, true, out var result) ? result : ServantType.Guard;
        }

        public static ServantFaction ParseServantFaction(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ServantFaction.Unknown;
            return Enum.TryParse<ServantFaction>(value, true, out var result) ? result : ServantFaction.Unknown;
        }

        public static ServantCommand ParseServantCommand(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ServantCommand.Attack;
            return Enum.TryParse<ServantCommand>(value, true, out var result) ? result : ServantCommand.Attack;
        }

        public static ServantFormation ParseServantFormation(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return ServantFormation.Circle;
            return Enum.TryParse<ServantFormation>(value, true, out var result) ? result : ServantFormation.Circle;
        }
    }
}