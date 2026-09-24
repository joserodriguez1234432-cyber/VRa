using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Vehicles;

namespace VehicleRaidFramework
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.DetermineNextJob))]
    public static class Patch_InfantryAutoBoard
    {
        private const float NearEnemyRadius = VRF_TransportUtil.CombatNearRadius;

        public static void Postfix(Pawn_JobTracker __instance, ref ThinkResult __result)
        {
            Pawn pawn = __instance.pawn;

            if (pawn == null || pawn.Dead || pawn.Downed) return;
            if (pawn.Faction == null || pawn.Faction.IsPlayer) return;
            if (pawn is VehiclePawn) return;
            if (pawn.Map == null) return;

            Lord lord = pawn.GetLord();
            if (!(lord?.LordJob is LordJob_VehicleRaid)) return;

            DutyDef duty = pawn.mindState?.duty?.def;
            if (duty == null) return;
            if (duty != VRF_DutyDefOf.VRF_InfantryAssault_Transport &&
                duty.defName != "VRF_InfantryAssault_Transport") return;

            if (__result.Job == null) return;
            string jobDefName = __result.Job.def?.defName ?? "";
            if (jobDefName == "Board") return;
            if (jobDefName == "Mount") return;

            if (VRF_TransportUtil.HasEnemy(pawn, NearEnemyRadius)) return;
            if (VRF_TransportUtil.IsOnReboardCooldown(pawn)) return;

            VehiclePawn vehicle = FindTransportVehicle(pawn);
            if (vehicle == null) return;
            if (VRF_TransportUtil.HasEnemy(vehicle, VRF_TransportUtil.GetVehicleCombatRadius(vehicle))) return;

            VehicleRoleHandler handler = VRF_TransportUtil.GetPassengerHandler(vehicle, pawn);
            if (handler == null) return;
            if (!pawn.CanReach(vehicle, PathEndMode.Touch, Danger.Deadly)) return;

            JobDef boardJobDef = DefDatabase<JobDef>.GetNamed("Board", false);
            if (boardJobDef == null) return;

            pawn.jobs?.jobQueue?.EnqueueFirst(__result.Job);

            vehicle.GiveLoadJob(pawn, handler);
            Job boardJob = JobMaker.MakeJob(boardJobDef, vehicle);
            boardJob.expiryInterval = 3000;
            boardJob.locomotionUrgency = vehicle.Position.DistanceToSquared(pawn.Position) > 100f
                ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;

            __result = new ThinkResult(boardJob, __result.SourceNode, __result.Tag, false);
        }

        private static VehiclePawn FindTransportVehicle(Pawn pawn)
        {
            VehiclePawn best = null;
            float bestDist = float.MaxValue;
            foreach (Pawn p in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (!(p is VehiclePawn v)) continue;
                if (v.Faction != pawn.Faction) continue;
                if (VRF_TransportUtil.IsVehicleImmobilized(v)) continue;
                if (v.GetComp<VehicleRaid.CompVehicleHover>()?.IsAirborne == true) continue;
                if (!VRF_TransportUtil.IsTransportVehicle(v) && !VRF_TransportUtil.IsArmedTransportVehicle(v)) continue;
                if (!VRF_TransportUtil.HasAvailablePassengerSlots(v)) continue;
                if (!(v.GetLord()?.LordJob is LordJob_VehicleRaid)) continue;
                float dist = v.Position.DistanceToSquared(pawn.Position);
                if (dist < bestDist && dist <= VRF_TransportUtil.BoardSearchRadius * VRF_TransportUtil.BoardSearchRadius)
                {
                    bestDist = dist;
                    best = v;
                }
            }
            return best;
        }
    }
}
