using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace FriendZone
{
    public static class SettlementLayoutPlanner
    {
        private const int FieldRowLength = 6;
        private const int FieldRowGap = 1;
        private const int FieldBufferFromZone = 2;

        public class FieldRowPlan
        {
            public List<IntVec3> Cells = new List<IntVec3>();
            public ThingDef PlantDef;
        }

        public static void EnsureLayout(Zone_Settlement zone, int requiredBeds)
        {
            if (zone == null || zone.Map == null || zone.cells == null || zone.cells.Count < 9)
            {
                return;
            }

            SettlementFactionUtility.ResolveFaction(zone);
            requiredBeds = Mathf.Max(1, requiredBeds);

            GenerateSharedStructures(zone);
            EnsureStorageContainer(zone);
            EnsureHousing(zone, requiredBeds);
            EnsureFieldMarkers(zone);
            zone.planGenerated = true;
        }

        public static IEnumerable<FieldRowPlan> GetFieldRows(Zone_Settlement zone)
        {
            return BuildFieldRows(zone);
        }

        public static IEnumerable<IntVec3> GetFieldCells(Zone_Settlement zone)
        {
            foreach (FieldRowPlan row in GetFieldRows(zone))
            {
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    yield return row.Cells[i];
                }
            }
        }

        public static IEnumerable<IntVec3> GetPlannedWorkCells(Zone_Settlement zone, int requiredBeds)
        {
            HashSet<IntVec3> plannedCells = new HashSet<IntVec3>();
            if (zone == null || zone.Map == null || zone.cells == null || zone.cells.Count < 9)
            {
                return plannedCells;
            }

            requiredBeds = Mathf.Max(1, requiredBeds);
            CollectSharedStructureCells(zone, plannedCells);
            CollectStorageCells(zone, plannedCells);
            CollectHousingCells(zone, requiredBeds, plannedCells);
            CollectFieldCells(zone, plannedCells);
            return plannedCells;
        }

        public static ThingDef GetDesiredCropForCell(Zone_Settlement zone, IntVec3 cell)
        {
            foreach (FieldRowPlan row in GetFieldRows(zone))
            {
                if (row.Cells.Contains(cell))
                {
                    return row.PlantDef ?? SettlementDefResolver.PotatoPlantDef();
                }
            }

            return SettlementDefResolver.PotatoPlantDef();
        }

        public static IntVec3 GetStorageCell(Zone_Settlement zone)
        {
            if (zone == null || zone.Map == null)
            {
                return IntVec3.Invalid;
            }

            ThingDef shelfDef = SettlementDefResolver.ShelfDef();
            foreach (IntVec3 cell in zone.cells)
            {
                List<Thing> things = cell.GetThingList(zone.Map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null)
                    {
                        continue;
                    }

                    if (thing is Building_Storage)
                    {
                        return cell;
                    }

                    if (shelfDef != null && (thing.def == shelfDef || thing.def.entityDefToBuild == shelfDef || GenConstruct.BuiltDefOf(thing.def) == shelfDef))
                    {
                        return cell;
                    }
                }
            }

            return FindOpenZoneCellNear(zone, zone.BestCenterCell + new IntVec3(0, 0, -3));
        }

        public static int CountExistingOrPlannedBeds(Zone_Settlement zone)
        {
            ThingDef bedDef = SettlementDefResolver.BedDef();
            if (zone == null || zone.Map == null || bedDef == null)
            {
                return 0;
            }

            int count = 0;
            HashSet<Thing> counted = new HashSet<Thing>();
            foreach (IntVec3 cell in zone.cells)
            {
                List<Thing> things = cell.GetThingList(zone.Map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (counted.Contains(thing))
                    {
                        continue;
                    }

                    if (thing.def == bedDef || thing.def.entityDefToBuild == bedDef || GenConstruct.BuiltDefOf(thing.def) == bedDef)
                    {
                        counted.Add(thing);
                        count += 1;
                    }
                }
            }

            return count;
        }

        private static List<FieldRowPlan> BuildFieldRows(Zone_Settlement zone)
        {
            List<FieldRowPlan> bestRows = new List<FieldRowPlan>();
            float bestScore = float.MinValue;
            List<ThingDef> crops = GetPlannedCrops(zone);
            if (zone == null || zone.Map == null || crops.Count == 0)
            {
                return bestRows;
            }

            for (int side = 0; side < 4; side++)
            {
                for (int offset = -6; offset <= 6; offset++)
                {
                    List<FieldRowPlan> candidate = CreateFieldRowsForSide(zone, crops, side, offset);
                    float score = ScoreFieldRows(zone, candidate);
                    if (candidate.Count > bestRows.Count || (candidate.Count == bestRows.Count && score > bestScore))
                    {
                        bestRows = candidate;
                        bestScore = score;
                    }
                }
            }

            return bestRows;
        }

        private static List<ThingDef> GetPlannedCrops(Zone_Settlement zone)
        {
            List<ThingDef> crops = new List<ThingDef>
            {
                SettlementDefResolver.RicePlantDef(),
                SettlementDefResolver.StrawberryPlantDef(),
                SettlementDefResolver.CornPlantDef(),
                SettlementDefResolver.HealrootPlantDef()
            };

            crops = crops.Where(def => def != null).Distinct().ToList();

            List<ThingDef> extras = SettlementDefResolver.OptionalFieldCropDefs()
                .Where(def => def != null && !crops.Contains(def))
                .Distinct()
                .ToList();

            int seed = Mathf.Abs(zone.BestCenterCell.x * 397 ^ zone.BestCenterCell.z * 131);
            for (int i = 0; i < Mathf.Min(2, extras.Count); i++)
            {
                crops.Add(extras[(seed + i) % extras.Count]);
            }

            return crops;
        }

        private static List<FieldRowPlan> CreateFieldRowsForSide(Zone_Settlement zone, List<ThingDef> crops, int side, int offset)
        {
            List<FieldRowPlan> rows = new List<FieldRowPlan>();
            CellRect bounds = zone.ZoneBounds;
            int bandWidth = crops.Count + ((crops.Count - 1) * FieldRowGap);

            if (side == 0 || side == 1)
            {
                int startX = bounds.CenterCell.x - (bandWidth / 2) + offset;
                int startZ = side == 0 ? bounds.maxZ + FieldBufferFromZone : bounds.minZ - FieldBufferFromZone - FieldRowLength + 1;

                for (int i = 0; i < crops.Count; i++)
                {
                    int x = startX + (i * (1 + FieldRowGap));
                    List<IntVec3> cells = new List<IntVec3>();
                    for (int step = 0; step < FieldRowLength; step++)
                    {
                        int z = startZ + step;
                        cells.Add(new IntVec3(x, 0, z));
                    }

                    if (IsValidFieldRow(zone, cells, crops[i]))
                    {
                        rows.Add(new FieldRowPlan { PlantDef = crops[i], Cells = cells });
                    }
                }
            }
            else
            {
                int startZ = bounds.CenterCell.z - (bandWidth / 2) + offset;
                int startX = side == 2 ? bounds.minX - FieldBufferFromZone - FieldRowLength + 1 : bounds.maxX + FieldBufferFromZone;

                for (int i = 0; i < crops.Count; i++)
                {
                    int z = startZ + (i * (1 + FieldRowGap));
                    List<IntVec3> cells = new List<IntVec3>();
                    for (int step = 0; step < FieldRowLength; step++)
                    {
                        int x = startX + step;
                        cells.Add(new IntVec3(x, 0, z));
                    }

                    if (IsValidFieldRow(zone, cells, crops[i]))
                    {
                        rows.Add(new FieldRowPlan { PlantDef = crops[i], Cells = cells });
                    }
                }
            }

            return rows;
        }

        private static bool IsValidFieldRow(Zone_Settlement zone, List<IntVec3> cells, ThingDef crop)
        {
            if (zone == null || zone.Map == null || cells == null || cells.Count != FieldRowLength || crop == null)
            {
                return false;
            }

            float fertilityMin = crop.plant != null ? crop.plant.fertilityMin : 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(zone.Map) || zone.ContainsCell(cell))
                {
                    return false;
                }

                if (cell.Fogged(zone.Map) || cell.Roofed(zone.Map))
                {
                    return false;
                }

                if (CellHasNonRemovableFieldBlocker(zone.Map, cell))
                {
                    return false;
                }

                TerrainDef terrain = cell.GetTerrain(zone.Map);
                if (terrain == null || terrain.fertility < fertilityMin)
                {
                    return false;
                }
            }

            return true;
        }

        private static float ScoreFieldRows(Zone_Settlement zone, List<FieldRowPlan> rows)
        {
            if (zone == null || rows == null || rows.Count == 0)
            {
                return float.MinValue;
            }

            float score = rows.Count * 1000f;
            for (int i = 0; i < rows.Count; i++)
            {
                FieldRowPlan row = rows[i];
                for (int j = 0; j < row.Cells.Count; j++)
                {
                    IntVec3 cell = row.Cells[j];
                    TerrainDef terrain = cell.GetTerrain(zone.Map);
                    score += terrain != null ? terrain.fertility : 0f;
                    score -= cell.DistanceTo(zone.BestCenterCell) * 0.05f;
                }
            }

            return score;
        }

        private static void GenerateSharedStructures(Zone_Settlement zone)
        {
            switch (zone.settlementKind)
            {
                case SettlementKind.Camp:
                    EnsureCampCommons(zone);
                    break;
                case SettlementKind.Farm:
                    EnsureFarmCommons(zone);
                    break;
                case SettlementKind.Tavern:
                    EnsureTavern(zone);
                    break;
                case SettlementKind.Inn:
                    EnsureInn(zone);
                    break;
                case SettlementKind.Village:
                    EnsureVillageCommons(zone);
                    break;
                default:
                    EnsureCampCommons(zone);
                    break;
            }
        }

        private static void EnsureStorageContainer(Zone_Settlement zone)
        {
            ThingDef shelfDef = SettlementDefResolver.ShelfDef();
            ThingDef stuff = SettlementDefResolver.WoodStuff();
            if (shelfDef == null)
            {
                return;
            }

            IntVec3 storageCell = GetStorageCell(zone);
            if (!storageCell.IsValid)
            {
                return;
            }

            TryPlace(zone, shelfDef, storageCell, Rot4.North, shelfDef.MadeFromStuff ? stuff : null);
        }

        private static void EnsureHousing(Zone_Settlement zone, int requiredBeds)
        {
            int existingBeds = CountExistingOrPlannedBeds(zone);
            int remainingBeds = Mathf.Max(0, requiredBeds - existingBeds);
            if (remainingBeds <= 0)
            {
                return;
            }

            List<CellRect> cabinRects = GetCandidateCabinRects(zone).ToList();
            ThingDef bedDef = SettlementDefResolver.BedDef();
            ThingDef wood = SettlementDefResolver.WoodStuff();

            for (int i = 0; i < cabinRects.Count && remainingBeds > 0; i++)
            {
                CellRect cabin = cabinRects[i];
                BuildSimpleRoom(zone, cabin, true);
                TryPlace(zone, bedDef, cabin.CenterCell + new IntVec3(0, 0, 1), Rot4.North, wood);
                remainingBeds--;
            }

            if (remainingBeds > 0)
            {
                CellRect dorm = InnerRoom(zone, Mathf.Max(7, zone.ZoneBounds.Width - 2), Mathf.Max(5, Mathf.Min(9, zone.ZoneBounds.Height - 4)));
                BuildSimpleRoom(zone, dorm, true);
                for (int x = dorm.minX + 1; x <= dorm.maxX - 1 && remainingBeds > 0; x += 2)
                {
                    IntVec3 bedCell = new IntVec3(x, 0, dorm.CenterCell.z + 1);
                    if (!zone.ContainsCell(bedCell))
                    {
                        continue;
                    }

                    if (TryPlace(zone, bedDef, bedCell, Rot4.North, wood))
                    {
                        remainingBeds--;
                    }
                }
            }
        }

        private static IEnumerable<CellRect> GetCandidateCabinRects(Zone_Settlement zone)
        {
            CellRect bounds = zone.ZoneBounds;
            int cabinWidth = 5;
            int cabinHeight = 5;
            int spacing = 1;
            int startZ = bounds.maxZ - cabinHeight;
            int minZ = bounds.CenterCell.z;

            for (int z = startZ; z >= minZ; z -= cabinHeight + spacing)
            {
                for (int x = bounds.minX + 1; x + cabinWidth - 1 <= bounds.maxX - 1; x += cabinWidth + spacing)
                {
                    CellRect rect = new CellRect(x, z, cabinWidth, cabinHeight);
                    rect.ClipInsideMap(zone.Map);
                    if (rect.Width != cabinWidth || rect.Height != cabinHeight)
                    {
                        continue;
                    }

                    if (!zone.ContainsAll(rect))
                    {
                        continue;
                    }

                    yield return rect;
                }
            }
        }

        private static void EnsureCampCommons(Zone_Settlement zone)
        {
            IntVec3 center = zone.BestCenterCell;
            ThingDef wood = SettlementDefResolver.WoodStuff();
            TryPlace(zone, SettlementDefResolver.CampfireDef(), center, Rot4.North);
            TryPlace(zone, SettlementDefResolver.TableDef(), center + new IntVec3(0, 0, -2), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, -3), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, -3), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.TorchLampDef(), center + new IntVec3(-2, 0, 0), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.TorchLampDef(), center + new IntVec3(2, 0, 0), Rot4.North, wood);
        }

        private static void EnsureFarmCommons(Zone_Settlement zone)
        {
            EnsureCampCommons(zone);
            IntVec3 storageCell = GetStorageCell(zone);
            ThingDef wood = SettlementDefResolver.WoodStuff();
            if (storageCell.IsValid)
            {
                TryPlace(zone, SettlementDefResolver.TorchLampDef(), storageCell + new IntVec3(-1, 0, 0), Rot4.North, wood);
                TryPlace(zone, SettlementDefResolver.TorchLampDef(), storageCell + new IntVec3(1, 0, 0), Rot4.North, wood);
            }
        }

        private static void EnsureTavern(Zone_Settlement zone)
        {
            CellRect room = InnerRoom(zone, Mathf.Min(11, zone.ZoneBounds.Width - 2), Mathf.Min(7, zone.ZoneBounds.Height - 4));
            BuildSimpleRoom(zone, room, true);
            IntVec3 center = room.CenterCell;
            ThingDef wood = SettlementDefResolver.WoodStuff();
            TryPlace(zone, SettlementDefResolver.TableDef(), center + new IntVec3(-2, 0, -1), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.TableDef(), center + new IntVec3(2, 0, -1), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(-3, 0, -2), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, -2), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, -2), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(3, 0, -2), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.CampfireDef(), center + new IntVec3(0, 0, 1), Rot4.North);
        }

        private static void EnsureInn(Zone_Settlement zone)
        {
            CellRect room = InnerRoom(zone, Mathf.Min(10, zone.ZoneBounds.Width - 2), Mathf.Min(8, zone.ZoneBounds.Height - 4));
            BuildSimpleRoom(zone, room, true);
            IntVec3 center = room.CenterCell;
            ThingDef wood = SettlementDefResolver.WoodStuff();
            TryPlace(zone, SettlementDefResolver.TableDef(), center + new IntVec3(0, 0, -1), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, -2), Rot4.South, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, -2), Rot4.South, wood);
        }

        private static void EnsureVillageCommons(Zone_Settlement zone)
        {
            EnsureCampCommons(zone);
            IntVec3 center = zone.BestCenterCell;
            ThingDef wood = SettlementDefResolver.WoodStuff();
            TryPlace(zone, SettlementDefResolver.TableDef(), center + new IntVec3(0, 0, 2), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, 1), Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, 1), Rot4.North, wood);
        }

        private static void EnsureFieldMarkers(Zone_Settlement zone)
        {
            ThingDef wood = SettlementDefResolver.WoodStuff();
            List<IntVec3> cells = GetFieldCells(zone).ToList();
            if (cells.Count == 0)
            {
                return;
            }

            TryPlace(zone, SettlementDefResolver.TorchLampDef(), cells[0], Rot4.North, wood);
            TryPlace(zone, SettlementDefResolver.TorchLampDef(), cells[cells.Count - 1], Rot4.North, wood);
        }

        private static CellRect InnerRoom(Zone_Settlement zone, int desiredWidth, int desiredHeight)
        {
            CellRect bounds = zone.ZoneBounds;
            int width = Mathf.Max(4, Mathf.Min(desiredWidth, bounds.Width - 2));
            int height = Mathf.Max(4, Mathf.Min(desiredHeight, bounds.Height - 2));
            return MakeCenteredRect(zone.BestCenterCell.x, zone.BestCenterCell.z, width, height, zone.Map);
        }

        private static CellRect MakeCenteredRect(int centerX, int centerZ, int width, int height, Map map)
        {
            int minX = centerX - width / 2;
            int minZ = centerZ - height / 2;

            minX = Mathf.Clamp(minX, 0, map.Size.x - width);
            minZ = Mathf.Clamp(minZ, 0, map.Size.z - height);

            return new CellRect(minX, minZ, width, height);
        }

        private static IntVec3 FindOpenZoneCellNear(Zone_Settlement zone, IntVec3 preferredCell)
        {
            if (zone == null || zone.Map == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;
            foreach (IntVec3 cell in zone.cells)
            {
                if (!cell.InBounds(zone.Map))
                {
                    continue;
                }

                if (CellHasPermanentStructureBlocker(zone.Map, cell))
                {
                    continue;
                }

                int distance = cell.DistanceToSquared(preferredCell);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }

            return best;
        }

        private static void BuildSimpleRoom(Zone_Settlement zone, CellRect room, bool addDoor)
        {
            ThingDef wall = SettlementDefResolver.WallDef();
            ThingDef door = SettlementDefResolver.DoorDef();
            ThingDef stuff = SettlementDefResolver.WoodStuff();
            if (wall == null)
            {
                return;
            }

            int doorX = room.CenterCell.x;

            for (int x = room.minX; x <= room.maxX; x++)
            {
                IntVec3 north = new IntVec3(x, 0, room.maxZ);
                IntVec3 south = new IntVec3(x, 0, room.minZ);
                if (addDoor && x == doorX && door != null)
                {
                    TryPlace(zone, door, south, Rot4.North, stuff);
                }
                else
                {
                    TryPlace(zone, wall, south, Rot4.North, stuff);
                }

                TryPlace(zone, wall, north, Rot4.North, stuff);
            }

            for (int z = room.minZ + 1; z < room.maxZ; z++)
            {
                TryPlace(zone, wall, new IntVec3(room.minX, 0, z), Rot4.North, stuff);
                TryPlace(zone, wall, new IntVec3(room.maxX, 0, z), Rot4.North, stuff);
            }

            if (zone.Map?.areaManager?.BuildRoof != null)
            {
                for (int x = room.minX + 1; x < room.maxX; x++)
                {
                    for (int z = room.minZ + 1; z < room.maxZ; z++)
                    {
                        IntVec3 roofCell = new IntVec3(x, 0, z);
                        if (roofCell.InBounds(zone.Map) && zone.ContainsCell(roofCell))
                        {
                            zone.Map.areaManager.BuildRoof[roofCell] = true;
                        }
                    }
                }
            }
        }

        private static void CollectSharedStructureCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            switch (zone.settlementKind)
            {
                case SettlementKind.Camp:
                    CollectCampCommonCells(zone, plannedCells);
                    break;
                case SettlementKind.Farm:
                    CollectFarmCommonCells(zone, plannedCells);
                    break;
                case SettlementKind.Tavern:
                    CollectTavernCells(zone, plannedCells);
                    break;
                case SettlementKind.Inn:
                    CollectInnCells(zone, plannedCells);
                    break;
                case SettlementKind.Village:
                    CollectVillageCommonCells(zone, plannedCells);
                    break;
                default:
                    CollectCampCommonCells(zone, plannedCells);
                    break;
            }
        }

        private static void CollectStorageCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            ThingDef shelfDef = SettlementDefResolver.ShelfDef();
            if (shelfDef == null)
            {
                return;
            }

            RecordPlannedPlacement(zone, plannedCells, shelfDef, GetStorageCell(zone), Rot4.North, includeInterior: true);
        }

        private static void CollectHousingCells(Zone_Settlement zone, int requiredBeds, HashSet<IntVec3> plannedCells)
        {
            int existingBeds = CountExistingOrPlannedBeds(zone);
            int remainingBeds = Mathf.Max(0, requiredBeds - existingBeds);
            if (remainingBeds <= 0)
            {
                return;
            }

            ThingDef bedDef = SettlementDefResolver.BedDef();
            List<CellRect> cabinRects = GetCandidateCabinRects(zone).ToList();
            for (int i = 0; i < cabinRects.Count && remainingBeds > 0; i++)
            {
                CellRect cabin = cabinRects[i];
                RecordRoomCells(zone, plannedCells, cabin);
                RecordPlannedPlacement(zone, plannedCells, bedDef, cabin.CenterCell + new IntVec3(0, 0, 1), Rot4.North, includeInterior: true);
                remainingBeds--;
            }

            if (remainingBeds > 0)
            {
                CellRect dorm = InnerRoom(zone, Mathf.Max(7, zone.ZoneBounds.Width - 2), Mathf.Max(5, Mathf.Min(9, zone.ZoneBounds.Height - 4)));
                RecordRoomCells(zone, plannedCells, dorm);
                for (int x = dorm.minX + 1; x <= dorm.maxX - 1 && remainingBeds > 0; x += 2)
                {
                    IntVec3 bedCell = new IntVec3(x, 0, dorm.CenterCell.z + 1);
                    if (!zone.ContainsCell(bedCell))
                    {
                        continue;
                    }

                    RecordPlannedPlacement(zone, plannedCells, bedDef, bedCell, Rot4.North, includeInterior: true);
                    remainingBeds--;
                }
            }
        }

        private static void CollectFieldCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            foreach (FieldRowPlan row in GetFieldRows(zone))
            {
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    if (row.Cells[i].InBounds(zone.Map))
                    {
                        plannedCells.Add(row.Cells[i]);
                    }
                }
            }
        }

        private static void CollectCampCommonCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            IntVec3 center = zone.BestCenterCell;
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.CampfireDef(), center, Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TableDef(), center + new IntVec3(0, 0, -2), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, -3), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, -3), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TorchLampDef(), center + new IntVec3(-2, 0, 0), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TorchLampDef(), center + new IntVec3(2, 0, 0), Rot4.North, includeInterior: true);
        }

        private static void CollectFarmCommonCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            CollectCampCommonCells(zone, plannedCells);
            IntVec3 storageCell = GetStorageCell(zone);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TorchLampDef(), storageCell + new IntVec3(-1, 0, 0), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TorchLampDef(), storageCell + new IntVec3(1, 0, 0), Rot4.North, includeInterior: true);
        }

        private static void CollectTavernCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            CellRect room = InnerRoom(zone, Mathf.Min(11, zone.ZoneBounds.Width - 2), Mathf.Min(7, zone.ZoneBounds.Height - 4));
            RecordRoomCells(zone, plannedCells, room);
            IntVec3 center = room.CenterCell;
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TableDef(), center + new IntVec3(-2, 0, -1), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TableDef(), center + new IntVec3(2, 0, -1), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(-3, 0, -2), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, -2), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, -2), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(3, 0, -2), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.CampfireDef(), center + new IntVec3(0, 0, 1), Rot4.North, includeInterior: true);
        }

        private static void CollectInnCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            CellRect room = InnerRoom(zone, Mathf.Min(10, zone.ZoneBounds.Width - 2), Mathf.Min(8, zone.ZoneBounds.Height - 4));
            RecordRoomCells(zone, plannedCells, room);
            IntVec3 center = room.CenterCell;
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TableDef(), center + new IntVec3(0, 0, -1), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, -2), Rot4.South, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, -2), Rot4.South, includeInterior: true);
        }

        private static void CollectVillageCommonCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells)
        {
            CollectCampCommonCells(zone, plannedCells);
            IntVec3 center = zone.BestCenterCell;
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.TableDef(), center + new IntVec3(0, 0, 2), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(-1, 0, 1), Rot4.North, includeInterior: true);
            RecordPlannedPlacement(zone, plannedCells, SettlementDefResolver.ChairDef(), center + new IntVec3(1, 0, 1), Rot4.North, includeInterior: true);
        }

        private static void RecordRoomCells(Zone_Settlement zone, HashSet<IntVec3> plannedCells, CellRect room)
        {
            foreach (IntVec3 cell in room)
            {
                if (cell.InBounds(zone.Map) && zone.ContainsCell(cell))
                {
                    plannedCells.Add(cell);
                }
            }
        }

        private static void RecordPlannedPlacement(Zone_Settlement zone, HashSet<IntVec3> plannedCells, BuildableDef def, IntVec3 cell, Rot4 rotation, bool includeInterior)
        {
            if (zone == null || zone.Map == null || plannedCells == null || def == null || !cell.InBounds(zone.Map))
            {
                return;
            }

            CellRect occupiedRect = GenAdj.OccupiedRect(cell, rotation, def.Size);
            foreach (IntVec3 occupiedCell in occupiedRect)
            {
                if (occupiedCell.InBounds(zone.Map))
                {
                    plannedCells.Add(occupiedCell);
                }
            }

            if (!includeInterior || occupiedRect.Width <= 1 || occupiedRect.Height <= 1)
            {
                return;
            }

            for (int x = occupiedRect.minX + 1; x < occupiedRect.maxX; x++)
            {
                for (int z = occupiedRect.minZ + 1; z < occupiedRect.maxZ; z++)
                {
                    IntVec3 interiorCell = new IntVec3(x, 0, z);
                    if (interiorCell.InBounds(zone.Map))
                    {
                        plannedCells.Add(interiorCell);
                    }
                }
            }
        }

        private static bool CellHasNonRemovableFieldBlocker(Map map, IntVec3 cell)
        {
            if (!cell.Walkable(map))
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    if (thing is Plant || (thing.def != null && thing.def.EverHaulable))
                    {
                        continue;
                    }

                    return true;
                }
            }

            return cell.GetEdifice(map) != null;
        }

        private static bool CellHasPermanentStructureBlocker(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
            {
                return true;
            }

            if (cell.GetEdifice(map) != null)
            {
                return true;
            }

            bool hasRemovableBlocker = false;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (thing is Plant || (thing.def != null && thing.def.EverHaulable))
                {
                    hasRemovableBlocker = true;
                    continue;
                }

                if (thing.def.passability != Traversability.Standable)
                {
                    return true;
                }
            }

            if (!cell.Walkable(map) && !hasRemovableBlocker)
            {
                return true;
            }

            return false;
        }

        private static bool TryPlace(Zone_Settlement zone, BuildableDef def, IntVec3 cell, Rot4 rotation, ThingDef stuff = null)
        {
            if (zone == null || def == null || !cell.InBounds(zone.Map))
            {
                return false;
            }

            Faction faction = SettlementFactionUtility.ResolveFaction(zone);
            if (faction == null)
            {
                return false;
            }

            CellRect occupiedRect = GenAdj.OccupiedRect(cell, rotation, def.Size);
            if (!zone.ContainsAll(occupiedRect))
            {
                return false;
            }

            if (HasMatchingThingOrPlan(zone.Map, def, cell))
            {
                return false;
            }

            ThingDef thingDef = def as ThingDef;
            if (stuff == null && thingDef != null && thingDef.MadeFromStuff)
            {
                stuff = SettlementDefResolver.WoodStuff();
            }

            AcceptanceReport placementReport = GenConstruct.CanPlaceBlueprintAt(def, cell, rotation, zone.Map, false, null, null, stuff);
            if (!placementReport.Accepted)
            {
                return false;
            }

            GenConstruct.PlaceBlueprintForBuild(def, cell, zone.Map, rotation, faction, stuff);
            return true;
        }

        private static bool HasMatchingThingOrPlan(Map map, BuildableDef def, IntVec3 cell)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing.def == def || thing.def.entityDefToBuild == def || GenConstruct.BuiltDefOf(thing.def) == def)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
