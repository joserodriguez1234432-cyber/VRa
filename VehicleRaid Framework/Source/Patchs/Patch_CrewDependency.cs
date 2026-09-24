using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Vehicles;
using UnityEngine;
using VehicleRaid;

namespace VehicleRaidFramework
{
    [HarmonyPatch(typeof(VehiclePawn), "Tick")]
    public static class Patch_RaidVehicle_CrewDependency
    {
        static void Postfix(VehiclePawn __instance)
        {
            if (__instance == null || !__instance.Spawned || __instance.Destroyed || __instance.Map == null) return;
            if (__instance.Faction == null || __instance.Faction.IsPlayer) return;

            Lord lord = __instance.GetLord();
            bool isVRFLord = lord?.LordJob is LordJob_VehicleRaid || lord?.LordJob is LordJob_VehicleTrade || lord?.LordJob is LordJob_HelicopterTrade;
            
            if (!isVRFLord)
            {
                bool isNaturalRaid = lord != null && (lord.LordJob.GetType().Name.Contains("AssaultColony") || lord.LordJob.GetType().Name.Contains("Raid") || lord.LordJob is LordJob_DefendBase);
                if (!isNaturalRaid) return;

                if (__instance.GetComp<VehicleRaid.CompVehicleHover>() == null)
                {
                    var settings = VRF_Mod.Settings;
                    if (settings == null) return;

                    var factionConfig = settings.factionConfigs?.Find(c => c.factionDefName == __instance.Faction?.def?.defName);
                    if (factionConfig == null) return;

                    var entry = factionConfig.vehicleEntries?.Find(e => e.vehicleKindDefName == __instance.VehicleDef.defName && e.enabled);
                    if (entry == null) return;
                }
            }

            if (__instance.IsHashIntervalTick(30))
            {
                Patch_VehicleNPCOnOff.UpdateVehiclePower(__instance);
            }

            if (!__instance.IsHashIntervalTick(60)) return;
            if (__instance.VehicleDef.type == VehicleType.Air && __instance.GetComp<VehicleRaid.CompVehicleHover>() == null)
            {
                if (VRF_TransportUtil.IsSiegeDropVehicle(__instance))
                {
                    VRF_SiegeDropBehavior.ReassignSiegeDropCrew(__instance);

                    if (__instance.IsHashIntervalTick(300) && lord?.LordJob is LordJob_VehicleRaid
                        && lord.CurLordToil is LordToil_VehicleSearchAndDestroy
                        && Find.TickManager.TicksGame - __instance.TickSpawned >= 300)
                    {
                        CheckRaidLordExitSync(__instance, lord);
                    }
                }
                return;
            }

            CrewManager.ReassignCrew(__instance);
            CrewManager.CheckAbandonment(__instance);
            CrewManager.CheckRetreat(__instance);

            if (VRF_TransportUtil.IsTransportVehicle(__instance) || VRF_TransportUtil.IsArmedTransportVehicle(__instance))
            {
                if (lord?.CurLordToil is LordToil_VehicleExitMap exitToil)
                {
                    CheckTransportExitDuty(__instance, exitToil);
                }
                else
                {
                    HandleTransportDisembark(__instance);
                }
            }

            if (lord != null)
            {
                if (__instance.IsHashIntervalTick(1250))
                {
                    FeedCrewFromInventory(__instance);
                    RefuelFromInventory(__instance);
                }

                HandleOverlappingPawns(__instance);

                if (!__instance.AllPawnsAboard.Any() && (__instance.Dead || __instance.Destroyed))
                {
                    lord.Notify_PawnLost(__instance, (PawnLostCondition)3, null);
                }

                if (__instance.IsHashIntervalTick(300) && lord.LordJob is LordJob_VehicleRaid)
                {
                    if (lord.CurLordToil is LordToil_VehicleSearchAndDestroy)
                    {
                        CheckRaidLordExitSync(__instance, lord);
                    }
                    else if (lord.CurLordToil is LordToil_VehicleExitMap)
                    {
                        CheckRaidLordStartExit(__instance, lord);
                    }
                }
            }
        }

