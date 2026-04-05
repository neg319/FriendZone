using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace FriendZone
{
    public static class SettlementLayoutPlanner
    {
        public static void EnsureLayout(Zone_Settlement zone, int requiredBeds)
        {
            if (zone == null || zone.Map == null || zone.cells == null || zone.cells.Count < 9)
            {
                return;
            }

            SettlementFactionUtility.ResolveFaction(zone);
            requiredBeds = Mathf.Max(1, requiredBeds);

            GenerateSharedStructures(zone);
            EnsureHousing(zone, requiredBeds);
            EnsureFieldMarkers(zone);
            zone.planGenerated = true;
        }

        public static IEnumerable<IntVec3> GetFieldCells(Zone_Settlement zone)
        {
            if (zone == null || zone.Map == null || zone.cells == null || zone.cells.Count == 0)
            {
                yield break;
            }

            CellRect bounds = zone.ZoneBounds;
            int fieldHeight = Mathf.Max(3, bounds.Height / 3);
            int minZ = bounds.minZ + 1;
            int maxZ = Mathf.Min(bounds.maxZ - 1, minZ + fieldHeight - 1);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = bounds.minX + 1; x <= bounds.maxX - 1; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(zone.Map) || !zone.ContainsCell(cell) || !cell.Standable(zone.Map))
                    {
                        continue;
                    }

                    Building edifice = cell.GetEdifice(zone.Map);
                    if (edifice != null)
                    {
                        continue;
                    }

                    yield return cell;
                }
            }
        }

        public static IntVec3 GetStorageCell(Zone_Settlement zone)
        {
            if (zone == null || zone.Map == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 center = zone.BestCenterCell;
            if (center.IsValid && center.InBounds(zone.Map) && zone.ContainsCell(center) && center.Standable(zone.Map) && center.GetEdifice(zone.Map) == null)
            {
                return center;
            }

            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;
            foreach (IntVec3 cell in zone.cells)
            {
                if (!cell.InBounds(zone.Map) || !cell.Standable(zone.Map) || cell.GetEdifice(zone.Map) != null)
                {
                    continue;
                }

                int distance = cell.DistanceToSquared(zone.BestCenterCell);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }

            return best;
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
                        count += Mathf.Max(1, thing.def.size.x * thing.def.size.z >= 2 ? 1 : 1);
                    }
                }
            }

            return count;
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
            ThingDef wood = SettlementDefResolver.WoodStuff();
            foreach (IntVec3 cell in GetFieldCells(zone).Take(4))
            {
                if (cell.GetEdifice(zone.Map) == null)
                {
                    TryPlace(zone, SettlementDefResolver.TorchLampDef(), cell, Rot4.North, wood);
                    break;
                }
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
            if (zone.settlementKind != SettlementKind.Farm && zone.settlementKind != SettlementKind.Village && zone.settlementKind != SettlementKind.Inn && zone.settlementKind != SettlementKind.Tavern && zone.settlementKind != SettlementKind.Camp)
            {
                return;
            }

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
