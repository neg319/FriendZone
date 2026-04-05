using System.Collections.Generic;
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

        public static ThingDef StoneBlocksDef()
        {
            return Thing("BlocksMarble")
                ?? Thing("BlocksSandstone")
                ?? Thing("BlocksGranite")
                ?? Thing("BlocksLimestone")
                ?? Thing("BlocksSlate")
                ?? Thing("MarbleBlocks")
                ?? Thing("SandstoneBlocks")
                ?? Thing("GraniteBlocks")
                ?? Thing("LimestoneBlocks")
                ?? Thing("SlateBlocks")
                ?? DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(def => def != null
                    && def.defName != null
                    && (def.defName == "BlocksMarble"
                        || def.defName == "BlocksSandstone"
                        || def.defName == "BlocksGranite"
                        || def.defName == "BlocksLimestone"
                        || def.defName == "BlocksSlate"));
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

        public static ThingDef RicePlantDef()
        {
            return Thing("Plant_Rice");
        }

        public static ThingDef StrawberryPlantDef()
        {
            return Thing("Plant_Strawberry");
        }

        public static ThingDef CornPlantDef()
        {
            return Thing("Plant_Corn");
        }

        public static ThingDef HealrootPlantDef()
        {
            return Thing("Plant_Healroot");
        }

        public static ThingDef CottonPlantDef()
        {
            return Thing("Plant_Cotton");
        }

        public static ThingDef HaygrassPlantDef()
        {
            return Thing("Plant_Haygrass");
        }

        public static ThingDef PsychoidPlantDef()
        {
            return Thing("Plant_Psychoid");
        }

        public static ThingDef SmokeleafPlantDef()
        {
            return Thing("Plant_Smokeleaf");
        }

        public static IEnumerable<ThingDef> OptionalFieldCropDefs()
        {
            yield return PotatoPlantDef();
            yield return CottonPlantDef();
            yield return HaygrassPlantDef();
            yield return PsychoidPlantDef();
            yield return SmokeleafPlantDef();
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

        public static ThingDef ShelfDef()
        {
            return Thing("ShelfSmall") ?? Thing("Shelf") ?? Thing("ShelfLarge");
        }

        public static ThingDef ShortBowDef()
        {
            return Thing("Bow_Short") ?? Thing("Greatbow") ?? Thing("Knife");
        }
    }
}
