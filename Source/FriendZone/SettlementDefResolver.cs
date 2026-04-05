using System.Linq;
using RimWorld;
using Verse;

namespace FriendZone
{
    public static class SettlementDefResolver
    {
        public static ThingDef Thing(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }

        public static JobDef Job(string defName)
        {
            return DefDatabase<JobDef>.GetNamedSilentFail(defName);
        }

        public static WorkTypeDef WorkType(string defName)
        {
            return DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
        }

        public static PawnKindDef PreferredSettlerKind()
        {
            return DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("SpaceRefugee")
                ?? DefDatabase<PawnKindDef>.AllDefsListForReading.FirstOrDefault(def => def.RaceProps != null && def.RaceProps.Humanlike);
        }

        public static FactionDef PreferredSettlementFactionDef()
        {
            return DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderCivil")
                ?? DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderRefugee")
                ?? DefDatabase<FactionDef>.GetNamedSilentFail("Empire");
        }

        public static ThingDef WoodStuff()
        {
            return Thing("WoodLog") ?? Thing("Steel");
        }

        public static ThingDef BedDef()
        {
            return Thing("Bed") ?? Thing("SleepingSpot");
        }

        public static ThingDef MealDef()
        {
            return Thing("MealSimple") ?? Thing("MealSurvivalPack");
        }

        public static ThingDef PotatoFoodDef()
        {
            return Thing("RawPotatoes") ?? Thing("Potato");
        }

        public static ThingDef PotatoPlantDef()
        {
            return Thing("Plant_Potato");
        }

        public static ThingDef WallDef()
        {
            return Thing("Wall");
        }

        public static ThingDef DoorDef()
        {
            return Thing("Door");
        }

        public static ThingDef CampfireDef()
        {
            return Thing("Campfire");
        }

        public static ThingDef TorchLampDef()
        {
            return Thing("TorchLamp");
        }

        public static ThingDef TableDef()
        {
            return Thing("Table1x2c") ?? Thing("Table2x2c");
        }

        public static ThingDef ChairDef()
        {
            return Thing("DiningChair") ?? Thing("Stool");
        }

        public static ThingDef ShortBowDef()
        {
            return Thing("Bow_Short") ?? Thing("Greatbow") ?? Thing("Knife");
        }
    }
}