        private static void CheckRaidLordStartExit(VehiclePawn vehicle, Lord vehLord)
        {
            Map map = vehicle.Map;
            foreach (Lord otherLord in map.lordManager.lords)
            {
                if (otherLord == vehLord) continue;
                if (otherLord.faction != vehLord.faction) continue;
                if (otherLord.LordJob is LordJob_VehicleRaid) continue;

                bool isNaturalRaid = otherLord.LordJob != null && 
                    (otherLord.LordJob.GetType().Name.Contains("AssaultColony") || 
                     otherLord.LordJob.GetType().Name.Contains("Raid") || 
                     otherLord.LordJob is LordJob_DefendBase);
                if (!isNaturalRaid) continue;

                bool alreadyExiting = false;
                LordToil curToil = otherLord.CurLordToil;
                if (curToil != null)
                {
                    string toilType = curToil.GetType().Name;
                    if (toilType.Contains("Exit") || toilType.Contains("Leave") ||
                        toilType.Contains("Flee") || toilType.Contains("Escape") ||
                        toilType.Contains("Steal") || toilType.Contains("Kidnap"))
                    {
                        alreadyExiting = true;
                    }
                }

                if (!alreadyExiting)
                {
                    LordToil newLordToil = otherLord.Graph?.lordToils?.FirstOrDefault(st => st is LordToil_PanicFlee);
                    if (newLordToil != null)
                    {
                        otherLord.GotoToil(newLordToil);
                    }
                    else
                    {
                        LordToil exitToil = otherLord.Graph?.lordToils?.FirstOrDefault(st => 
                            st.GetType().Name.Contains("Exit") || 
                            st.GetType().Name.Contains("Leave") || 
                            st.GetType().Name.Contains("Flee") || 
                            st.GetType().Name.Contains("Escape") || 
                            st.GetType().Name.Contains("Steal") || 
                            st.GetType().Name.Contains("Kidnap"));
                        if (exitToil != null)
                        {
                            otherLord.GotoToil(exitToil);
                        }
                    }
                }
            }
        }

        private static void CheckRaidLordExitSync(VehiclePawn vehicle, Lord vehLord)
        {
            Map map = vehicle.Map;
            foreach (Lord otherLord in map.lordManager.lords)
            {
                if (otherLord == vehLord) continue;
                if (otherLord.faction != vehLord.faction) continue;
                if (otherLord.LordJob is LordJob_VehicleRaid) continue;

                int livingActiveCount = 0;
                foreach (Pawn p in otherLord.ownedPawns)
                {
                    if (p.Dead || p.Downed || !p.Spawned || p.Map != map) continue;
                    if (p.ParentHolder is VehicleRoleHandler) continue;
                    livingActiveCount++;
                }

                if (livingActiveCount == 0) continue;

                bool shouldExit = false;

                LordToil curToil = otherLord.CurLordToil;
                if (curToil != null)
                {
                    string toilType = curToil.GetType().Name;
                    if (toilType.Contains("Exit") || toilType.Contains("Leave") ||
                        toilType.Contains("Flee") || toilType.Contains("Escape") ||
                        toilType.Contains("Steal") || toilType.Contains("Kidnap"))
                    {
                        shouldExit = true;
                    }
                }

                if (!shouldExit)
                {
                    foreach (Pawn p in otherLord.ownedPawns)
                    {
                        if (p.Dead || p.Downed || !p.Spawned || p.Map != map) continue;
                        if (p.ParentHolder is VehicleRoleHandler) continue;
                        DutyDef duty = p.mindState?.duty?.def;
                        if (duty != null)
                        {
                            string dName = duty.defName;
                            if (dName.Contains("Exit") || dName.Contains("Leave") ||
                                dName.Contains("Flee") || dName == "ExitMapBest" ||
                                dName == "ExitMapRandom" || dName == "ExitMapNear")
                            {
                                shouldExit = true;
                                break;
                            }
                        }
                    }
                }

                if (shouldExit)
                {
                    vehLord.ReceiveMemo("RaidNaturalExit");
                    return;
                }
            }
        }

