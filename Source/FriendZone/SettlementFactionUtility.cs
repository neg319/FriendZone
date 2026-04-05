using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace FriendZone
{
    public static class SettlementFactionUtility
    {
        public static Faction ResolveFaction(Zone_Settlement zone)
        {
            if (zone?.settlementFaction != null && IsValidSettlementFaction(zone.settlementFaction))
            {
                return zone.settlementFaction;
            }

            Faction faction = FindBestExistingFaction();
            if (zone != null)
            {
                zone.settlementFaction = faction;
            }

            return faction;
        }

        private static Faction FindBestExistingFaction()
        {
            if (Find.FactionManager == null || Faction.OfPlayer == null)
            {
                return null;
            }

            List<Faction> candidates = Find.FactionManager.AllFactionsListForReading
                .Where(IsValidSettlementFaction)
                .ToList();

            Faction allied = candidates
                .Where(f => f.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
                .OrderByDescending(f => f.GoodwillWith(Faction.OfPlayer))
                .FirstOrDefault();

            if (allied != null)
            {
                return allied;
            }

            Faction friendly = candidates
                .Where(f => f.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                .OrderByDescending(f => f.GoodwillWith(Faction.OfPlayer))
                .FirstOrDefault();

            if (friendly != null)
            {
                return friendly;
            }

            return Find.FactionManager.AllFactionsListForReading
                .Where(f => f != null && !f.IsPlayer && f.def != null && !f.Hidden && !f.HostileTo(Faction.OfPlayer))
                .OrderByDescending(f => f.GoodwillWith(Faction.OfPlayer))
                .FirstOrDefault();
        }

        private static bool IsValidSettlementFaction(Faction faction)
        {
            if (faction == null || faction.IsPlayer || faction.def == null || faction.Hidden || Faction.OfPlayer == null)
            {
                return false;
            }

            if (faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            return faction.def == SettlementDefResolver.PreferredSettlementFactionDef()
                || faction.def.defName == "OutlanderCivil"
                || faction.def.defName == "OutlanderRefugee"
                || faction.def.defName == "Empire";
        }
    }
}
