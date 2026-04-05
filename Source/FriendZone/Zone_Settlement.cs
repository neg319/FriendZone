using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace FriendZone
{
    public class Zone_Settlement : Zone
    {
        public SettlementKind settlementKind = SettlementKind.Camp;
        public bool settlementEnabled = true;
        public bool planGenerated;
        public bool starterSuppliesDelivered;
        public int nextSettlerSpawnTick = -1;
        public List<Pawn> settlers = new List<Pawn>();
        public Faction settlementFaction;
        public Dictionary<string, int> lastKnownResourceTotals = new Dictionary<string, int>();
        public Dictionary<string, float> rentCarry = new Dictionary<string, float>();

        private List<string> lastKnownResourceTotalKeys;
        private List<int> lastKnownResourceTotalValues;
        private List<string> rentCarryKeys;
        private List<float> rentCarryValues;

        public override bool IsMultiselectable => true;

        protected override Color NextZoneColor => ZoneColorUtility.NextGrowingZoneColor();

        public Zone_Settlement()
        {
        }

        public Zone_Settlement(ZoneManager zoneManager)
            : base("FriendZone".Translate(), zoneManager)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref settlementKind, "settlementKind", SettlementKind.Camp);
            Scribe_Values.Look(ref settlementEnabled, "settlementEnabled", true);
            Scribe_Values.Look(ref planGenerated, "planGenerated", false);
            Scribe_Values.Look(ref starterSuppliesDelivered, "starterSuppliesDelivered", false);
            Scribe_Values.Look(ref nextSettlerSpawnTick, "nextSettlerSpawnTick", -1);
            Scribe_Collections.Look(ref settlers, "settlers", LookMode.Reference);
            Scribe_References.Look(ref settlementFaction, "settlementFaction");
            Scribe_Collections.Look(ref lastKnownResourceTotals, "lastKnownResourceTotals", LookMode.Value, LookMode.Value, ref lastKnownResourceTotalKeys, ref lastKnownResourceTotalValues);
            Scribe_Collections.Look(ref rentCarry, "rentCarry", LookMode.Value, LookMode.Value, ref rentCarryKeys, ref rentCarryValues);

            if (settlers == null)
            {
                settlers = new List<Pawn>();
            }

            if (lastKnownResourceTotals == null)
            {
                lastKnownResourceTotals = new Dictionary<string, int>();
            }

            if (rentCarry == null)
            {
                rentCarry = new Dictionary<string, float>();
            }
        }

        public int ActiveSettlerCount
        {
            get
            {
                CleanupSettlers();
                return settlers.Count;
            }
        }

        public int DesiredSettlerCount
        {
            get
            {
                int areaScaledTarget = Mathf.Max(1, cells.Count / 18);
                return Mathf.Min(settlementKind.BaseSettlerTarget(), areaScaledTarget + 1);
            }
        }

        public CellRect ZoneBounds
        {
            get
            {
                if (cells == null || cells.Count == 0)
                {
                    return CellRect.Empty;
                }

                int minX = cells[0].x;
                int maxX = cells[0].x;
                int minZ = cells[0].z;
                int maxZ = cells[0].z;

                for (int i = 1; i < cells.Count; i++)
                {
                    IntVec3 cell = cells[i];
                    if (cell.x < minX) minX = cell.x;
                    if (cell.x > maxX) maxX = cell.x;
                    if (cell.z < minZ) minZ = cell.z;
                    if (cell.z > maxZ) maxZ = cell.z;
                }

                return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
            }
        }

        public IntVec3 BestCenterCell
        {
            get
            {
                if (cells == null || cells.Count == 0)
                {
                    return IntVec3.Invalid;
                }

                CellRect bounds = ZoneBounds;
                IntVec3 center = bounds.CenterCell;
                if (ContainsCell(center))
                {
                    return center;
                }

                IntVec3 closest = cells[0];
                int bestDistance = center.DistanceToSquared(closest);
                for (int i = 1; i < cells.Count; i++)
                {
                    int distance = center.DistanceToSquared(cells[i]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        closest = cells[i];
                    }
                }

                return closest;
            }
        }

        public override string GetInspectString()
        {
            CleanupSettlers();

            string text = "FriendZoneTypeInspect".Translate(settlementKind.LabelFor());
            text += "\n" + "FriendZonePopulationInspect".Translate(ActiveSettlerCount, DesiredSettlerCount);
            text += "\n" + (settlementEnabled ? "FriendZoneStatusEnabled".Translate() : "FriendZoneStatusDisabled".Translate());
            text += "\n" + "FriendZoneRentInspect".Translate();

            if (settlementFaction != null)
            {
                text += "\n" + "FriendZoneFactionInspect".Translate(settlementFaction.Name);
            }

            if (!planGenerated)
            {
                text += "\n" + "FriendZonePlanPending".Translate();
            }

            return text;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = "FriendZoneChooseTypeLabel".Translate(),
                defaultDesc = "FriendZoneChooseTypeDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/SettlementType", true),
                action = OpenTypeMenu
            };

            yield return new Command_Toggle
            {
                defaultLabel = "FriendZoneEnableLabel".Translate(),
                defaultDesc = "FriendZoneEnableDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/SettlementToggle", true),
                isActive = () => settlementEnabled,
                toggleAction = delegate
                {
                    settlementEnabled = !settlementEnabled;
                    if (settlementEnabled && nextSettlerSpawnTick < 0)
                    {
                        nextSettlerSpawnTick = Find.TickManager.TicksGame + 600;
                    }
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "FriendZoneRefreshPlanLabel".Translate(),
                defaultDesc = "FriendZoneRefreshPlanDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/SettlementRefresh", true),
                action = delegate
                {
                    ResetPlanState();
                    Messages.Message("FriendZonePlanRefreshQueued".Translate(label), MessageTypeDefOf.TaskCompletion, false);
                }
            };
        }

        public override IEnumerable<Gizmo> GetZoneAddGizmos()
        {
            yield return DesignatorUtility.FindAllowedDesignator<Designator_ZoneAdd_Settlement>();
        }

        public void NotifySettlerJoined(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            if (settlers == null)
            {
                settlers = new List<Pawn>();
            }

            if (!settlers.Contains(pawn))
            {
                settlers.Add(pawn);
            }
        }

        public bool ContainsAll(CellRect rect)
        {
            foreach (IntVec3 cell in rect)
            {
                if (!ContainsCell(cell))
                {
                    return false;
                }
            }

            return true;
        }

        public void CleanupSettlers()
        {
            if (settlers == null)
            {
                settlers = new List<Pawn>();
                return;
            }

            settlers.RemoveAll(delegate(Pawn pawn)
            {
                return pawn == null || pawn.Destroyed || pawn.Dead || pawn.Map != Map || (settlementFaction != null && pawn.Faction != settlementFaction);
            });
        }

        public void ResetPlanState()
        {
            planGenerated = false;
            starterSuppliesDelivered = false;
            nextSettlerSpawnTick = Find.TickManager.TicksGame + 300;
            lastKnownResourceTotals.Clear();
            rentCarry.Clear();
        }

        private void OpenTypeMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (SettlementKind kind in SettlementLabelUtility.AllKinds)
            {
                SettlementKind capturedKind = kind;
                string labelText = kind.LabelFor();
                if (capturedKind == settlementKind)
                {
                    labelText += " ✓";
                }

                options.Add(new FloatMenuOption(labelText, delegate
                {
                    settlementKind = capturedKind;
                    ResetPlanState();
                    Messages.Message("FriendZoneTypeChanged".Translate(label, capturedKind.LabelFor()), MessageTypeDefOf.TaskCompletion, false);
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