        private static void CheckTransportExitDuty(VehiclePawn vehicle, LordToil_VehicleExitMap exitToil)
        {
            if (exitToil.ExitToilStartTick >= 0 &&
                Find.TickManager.TicksGame - exitToil.ExitToilStartTick < LordToil_VehicleExitMap.MinTicksBeforeExit)
                return;

            int totalPassengerSlots = 0;
            int boardedPassengers = 0;
            foreach (var handler in vehicle.handlers)
            {
                if (handler?.role == null) continue;
                bool isPassengerSlot = (handler.role.HandlingTypes & HandlingType.Movement) == 0 &&
                                       (handler.role.HandlingTypes & HandlingType.Turret) == 0;
                if (!isPassengerSlot) continue;
                totalPassengerSlots += handler.role.Slots;
                boardedPassengers += handler.thingOwner.Count;
            }

            bool allBoarded = totalPassengerSlots > 0 && boardedPassengers >= totalPassengerSlots;

            bool anyInfantryOnMap = false;
            Lord lord = vehicle.GetLord();
            if (lord != null && !allBoarded)
            {
                foreach (Pawn p in lord.ownedPawns)
                {
                    if (p is VehiclePawn) continue;
                    if (p.Dead || p.Downed) continue;
                    if (!p.Spawned || p.Map != vehicle.Map) continue;
                    if (p.ParentHolder is VehicleRoleHandler) continue;
                    if (p.Position.DistanceTo(vehicle.Position) > 60f) continue;
                    anyInfantryOnMap = true;
                    break;
                }
            }

            DutyDef targetDuty;
            if (allBoarded || !anyInfantryOnMap)
            {
                targetDuty = VRF_DutyDefOf.VRF_VehicleExitMap ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleExitMap", false) ?? DutyDefOf.ExitMapBest;
            }
            else if (VRF_TransportUtil.IsArmedTransportVehicle(vehicle))
            {
                targetDuty = VRF_DutyDefOf.VRF_VehicleArmedTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleArmedTransport", false);
            }
            else
            {
                targetDuty = VRF_DutyDefOf.VRF_VehicleTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleTransport", false);
            }

            if (targetDuty != null && vehicle.mindState.duty?.def != targetDuty)
                vehicle.mindState.duty = new PawnDuty(targetDuty);
        }

        private static void HandleTransportDisembark(VehiclePawn vehicle)
        {
            float detectionRadius = VRF_TransportUtil.GetVehicleCombatRadius(vehicle);
            if (!VRF_TransportUtil.HasEnemy(vehicle, detectionRadius)) return;

            if (!VRF_TransportUtil.HasEnemy(vehicle, 32f))
                return;

            bool isUnarmedTransport = VRF_TransportUtil.IsTransportVehicle(vehicle);

            bool hasPawnsToDisembark = false;
            foreach (var handler in vehicle.handlers)
            {
                if (handler?.role == null) continue;
                bool isPassengerSlot = (handler.role.HandlingTypes & HandlingType.Movement) == 0 &&
                                       (handler.role.HandlingTypes & HandlingType.Turret) == 0;
                if (!isPassengerSlot && !isUnarmedTransport) continue;
                foreach (Pawn p in handler.thingOwner)
                {
                    if (p != null && !p.Dead && !p.Downed) { hasPawnsToDisembark = true; break; }
                }
                if (hasPawnsToDisembark) break;
            }

            if (!hasPawnsToDisembark) return;

            Lord lord = vehicle.GetLord();
            bool isExiting = lord?.CurLordToil is LordToil_VehicleExitMap;

            if (isExiting) return;

            List<Pawn> toDisembark = new List<Pawn>();
            foreach (var handler in vehicle.handlers)
            {
                if (handler?.role == null) continue;
                bool isPassengerSlot = (handler.role.HandlingTypes & HandlingType.Movement) == 0 &&
                                       (handler.role.HandlingTypes & HandlingType.Turret) == 0;
                if (!isPassengerSlot && !isUnarmedTransport) continue;
                foreach (Pawn p in handler.thingOwner)
                {
                    if (p == null || p.Dead || p.Downed) continue;
                    DutyDef pDuty = p.mindState?.duty?.def;
                    if (pDuty != null && (pDuty == VRF_DutyDefOf.VRF_InfantryExit ||
                        pDuty.defName == "VRF_InfantryExit")) continue;
                    toDisembark.Add(p);
                }
            }

            if (toDisembark.Count == 0) return;

            Map map = vehicle.Map;
            CellRect vehicleRect = vehicle.OccupiedRect();
            Thing nearestEnemy = FindNearestEnemy(vehicle);

            foreach (Pawn pawn in toDisembark)
            {
                IntVec3 exitCell = IntVec3.Invalid;
                for (int radius = 1; radius <= 5 && !exitCell.IsValid; radius++)
                {
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(vehicle.Position, radius, false))
                    {
                        if (!cell.InBounds(map)) continue;
                        if (!cell.Standable(map)) continue;
                        if (vehicleRect.Contains(cell)) continue;
                        exitCell = cell;
                        break;
                    }
                }

                if (!exitCell.IsValid) continue;

                VRF_TransportUtil.LastDisembarkTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
                vehicle.DisembarkPawn(pawn);

                if (!pawn.Spawned && pawn.ParentHolder == null) continue;

                CrewManager.SyncDisembarkedPawnLord(pawn, vehicle);

                DutyDef dutyDef =
                    VRF_DutyDefOf.VRF_InfantryAssault_Transport ??
                    DefDatabase<DutyDef>.GetNamed("VRF_InfantryAssault_Transport", false) ??
                    VRF_DutyDefOf.VRF_InfantryAssault ??
                    DefDatabase<DutyDef>.GetNamed("VRF_InfantryAssault", false);

                pawn.mindState.duty = nearestEnemy != null
                    ? new PawnDuty(dutyDef, nearestEnemy.Position)
                    : new PawnDuty(dutyDef);

                pawn.jobs?.StopAll();
            }
        }

