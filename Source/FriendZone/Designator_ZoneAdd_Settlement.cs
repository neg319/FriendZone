using RimWorld;
using UnityEngine;
using Verse;

namespace FriendZone
{
    public class Designator_ZoneAdd_Settlement : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "FriendZone".Translate();

        public Designator_ZoneAdd_Settlement()
        {
            zoneTypeToPlace = typeof(Zone_Settlement);
            defaultLabel = "FriendZone".Translate();
            defaultDesc = "DesignatorFriendZoneDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Settlement", true);
            tutorTag = "ZoneAdd_Settlement";
            hotKey = KeyBindingDefOf.Misc2;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            AcceptanceReport baseReport = base.CanDesignateCell(c);
            if (!baseReport.Accepted)
            {
                return baseReport;
            }

            if (!c.Standable(Map))
            {
                return "FriendZoneMustBeStandable".Translate();
            }

            return true;
        }

        protected override Zone MakeNewZone()
        {
            return new Zone_Settlement(Find.CurrentMap.zoneManager);
        }
    }
}
