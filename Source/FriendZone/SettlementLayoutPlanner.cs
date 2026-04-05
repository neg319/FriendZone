using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

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

            CellRect room = MainRoom(zone);
            EnsureMainRoom(zone, room);
            EnsureSettlementFurniture(zone, room);
            EnsureHousing(zone, room, requiredBeds);
            EnsureStorageContainer(zone);
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
                foreach (IntVec3 cell in row.Cells)
                {
                    yield return cell;
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

            CellRect room = MainRoom(zone);
            AddRectCells(zone, plannedCells, room, false);

            AddBuildableCells(zone, plannedCells, SettlementDefResolver.CampfireDef(), OutsideMainDoor(zone, 2), Rot4.North, true);
            IntVec3 storageCell = PreferredStorageCell(zone);
            AddBuildableCells(zone, plannedCells, SettlementDefResolver.ShelfDef(), storageCell, Rot4.North, true);

            foreach (IntVec3 cell in GetFurnitureCells(zone, room))
            {
                if (cell.InBounds(zone.Map))
                {
                    plannedCells.Add(cell);
                }
            }

            foreach (IntVec3 cell in GetBedCells(room, zone.settlementKind).Take(Mathf.Max(1, requiredBeds)))
            {
                if (cell.InBounds(zone.Map))
                {
                    plannedCells.Add(cell);
                }
            }

            foreach (IntVec3 cell in GetFieldCells(zone))
            {
                if (cell.InBounds(zone.Map))
                {
                    plannedCells.Add(cell);
                }
            }

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

        private static IntVec3 PreferredStorageCell(Zone_Settlement zone)
        {
            if (zone == null || zone.Map == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 preferred = OutsideMainDoor(zone, 3) + new IntVec3(2, 0, 0);
            return FindExternalCellNear(zone, preferred);
        }

        public static IntVec3 GetStorageCell(Zone_Settlement zone)
        {
            if (zone == null || zone.Map == null)
            {
                return IntVec3.Invalid;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(zone.BestCenterCell, 16f, true))
            {
                if (!cell.InBounds(zone.Map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(zone.Map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing is Building_Storage && (thing.Faction == null || thing.Faction == zone.settlementFaction))
                    {
                        return cell;
                    }
                }
            }

            return PreferredStorageCell(zone);
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
                    if (thing == null || counted.Contains(thing))
                    {
                        continue;
                    }

                    if (thing.def == bedDef || thing.def.entityDefToBuild == bedDef || GenConstruct.BuiltDefOf(thing.def) == bedDef)
                    {
                        counted.Add(thing);
                        count++;
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
                        cells.Add(new IntVec3(x, 0, startZ + step));
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
                        cells.Add(new IntVec3(startX + step, 0, z));
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
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(zone.Map) || zone.ContainsCell(cell) || cell.Fogged(zone.Map) || cell.Roofed(zone.Map))
                {
                    return false;
                }

                if (CellHasPermanentBlocker(zone.Map, cell) || IsReservedExteriorCell(zone, cell))
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


        private static bool IsReservedExteriorCell(Zone_Settlement zone, IntVec3 cell)
        {
            if (zone == null || zone.Map == null || !cell.InBounds(zone.Map))
            {
                return true;
            }

            IntVec3 campfireCell = OutsideMainDoor(zone, 2);
            if (cell == campfireCell)
            {
                return true;
            }

            IntVec3 storageCell = PreferredStorageCell(zone);
            if (storageCell.IsValid)
            {
                ThingDef shelfDef = SettlementDefResolver.ShelfDef();
                IntVec2 shelfSize = shelfDef != null ? shelfDef.Size : IntVec2.One;
                CellRect storageRect = GenAdj.OccupiedRect(storageCell, Rot4.North, shelfSize);
                if (storageRect.Contains(cell))
                {
                    return true;
                }
            }

            return false;
        }

        private static float ScoreFieldRows(Zone_Settlement zone, List<FieldRowPlan> rows)
        {
            if (zone == null || rows == null || rows.Count == 0)
            {
                return float.MinValue;
            }

            float score = rows.Count * 1000f;
            foreach (FieldRowPlan row in rows)
            {
                foreach (IntVec3 cell in row.Cells)
                {
                    TerrainDef terrain = cell.GetTerrain(zone.Map);
                    score += terrain != null ? terrain.fertility : 0f;
                    score -= cell.DistanceTo(zone.BestCenterCell) * 0.05f;
                }
            }

            return score;
        }

        private static CellRect MainRoom(Zone_Settlement zone)
        {
            CellRect bounds = zone.ZoneBounds;
            int desiredWidth;
            int desiredHeight;
            switch (zone.settlementKind)
            {
                case SettlementKind.Camp:
                    desiredWidth = 7;
                    desiredHeight = 7;
                    break;
                case SettlementKind.Farm:
                    desiredWidth = 9;
                    desiredHeight = 7;
                    break;
                case SettlementKind.Tavern:
                    desiredWidth = 11;
                    desiredHeight = 9;
                    break;
                case SettlementKind.Inn:
                    desiredWidth = 11;
                    desiredHeight = 9;
                    break;
                case SettlementKind.Village:
                    desiredWidth = 13;
                    desiredHeight = 9;
                    break;
                default:
                    desiredWidth = 9;
                    desiredHeight = 7;
                    break;
            }

            int width = Mathf.Clamp(desiredWidth, 5, Mathf.Max(5, bounds.Width - 2));
            int height = Mathf.Clamp(desiredHeight, 5, Mathf.Max(5, bounds.Height - 2));
            int minX = Mathf.Clamp(bounds.CenterCell.x - width / 2, bounds.minX, bounds.maxX - width + 1);
            int minZ = Mathf.Clamp(bounds.CenterCell.z - height / 2, bounds.minZ, bounds.maxZ - height + 1);
            minX = Mathf.Clamp(minX, 0, zone.Map.Size.x - width);
            minZ = Mathf.Clamp(minZ, 0, zone.Map.Size.z - height);
            return new CellRect(minX, minZ, width, height);
        }

        private static void EnsureMainRoom(Zone_Settlement zone, CellRect room)
        {
            ThingDef wall = SettlementDefResolver.WallDef();
            ThingDef door = SettlementDefResolver.DoorDef();
            ThingDef wallStuff = SettlementDefResolver.StoneBlocksDef() ?? SettlementDefResolver.WoodStuff();
            ThingDef doorStuff = SettlementDefResolver.WoodStuff();
            if (wall == null)
            {
                return;
            }

            int doorX = room.CenterCell.x;
            for (int x = room.minX; x <= room.maxX; x++)
            {
                IntVec3 north = new IntVec3(x, 0, room.maxZ);
                IntVec3 south = new IntVec3(x, 0, room.minZ);
                EnsureBuildable(zone, wall, north, Rot4.North, wallStuff);
                if (x == doorX && door != null)
                {
                    EnsureBuildable(zone, door, south, Rot4.North, doorStuff);
                }
                else
                {
                    EnsureBuildable(zone, wall, south, Rot4.North, wallStuff);
                }
            }

            for (int z = room.minZ + 1; z < room.maxZ; z++)
            {
                EnsureBuildable(zone, wall, new IntVec3(room.minX, 0, z), Rot4.North, wallStuff);
                EnsureBuildable(zone, wall, new IntVec3(room.maxX, 0, z), Rot4.North, wallStuff);
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

        private static void EnsureSettlementFurniture(Zone_Settlement zone, CellRect room)
        {
            ThingDef wood = SettlementDefResolver.WoodStuff();
            ThingDef table = SettlementDefResolver.TableDef();
            ThingDef chair = SettlementDefResolver.ChairDef();
            ThingDef torch = SettlementDefResolver.TorchLampDef();
            IntVec3 center = room.CenterCell;

            if (zone.settlementKind == SettlementKind.Camp || zone.settlementKind == SettlementKind.Farm)
            {
                EnsureBuildable(zone, table, center + new IntVec3(0, 0, -1), Rot4.North, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-1, 0, -2), Rot4.East, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(1, 0, -2), Rot4.West, wood);
            }
            else if (zone.settlementKind == SettlementKind.Tavern)
            {
                EnsureBuildable(zone, table, center + new IntVec3(-2, 0, 0), Rot4.North, wood);
                EnsureBuildable(zone, table, center + new IntVec3(2, 0, 0), Rot4.North, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-3, 0, -1), Rot4.East, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-1, 0, -1), Rot4.West, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(1, 0, -1), Rot4.East, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(3, 0, -1), Rot4.West, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-2, 0, 1), Rot4.North, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(2, 0, 1), Rot4.North, wood);
            }
            else if (zone.settlementKind == SettlementKind.Inn)
            {
                EnsureBuildable(zone, table, center + new IntVec3(0, 0, -1), Rot4.North, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-1, 0, -2), Rot4.East, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(1, 0, -2), Rot4.West, wood);
                EnsureBuildable(zone, torch, new IntVec3(room.minX + 1, 0, room.minZ + 1), Rot4.North, wood);
                EnsureBuildable(zone, torch, new IntVec3(room.maxX - 1, 0, room.minZ + 1), Rot4.North, wood);
            }
            else
            {
                EnsureBuildable(zone, table, center + new IntVec3(0, 0, -1), Rot4.North, wood);
                EnsureBuildable(zone, table, center + new IntVec3(0, 0, 1), Rot4.North, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-1, 0, -2), Rot4.East, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(1, 0, -2), Rot4.West, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(-1, 0, 2), Rot4.East, wood);
                EnsureBuildable(zone, chair, center + new IntVec3(1, 0, 2), Rot4.West, wood);
                EnsureBuildable(zone, torch, new IntVec3(room.minX + 1, 0, room.minZ + 1), Rot4.North, wood);
                EnsureBuildable(zone, torch, new IntVec3(room.maxX - 1, 0, room.minZ + 1), Rot4.North, wood);
            }

            EnsureBuildable(zone, SettlementDefResolver.CampfireDef(), OutsideMainDoor(zone, 2), Rot4.North, null, true);
        }

        private static IEnumerable<IntVec3> GetFurnitureCells(Zone_Settlement zone, CellRect room)
        {
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            IntVec3 center = room.CenterCell;
            void Add(IntVec3 cell)
            {
                if (cell.InBounds(zone.Map))
                {
                    cells.Add(cell);
                }
            }

            Add(center + new IntVec3(0, 0, -1));
            Add(OutsideMainDoor(zone, 2));
            switch (zone.settlementKind)
            {
                case SettlementKind.Camp:
                case SettlementKind.Farm:
                    Add(center + new IntVec3(-1, 0, -2));
                    Add(center + new IntVec3(1, 0, -2));
                    break;
                case SettlementKind.Tavern:
                    Add(center + new IntVec3(-2, 0, 0));
                    Add(center + new IntVec3(2, 0, 0));
                    Add(center + new IntVec3(-3, 0, -1));
                    Add(center + new IntVec3(-1, 0, -1));
                    Add(center + new IntVec3(1, 0, -1));
                    Add(center + new IntVec3(3, 0, -1));
                    Add(center + new IntVec3(-2, 0, 1));
                    Add(center + new IntVec3(2, 0, 1));
                    break;
                case SettlementKind.Inn:
                    Add(center + new IntVec3(-1, 0, -2));
                    Add(center + new IntVec3(1, 0, -2));
                    Add(new IntVec3(room.minX + 1, 0, room.minZ + 1));
                    Add(new IntVec3(room.maxX - 1, 0, room.minZ + 1));
                    break;
                case SettlementKind.Village:
                    Add(center + new IntVec3(0, 0, 1));
                    Add(center + new IntVec3(-1, 0, -2));
                    Add(center + new IntVec3(1, 0, -2));
                    Add(center + new IntVec3(-1, 0, 2));
                    Add(center + new IntVec3(1, 0, 2));
                    Add(new IntVec3(room.minX + 1, 0, room.minZ + 1));
                    Add(new IntVec3(room.maxX - 1, 0, room.minZ + 1));
                    break;
            }

            return cells;
        }

        private static void EnsureHousing(Zone_Settlement zone, CellRect room, int requiredBeds)
        {
            int remainingBeds = Mathf.Max(0, requiredBeds - CountExistingOrPlannedBeds(zone));
            if (remainingBeds <= 0)
            {
                return;
            }

            ThingDef bedDef = SettlementDefResolver.BedDef();
            ThingDef wood = SettlementDefResolver.WoodStuff();
            if (bedDef == null)
            {
                return;
            }

            foreach (IntVec3 bedCell in GetBedCells(room, zone.settlementKind))
            {
                if (remainingBeds <= 0)
                {
                    break;
                }

                if (EnsureBuildable(zone, bedDef, bedCell, Rot4.North, wood))
                {
                    remainingBeds--;
                }
            }
        }

        private static IEnumerable<IntVec3> GetBedCells(CellRect room, SettlementKind kind)
        {
            HashSet<IntVec3> yielded = new HashSet<IntVec3>();

            void AddIfValid(IntVec3 cell)
            {
                if (cell.x <= room.minX || cell.x >= room.maxX || cell.z <= room.minZ || cell.z >= room.maxZ)
                {
                    return;
                }

                yielded.Add(cell);
            }

            for (int x = room.minX + 1; x <= room.maxX - 1; x += 3)
            {
                AddIfValid(new IntVec3(x, 0, room.maxZ - 2));
            }

            if (kind == SettlementKind.Inn || kind == SettlementKind.Village || kind == SettlementKind.Tavern)
            {
                for (int z = room.maxZ - 4; z >= room.minZ + 2; z -= 3)
                {
                    AddIfValid(new IntVec3(room.minX + 1, 0, z));
                    AddIfValid(new IntVec3(room.maxX - 1, 0, z));
                }
            }
            else
            {
                AddIfValid(new IntVec3(room.minX + 1, 0, room.CenterCell.z + 1));
                AddIfValid(new IntVec3(room.maxX - 1, 0, room.CenterCell.z + 1));
            }

            return yielded;
        }

        private static void EnsureStorageContainer(Zone_Settlement zone)
        {
            ThingDef shelfDef = SettlementDefResolver.ShelfDef();
            if (shelfDef == null)
            {
                return;
            }

            ThingDef stuff = shelfDef.MadeFromStuff ? (SettlementDefResolver.WoodStuff() ?? SettlementDefResolver.StoneBlocksDef()) : null;
            EnsureBuildable(zone, shelfDef, GetStorageCell(zone), Rot4.North, stuff, true);
        }

        private static IntVec3 MainDoorCell(Zone_Settlement zone)
        {
            CellRect room = MainRoom(zone);
            return new IntVec3(room.CenterCell.x, 0, room.minZ);
        }

        private static IntVec3 OutsideMainDoor(Zone_Settlement zone, int distance)
        {
            IntVec3 door = MainDoorCell(zone);
            return new IntVec3(door.x, 0, door.z - Mathf.Max(1, distance));
        }

        private static IntVec3 FindExternalCellNear(Zone_Settlement zone, IntVec3 preferredCell)
        {
            if (zone == null || zone.Map == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;
            for (int radius = 0; radius <= 12; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(preferredCell, radius, true))
                {
                    if (!cell.InBounds(zone.Map) || zone.ContainsCell(cell))
                    {
                        continue;
                    }

                    if (CellHasPermanentBlocker(zone.Map, cell))
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

                if (best.IsValid)
                {
                    return best;
                }
            }

            return best;
        }

        private static void AddRectCells(Zone_Settlement zone, HashSet<IntVec3> cells, CellRect rect, bool allowOutsideZone)
        {
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(zone.Map))
                {
                    continue;
                }

                if (!allowOutsideZone && !zone.ContainsCell(cell))
                {
                    continue;
                }

                cells.Add(cell);
            }
        }

        private static void AddBuildableCells(Zone_Settlement zone, HashSet<IntVec3> cells, BuildableDef def, IntVec3 position, Rot4 rotation, bool allowOutsideZone)
        {
            if (zone == null || zone.Map == null || def == null || !position.InBounds(zone.Map))
            {
                return;
            }

            CellRect occupied = GenAdj.OccupiedRect(position, rotation, def.Size);
            foreach (IntVec3 cell in occupied)
            {
                if (!cell.InBounds(zone.Map))
                {
                    continue;
                }

                if (allowOutsideZone || zone.ContainsCell(cell))
                {
                    cells.Add(cell);
                }
            }
        }

        private static bool CellHasPermanentBlocker(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
            {
                return true;
            }

            if (cell.GetEdifice(map) != null)
            {
                return true;
            }

            bool hasRemovable = false;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (thing is Plant || IsChunk(thing))
                {
                    hasRemovable = true;
                    continue;
                }

                if (thing.def != null && !thing.def.EverHaulable && thing.def.passability != Traversability.Standable)
                {
                    return true;
                }
            }

            return !cell.Walkable(map) && !hasRemovable;
        }

        private static bool HasUnclearedRemovableBlocker(Map map, CellRect rect)
        {
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    return true;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    if (thing is Plant || IsChunk(thing))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsChunk(Thing thing)
        {
            return thing != null
                && thing.def != null
                && thing.def.EverHaulable
                && thing.def.defName != null
                && thing.def.defName.IndexOf("Chunk", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EnsureBuildable(Zone_Settlement zone, BuildableDef def, IntVec3 cell, Rot4 rotation, ThingDef stuff = null, bool allowOutsideZone = false)
        {
            if (zone == null || zone.Map == null || zone.settlementFaction == null || def == null || !cell.InBounds(zone.Map))
            {
                return false;
            }

            CellRect occupiedRect = GenAdj.OccupiedRect(cell, rotation, def.Size);
            foreach (IntVec3 occupiedCell in occupiedRect)
            {
                if (!occupiedCell.InBounds(zone.Map))
                {
                    return false;
                }

                if (!allowOutsideZone && !zone.ContainsCell(occupiedCell))
                {
                    return false;
                }
            }

            ThingDef thingDef = def as ThingDef;
            if (thingDef != null && thingDef.MadeFromStuff && stuff == null)
            {
                stuff = SettlementDefResolver.DefaultStuffFor(thingDef);
            }

            if (HasMatchingThingOrPlan(zone.Map, def, occupiedRect))
            {
                return false;
            }

            if (HasUnclearedRemovableBlocker(zone.Map, occupiedRect))
            {
                return false;
            }

            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(def, cell, rotation, zone.Map, false, null, null, stuff);
            if (!report.Accepted)
            {
                return false;
            }

            GenConstruct.PlaceBlueprintForBuild(def, cell, zone.Map, rotation, zone.settlementFaction, stuff);
            return true;
        }

        private static bool HasMatchingThingOrPlan(Map map, BuildableDef def, CellRect occupiedRect)
        {
            foreach (IntVec3 cell in occupiedRect)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null)
                    {
                        continue;
                    }

                    if (thing.def == def || thing.def.entityDefToBuild == def || GenConstruct.BuiltDefOf(thing.def) == def)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