        private static Thing FindNearestEnemy(VehiclePawn vehicle)
        {
            var targets = vehicle.Map.attackTargetsCache.TargetsHostileToFaction(vehicle.Faction);
            if (targets == null || targets.Count == 0) return null;
            Thing best = null;
            float bestDist = float.MaxValue;
            foreach (var t in targets)
            {
                Thing thing = t.Thing;
                if (thing == null || thing.Destroyed) continue;
                if (thing.Map == null || thing.Map.fogGrid.IsFogged(thing.Position)) continue;
                if (thing is Pawn p && (p.Dead || p.Downed)) continue;
                float d = thing.Position.DistanceToSquared(vehicle.Position);
                if (d < bestDist) { bestDist = d; best = thing; }
            }
            return best;
        }

        private static void FeedCrewFromInventory(VehiclePawn vehicle)
        {
            if (vehicle.inventory == null || vehicle.inventory.innerContainer == null || vehicle.inventory.innerContainer.Count == 0) return;

            Thing food = vehicle.inventory.innerContainer.FirstOrDefault(t => t.def.IsIngestible);
            if (food == null) return;

            foreach (var handler in vehicle.handlers)
            {
                foreach (Pawn occupant in handler.thingOwner)
                {
                    if (occupant != null && occupant.needs?.food != null)
                    {
                        if (occupant.needs.food.CurLevelPercentage < 0.4f)
                        {
                            float nutrition = food.def.ingestible.CachedNutrition;
                            occupant.needs.food.CurLevel += nutrition;
                            food.stackCount--;
                            if (food.stackCount <= 0)
                            {
                                food.Destroy();
                                food = vehicle.inventory.innerContainer.FirstOrDefault(t => t.def.IsIngestible);
                                if (food == null) return;
                            }
                        }
                    }
                }
            }
        }

        private static void RefuelFromInventory(VehiclePawn vehicle)
        {
            CompFueledTravel comp = vehicle.GetComp<CompFueledTravel>();
            if (comp == null || comp.Props.ElectricPowered || comp.FuelPercent > 0.10f) return;

            float needed = comp.FuelCapacity - comp.Fuel;
            if (needed <= 0) return;

            int availableFuel = 0;
            var fuelThings = CompFueledTravel.AllFuelFromInventory(vehicle).ToList();
            foreach (var t in fuelThings) availableFuel += t.stackCount;

            if (availableFuel > 0)
            {
                int toConsume = Mathf.Min(availableFuel, Mathf.CeilToInt(needed));
                comp.ConsumeFuelFromInventory(toConsume);

                Patch_VehicleNPCOnOff.UpdateVehiclePower(vehicle);
            }
        }

