using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using RimWorld;
using Vehicles;

namespace VehicleRaidFramework
{
    [HarmonyPatch(typeof(VehiclePathFollower), nameof(VehiclePathFollower.PatherTick))]
    public static class Patch_VehicleNPCPathUpdate
    {
        [HarmonyPostfix]
        public static void Postfix(VehiclePathFollower __instance, VehiclePawn ___vehicle)
        {
            if (___vehicle == null || !___vehicle.Spawned || ___vehicle.Map == null) return;
            if (___vehicle.Faction == null || ___vehicle.Faction.IsPlayer) return;
            if (!__instance.Moving) return;
            if (___vehicle.CurJobDef != JobDefOf.Goto) return;
            if (___vehicle.VehicleDef.type == VehicleType.Sea) return;
            if (!(___vehicle.GetLord()?.LordJob is LordJob_VehicleRaid)) return;
            if (__instance.RequestStatus == VehiclePathFollower.PathRequestStatus.Calculating) return;

            if (!___vehicle.IsHashIntervalTick(60)) return;

            Thing enemy = FindNearestEnemy(___vehicle);
            if (enemy == null || enemy.Destroyed || !enemy.Spawned) return;

            LocalTargetInfo currentDest = __instance.Destination;
            if (!currentDest.IsValid) return;

            float maxRange = ___vehicle.CompVehicleTurrets?.MaxRange ?? 60f;
            float minRange = ___vehicle.CompVehicleTurrets?.MinRange ?? 0f;

            bool isMortar = false;
            if (___vehicle.CompVehicleTurrets != null && ___vehicle.CompVehicleTurrets.Turrets != null)
            {
                foreach (var turret in ___vehicle.CompVehicleTurrets.Turrets)
                {
                    if (turret.ProjectileDef?.projectile?.flyOverhead == true)
                    {
                        isMortar = true;
                        break;
                    }
                }
            }

            bool isTransporting = ___vehicle.AllPawnsAboard.Any(p =>
            {
                if (p.Dead || p.Downed) return false;
                var h = ___vehicle.handlers.FirstOrDefault(hh => hh.thingOwner.Contains(p));
                return h?.role != null && (h.role.HandlingTypes & HandlingType.Movement) == 0 &&
                       (h.role.HandlingTypes & HandlingType.Turret) == 0;
            });

            if (isTransporting)
            {
                maxRange = 36f;
                minRange = 10f;
            }

            float currentDistToEnemy = ___vehicle.Position.DistanceTo(enemy.Position);
            bool currentPosValid = currentDistToEnemy >= minRange && currentDistToEnemy <= maxRange
                                   && (isMortar || GenSight.LineOfSight(___vehicle.Position, enemy.Position, ___vehicle.Map));

            if (currentPosValid)
            {
                ___vehicle.jobs.EndCurrentJob(JobCondition.Succeeded);
                return;
            }

            float destToEnemy = currentDest.Cell.DistanceTo(enemy.Position);
            bool destStillValid = destToEnemy >= minRange && destToEnemy <= maxRange
                                  && (isMortar || GenSight.LineOfSight(currentDest.Cell, enemy.Position, ___vehicle.Map));

            if (destStillValid) return;

            if (currentDistToEnemy > maxRange + 15f && currentDistToEnemy > 80f)
            {
                return;
            }

            float idealRange = Mathf.Clamp(maxRange * 0.7f, minRange + 3f, maxRange - 2f);
            if (isTransporting) idealRange = 32f;

            IntVec3 newDest = FindPositionAtIdealRange(___vehicle, enemy, idealRange, maxRange, minRange, isMortar);
            if (!newDest.IsValid) return;
            if (currentDest.Cell.DistanceToSquared(newDest) < 225 && !currentDest.HasThing) return;

            Job newGotoJob = JobMaker.MakeJob(JobDefOf.Goto, newDest);
            newGotoJob.expiryInterval = 2000;
            newGotoJob.checkOverrideOnExpire = true;
            ___vehicle.jobs.StartJob(newGotoJob, JobCondition.InterruptForced);
        }

        private static Thing FindNearestEnemy(VehiclePawn vehicle)
        {
            var targets = vehicle.Map.attackTargetsCache.GetPotentialTargetsFor(vehicle);
            if (targets == null || targets.Count == 0) return null;
            Thing best = null;
            float bestDist = float.MaxValue;
            foreach (var t in targets)
            {
                if (t.ThreatDisabled(vehicle)) continue;
                if (!AttackTargetFinder.IsAutoTargetable(t)) continue;
                Thing thing = t.Thing;
                if (thing == null || thing.Destroyed || thing.Map == null) continue;
                if (thing.Map.fogGrid.IsFogged(thing.Position)) continue;
                if (thing is Pawn p && (p.Dead || p.Downed)) continue;
                float d = thing.Position.DistanceToSquared(vehicle.Position);
                if (d < bestDist) { bestDist = d; best = thing; }
            }
            return best;
        }

