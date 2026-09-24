using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Vehicles;

namespace VehicleRaidFramework
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.DetermineNextJob))]
    public static class Patch_NPCVehicleRepair
    {
        private const float EnemyDetectionRadius = 40f;
        private const float TriggerThreshold = 0.70f;

        [HarmonyPriority(Priority.Low)]
        public static void Postfix(Pawn_JobTracker __instance, ref ThinkResult __result)
        {
            Pawn pawn = __instance.pawn;

            if (pawn == null || pawn.Dead || pawn.Downed) return;
            if (pawn.Faction == null || pawn.Faction.IsPlayer) return;
            if (pawn is VehiclePawn) return;
            if (pawn.Map == null) return;
            if (pawn.ParentHolder is VehicleRoleHandler) return;

            Lord lord = pawn.GetLord();
            if (!(lord?.LordJob is LordJob_VehicleRaid)) return;

            if (__result.Job == null) return;
            string curJobName = __result.Job.def?.defName ?? "";
            if (curJobName == "VRF_RepairVehicle") return;
            if (curJobName == "Board") return;

            DutyDef duty = pawn.mindState?.duty?.def;
            if (duty == null) return;
            bool isAssaultDuty = duty == VRF_DutyDefOf.VRF_InfantryAssault ||
                                 duty.defName == "VRF_InfantryAssault" ||
                                 duty == VRF_DutyDefOf.VRF_InfantryAssault_Transport ||
                                 duty.defName == "VRF_InfantryAssault_Transport";
            if (!isAssaultDuty) return;

            if (VRF_TransportUtil.HasEnemy(pawn, EnemyDetectionRadius)) return;

            VehiclePawn target = FindRepairTarget(pawn, lord);
            if (target == null) return;

            IntVec3 standCell = FindStandCell(pawn, target);
            if (!standCell.IsValid) return;

            JobDef repairJobDef = DefDatabase<JobDef>.GetNamed("VRF_RepairVehicle", false);
            if (repairJobDef == null) return;

            Job repairJob = JobMaker.MakeJob(repairJobDef, target, standCell);

            __result = new ThinkResult(repairJob, __result.SourceNode, __result.Tag, false);
        }

        private static VehiclePawn FindRepairTarget(Pawn pawn, Lord lord)
        {
            VehiclePawn best = null;
            float bestDist = float.MaxValue;

            foreach (Pawn p in lord.ownedPawns)
            {
                if (!(p is VehiclePawn v)) continue;
                if (v.Dead || !v.Spawned || v.Map != pawn.Map) continue;
                if (v.Faction != pawn.Faction) continue;
                if (v.GetComp<VehicleRaid.CompVehicleHover>()?.IsAirborne == true) continue;
                if (v.vehiclePather != null && v.vehiclePather.Moving) continue;
                if (!VehicleNeedsRepair(v)) continue;
                if (!pawn.CanReach(v, PathEndMode.Touch, Danger.Deadly)) continue;

                float dist = v.Position.DistanceToSquared(pawn.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = v;
                }
            }

            return best;
        }

        private static bool VehicleNeedsRepair(VehiclePawn vehicle)
        {
            if (vehicle.statHandler?.components == null) return false;
            foreach (var component in vehicle.statHandler.components)
            {
                if (component.HealthPercent < TriggerThreshold)
                    return true;
            }
            return false;
        }

        private static IntVec3 FindStandCell(Pawn pawn, VehiclePawn vehicle)
        {
            Map map = vehicle.Map;
            CellRect occupied = vehicle.OccupiedRect();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(vehicle.Position, 3, false))
            {
                if (!cell.InBounds(map)) continue;
                if (!cell.Standable(map)) continue;
                if (occupied.Contains(cell)) continue;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
                return cell;
            }

            return IntVec3.Invalid;
        }
    }
}