        private static void HandleOverlappingPawns(VehiclePawn vehicle)
        {
            if (vehicle == null || !vehicle.Spawned || vehicle.Destroyed || vehicle.Map == null || vehicle.Faction == null) return;

            var hoverComp = vehicle.GetComp<CompVehicleHover>();
            if (hoverComp != null && hoverComp.IsAirborne)
            {
                return;
            }

            CellRect rect = vehicle.OccupiedRect();
            Map map = vehicle.Map;

            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map)) continue;

                List<Thing> thingList = cell.GetThingList(map);
                for (int i = thingList.Count - 1; i >= 0; i--)
                {
                    if (thingList[i] is Pawn p && p != vehicle && !(p is VehiclePawn) && !vehicle.handlers.Any(h => h.thingOwner.Contains(p)))
                    {
                        ResolvePawnOverlap(vehicle, p);
                    }
                }
            }
        }

        private static void ResolvePawnOverlap(VehiclePawn vehicle, Pawn pawn)
        {
            ApplyOverlapDamage(vehicle, pawn);

            Map map = vehicle.Map;
            IntVec3 bestPos = IntVec3.Invalid;
            float minDist = float.MaxValue;
            CellRect vehicleRect = vehicle.OccupiedRect();

            for (int radius = 1; radius <= 3; radius++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, radius, true))
                {
                    if (cell.InBounds(map) && cell.Walkable(map) && !vehicleRect.Contains(cell))
                    {
                        float d = cell.DistanceToSquared(pawn.Position);
                        if (d < minDist)
                        {
                            minDist = d;
                            bestPos = cell;
                        }
                    }
                }
                if (bestPos.IsValid) break;
            }

            if (bestPos.IsValid)
            {
                pawn.Position = bestPos;
                pawn.Notify_Teleported(true, false);
            }
        }

        private static void ApplyOverlapDamage(VehiclePawn vehicle, Pawn pawn)
        {
            if (pawn.Faction == null || vehicle.Faction == null) return;

            float damageMultiplier = 0f;

            if (pawn.Faction == vehicle.Faction || !pawn.Faction.HostileTo(vehicle.Faction))
            {
                damageMultiplier = 0f;
            }
            else if (vehicle.Faction.RelationKindWith(pawn.Faction) == FactionRelationKind.Neutral)
            {
                damageMultiplier = 0.1f;
            }
            else
            {
                damageMultiplier = 1.0f;
            }

            if (damageMultiplier > 0)
            {
                var damages = VehiclePawn.CalculateImpactDamage(pawn, vehicle, 5f);
                float pawnDamage = damages.pawnDamage * damageMultiplier;

                if (pawnDamage > 0.5f)
                {
                    DamageInfo dinfo = new DamageInfo(DamageDefOf.Blunt, pawnDamage, 0f, -1f, vehicle);
                    pawn.TakeDamage(dinfo);
                }
            }
        }
    }

    [HarmonyPatch(typeof(VehicleActions), nameof(VehicleActions.DisembarkAll))]
    public static class Patch_DisembarkAll_ProtectHoverDriver
    {
        [HarmonyPrefix]
        public static bool Prefix(VehiclePawn vehicle)
        {
            if (vehicle == null || vehicle.Faction == null || vehicle.Faction.IsPlayer) return true;
            if (!(vehicle.GetLord()?.LordJob is LordJob_VehicleRaid)) return true;

            // Gravships manage their own crew — never force-disembark them
            if (CrewManager.IsGravshipVehicle(vehicle)) return false;

            if (VRF_TransportUtil.IsSiegeDropVehicle(vehicle)) return false;

            var hoverComp = vehicle.GetComp<CompVehicleHover>();
            if (hoverComp == null || hoverComp.State != HoverState.Hovering) return true;

            var turrets = vehicle.CompVehicleTurrets;
            if (turrets != null && turrets.Turrets != null && turrets.Turrets.Count > 0) return true;

            Pawn driver = null;
            foreach (var handler in vehicle.handlers)
            {
                if (handler?.role == null) continue;
                if ((handler.role.HandlingTypes & HandlingType.Movement) == 0) continue;
                foreach (var thing in handler.thingOwner)
                {
                    if (thing is Pawn p && !p.Dead && !p.Downed) { driver = p; break; }
                }
                if (driver != null) break;
            }

            if (driver == null) return true;

            List<Pawn> toDisembark = new List<Pawn>();
            foreach (Pawn p in vehicle.AllPawnsAboard)
            {
                if (p != driver) toDisembark.Add(p);
            }

            foreach (Pawn p in toDisembark)
                vehicle.DisembarkPawn(p);

            return false;
        }
    }
}