        private static IntVec3 FindPositionAtIdealRange(VehiclePawn vehicle, Thing target, float idealRange, float maxRange, float minRange, bool isMortar)
        {
            Map map = vehicle.Map;
            
            float searchRadius = Mathf.Min(maxRange, 80f);
            float searchMin = Mathf.Min(minRange, searchRadius - 5f);
            if (searchMin < 0) searchMin = 0;
            float searchIdeal = Mathf.Clamp(idealRange, searchMin + 3f, searchRadius - 2f);

            var allyRects = new List<CellRect>();
            var allyDestinations = new List<KeyValuePair<IntVec3, int>>();

            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p is VehiclePawn v && v != vehicle && v.Faction == vehicle.Faction)
                {
                    int vSize = Mathf.Max(v.def.size.x, v.def.size.z);
                    allyRects.Add(v.OccupiedRect().ExpandedBy(3));

                    if (v.CurJob != null && v.CurJob.def == JobDefOf.Goto && v.CurJob.targetA.IsValid)
                    {
                        allyDestinations.Add(new KeyValuePair<IntVec3, int>(v.CurJob.targetA.Cell, vSize));
                    }
                }
            }

            var candidates = new List<KeyValuePair<IntVec3, float>>();
            int validCellsFound = 0;
            float skipRate = (searchRadius > 40f) ? 0.7f : 0.0f; 

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(target.Position, searchRadius, true))
            {
                if (!cell.InBounds(map)) continue;
                
                float distToTarget = cell.DistanceTo(target.Position);
                if (distToTarget < searchMin || distToTarget > searchRadius) continue;

                if (Rand.Value < skipRate) continue;

                if (!cell.Standable(map)) continue;
                if (!isMortar && !GenSight.LineOfSight(cell, target.Position, map)) continue;

                bool insideAlly = false;
                for (int i = 0; i < allyRects.Count; i++)
                {
                    if (allyRects[i].Contains(cell)) { insideAlly = true; break; }
                }
                if (insideAlly) continue;

                bool destinationTaken = false;
                for (int i = 0; i < allyDestinations.Count; i++)
                {
                    float standoffDistance = allyDestinations[i].Value + 2f;
                    if (cell.DistanceToSquared(allyDestinations[i].Key) < (standoffDistance * standoffDistance))
                    {
                        destinationTaken = true;
                        break;
                    }
                }
                if (destinationTaken) continue;

                float rangeScore = Mathf.Abs(distToTarget - searchIdeal) * 2f;
                float travelScore = cell.DistanceTo(vehicle.Position) * 0.3f;
                float score = rangeScore + travelScore + Rand.Range(0f, 8f);

                candidates.Add(new KeyValuePair<IntVec3, float>(cell, score));
                validCellsFound++;
                
                if (validCellsFound > 100) break;
            }

            candidates.Sort((a, b) => a.Value.CompareTo(b.Value));

            int pathChecks = 0;
            foreach (var kvp in candidates)
            {
                if (pathChecks++ >= 5) break;
                if (vehicle.CanReachVehicle(new LocalTargetInfo(kvp.Key), PathEndMode.OnCell, Danger.Deadly, TraverseMode.NoPassClosedDoors))
                {
                    return kvp.Key;
                }
            }

            return IntVec3.Invalid;
        }
    }

    public static class Patch_VVE_MovementController_NPC
    {
        public static System.Type controllerType;
        public static FieldInfo curMovementModeField;
        public static FieldInfo currentSpeedField;
        public static PropertyInfo vehicleProperty;

        public static void TryApply(Harmony harmony)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    controllerType = asm.GetType("VanillaVehiclesExpanded.CompVehicleMovementController");
                    if (controllerType != null) break;
                }
                catch { }
            }

            if (controllerType == null) return;

            curMovementModeField = AccessTools.Field(controllerType, "curMovementMode");
            currentSpeedField    = AccessTools.Field(controllerType, "currentSpeed");
            vehicleProperty      = AccessTools.Property(controllerType, "Vehicle");

            MethodInfo target = AccessTools.Method(controllerType, "StartMove");
            if (target == null) return;

            harmony.Patch(target,
                postfix: new HarmonyMethod(typeof(Patch_VVE_MovementController_NPC), nameof(Postfix)));
        }

        public static void Postfix(object __instance)
        {
            if (curMovementModeField == null || currentSpeedField == null || vehicleProperty == null) return;

            var vehicle = vehicleProperty.GetValue(__instance) as VehiclePawn;
            if (vehicle == null) return;
            if (vehicle.Faction == null || vehicle.Faction.IsPlayer) return;

            Lord lord = vehicle.GetLord();
            if (!(lord?.LordJob is LordJob_VehicleRaid)) return;

            curMovementModeField.SetValue(__instance, System.Enum.ToObject(curMovementModeField.FieldType, 1));
            currentSpeedField.SetValue(__instance, 0f);
        }
    }
}
