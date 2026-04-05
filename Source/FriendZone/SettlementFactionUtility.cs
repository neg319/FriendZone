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
            if (zone?.settlementFaction != null)
            {
                return zone.settlementFaction;
            }

            Faction faction = FindBestExistingFaction();
            if (faction == null)
            {
                faction = CreateNewSettlementFaction();
            }

            if (faction != null && Faction.OfPlayer != null)
            {
                faction.TrySetRelationKind(Faction.OfPlayer, FactionRelationKind.Ally, canSendLetter: false);
            }

            if (zone != null)
            {
                zone.settlementFaction = faction;
            }

            return faction;
        }

        private static Faction FindBestExistingFaction()
        {
            if (Find.FactionManager == null)
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

            return candidates
                .Where(f => f.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Hostile)
                .OrderByDescending(f => f.GoodwillWith(Faction.OfPlayer))
                .FirstOrDefault();
        }

        private static bool IsValidSettlementFaction(Faction faction)
        {
            if (faction == null || faction.IsPlayer || faction.def == null || faction.Hidden || faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            return faction.def == SettlementDefResolver.PreferredSettlementFactionDef()
                || faction.def.defName == "OutlanderCivil"
                || faction.def.defName == "OutlanderRefugee"
                || faction.def.defName == "Empire";
        }

        private static Faction CreateNewSettlementFaction()
        {
            FactionDef factionDef = SettlementDefResolver.PreferredSettlementFactionDef();
            if (factionDef == null)
            {
                return null;
            }

            Faction faction = FactionGenerator.NewGeneratedFaction(factionDef);
            if (faction == null)
            {
                return null;
            }

            Find.FactionManager.Add(faction);
            return faction;
        }
    }
}
