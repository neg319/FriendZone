using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FriendZone
{
    public class MapComponent_SettlementManager : MapComponent
    {
        private const int TickInterval = 250;
        private const int InitialSpawnDelay = 1200;
        private const int RepeatSpawnDelay = 18000;
        private const int RetryDelay = 2400;
        private const float RentRate = 0.10f;
        public MapComponent_SettlementManager(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }

            foreach (Zone_Settlement zone in map.zoneManager.AllZones.OfType<Zone_Settlement>())
            {
                ProcessZone(zone);
            }
        }

        private void ProcessZone(Zone_Settlement zone)
        {
            if (zone == null || !zone.settlementEnabled || zone.cells == null || zone.cells.Count < 9)
            {
                return;
            }

            zone.CleanupSettlers();
            SettlementFactionUtility.ResolveFaction(zone);
            SettlementLayoutPlanner.EnsureLayout(zone, Mathf.Max(zone.DesiredSettlerCount, zone.ActiveSettlerCount + 1));
            RefreshSettlementOwnership(zone);

            if (!zone.starterSuppliesDelivered)
            {
                DropStarterSupplies(zone);
                zone.starterSuppliesDelivered = true;
                InitializeResourceSnapshot(zone);
            }

            EnsureLord(zone);
            AssignSettlementJobs(zone);
            ProgressConstruction(zone);
            ProcessRent(zone);
            UpdateZoneStatus(zone);

            if (zone.ActiveSettlerCount >= zone.DesiredSettlerCount)
            {
                return;
            }

            if (zone.nextSettlerSpawnTick < 0)
            {
                zone.nextSettlerSpawnTick = Find.TickManager.TicksGame + InitialSpawnDelay;
            }

            if (Find.TickManager.TicksGame < zone.nextSettlerSpawnTick)
            {
                return;
            }

            if (TrySpawnSettler(zone))
            {
                SettlementLayoutPlanner.EnsureLayout(zone, Mathf.Max(zone.DesiredSettlerCount, zone.ActiveSettlerCount));
                RefreshSettlementOwnership(zone);
                zone.nextSettlerSpawnTick = Find.TickManager.TicksGame + RepeatSpawnDelay;
            }
            else
            {
                zone.nextSettlerSpawnTick = Find.TickManager.TicksGame + RetryDelay;
            }
        }


        private bool TrySpawnSettler(Zone_Settlement zone)
        {
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entryCell, map, 0f, true, null))
            {
                return false;
            }

            Faction faction = SettlementFactionUtility.ResolveFaction(zone);
            PawnKindDef pawnKind = SettlementDefResolver.PreferredSettlerKind();
            if (faction == null || pawnKind == null)
            {
                return false;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind: pawnKind,
                faction: faction,
                context: PawnGenerationContext.NonPlayer,
                tile: map.Tile);

            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                return false;
            }

            GenSpawn.Spawn(pawn, entryCell, map, Rot4.Random);
            ConfigurePawnForSettlement(pawn);
            GiveStarterFood(pawn);
            GiveSimpleWeapon(pawn);
            zone.NotifySettlerJoined(pawn);
            EnsureLord(zone);

            Messages.Message(
                "FriendZoneSettlerArrived".Translate(pawn.LabelShortCap, zone.label, zone.settlementKind.LabelFor(), zone.settlementFaction.Name),
                pawn,
                MessageTypeDefOf.PositiveEvent,
                false);

            return true;
        }

        private void ConfigurePawnForSettlement(Pawn pawn)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                return;
            }

            pawn.workSettings.EnableAndInitialize();
            SetWorkPriority(pawn, "Construction", 1);
            SetWorkPriority(pawn, "Growing", 1);
            SetWorkPriority(pawn, "PlantCutting", 1);
            SetWorkPriority(pawn, "Hauling", 2);
            SetWorkPriority(pawn, "Hunting", 2);
        }

        private void SetWorkPriority(Pawn pawn, string workTypeDefName, int priority)
        {
            WorkTypeDef workType = SettlementDefResolver.WorkType(workTypeDefName);
            if (workType != null && pawn.workSettings.WorkIsActive(workType))
            {
                pawn.workSettings.SetPriority(workType, priority);
            }
        }

        private void GiveSimpleWeapon(Pawn pawn)
        {
            if (pawn?.equipment == null || pawn.equipment.Primary != null)
            {
                return;
            }

            ThingDef weaponDef = SettlementDefResolver.ShortBowDef();
            if (weaponDef == null)
            {
                return;
            }

            Thing weaponThing = ThingMaker.MakeThing(weaponDef);
            if (weaponThing is ThingWithComps weaponWithComps)
            {
                pawn.equipment.AddEquipment(weaponWithComps);
            }
        }

        private void GiveStarterFood(Pawn pawn)
        {
            ThingDef mealDef = SettlementDefResolver.MealDef();
            if (pawn?.inventory?.innerContainer == null || mealDef == null)
            {
                return;
            }

            Thing meal = ThingMaker.MakeThing(mealDef);
            if (meal != null)
            {
                meal.stackCount = 2;
                pawn.inventory.innerContainer.TryAdd(meal);
            }
        }

        private void DropStarterSupplies(Zone_Settlement zone)
        {
            IntVec3 dropCell = FindSettlementDropCell(zone, SettlementLayoutPlanner.GetStorageCell(zone));
            if (!dropCell.IsValid)
            {
                dropCell = FindSettlementDropCell(zone, zone.BestCenterCell);
            }

            // Construction is abstracted for FriendZone settlements.
            // Settlers still need some food on hand, but buildings no longer consume
            // real wood, stone, or steel from world items.
            DropSupply(zone, dropCell, SettlementDefResolver.MealDef(), Mathf.Max(8, zone.DesiredSettlerCount * 2));
            DropSupply(zone, dropCell, SettlementDefResolver.PotatoFoodDef(), 60);
        }

        private void DropSupply(Zone_Settlement zone, IntVec3 nearCell, ThingDef def, int count)
        {
            if (zone == null || def == null || count <= 0)
            {
                return;
            }

            Thing thing = ThingMaker.MakeThing(def);
            if (thing == null)
            {
                return;
            }

            if (thing.def != null && thing.def.CanHaveFaction && zone.settlementFaction != null)
            {
                thing.SetFaction(zone.settlementFaction);
            }

            thing.stackCount = count;
            IntVec3 dropCell = FindSettlementDropCell(zone, nearCell);
            if (!dropCell.IsValid)
            {
                dropCell = nearCell.IsValid ? nearCell : zone.BestCenterCell;
            }

            if (!GenPlace.TryPlaceThing(thing, dropCell, map, ThingPlaceMode.Direct))
            {
                GenPlace.TryPlaceThing(thing, dropCell, map, ThingPlaceMode.Near);
            }
        }

        private Dictionary<ThingDef, int> CalculateRequiredConstructionSupplies(Zone_Settlement zone)
        {
            Dictionary<ThingDef, int> required = new Dictionary<ThingDef, int>();
            foreach (Thing thing in EnumerateSettlementThings(zone))
            {
                if (thing is Blueprint_Build blueprint)
                {
                    AddConstructionCostsToTotals(blueprint.def?.entityDefToBuild as ThingDef, blueprint.Stuff, required);
                    continue;
                }

                if (thing is Frame frame)
                {
                    AddConstructionCostsToTotals(frame.def?.entityDefToBuild as ThingDef, frame.Stuff, required);
                }
            }

            foreach (ThingDef def in required.Keys.ToList())
            {
                required[def] = Mathf.Max(0, required[def] - CountSettlementResources(zone, def));
            }

            return required;
        }

        private void AddConstructionCostsToTotals(ThingDef thingDef, ThingDef stuff, Dictionary<ThingDef, int> totals)
        {
            Dictionary<ThingDef, int> costs = GetConstructionCosts(thingDef, stuff);
            foreach (KeyValuePair<ThingDef, int> pair in costs)
            {
                if (pair.Key == null || pair.Value <= 0)
                {
                    continue;
                }

                if (!totals.ContainsKey(pair.Key))
                {
                    totals[pair.Key] = 0;
                }

                totals[pair.Key] += pair.Value;
            }
        }

        private void EnsureLord(Zone_Settlement zone)
        {
            List<Pawn> spawnedSettlers = zone.settlers.Where(p => p != null && p.Spawned && !p.Dead).ToList();
            if (spawnedSettlers.Count == 0 || zone.settlementFaction == null)
            {
                return;
            }

            if (spawnedSettlers.Any(p => p.GetLord() != null))
            {
                return;
            }

            LordMaker.MakeNewLord(zone.settlementFaction, new LordJob_DefendBase(zone.settlementFaction, zone.BestCenterCell, 251999, false), map, spawnedSettlers);
        }

        private void AssignSettlementJobs(Zone_Settlement zone)
        {
            foreach (Pawn pawn in zone.settlers)
            {
                if (!ShouldAssignJob(pawn))
                {
                    continue;
                }

                if (TryAssignCleanupJob(pawn, zone))
                {
                    continue;
                }

                if (TryAssignConstructionApproachJob(pawn, zone))
                {
                    continue;
                }

                if (TryAssignFarmJob(pawn, zone))
                {
                    continue;
                }

                if (TryDepositInventoryToStorage(pawn, zone))
                {
                    continue;
                }

                if (TryAssignHuntJob(pawn, zone))
                {
                    continue;
                }

                TryReturnToSettlement(pawn, zone);
            }
        }


        private bool ShouldAssignJob(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned || pawn.Downed || pawn.jobs == null)
            {
                return false;
            }

            Job curJob = pawn.CurJob;
            if (curJob == null || curJob.def == null)
            {
                return true;
            }

            string defName = curJob.def.defName;
            return defName != "CutPlant"
                && defName != "HaulToCell"
                && defName != "Harvest"
                && defName != "Sow"
                && defName != "Hunt"
                && defName != "Goto";
        }

        private void ProgressConstruction(Zone_Settlement zone)
        {
            Thing site = FindNextConstructionSite(zone);
            if (site == null)
            {
                return;
            }

            Pawn builder = FindClosestAvailableSettler(zone, site.Position);
            if (builder == null)
            {
                return;
            }

            MoveBuilderTowardSite(builder, site.Position);
            if (builder.Position.DistanceToSquared(site.Position) > 2)
            {
                return;
            }

            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }

            if (TryCompleteConstructionSite(zone, site))
            {
                RefreshSettlementOwnership(zone);
            }
        }


        private Thing FindNextConstructionSite(Zone_Settlement zone)
        {
            return EnumerateSettlementThings(zone)
                .Where(thing => thing != null
                    && !thing.Destroyed
                    && thing.Faction == zone.settlementFaction
                    && (thing is Blueprint_Build || thing is Frame))
                .OrderByDescending(ConstructionPriority)
                .ThenBy(thing => thing.Position.DistanceToSquared(zone.BestCenterCell))
                .FirstOrDefault();
        }


        private Pawn FindClosestAvailableSettler(Zone_Settlement zone, IntVec3 targetCell)
        {
            return zone.settlers
                .Where(pawn => pawn != null
                    && !pawn.Dead
                    && !pawn.Destroyed
                    && pawn.Spawned
                    && !pawn.Downed
                    && pawn.Position.InBounds(map))
                .OrderBy(pawn => pawn.Position.DistanceToSquared(targetCell))
                .FirstOrDefault();
        }

        private void MoveBuilderTowardSite(Pawn pawn, IntVec3 targetCell)
        {
            if (pawn == null || pawn.jobs == null || pawn.pather == null)
            {
                return;
            }

            if (pawn.Position.DistanceToSquared(targetCell) <= 2)
            {
                return;
            }

            JobDef gotoJobDef = SettlementDefResolver.Job("Goto");
            if (gotoJobDef == null)
            {
                return;
            }

            if (pawn.CurJob != null && pawn.CurJob.def == gotoJobDef)
            {
                return;
            }

            Job job = JobMaker.MakeJob(gotoJobDef, targetCell);
            TryTakeJob(pawn, job);
        }

        private bool TryCompleteConstructionSite(Zone_Settlement zone, Thing site)
        {
            if (site is Blueprint_Build blueprint)
            {
                return TryCompleteBlueprint(zone, blueprint);
            }

            if (site is Frame frame)
            {
                return TryCompleteFrame(zone, frame);
            }

            return false;
        }

        private bool TryCompleteBlueprint(Zone_Settlement zone, Blueprint_Build blueprint)
        {
            ThingDef thingDef = blueprint?.def?.entityDefToBuild as ThingDef;
            if (thingDef == null)
            {
                return false;
            }

            ThingDef stuff = blueprint.Stuff;
            if (thingDef.MadeFromStuff && stuff == null)
            {
                stuff = SettlementDefResolver.DefaultStuffFor(thingDef);
            }

            Thing builtThing = ThingMaker.MakeThing(thingDef, thingDef.MadeFromStuff ? stuff : null);
            if (builtThing == null)
            {
                return false;
            }

            IntVec3 cell = blueprint.Position;
            Rot4 rotation = blueprint.Rotation;
            blueprint.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(builtThing, cell, map, rotation);
            if (builtThing.def != null && builtThing.def.CanHaveFaction && zone.settlementFaction != null)
            {
                builtThing.SetFaction(zone.settlementFaction);
            }
            return true;
        }

        private bool TryCompleteFrame(Zone_Settlement zone, Frame frame)
        {
            ThingDef thingDef = frame?.def?.entityDefToBuild as ThingDef;
            if (thingDef == null)
            {
                return false;
            }

            ThingDef stuff = frame.Stuff;
            if (thingDef.MadeFromStuff && stuff == null)
            {
                stuff = SettlementDefResolver.DefaultStuffFor(thingDef);
            }

            Thing builtThing = ThingMaker.MakeThing(thingDef, thingDef.MadeFromStuff ? stuff : null);
            if (builtThing == null)
            {
                return false;
            }

            IntVec3 cell = frame.Position;
            Rot4 rotation = frame.Rotation;
            frame.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(builtThing, cell, map, rotation);
            if (builtThing.def != null && builtThing.def.CanHaveFaction && zone.settlementFaction != null)
            {
                builtThing.SetFaction(zone.settlementFaction);
            }
            return true;
        }

        private bool TrySpendConstructionResources(Zone_Settlement zone, ThingDef thingDef, ThingDef stuff)
        {
            Dictionary<ThingDef, int> costs = GetConstructionCosts(thingDef, stuff);
            if (costs.Count == 0)
            {
                return true;
            }

            foreach (KeyValuePair<ThingDef, int> cost in costs)
            {
                if (CountSettlementResources(zone, cost.Key) < cost.Value)
                {
                    return false;
                }
            }

            foreach (KeyValuePair<ThingDef, int> cost in costs)
            {
                RemoveSettlementResources(zone, cost.Key, cost.Value);
            }

            return true;
        }

        private Dictionary<ThingDef, int> GetConstructionCosts(ThingDef thingDef, ThingDef stuff)
        {
            Dictionary<ThingDef, int> costs = new Dictionary<ThingDef, int>();
            if (thingDef == null)
            {
                return costs;
            }

            if (thingDef.MadeFromStuff)
            {
                if (stuff == null)
                {
                    stuff = SettlementDefResolver.DefaultStuffFor(thingDef);
                }
                if (stuff != null)
                {
                    int stuffCount = Mathf.Max(1, Mathf.CeilToInt(thingDef.CostStuffCount));
                    costs[stuff] = stuffCount;
                }
            }

            if (thingDef.CostList == null)
            {
                return costs;
            }

            foreach (ThingDefCountClass cost in thingDef.CostList)
            {
                if (cost?.thingDef == null || cost.count <= 0)
                {
                    continue;
                }

                if (!costs.ContainsKey(cost.thingDef))
                {
                    costs[cost.thingDef] = 0;
                }

                costs[cost.thingDef] += cost.count;
            }

            return costs;
        }

        private bool TryAssignCleanupJob(Pawn pawn, Zone_Settlement zone)
        {
            JobDef cutPlantJobDef = SettlementDefResolver.Job("CutPlant");
            JobDef haulToCellJobDef = SettlementDefResolver.Job("HaulToCell");
            if (cutPlantJobDef == null && haulToCellJobDef == null)
            {
                return false;
            }

            IEnumerable<IntVec3> plannedCells = SettlementLayoutPlanner.GetPlannedWorkCells(zone, Mathf.Max(zone.DesiredSettlerCount, zone.ActiveSettlerCount + 1));
            HashSet<IntVec3> fieldCells = new HashSet<IntVec3>(SettlementLayoutPlanner.GetFieldCells(zone));
            foreach (IntVec3 cell in plannedCells.OrderBy(targetCell => targetCell.DistanceToSquared(pawn.Position)))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    if (thing is Plant plant)
                    {
                        if (fieldCells.Contains(cell))
                        {
                            ThingDef desiredCrop = SettlementLayoutPlanner.GetDesiredCropForCell(zone, cell);
                            if (desiredCrop != null && plant.def == desiredCrop)
                            {
                                continue;
                            }
                        }

                        if (cutPlantJobDef != null && pawn.CanReserveAndReach(plant, PathEndMode.Touch, Danger.Some))
                        {
                            Job cutJob = JobMaker.MakeJob(cutPlantJobDef, plant);
                            if (TryTakeJob(pawn, cutJob))
                            {
                                return true;
                            }
                        }

                        continue;
                    }

                    if (!IsChunkBlocker(thing) || haulToCellJobDef == null || !pawn.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Some))
                    {
                        continue;
                    }

                    IntVec3 dumpCell = FindCleanupDumpCell(zone, cell);
                    if (!dumpCell.IsValid)
                    {
                        continue;
                    }

                    Job haulJob = JobMaker.MakeJob(haulToCellJobDef, thing, dumpCell);
                    haulJob.count = Mathf.Min(thing.stackCount, thing.def.stackLimit);
                    if (TryTakeJob(pawn, haulJob))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsChunkBlocker(Thing thing)
        {
            if (thing == null || thing.def == null || thing.Destroyed)
            {
                return false;
            }

            if (!thing.def.EverHaulable)
            {
                return false;
            }

            return thing.def.defName != null && thing.def.defName.IndexOf("Chunk", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IntVec3 FindCleanupDumpCell(Zone_Settlement zone, IntVec3 blockedCell)
        {
            IEnumerable<IntVec3> reservedCells = SettlementLayoutPlanner.GetPlannedWorkCells(zone, Mathf.Max(zone.DesiredSettlerCount, zone.ActiveSettlerCount + 1));
            HashSet<IntVec3> reserved = new HashSet<IntVec3>(reservedCells);
            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;

            for (int radius = 2; radius <= 16; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(blockedCell, radius, true))
                {
                    if (!cell.InBounds(map) || zone.ContainsCell(cell) || reserved.Contains(cell))
                    {
                        continue;
                    }

                    if (!cell.Standable(map) || cell.GetEdifice(map) != null)
                    {
                        continue;
                    }

                    int distance = cell.DistanceToSquared(blockedCell);
                    if (distance < bestDistance)
                    {
                        best = cell;
                        bestDistance = distance;
                    }
                }

                if (best.IsValid)
                {
                    return best;
                }
            }

            return best;
        }

        private IEnumerable<IntVec3> EnumerateSettlementResourceCells(Zone_Settlement zone)
        {
            HashSet<IntVec3> seen = new HashSet<IntVec3>();
            if (zone?.cells != null)
            {
                foreach (IntVec3 cell in zone.cells)
                {
                    if (cell.InBounds(map) && seen.Add(cell))
                    {
                        yield return cell;
                    }
                }
            }

            IntVec3 storageCell = SettlementLayoutPlanner.GetStorageCell(zone);
            if (storageCell.IsValid)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(storageCell, 2.9f, true))
                {
                    if (cell.InBounds(map) && seen.Add(cell))
                    {
                        yield return cell;
                    }
                }
            }
        }

        private IntVec3 FindSettlementDropCell(Zone_Settlement zone, IntVec3 preferredCell)
        {
            if (zone == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 anchor = preferredCell.IsValid ? preferredCell : SettlementLayoutPlanner.GetStorageCell(zone);
            if (!anchor.IsValid)
            {
                anchor = zone.BestCenterCell;
            }

            for (int radius = 0; radius <= 8; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(anchor, radius, true)
                    .OrderBy(candidate => candidate.DistanceToSquared(anchor)))
                {
                    if (!cell.InBounds(map) || zone.ContainsCell(cell) || !cell.Standable(map))
                    {
                        continue;
                    }

                    Building edifice = cell.GetEdifice(map);
                    if (edifice != null && !(edifice is Building_Storage))
                    {
                        continue;
                    }

                    return cell;
                }
            }

            return anchor;
        }

        private int CountSettlementResources(Zone_Settlement zone, ThingDef def)
        {
            if (zone == null || def == null)
            {
                return 0;
            }

            int count = 0;

            foreach (Pawn pawn in zone.settlers)
            {
                if (pawn?.inventory?.innerContainer == null)
                {
                    continue;
                }

                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    Thing thing = pawn.inventory.innerContainer[i];
                    if (thing != null && thing.def == def && !thing.Destroyed)
                    {
                        count += thing.stackCount;
                    }
                }
            }

            foreach (IntVec3 cell in EnumerateSettlementResourceCells(zone))
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing != null && thing.def == def && !thing.Destroyed)
                    {
                        count += thing.stackCount;
                    }
                }
            }

            return count;
        }

        private bool TryAssignFarmJob(Pawn pawn, Zone_Settlement zone)
        {
            JobDef harvestJobDef = SettlementDefResolver.Job("Harvest");
            JobDef sowJobDef = SettlementDefResolver.Job("Sow");
            JobDef cutPlantJobDef = SettlementDefResolver.Job("CutPlant");

            foreach (IntVec3 cell in SettlementLayoutPlanner.GetFieldCells(zone))
            {
                Plant plant = GetPlantAt(cell);
                if (plant != null && plant.HarvestableNow && harvestJobDef != null && pawn.CanReserveAndReach(plant, PathEndMode.Touch, Danger.Some))
                {
                    Job job = JobMaker.MakeJob(harvestJobDef, plant);
                    if (TryTakeJob(pawn, job))
                    {
                        return true;
                    }
                }
            }

            foreach (IntVec3 cell in SettlementLayoutPlanner.GetFieldCells(zone))
            {
                ThingDef desiredPlantDef = SettlementLayoutPlanner.GetDesiredCropForCell(zone, cell);
                Plant plant = GetPlantAt(cell);
                if (plant != null)
                {
                    if (desiredPlantDef != null && plant.def != desiredPlantDef && cutPlantJobDef != null && pawn.CanReserveAndReach(plant, PathEndMode.Touch, Danger.Some))
                    {
                        Job cutJob = JobMaker.MakeJob(cutPlantJobDef, plant);
                        if (TryTakeJob(pawn, cutJob))
                        {
                            return true;
                        }
                    }

                    continue;
                }

                if (sowJobDef == null || desiredPlantDef == null || !pawn.CanReserveAndReach(cell, PathEndMode.Touch, Danger.Some))
                {
                    continue;
                }

                Job sowJob = JobMaker.MakeJob(sowJobDef, cell);
                sowJob.plantDefToSow = desiredPlantDef;
                if (TryTakeJob(pawn, sowJob))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryDepositInventoryToStorage(Pawn pawn, Zone_Settlement zone)
        {
            if (pawn?.inventory?.innerContainer == null || pawn.inventory.innerContainer.Count == 0)
            {
                return false;
            }

            Thing thingToDeposit = pawn.inventory.innerContainer
                .FirstOrDefault(thing => thing != null && !thing.Destroyed && IsRentableResource(thing));
            if (thingToDeposit == null)
            {
                return false;
            }

            IntVec3 storageCell = SettlementLayoutPlanner.GetStorageCell(zone);
            if (!storageCell.IsValid)
            {
                return false;
            }

            JobDef gotoJobDef = SettlementDefResolver.Job("Goto");
            if (pawn.Position.DistanceToSquared(storageCell) > 4)
            {
                if (gotoJobDef == null)
                {
                    return false;
                }

                Job gotoJob = JobMaker.MakeJob(gotoJobDef, storageCell);
                return TryTakeJob(pawn, gotoJob);
            }

            Thing carried = thingToDeposit.SplitOff(thingToDeposit.stackCount);
            if (carried == null)
            {
                return false;
            }

            IntVec3 dropCell = FindSettlementDropCell(zone, storageCell);
            if (!GenPlace.TryPlaceThing(carried, dropCell, map, ThingPlaceMode.Direct))
            {
                GenPlace.TryPlaceThing(carried, dropCell, map, ThingPlaceMode.Near);
            }
            return true;
        }

        private bool TryAssignHuntJob(Pawn pawn, Zone_Settlement zone)
        {
            JobDef huntJobDef = SettlementDefResolver.Job("Hunt");
            if (huntJobDef == null || pawn.equipment == null || pawn.equipment.Primary == null)
            {
                return false;
            }

            Pawn prey = map.mapPawns.AllPawnsSpawned
                .Where(other => other != null
                    && other.RaceProps != null
                    && other.RaceProps.Animal
                    && !other.Dead
                    && !other.Downed
                    && other.Faction == null
                    && other.Position.InBounds(map)
                    && other.Position.DistanceTo(zone.BestCenterCell) <= 28f)
                .OrderBy(other => other.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault(other => pawn.CanReserveAndReach(other, PathEndMode.Touch, Danger.Some));

            if (prey == null)
            {
                return false;
            }

            Job huntJob = JobMaker.MakeJob(huntJobDef, prey);
            return TryTakeJob(pawn, huntJob);
        }

        private void TryReturnToSettlement(Pawn pawn, Zone_Settlement zone)
        {
            IntVec3 targetCell = SettlementLayoutPlanner.GetStorageCell(zone);
            JobDef gotoJobDef = SettlementDefResolver.Job("Goto");
            if (!targetCell.IsValid || gotoJobDef == null)
            {
                return;
            }

            if (pawn.Position.DistanceToSquared(targetCell) <= 4)
            {
                return;
            }

            Job job = JobMaker.MakeJob(gotoJobDef, targetCell);
            TryTakeJob(pawn, job);
        }

        private bool TryTakeJob(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || job.def == null)
            {
                return false;
            }

            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            return true;
        }

        private IEnumerable<Thing> EnumerateSettlementThings(Zone_Settlement zone)
        {
            HashSet<Thing> seen = new HashSet<Thing>();
            HashSet<IntVec3> cellsToCheck = new HashSet<IntVec3>();

            if (zone?.cells != null)
            {
                foreach (IntVec3 cell in zone.cells)
                {
                    if (cell.InBounds(map))
                    {
                        cellsToCheck.Add(cell);
                    }
                }
            }

            foreach (IntVec3 cell in SettlementLayoutPlanner.GetPlannedWorkCells(zone, Mathf.Max(zone.DesiredSettlerCount, zone.ActiveSettlerCount + 1)))
            {
                if (cell.InBounds(map))
                {
                    cellsToCheck.Add(cell);
                }
            }

            foreach (IntVec3 cell in EnumerateSettlementResourceCells(zone))
            {
                if (cell.InBounds(map))
                {
                    cellsToCheck.Add(cell);
                }
            }

            foreach (IntVec3 cell in cellsToCheck)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || seen.Contains(thing))
                    {
                        continue;
                    }

                    seen.Add(thing);
                    yield return thing;
                }
            }
        }


        private bool TryAssignConstructionApproachJob(Pawn pawn, Zone_Settlement zone)
        {
            Thing site = FindNextConstructionSite(zone);
            if (site == null)
            {
                return false;
            }

            if (pawn.Position.DistanceToSquared(site.Position) <= 2)
            {
                return false;
            }

            JobDef gotoJobDef = SettlementDefResolver.Job("Goto");
            if (gotoJobDef == null || !pawn.CanReach(site.Position, PathEndMode.Touch, Danger.Some))
            {
                return false;
            }

            Job job = JobMaker.MakeJob(gotoJobDef, site.Position);
            job.expiryInterval = 600;
            return TryTakeJob(pawn, job);
        }

        private float ConstructionPriority(Thing thing)
        {
            ThingDef buildDef = thing?.def?.entityDefToBuild as ThingDef;
            if (buildDef == null)
            {
                return 0f;
            }

            if (buildDef == SettlementDefResolver.DoorDef())
            {
                return 300f;
            }

            if (buildDef == SettlementDefResolver.WallDef())
            {
                return 250f;
            }

            if (buildDef == SettlementDefResolver.BedDef())
            {
                return 200f;
            }

            if (buildDef == SettlementDefResolver.ShelfDef())
            {
                return 150f;
            }

            if (buildDef == SettlementDefResolver.TableDef() || buildDef == SettlementDefResolver.ChairDef())
            {
                return 100f;
            }

            return 50f;
        }

        private void RefreshSettlementOwnership(Zone_Settlement zone)
        {
            if (zone == null || zone.settlementFaction == null)
            {
                return;
            }

            foreach (Thing thing in EnumerateSettlementThings(zone))
            {
                if (thing == null || thing.Destroyed || thing is Pawn || thing is Plant || thing.def == null || !thing.def.CanHaveFaction)
                {
                    continue;
                }

                if (thing.Faction == Faction.OfPlayer)
                {
                    continue;
                }

                if (thing.Faction == null || thing.Faction == zone.settlementFaction || !thing.Faction.IsPlayer)
                {
                    thing.SetFaction(zone.settlementFaction);
                }
            }
        }

        private void UpdateZoneStatus(Zone_Settlement zone)
        {
            if (zone == null)
            {
                return;
            }

            zone.pendingConstructionCount = EnumerateSettlementThings(zone)
                .Count(thing => thing != null && !thing.Destroyed && thing.Faction == zone.settlementFaction && (thing is Blueprint_Build || thing is Frame));
            zone.pendingBlockerCount = CountPendingBlockers(zone);
            zone.fieldCellCount = SettlementLayoutPlanner.GetFieldCells(zone).Count();
            zone.currentProjectText = DescribeCurrentProject(zone);

            if (zone.pendingBlockerCount > 0)
            {
                zone.currentStatusText = "Clearing ground";
            }
            else if (zone.pendingConstructionCount > 0)
            {
                zone.currentStatusText = "Building settlement";
            }
            else if (zone.ActiveSettlerCount < zone.DesiredSettlerCount)
            {
                zone.currentStatusText = "Waiting for settlers";
            }
            else if (HasHarvestableCrops(zone))
            {
                zone.currentStatusText = "Harvesting fields";
            }
            else if (HasEmptyFieldCells(zone))
            {
                zone.currentStatusText = "Sowing fields";
            }
            else
            {
                zone.currentStatusText = "Maintaining settlement";
            }
        }

        private int CountPendingBlockers(Zone_Settlement zone)
        {
            HashSet<IntVec3> seen = new HashSet<IntVec3>();
            int count = 0;
            foreach (IntVec3 cell in SettlementLayoutPlanner.GetPlannedWorkCells(zone, Mathf.Max(zone.DesiredSettlerCount, zone.ActiveSettlerCount + 1)))
            {
                if (!cell.InBounds(map) || !seen.Add(cell))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    if (thing is Plant plant)
                    {
                        ThingDef desiredCrop = SettlementLayoutPlanner.GetDesiredCropForCell(zone, cell);
                        if (desiredCrop != null && plant.def == desiredCrop)
                        {
                            continue;
                        }

                        count++;
                        break;
                    }

                    if (IsChunkBlocker(thing))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private string DescribeCurrentProject(Zone_Settlement zone)
        {
            Thing nextSite = FindNextConstructionSite(zone);
            if (nextSite != null)
            {
                ThingDef buildDef = nextSite.def?.entityDefToBuild as ThingDef;
                if (buildDef != null)
                {
                    return "Constructing " + buildDef.label;
                }
            }

            if (zone.pendingBlockerCount > 0)
            {
                return "Clearing trees and chunks";
            }

            if (HasHarvestableCrops(zone))
            {
                return "Harvesting crops";
            }

            if (HasEmptyFieldCells(zone))
            {
                return "Planting crops";
            }

            return zone.planGenerated ? "Settlement established" : "Generating layout";
        }

        private bool HasHarvestableCrops(Zone_Settlement zone)
        {
            foreach (IntVec3 cell in SettlementLayoutPlanner.GetFieldCells(zone))
            {
                Plant plant = GetPlantAt(cell);
                if (plant != null && plant.HarvestableNow)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasEmptyFieldCells(Zone_Settlement zone)
        {
            foreach (IntVec3 cell in SettlementLayoutPlanner.GetFieldCells(zone))
            {
                if (GetPlantAt(cell) == null)
                {
                    return true;
                }
            }

            return false;
        }

        private void InitializeResourceSnapshot(Zone_Settlement zone)
        {
            zone.lastKnownResourceTotals = CalculateResourceTotals(zone);
            zone.rentCarry.Clear();
        }

        private void ProcessRent(Zone_Settlement zone)
        {
            zone.lastRentDeliveredCount = 0;
            zone.lastRentDeliveredDefName = string.Empty;

            Dictionary<string, int> currentTotals = CalculateResourceTotals(zone);
            if (zone.lastKnownResourceTotals == null || zone.lastKnownResourceTotals.Count == 0)
            {
                zone.lastKnownResourceTotals = currentTotals;
                return;
            }

            foreach (KeyValuePair<string, int> pair in currentTotals.ToList())
            {
                int previousAmount = 0;
                zone.lastKnownResourceTotals.TryGetValue(pair.Key, out previousAmount);
                int gained = pair.Value - previousAmount;
                if (gained <= 0)
                {
                    continue;
                }

                float carry = 0f;
                zone.rentCarry.TryGetValue(pair.Key, out carry);
                carry += gained * RentRate;
                int wholeRent = Mathf.FloorToInt(carry);
                if (wholeRent <= 0)
                {
                    zone.rentCarry[pair.Key] = carry;
                    continue;
                }

                ThingDef resourceDef = SettlementDefResolver.Thing(pair.Key);
                if (resourceDef == null)
                {
                    zone.rentCarry[pair.Key] = carry;
                    continue;
                }

                int removed = RemoveSettlementResources(zone, resourceDef, wholeRent);
                if (removed <= 0)
                {
                    zone.rentCarry[pair.Key] = carry;
                    continue;
                }

                carry -= removed;
                zone.rentCarry[pair.Key] = carry;
                currentTotals[pair.Key] = Mathf.Max(0, pair.Value - removed);
                zone.lastRentDeliveredCount += removed;
                zone.lastRentDeliveredDefName = resourceDef.label;
                DeliverRentToPlayer(zone, resourceDef, removed);
            }

            zone.lastKnownResourceTotals = currentTotals;
        }


        private Dictionary<string, int> CalculateResourceTotals(Zone_Settlement zone)
        {
            Dictionary<string, int> totals = new Dictionary<string, int>();

            foreach (Pawn pawn in zone.settlers)
            {
                if (pawn?.inventory?.innerContainer == null)
                {
                    continue;
                }

                for (int i = 0; i < pawn.inventory.innerContainer.Count; i++)
                {
                    Thing thing = pawn.inventory.innerContainer[i];
                    AddThingToTotals(totals, thing);
                }
            }

            foreach (IntVec3 cell in EnumerateSettlementResourceCells(zone))
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    AddThingToTotals(totals, things[i]);
                }
            }

            return totals;
        }

        private void AddThingToTotals(Dictionary<string, int> totals, Thing thing)
        {
            if (!IsRentableResource(thing))
            {
                return;
            }

            if (!totals.ContainsKey(thing.def.defName))
            {
                totals[thing.def.defName] = 0;
            }

            totals[thing.def.defName] += thing.stackCount;
        }

        private bool IsRentableResource(Thing thing)
        {
            if (thing == null || thing.def == null || thing.Destroyed)
            {
                return false;
            }

            if (thing.def.category != ThingCategory.Item)
            {
                return false;
            }

            if (!thing.def.EverHaulable || thing.def.stackLimit <= 1)
            {
                return false;
            }

            return true;
        }

        private int RemoveSettlementResources(Zone_Settlement zone, ThingDef def, int count)
        {
            int remaining = count;

            foreach (Pawn pawn in zone.settlers)
            {
                if (remaining <= 0 || pawn?.inventory?.innerContainer == null)
                {
                    continue;
                }

                for (int i = pawn.inventory.innerContainer.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    Thing thing = pawn.inventory.innerContainer[i];
                    if (thing == null || thing.def != def)
                    {
                        continue;
                    }

                    int take = Mathf.Min(remaining, thing.stackCount);
                    Thing taken = thing.SplitOff(take);
                    if (taken != null)
                    {
                        remaining -= taken.stackCount;
                        taken.Destroy();
                    }
                }
            }

            foreach (IntVec3 cell in EnumerateSettlementResourceCells(zone))
            {
                if (remaining <= 0)
                {
                    break;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.def != def || thing.Destroyed)
                    {
                        continue;
                    }

                    int take = Mathf.Min(remaining, thing.stackCount);
                    Thing taken = thing.SplitOff(take);
                    if (taken != null)
                    {
                        remaining -= taken.stackCount;
                        taken.Destroy();
                    }
                }
            }

            return count - remaining;
        }

        private void DeliverRentToPlayer(Zone_Settlement zone, ThingDef def, int count)
        {
            if (def == null || count <= 0)
            {
                return;
            }

            Thing taxThing = ThingMaker.MakeThing(def);
            if (taxThing == null)
            {
                return;
            }

            taxThing.stackCount = count;
            IntVec3 deliveryCell = FindPlayerStockpileCell(def, zone);
            if (!deliveryCell.IsValid)
            {
                deliveryCell = zone.BestCenterCell;
            }

            GenPlace.TryPlaceThing(taxThing, deliveryCell, map, ThingPlaceMode.Near);
        }

        private IntVec3 FindPlayerStockpileCell(ThingDef def, Zone_Settlement zone)
        {
            Zone_Stockpile stockpile = map.zoneManager.AllZones
                .OfType<Zone_Stockpile>()
                .Where(z => z.cells != null && z.cells.Count > 0)
                .OrderBy(z => z.cells[0].DistanceToSquared(zone.BestCenterCell))
                .FirstOrDefault();

            if (stockpile == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;
            foreach (IntVec3 cell in stockpile.cells)
            {
                if (!cell.InBounds(map) || !cell.Standable(map))
                {
                    continue;
                }

                int distance = cell.DistanceToSquared(zone.BestCenterCell);
                if (distance < bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private Plant GetPlantAt(IntVec3 cell)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Plant plant)
                {
                    return plant;
                }
            }

            return null;
        }
    }
}
