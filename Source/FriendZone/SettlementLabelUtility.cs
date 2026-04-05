using System.Collections.Generic;
using Verse;

namespace FriendZone
{
    public static class SettlementLabelUtility
    {
        public static readonly List<SettlementKind> AllKinds = new List<SettlementKind>
        {
            SettlementKind.Camp,
            SettlementKind.Farm,
            SettlementKind.Tavern,
            SettlementKind.Inn,
            SettlementKind.Village
        };

        public static string LabelFor(this SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.Camp:
                    return "SettlementKind_Camp".Translate();
                case SettlementKind.Farm:
                    return "SettlementKind_Farm".Translate();
                case SettlementKind.Tavern:
                    return "SettlementKind_Tavern".Translate();
                case SettlementKind.Inn:
                    return "SettlementKind_Inn".Translate();
                case SettlementKind.Village:
                    return "SettlementKind_Village".Translate();
                default:
                    return kind.ToString();
            }
        }

        public static int BaseSettlerTarget(this SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.Camp:
                    return 2;
                case SettlementKind.Farm:
                    return 3;
                case SettlementKind.Tavern:
                    return 4;
                case SettlementKind.Inn:
                    return 4;
                case SettlementKind.Village:
                    return 6;
                default:
                    return 2;
            }
        }
    }
}
