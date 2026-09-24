using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using RimWorld;
using Vehicles;
using SmashTools;

namespace VehicleRaidFramework
{

    public class LordJob_VehicleRaid : LordJob
    {
        private Faction assaulterFaction;
        private int stayTicks = 30000;
        public bool updatingDuties = false;
        public Lord naturalRaidLord;
        private static readonly IntRange AssaultTimeRange = new IntRange(30000, 45000);

        private VRF_NaturalRaidBehavior raidBehavior = VRF_NaturalRaidBehavior.ImmediateAssault;
        private int holdTicks = 0;

        public LordJob_VehicleRaid() { }

        public LordJob_VehicleRaid(Faction faction, int stayTicks = 0,
            VRF_NaturalRaidBehavior behavior = VRF_NaturalRaidBehavior.ImmediateAssault,
            int holdTicks = 0)
        {
            this.assaulterFaction = faction;
            this.stayTicks        = stayTicks > 0 ? stayTicks : AssaultTimeRange.RandomInRange;
            this.raidBehavior     = behavior;
            this.holdTicks        = holdTicks;
        }

        public override bool GuiltyOnDowned => true;

        public override StateGraph CreateGraph()
        {
            StateGraph stateGraph = new StateGraph();

            LordToil_VehicleExitMap exitToil = new LordToil_VehicleExitMap();
            stateGraph.AddToil(exitToil);

            LordToil assaultToil = new LordToil_VehicleSearchAndDestroy();
            stateGraph.AddToil(assaultToil);

            Transition timeout = new Transition(assaultToil, exitToil);
            timeout.AddTrigger(new Trigger_TicksPassed(stayTicks));
            timeout.AddPreAction(new TransitionAction_Message("MessageRaidersGivenUpLeaving".Translate(assaulterFaction.def.pawnsPlural.CapitalizeFirst(), assaulterFaction.Name)));
            stateGraph.AddTransition(timeout);

            Transition peace = new Transition(assaultToil, exitToil);
            peace.AddTrigger(new Trigger_BecameNonHostileToPlayer());
            peace.AddPreAction(new TransitionAction_Message("MessageRaidersLeaving".Translate(assaulterFaction.def.pawnsPlural.CapitalizeFirst(), assaulterFaction.Name)));
            stateGraph.AddTransition(peace);

            Transition satisfied = new Transition(assaultToil, exitToil);
            satisfied.AddTrigger(new Trigger_FractionColonyDamageTaken(0.30f, 900f));
            satisfied.AddPreAction(new TransitionAction_Message("MessageRaidersSatisfiedLeaving".Translate(assaulterFaction.def.pawnsPlural.CapitalizeFirst(), assaulterFaction.Name)));
            stateGraph.AddTransition(satisfied);

            Transition retreat = new Transition(assaultToil, exitToil);
            retreat.AddTrigger(new Trigger_FractionPawnsLost(0.5f));
            retreat.AddPreAction(new TransitionAction_Message("MessageRaidersGivenUpLeaving".Translate(assaulterFaction.def.pawnsPlural.CapitalizeFirst(), assaulterFaction.Name)));
            stateGraph.AddTransition(retreat);

            Transition naturalExit = new Transition(assaultToil, exitToil);
            naturalExit.AddTrigger(new Trigger_Memo("RaidNaturalExit"));
            stateGraph.AddTransition(naturalExit);

            if (!assaulterFaction.HostileTo(Faction.OfPlayer))
            {
                Transition allyVictory = new Transition(assaultToil, exitToil);
                allyVictory.AddTrigger(new Trigger_TicksPassedWithoutHarm(5000));
                allyVictory.AddPreAction(new TransitionAction_Message("MessageFriendlyFightersLeaving".Translate(assaulterFaction.def.pawnsPlural.CapitalizeFirst(), assaulterFaction.Name)));
                stateGraph.AddTransition(allyVictory);
            }

            if (raidBehavior == VRF_NaturalRaidBehavior.HoldThenAssault && holdTicks > 0)
            {
                LordToil_VehicleHoldPosition holdToil = new LordToil_VehicleHoldPosition();
                stateGraph.AddToil(holdToil);
                stateGraph.StartingToil = holdToil;

                Transition holdParentAssault = new Transition(holdToil, assaultToil);
                holdParentAssault.AddTrigger(new Trigger_Memo("VRF_ParentAssaulting"));
                holdParentAssault.AddPreAction(new TransitionAction_Message(
                    "MessageRaidersAssaulting".Translate(
                        assaulterFaction.def.pawnsPlural.CapitalizeFirst(),
                        assaulterFaction.Name),
                    MessageTypeDefOf.ThreatBig));
                holdParentAssault.AddPostAction(new TransitionAction_WakeAll());
                stateGraph.AddTransition(holdParentAssault);

                Transition holdDone = new Transition(holdToil, assaultToil);
                holdDone.AddTrigger(new Trigger_TicksPassed(holdTicks));
                holdDone.AddPreAction(new TransitionAction_Message(
                    "MessageRaidersAssaulting".Translate(
                        assaulterFaction.def.pawnsPlural.CapitalizeFirst(),
                        assaulterFaction.Name),
                    MessageTypeDefOf.ThreatBig));
                holdDone.AddPostAction(new TransitionAction_WakeAll());
                stateGraph.AddTransition(holdDone);

                Transition holdPeace = new Transition(holdToil, exitToil);
                holdPeace.AddTrigger(new Trigger_BecameNonHostileToPlayer());
                holdPeace.AddPreAction(new TransitionAction_Message(
                    "MessageRaidersLeaving".Translate(
                        assaulterFaction.def.pawnsPlural.CapitalizeFirst(),
                        assaulterFaction.Name)));
                stateGraph.AddTransition(holdPeace);
            }
            else
            {
                stateGraph.StartingToil = assaultToil;
            }

            return stateGraph;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref assaulterFaction, "assaulterFaction");
            Scribe_Values.Look(ref stayTicks,    "stayTicks",    30000);
            Scribe_References.Look(ref naturalRaidLord, "naturalRaidLord");
            Scribe_Values.Look(ref raidBehavior, "raidBehavior", VRF_NaturalRaidBehavior.ImmediateAssault);
            Scribe_Values.Look(ref holdTicks,    "holdTicks",    0);
        }
    }

    public class LordToil_VehicleSearchAndDestroy : LordToil
    {
        public override bool ForceHighStoryDanger => true;
        public override bool AllowSatisfyLongNeeds => false;

        public override void Init()
        {
            base.Init();
            LessonAutoActivator.TeachOpportunity(ConceptDefOf.Drafting, OpportunityType.Critical);
        }

        private const int GracePeriodTicks = 300;

        public override void UpdateAllDuties()
        {
            var vJob = this.lord.LordJob as LordJob_VehicleRaid;
            if (vJob != null) vJob.updatingDuties = true;

            try
            {
                var leaderManager = this.lord.Map.GetComponent<VRF_LeaderManager>();
                leaderManager?.ForceRefresh();

                bool hasTransport = this.lord.ownedPawns.Any(lp =>
                    lp is VehiclePawn tv &&
                    !tv.Dead &&
                    (VRF_TransportUtil.IsTransportVehicle(tv) || VRF_TransportUtil.IsArmedTransportVehicle(tv)) &&
                    VRF_TransportUtil.HasPassengerOnlySlots(tv));

                Lord naturalRaidLord = vJob?.naturalRaidLord;
                bool isStagingOrSieging = false;
                IntVec3 targetSpot = IntVec3.Invalid;

                if (naturalRaidLord != null && naturalRaidLord.Map == this.lord.Map)
                {
                    LordToil curToil = naturalRaidLord.CurLordToil;
                    if (curToil != null)
                    {
                        string toilName = curToil.GetType().Name;
                        if (toilName.Contains("Stage") || toilName.Contains("Siege") ||
                            (naturalRaidLord.LordJob?.GetType().Name.Contains("Siege") == true && toilName.Contains("Travel")))
                        {
                            targetSpot = curToil.FlagLoc;
                            if (targetSpot.IsValid && targetSpot.InBounds(this.lord.Map))
                            {
                                isStagingOrSieging = true;
                            }
                        }
                    }
                }

                List<Pawn> pawnsToRemove = new List<Pawn>();
                int nowTick = Find.TickManager.TicksGame;

                foreach (Pawn pawn in this.lord.ownedPawns)
                {
                    if (pawn is VehiclePawn v)
                    {
                        if (v.Dead || v.Destroyed)
                        {
                            pawnsToRemove.Add(v);
                            continue;
                        }

                        if (v.mindState.duty?.def.defName == "VRF_VehicleExitMap" || v.mindState.duty?.def == DutyDefOf.ExitMapBest)
                            continue;

                        bool isSiegeDrop = VRF_TransportUtil.IsSiegeDropVehicle(v);
                        bool isHoverAirborne = v.GetComp<VehicleRaid.CompVehicleHover>()?.IsAirborne == true;
                        bool isTransport = VRF_TransportUtil.IsTransportVehicle(v);
                        bool hasDriver = CrewManager.HasOperationalDriver(v);
                        bool hasInfantry = CrewManager.AnyFriendlyInfantryNearby(v);
                        bool isBoarding = CrewManager.IsAnyPawnBoarding(v);

                        if (isStagingOrSieging)
                        {
                            DutyDef vehicleHoldDuty = VRF_DutyDefOf.VRF_VehicleSearchAndDestroy
                                ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleSearchAndDestroy", false);
                            if (vehicleHoldDuty != null &&
                                (pawn.mindState.duty?.def != vehicleHoldDuty ||
                                 pawn.mindState.duty?.focus.Cell != targetSpot))
                            {
                                pawn.mindState.duty = new PawnDuty(vehicleHoldDuty, targetSpot);
                            }
                        }
                    else
                    {
                        if (pawn.mindState.duty?.def == DutyDefOf.Defend)
                        {
                        }
                        else
                        {
                        DutyDef vehicleDuty;
                        if (isSiegeDrop)
                        {
                            vehicleDuty = VRF_DutyDefOf.VRF_VehicleSearchAndDestroy
                                ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleSearchAndDestroy", false);
                            if (vehicleDuty != null && pawn.mindState.duty?.def != vehicleDuty)
                                pawn.mindState.duty = new PawnDuty(vehicleDuty, v.Position);
                        }
                        else if (isHoverAirborne && isTransport)
                            vehicleDuty = VRF_DutyDefOf.VRF_VehicleTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleTransport", false);
                        else if (isTransport)
                            vehicleDuty = VRF_DutyDefOf.VRF_VehicleTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleTransport", false);
                        else if (VRF_TransportUtil.IsArmedTransportVehicle(v))
                            vehicleDuty = VRF_DutyDefOf.VRF_VehicleArmedTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleArmedTransport", false) ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleSearchAndDestroy", false);
                        else
                            vehicleDuty = VRF_DutyDefOf.VRF_VehicleSearchAndDestroy ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleSearchAndDestroy", false);

                        if (!isSiegeDrop && vehicleDuty != null && pawn.mindState.duty?.def != vehicleDuty)
                            pawn.mindState.duty = new PawnDuty(vehicleDuty);
                        }
                    }

                        bool withinGracePeriod = v.Spawned && (nowTick - v.TickSpawned) < GracePeriodTicks;
                        if (!isSiegeDrop && !hasDriver && !hasInfantry && !isBoarding && !withinGracePeriod)
                            pawnsToRemove.Add(v);
                    }
                    else
                    {
                        if (isStagingOrSieging)
                        {
                            if (pawn.mindState.duty?.def != DutyDefOf.Defend || pawn.mindState.duty?.focus.Cell != targetSpot)
                                pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, targetSpot, 28f);
                        }
                        else
                        {
                            if (pawn.mindState.duty?.def == DutyDefOf.Defend) { /* guard — keep duty */ }
                            else
                            {
                            DutyDef infantryDuty = hasTransport
                                ? (VRF_DutyDefOf.VRF_InfantryAssault_Transport ?? DefDatabase<DutyDef>.GetNamed("VRF_InfantryAssault_Transport", false))
                                : (VRF_DutyDefOf.VRF_InfantryAssault ?? DefDatabase<DutyDef>.GetNamed("VRF_InfantryAssault", false));

                            if (pawn.mindState.duty?.def != infantryDuty)
                                pawn.mindState.duty = new PawnDuty(infantryDuty);
                            }
                        }
                    }
                }

                foreach (Pawn p in pawnsToRemove)
                {
                    if (p.jobs != null)
                    {
                        p.jobs.StopAll();
                        p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Combat, 1000, true), JobCondition.InterruptForced);
                    }
                    p.mindState.duty = null;
                    this.lord.RemovePawn(p);
                }
            }
            finally
            {
                if (vJob != null) vJob.updatingDuties = false;
            }
        }

        public override void Notify_PawnLost(Pawn p, PawnLostCondition condition)
        {
            base.Notify_PawnLost(p, condition);
            var vJob = this.lord.LordJob as LordJob_VehicleRaid;
            if (p is VehiclePawn && !(vJob?.updatingDuties ?? false))
            {
                var leaderManager = this.lord.Map.GetComponent<VRF_LeaderManager>();
                leaderManager?.ForceRefresh();
                UpdateAllDuties();
            }
        }
    }

    public class LordToil_VehicleExitMap : LordToil
    {
        public override bool AllowSatisfyLongNeeds => false;

        public int ExitToilStartTick = -1;
        public const int MinTicksBeforeExit = 300;

        public override void Init()
        {
            base.Init();
            ExitToilStartTick = Find.TickManager.TicksGame;
            if (this.lord != null && this.lord.Map != null)
            {
                Map map = this.lord.Map;
                foreach (Lord otherLord in map.lordManager.lords)
                {
                    if (otherLord == this.lord) continue;
                    if (otherLord.faction != this.lord.faction) continue;
                    if (otherLord.LordJob is LordJob_VehicleRaid) continue;

                    bool isNaturalRaid = otherLord.LordJob != null &&
                        (otherLord.LordJob.GetType().Name.Contains("AssaultColony") ||
                         otherLord.LordJob.GetType().Name.Contains("Raid") ||
                         otherLord.LordJob is LordJob_DefendBase);
                    if (!isNaturalRaid) continue;

                    LordToil curToil = otherLord.CurLordToil;
                    if (curToil != null)
                    {
                        string toilType = curToil.GetType().Name;
                        if (toilType.Contains("Exit") || toilType.Contains("Leave") ||
                            toilType.Contains("Flee") || toilType.Contains("Escape") ||
                            toilType.Contains("Steal") || toilType.Contains("Kidnap"))
                        {
                            continue;
                        }
                    }

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

        private static bool HasAllyInfantryInLord(Lord lord)
        {
            foreach (Pawn p in lord.ownedPawns)
            {
                if (p is VehiclePawn) continue;
                if (p.Dead || p.Downed) continue;
                if (!p.Spawned) continue;
                if (p.ParentHolder is VehicleRoleHandler) continue;
                return true;
            }
            return false;
        }

        public override void UpdateAllDuties()
        {
            var vJob = this.lord.LordJob as LordJob_VehicleRaid;
            if (vJob != null) vJob.updatingDuties = true;

            try
            {
                List<Pawn> pawnsToRemove = new List<Pawn>();

                foreach (Pawn pawn in this.lord.ownedPawns)
                {
                    if (pawn is VehiclePawn v)
                    {
                        if (CrewManager.HasOperationalDriver(v) || CrewManager.AnyFriendlyInfantryNearby(v) || CrewManager.IsAnyPawnBoarding(v))
                        {
                            DutyDef vehicleDuty;
                            bool isHoverAirborne = v.GetComp<VehicleRaid.CompVehicleHover>()?.IsAirborne == true;
                            if (VRF_TransportUtil.IsSiegeDropVehicle(v))
                            {
                                vehicleDuty = DefDatabase<DutyDef>.GetNamedSilentFail("VRF_HelicopterTakeoff")
                                    ?? DutyDefOf.ExitMapBest;
                            }
                            else if (isHoverAirborne && VRF_TransportUtil.IsTransportVehicle(v))
                            {
                                vehicleDuty = VRF_DutyDefOf.VRF_VehicleExitMap ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleExitMap", false) ?? DutyDefOf.ExitMapBest;
                            }
                            else if (VRF_TransportUtil.IsTransportVehicle(v) && HasAllyInfantryInLord(this.lord))
                            {
                                vehicleDuty = VRF_DutyDefOf.VRF_VehicleTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleTransport", false);
                            }
                            else if (VRF_TransportUtil.IsArmedTransportVehicle(v) && HasAllyInfantryInLord(this.lord))
                            {
                                vehicleDuty = VRF_DutyDefOf.VRF_VehicleArmedTransport ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleArmedTransport", false) ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleExitMap", false) ?? DutyDefOf.ExitMapBest;
                            }
                            else
                            {
                                vehicleDuty = VRF_DutyDefOf.VRF_VehicleExitMap ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleExitMap", false) ?? DutyDefOf.ExitMapBest;
                            }
                            if (pawn.mindState.duty?.def != vehicleDuty)
                                pawn.mindState.duty = new PawnDuty(vehicleDuty);
                        }
                        else
                        {
                            pawnsToRemove.Add(v);
                        }
                    }
                    else
                    {
                        bool boardedOrExiting = false;
                        if (pawn.mindState.duty?.def == DutyDefOf.Defend && pawn.Spawned)
                        {
                            VehiclePawn targetPod = null;
                            foreach (Pawn lp in this.lord.ownedPawns)
                            {
                                if (!(lp is VehiclePawn vp)) continue;
                                if (!VRF_TransportUtil.IsSiegeDropVehicle(vp)) continue;
                                if (!vp.Spawned) continue;
                                if (vp.handlers.Any(h => h?.role != null && h.AreSlotsAvailable))
                                {
                                    targetPod = vp;
                                    break;
                                }
                            }

                            if (targetPod != null)
                            {
                                VehicleRoleHandler handler = VRF_TransportUtil.GetBestAvailableHandler(targetPod, pawn);
                                if (handler != null && pawn.CanReach(targetPod, PathEndMode.Touch, Danger.Deadly))
                                {
                                    JobDef boardJobDef = DefDatabase<JobDef>.GetNamed("Board", false);
                                    if (boardJobDef != null)
                                    {
                                        targetPod.GiveLoadJob(pawn, handler);
                                        Job boardJob = JobMaker.MakeJob(boardJobDef, targetPod);
                                        boardJob.expiryInterval = 3000;
                                        boardJob.locomotionUrgency = LocomotionUrgency.Sprint;
                                        pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend,
                                            pawn.mindState.duty.focus, pawn.mindState.duty.radius);
                                        pawn.jobs.StartJob(boardJob, JobCondition.InterruptForced,
                                            null, false, true);
                                        boardedOrExiting = true;
                                    }
                                }
                            }
                        }

                        if (!boardedOrExiting)
                        {
                            var dutyDef = VRF_DutyDefOf.VRF_InfantryExit
                                ?? DefDatabase<DutyDef>.GetNamed("VRF_InfantryExit", false)
                                ?? DutyDefOf.ExitMapBest;
                            if (pawn.mindState.duty?.def != dutyDef)
                                pawn.mindState.duty = new PawnDuty(dutyDef);
                        }
                    }
                }

                foreach (Pawn p in pawnsToRemove)
                {
                    if (p.jobs != null)
                    {
                        p.jobs.StopAll();
                        p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Combat, 1000, true), JobCondition.InterruptForced);
                    }
                    p.mindState.duty = null;
                    this.lord.RemovePawn(p);
                }
            }
            finally
            {
                if (vJob != null) vJob.updatingDuties = false;
            }
        }

        public override void LordToilTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0) return;

            foreach (Pawn pawn in this.lord.ownedPawns)
            {
                if (!(pawn is VehiclePawn pod)) continue;
                if (!VRF_TransportUtil.IsSiegeDropVehicle(pod)) continue;
                if (!pod.Spawned || pod.Map == null) continue;

                CompVehicleLauncher launcher = pod.CompVehicleLauncher;
                if (launcher == null || launcher.inFlight) continue;

                int earliestTakeoff = Mathf.Max(
                    ExitToilStartTick >= 0 ? ExitToilStartTick : 0,
                    pod.TickSpawned) + MinTicksBeforeExit;

                if (Find.TickManager.TicksGame < earliestTakeoff)
                    continue;

                bool anyGuardStillOutside = false;
                foreach (Pawn other in this.lord.ownedPawns)
                {
                    if (other is VehiclePawn) continue;
                    if (other.Dead || other.Downed) continue;
                    if (!other.Spawned || other.Map != pod.Map) continue;
                    if (other.ParentHolder is VehicleRoleHandler) continue;
                    anyGuardStillOutside = true;
                    break;
                }
                if (anyGuardStillOutside) continue;

                launcher.inFlight = true;
                launcher.launchProtocol.OrderProtocol(LaunchProtocol.LaunchType.Takeoff);

                VehicleSkyfaller_Leaving skyfaller =
                    (VehicleSkyfaller_Leaving)VehicleSkyfallerMaker.MakeSkyfaller(
                        launcher.Props.skyfallerLeaving, pod);
                skyfaller.createWorldObject = false;

                GenSpawn.Spawn(skyfaller, pod.Position, pod.Map);
            }
        }
    }

    public class VRF_JobGiver_DynamicAssault : ThinkNode_JobGiver
    {
        private const int WallBaseCost = 100;
        private const float WallHpCost = 0.1f;

        public override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is VehiclePawn vehicle) || !vehicle.Spawned || vehicle.Map == null) return null;

            var leaderManager = vehicle.Map.GetComponent<VRF_LeaderManager>();
            if (leaderManager != null && leaderManager.IsMortar(vehicle)) return null;

            if (vehicle.VehicleDef.type == VehicleType.Sea)
            {
                return JobGiver_SeaVehicleMove.TryGiveSeaVehicleJob(vehicle, FindNearestEnemy(vehicle));
            }

            if (!CrewManager.CanMove(vehicle))
            {
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 2000, true);
            }

            if (CrewManager.IsAnyPawnBoarding(vehicle))
            {
                if (vehicle.CurJob != null && vehicle.CurJob.def == JobDefOf.Wait_Combat) return null;
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 500, true);
            }

            if (CrewManager.IsOutOfAmmo(vehicle))
            {
                CrewManager.CheckRetreat(vehicle);
                return null;
            }

            if (vehicle.CurJobDef == JobDefOf.Goto && vehicle.vehiclePather.Moving)
            {
                return null;
            }

            Thing enemy = FindNearestEnemy(vehicle);
            if (enemy == null) return null;

            vehicle.mindState.enemyTarget = enemy;
            float maxRange = vehicle.CompVehicleTurrets?.MaxRange ?? 60f;
            float minRange = vehicle.CompVehicleTurrets?.MinRange ?? 0f;
            float idealRange = Mathf.Clamp(maxRange * 0.7f, minRange + 3f, maxRange - 2f);
            int vehicleWidth = (vehicle.Rotation == Rot4.North || vehicle.Rotation == Rot4.South) ? vehicle.def.size.x : vehicle.def.size.z;
            if (vehicle.CanReachVehicle(new LocalTargetInfo(enemy.Position), PathEndMode.Touch, Danger.Deadly, TraverseMode.NoPassClosedDoors))
            {
                return HandleDirectAssault(vehicle, enemy, idealRange, maxRange, minRange);
            }

            int widthMultiplier = Mathf.Max(vehicleWidth, 1);

            // Use vanilla PathFinder with cost tuning scaled by vehicle width.
            // VehiclePathFinder does not support PassAllDestroyableThings for enclosed targets.
            Thing wallToBreak = null;
            bool pathFound = false;
            List<IntVec3> pathNodes = null;
            PathFinderCostTuning tuning = new PathFinderCostTuning
            {
                costBlockedDoor = WallBaseCost * widthMultiplier,
                costBlockedWallBase = WallBaseCost * widthMultiplier,
                costBlockedDoorPerHitPoint = WallHpCost * widthMultiplier,
                costBlockedWallExtraPerHitPoint = WallHpCost * widthMultiplier
            };
            using (PawnPath path = pawn.Map.pathFinder.FindPathNow(pawn.Position, new LocalTargetInfo(enemy.Position), TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings), tuning))
            {
                pathFound = path.Found;
                if (path.Found)
                {
                    wallToBreak = path.FirstBlockingBuilding(out _, pawn);
                    if (wallToBreak == null && vehicleWidth > 1)
                    {
                        // Path is clear for 1x1 but vehicle is wider.
                        // Save the actual path nodes so we can scan width along the real route.
                        pathNodes = new List<IntVec3>(path.NodesReversed);
                    }
                }
            }

            if (!pathFound) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 500, true);

            if (wallToBreak == null)
            {
                if (vehicle.CanReachVehicle(new LocalTargetInfo(enemy.Position), PathEndMode.Touch, Danger.Deadly, TraverseMode.NoPassClosedDoors))
                {
                    return HandleDirectAssault(vehicle, enemy, idealRange, maxRange, minRange);
                }
                else
                {
                    // Path is clear for a 1x1 pawn, but vehicle hitbox is wider.
                    // Scan along the ACTUAL path cells for walls that block the vehicle's full width.
                    if (pathNodes != null && pathNodes.Count > 1)
                    {
                        wallToBreak = FindWidthBlockingWallAlongPath(vehicle, pathNodes, vehicleWidth);
                    }
                    if (wallToBreak == null)
                    {
                        // If no width-blocking wall found along path, try sector scan
                        wallToBreak = FindBestBreachTargetSector(vehicle, enemy);
                    }
                    if (wallToBreak == null) return JobMaker.MakeJob(JobDefOf.Wait_Combat, 500, true);
                }
            }

            if (IsBlockedByFriendlyVehicle(vehicle, enemy, out VehiclePawn blockingAlly))
            {
                if (blockingAlly.vehiclePather != null && blockingAlly.vehiclePather.Moving)
                {
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 150, true);
                }

                if (vehicle.Position.DistanceToSquared(enemy.Position) > blockingAlly.Position.DistanceToSquared(enemy.Position))
                {
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 250, true);
                }
            }

            if (wallToBreak != null && wallToBreak.Faction != null && !wallToBreak.Faction.HostileTo(vehicle.Faction))
            {
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 500, true);
            }

            Thing targetWall = FindBestBreachTarget(vehicle, wallToBreak, vehicleWidth) ?? wallToBreak;
            return HandleBreaching(vehicle, targetWall, idealRange, maxRange, minRange);
        }

        private Job HandleDirectAssault(VehiclePawn vehicle, Thing enemy, float idealRange, float maxRange, float minRange)
        {
            float dist = vehicle.Position.DistanceTo(enemy.Position);
            bool restricted = TryGetRestrictedTurretAngle(vehicle, enemy, out float idealTurretAngle);

            if (dist < minRange + 1f && minRange > 0f)
            {
                IntVec3 retreatCell = FindRetreatCell(vehicle, enemy, idealRange, maxRange, minRange, restricted, idealTurretAngle);
                if (retreatCell.IsValid)
                {
                    Job retreatJob = JobMaker.MakeJob(JobDefOf.Goto, retreatCell);
                    retreatJob.expiryInterval = 1200;
                    retreatJob.checkOverrideOnExpire = true;
                    return retreatJob;
                }
                IntVec3 fallback = FindPositionAtIdealRange(vehicle, enemy, idealRange, maxRange, minRange, restricted, idealTurretAngle, true);
                if (fallback.IsValid)
                {
                    Job fallbackJob = JobMaker.MakeJob(JobDefOf.Goto, fallback);
                    fallbackJob.expiryInterval = 1200;
                    fallbackJob.checkOverrideOnExpire = true;
                    return fallbackJob;
                }
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
            }

            if (dist >= minRange && dist <= maxRange && GenSight.LineOfSight(vehicle.Position, enemy.Position, vehicle.Map))
            {
                if (restricted)
                {
                    var turretComp = vehicle.CompVehicleTurrets;
                    bool canFire, blocked;
                    CheckTurretAngles(turretComp, enemy, out canFire, out blocked);

                    if (blocked && !canFire)
                    {
                        IntVec3 alignedCell = FindTurretAlignedCell(vehicle, enemy, maxRange, minRange, idealTurretAngle);
                        if (alignedCell.IsValid)
                        {
                            Job maneuverJob = JobMaker.MakeJob(JobDefOf.Goto, alignedCell);
                            maneuverJob.expiryInterval = 1200;
                            maneuverJob.checkOverrideOnExpire = true;
                            return maneuverJob;
                        }
                    }
                }

                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 250, true);
            }

            IntVec3 targetPos = FindPositionAtIdealRange(vehicle, enemy, idealRange, maxRange, minRange, restricted, idealTurretAngle);
            if (targetPos.IsValid && targetPos != vehicle.Position)
            {
                Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, targetPos);
                gotoJob.expiryInterval = 2000;
                gotoJob.checkOverrideOnExpire = true;
                return gotoJob;
            }

            Job ramJob = JobMaker.MakeJob(JobDefOf.Goto, enemy);
            ramJob.expiryInterval = 2000;
            ramJob.checkOverrideOnExpire = true;
            return ramJob;
        }

        private Job HandleBreaching(VehiclePawn vehicle, Thing wall, float baseIdealRange, float maxRange, float minRange)
        {
            if (wall.Destroyed)
            {
                // Current target is destroyed — check if there are adjacent walls
                // still blocking the vehicle's full width at the breach point
                int vehicleWidth = Mathf.Max(
                    (vehicle.Rotation == Rot4.North || vehicle.Rotation == Rot4.South) ? vehicle.def.size.x : vehicle.def.size.z, 1);
                Thing nextWall = FindBestBreachTarget(vehicle, wall, vehicleWidth);
                if (nextWall != null && !nextWall.Destroyed)
                {
                    // Continue breaching the next adjacent wall
                    wall = nextWall;
                }
                else
                {
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 30, true);
                }
            }

            float breachRange = Rand.Range(Mathf.Max(minRange + 5f, 10f), maxRange - 4f);
            bool restricted = TryGetRestrictedTurretAngle(vehicle, wall, out float idealTurretAngle);

            float dist = vehicle.Position.DistanceTo(wall.Position);
            if (dist < minRange + 1f && minRange > 0f)
            {
                IntVec3 retreatCell = FindRetreatCell(vehicle, wall, breachRange, maxRange, minRange, restricted, idealTurretAngle);
                if (retreatCell.IsValid)
                {
                    Job retreatJob = JobMaker.MakeJob(JobDefOf.Goto, retreatCell);
                    retreatJob.expiryInterval = 1200;
                    retreatJob.checkOverrideOnExpire = true;
                    return retreatJob;
                }
                IntVec3 fallback = FindPositionAtIdealRange(vehicle, wall, breachRange, maxRange, minRange, restricted, idealTurretAngle, true);
                if (fallback.IsValid) return JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(fallback), 1200, true);
            }
            if (dist >= minRange && dist <= maxRange && GenSight.LineOfSight(vehicle.Position, wall.Position, vehicle.Map))
            {
                if (restricted)
                {
                    var turretComp = vehicle.CompVehicleTurrets;
                    bool canFire, blocked;
                    CheckTurretAngles(turretComp, wall, out canFire, out blocked);

                    if (blocked && !canFire)
                    {
                        IntVec3 alignedCell = FindTurretAlignedCell(vehicle, wall, maxRange, minRange, idealTurretAngle);
                        if (alignedCell.IsValid)
                        {
                            Job maneuverJob = JobMaker.MakeJob(JobDefOf.Goto, alignedCell);
                            maneuverJob.expiryInterval = 1200;
                            maneuverJob.checkOverrideOnExpire = true;
                            return maneuverJob;
                        }
                    }
                }

                vehicle.mindState.breachingTarget = new BreachingTargetData(wall, vehicle.Position);
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
            }
            IntVec3 approachPos = FindPositionAtIdealRange(vehicle, wall, breachRange, maxRange, minRange, restricted, idealTurretAngle);
            if (approachPos.IsValid) return JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(approachPos), 1200, true);

            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, true);
        }

        /// <summary>
        /// Finds the best wall to breach across the vehicle's full width.
        /// Cycles through all wall positions needed for the vehicle to pass through.
        /// Prioritizes undestroyed walls, selecting the one with the lowest HP
        /// so the vehicle clears the entire breach width progressively.
        /// </summary>
        private Thing FindBestBreachTarget(VehiclePawn vehicle, Thing mainWall, int vehicleWidth)
        {
            Map map = vehicle.Map;
            IntVec3 wallPos = mainWall.Position;
            IntVec3 dir = wallPos - vehicle.Position;
            bool expandX = Mathf.Abs(dir.x) < Mathf.Abs(dir.z);

            // Collect ALL undestroyed walls across the breach width
            var candidates = new List<Thing>();
            for (int offset = -(vehicleWidth / 2); offset <= (vehicleWidth / 2); offset++)
            {
                IntVec3 checkPos = expandX ? wallPos + new IntVec3(offset, 0, 0) : wallPos + new IntVec3(0, 0, offset);
                if (!checkPos.InBounds(map)) continue;
                Building edifice = checkPos.GetEdifice(map);
                if (edifice != null && !edifice.Destroyed && edifice.def.useHitPoints && (edifice.def.fillPercent > 0.5f || edifice is Building_Door))
                {
                    candidates.Add(edifice);
                }
            }

            if (candidates.Count == 0) return null;

            // If the main wall is already destroyed, check if we still have walls blocking the width
            if (mainWall.Destroyed && candidates.Count > 0)
            {
                // Pick the weakest remaining wall to clear the breach
                Thing weakest = candidates[0];
                for (int i = 1; i < candidates.Count; i++)
                {
                    if (candidates[i].HitPoints < weakest.HitPoints)
                        weakest = candidates[i];
                }
                return weakest;
            }

            // Pick the wall with lowest HP (easiest to destroy next)
            Thing best = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                if (candidates[i].HitPoints < best.HitPoints)
                    best = candidates[i];
            }
            return best;
        }

        /// <summary>
        /// Walks along the actual path nodes from the vanilla PathFinder and checks
        /// perpendicular cells at each step based on the vehicle's width.
        /// The perpendicular direction is calculated from the path's travel direction
        /// at each node, making it accurate even for curved/diagonal paths.
        /// Returns the weakest wall that blocks the vehicle's full width.
        /// </summary>
        private Thing FindWidthBlockingWallAlongPath(VehiclePawn vehicle, List<IntVec3> pathNodesReversed, int vehicleWidth)
        {
            Map map = vehicle.Map;
            int halfWidth = vehicleWidth / 2;
            if (halfWidth < 1) return null;
            if (pathNodesReversed == null || pathNodesReversed.Count < 2) return null;

            Thing bestWall = null;
            float bestHp = float.MaxValue;

            // NodesReversed is destination-first, so iterate backwards for start-to-end order
            for (int i = pathNodesReversed.Count - 1; i >= 1; i--)
            {
                IntVec3 current = pathNodesReversed[i];
                IntVec3 next = pathNodesReversed[i - 1];

                // Calculate travel direction at this point
                IntVec3 delta = next - current;
                if (delta.x == 0 && delta.z == 0) continue;

                // Perpendicular direction (rotate 90 degrees)
                // If traveling (dx, dz), perpendicular is (-dz, dx)
                IntVec3 perp = new IntVec3(-delta.z, 0, delta.x);
                // Normalize to unit steps for grid scanning
                float perpLen = Mathf.Sqrt(perp.x * perp.x + perp.z * perp.z);
                if (perpLen < 0.1f) continue;

                // Scan perpendicular cells at this path position
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    if (w == 0) continue; // center cell is on the path and already clear

                    // Scale perpendicular offset by the unit perpendicular direction
                    IntVec3 widthCell = current + new IntVec3(
                        Mathf.RoundToInt((perp.x / perpLen) * w),
                        0,
                        Mathf.RoundToInt((perp.z / perpLen) * w)
                    );

                    if (!widthCell.InBounds(map)) continue;

                    Building edifice = widthCell.GetEdifice(map);
                    if (edifice != null && !edifice.Destroyed && edifice.def.useHitPoints &&
                        (edifice.def.fillPercent > 0.5f || edifice is Building_Door))
                    {
                        if (edifice.HitPoints < bestHp)
                        {
                            bestHp = edifice.HitPoints;
                            bestWall = edifice;
                        }
                    }
                }

                // Return the first blocking wall we find (closest to vehicle along the path)
                if (bestWall != null) return bestWall;
            }

            return bestWall;
        }

        private IntVec3 FindPositionAtIdealRange(VehiclePawn vehicle, Thing target, float idealRange, float maxRange, float minRange, bool hasRestrictedTurrets = false, float idealTurretAngle = 0f, bool isRetreating = false)
        {
            Map map = vehicle.Map;
            var allyRects = new List<CellRect>();
            var allyDestinations = new List<KeyValuePair<IntVec3, int>>();
            float distVehicleToTarget = vehicle.Position.DistanceTo(target.Position);
            bool tooClose = distVehicleToTarget < minRange + 1f;

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
            int cellsChecked = 0;

            float scanRadius = (tooClose || isRetreating) ? Mathf.Min(maxRange + 5f, 70f) : Mathf.Min(idealRange + 5f, maxRange);
            scanRadius = Mathf.Min(scanRadius, 70f);
            int maxCells = (tooClose || isRetreating) ? 800 : 500;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(target.Position, scanRadius, true))
            {
                if (cellsChecked++ > maxCells) break;

                float distToTarget = cell.DistanceTo(target.Position);
                if (distToTarget < minRange + 1f || !cell.Standable(map) || !GenSight.LineOfSight(cell, target.Position, map)) continue;

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

                float rangeScore = Mathf.Abs(distToTarget - idealRange) * 3f;
                float travelScore = tooClose ? cell.DistanceTo(vehicle.Position) * 0.3f : cell.DistanceTo(vehicle.Position);
                float score = rangeScore + travelScore + Rand.Range(0f, 10f);

                if (tooClose && distToTarget >= minRange + 2f)
                    score -= 15f;

                if (hasRestrictedTurrets)
                {
                    Vector3 approachDir = (cell.ToVector3Shifted() - vehicle.Position.ToVector3Shifted());
                    Vector3 cellToTarget = (target.DrawPos - cell.ToVector3Shifted());
                    approachDir.y = 0f;
                    cellToTarget.y = 0f;
                    if (approachDir.sqrMagnitude > 1f && cellToTarget.sqrMagnitude > 1f)
                    {
                        Vector3 idealFacingDir = Quaternion.Euler(0f, -idealTurretAngle, 0f) * cellToTarget;
                        float alignment = Vector3.Dot(approachDir.normalized, idealFacingDir.normalized);
                        score += (1f - alignment) * 20f;
                    }
                }

                candidates.Add(new KeyValuePair<IntVec3, float>(cell, score));
            }

            candidates.Sort((a, b) => a.Value.CompareTo(b.Value));

            int pathChecks = 0;
            int maxPathChecks = (tooClose || isRetreating) ? 8 : 5;
            foreach (var kvp in candidates)
            {
                if (pathChecks++ >= maxPathChecks) break;
                if (vehicle.CanReachVehicle(new LocalTargetInfo(kvp.Key), PathEndMode.OnCell, Danger.Deadly, TraverseMode.NoPassClosedDoors))
                {
                    return kvp.Key;
                }
            }

            return IntVec3.Invalid;
        }

        public Thing FindNearestEnemy(VehiclePawn vehicle)
        {
            var hostileTargets = vehicle.Map.attackTargetsCache.TargetsHostileToFaction(vehicle.Faction);
            if (hostileTargets == null || hostileTargets.Count == 0) return null;

            Thing bestTarget = null;
            float bestDistSq = float.MaxValue;

            foreach (var target in hostileTargets)
            {
                Thing t = target.Thing;
                if (t == null || t.Destroyed || t.Map == null || t.Map.fogGrid.IsFogged(t.Position)) continue;

                if (t is Pawn p && (p.Dead || p.Downed)) continue;

                float distSq = t.Position.DistanceToSquared(vehicle.Position);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTarget = t;
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// Scans multiple angular sectors (15° increments) from the vehicle toward the enemy,
        /// evaluating each for total wall thickness and HP. Returns the wall from the sector
        /// with the lowest obstruction cost, preferring routes with fewer/weaker walls.
        /// </summary>
        private Thing FindBestBreachTargetSector(VehiclePawn vehicle, Thing enemy)
        {
            Map map = vehicle.Map;
            int vehicleWidth = Mathf.Max(
                (vehicle.Rotation == Rot4.North || vehicle.Rotation == Rot4.South) ? vehicle.def.size.x : vehicle.def.size.z, 1);

            Vector3 baseDir = (enemy.DrawPos - vehicle.DrawPos);
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 1f) return null;
            baseDir = baseDir.normalized;

            float scanDist = Mathf.Min(vehicle.Position.DistanceTo(enemy.Position) + 5f, 80f);

            Thing bestWall = null;
            float bestScore = float.MaxValue;

            // Scan 24 sectors: 0°, ±15°, ±30°, ... ±180° from the direct line to enemy
            for (int sector = 0; sector < 25; sector++)
            {
                float angle = (sector == 0) ? 0f : ((sector % 2 == 1) ? 1f : -1f) * ((sector + 1) / 2) * 15f;
                Vector3 scanDir = Quaternion.Euler(0f, angle, 0f) * baseDir;

                Thing firstWall = null;
                int wallCount = 0;
                float totalHp = 0f;
                int totalCells = 0;

                for (float d = 1f; d <= scanDist; d += 1f)
                {
                    IntVec3 cell = vehicle.Position + (scanDir * d).ToIntVec3();
                    if (!cell.InBounds(map)) break;
                    totalCells++;

                    Building edifice = cell.GetEdifice(map);
                    if (edifice != null && !edifice.Destroyed && edifice.def.useHitPoints &&
                        (edifice.def.fillPercent > 0.5f || edifice is Building_Door))
                    {
                        wallCount++;
                        totalHp += edifice.HitPoints;
                        if (firstWall == null) firstWall = edifice;
                    }
                }

                if (firstWall == null) continue;
                if (totalCells == 0) continue;

                // Score = obstruction ratio * HP cost * width penalty + angular deviation penalty
                float obstructionRatio = (float)wallCount / totalCells;
                float hpCost = totalHp * WallHpCost * vehicleWidth;
                float wallBasePenalty = wallCount * WallBaseCost * vehicleWidth;
                float anglePenalty = Mathf.Abs(angle) * 0.5f;
                float score = (obstructionRatio * 500f) + hpCost + wallBasePenalty + anglePenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestWall = firstWall;
                }
            }

            return bestWall;
        }

        private bool IsBlockedByFriendlyVehicle(VehiclePawn vehicle, Thing enemy, out VehiclePawn blocker)
        {
            blocker = null;
            if (vehicle.Map == null || enemy == null) return false;

            Vector3 dir = (enemy.Position.ToVector3Shifted() - vehicle.Position.ToVector3Shifted()).normalized;

            for (int dist = 1; dist <= 4; dist++)
            {
                IntVec3 checkCenter = vehicle.Position + (dir * dist).ToIntVec3();
                CellRect checkRect = CellRect.CenteredOn(checkCenter, vehicle.def.size.x, vehicle.def.size.z);

                foreach (IntVec3 cell in checkRect)
                {
                    if (!cell.InBounds(vehicle.Map)) continue;

                    VehiclePawn other = PathingHelper.AnyVehicleBlockingPathAt(cell, vehicle);
                    if (other != null && other != vehicle && other.Faction == vehicle.Faction)
                    {
                        blocker = other;
                        return true;
                    }
                }
            }
            return false;
        }

        private void CheckTurretAngles(CompVehicleTurrets turretComp, Thing target, out bool canFire, out bool blocked)
        {
            canFire = false;
            blocked = false;
            if (turretComp == null || turretComp.Turrets == null) return;

            foreach (var turret in turretComp.Turrets)
            {
                if (!turret.InRange(new LocalTargetInfo(target))) continue;

                if (turret.AngleBetween(target.DrawPos))
                    canFire = true;
                else
                    blocked = true;
            }
        }

        private bool TryGetRestrictedTurretAngle(VehiclePawn vehicle, Thing target, out float angle)
        {
            angle = 0f;
            var turretComp = vehicle.CompVehicleTurrets;
            if (turretComp == null || turretComp.Turrets == null) return false;
            foreach (var turret in turretComp.Turrets)
            {
                if (turret.angleRestricted != Vector2.zero && turret.InRange(new LocalTargetInfo(target)))
                {
                    float min = turret.angleRestricted.x;
                    float max = turret.angleRestricted.y;
                    if (min > max)
                    {
                        float diff = (360f - min) + max;
                        angle = (min + diff / 2f) % 360f;
                    }
                    else
                    {
                        angle = (min + max) / 2f;
                    }
                    return true;
                }
            }
            return false;
        }

        private IntVec3 FindTurretAlignedCell(VehiclePawn vehicle, Thing target, float maxRange, float minRange, float idealTurretAngle)
        {
            Map map = vehicle.Map;
            Vector3 toTarget = (target.DrawPos - vehicle.DrawPos).normalized;
            Vector3 idealFacing = Quaternion.Euler(0f, -idealTurretAngle, 0f) * toTarget;

            float currentDist = vehicle.Position.DistanceTo(target.Position);
            bool tooClose = currentDist < minRange + 1f;

            Vector3 baseDir = tooClose ? -idealFacing : idealFacing;

            for (int i = 0; i < 16; i++)
            {
                float offsetAngle = (i == 0) ? 0f : ((i % 2 == 1) ? 1f : -1f) * ((i + 1) / 2) * 22.5f;
                Vector3 moveDir = Quaternion.Euler(0f, offsetAngle, 0f) * baseDir;

                for (float d = 3f; d <= 12f; d += 1.5f)
                {
                    IntVec3 candidate = vehicle.Position + (moveDir * d).ToIntVec3();
                    if (!candidate.InBounds(map) || !candidate.Standable(map)) continue;
                    if (candidate == vehicle.Position) continue;

                    float distToTarget = candidate.DistanceTo(target.Position);
                    if (distToTarget < minRange + 1f || distToTarget > maxRange) continue;

                    bool vehicleBlocked = false;
                    foreach (Thing t in candidate.GetThingList(map))
                    {
                        if (t is VehiclePawn v && v != vehicle) { vehicleBlocked = true; break; }
                    }
                    if (vehicleBlocked) continue;

                    if (vehicle.CanReachVehicle(new LocalTargetInfo(candidate), PathEndMode.OnCell, Danger.Deadly, TraverseMode.NoPassClosedDoors))
                        return candidate;
                }
            }

            return IntVec3.Invalid;
        }

        private IntVec3 FindRetreatCell(VehiclePawn vehicle, Thing threat, float idealRange, float maxRange, float minRange, bool hasRestrictedTurrets, float idealTurretAngle)
        {
            Map map = vehicle.Map;
            Vector3 awayFromThreat = (vehicle.DrawPos - threat.DrawPos).normalized;

            var candidates = new List<KeyValuePair<IntVec3, float>>();

            for (int i = 0; i < 16; i++)
            {
                float offsetAngle = (i == 0) ? 0f : ((i % 2 == 1) ? 1f : -1f) * ((i + 1) / 2) * 22.5f;
                Vector3 moveDir = Quaternion.Euler(0f, offsetAngle, 0f) * awayFromThreat;

                for (float d = 3f; d <= maxRange; d += 2f)
                {
                    IntVec3 candidate = vehicle.Position + (moveDir * d).ToIntVec3();
                    if (!candidate.InBounds(map) || !candidate.Standable(map)) continue;
                    if (candidate == vehicle.Position) continue;

                    float distToThreat = candidate.DistanceTo(threat.Position);
                    if (distToThreat < minRange + 1f || distToThreat > maxRange) continue;

                    bool vehicleBlocked = false;
                    foreach (Thing t in candidate.GetThingList(map))
                    {
                        if (t is VehiclePawn v && v != vehicle) { vehicleBlocked = true; break; }
                    }
                    if (vehicleBlocked) continue;

                    float rangeScore = Mathf.Abs(distToThreat - idealRange);
                    float angleScore = Mathf.Abs(offsetAngle) * 0.1f;
                    float score = rangeScore + angleScore + Rand.Range(0f, 3f);

                    if (hasRestrictedTurrets)
                    {
                        Vector3 approachDir = (candidate.ToVector3Shifted() - vehicle.Position.ToVector3Shifted());
                        Vector3 cellToThreat = (threat.DrawPos - candidate.ToVector3Shifted());
                        approachDir.y = 0f;
                        cellToThreat.y = 0f;
                        if (approachDir.sqrMagnitude > 1f && cellToThreat.sqrMagnitude > 1f)
                        {
                            Vector3 idealFacingDir = Quaternion.Euler(0f, -idealTurretAngle, 0f) * cellToThreat;
                            float alignment = Vector3.Dot(approachDir.normalized, idealFacingDir.normalized);
                            score += (1f - alignment) * 15f;
                        }
                    }

                    candidates.Add(new KeyValuePair<IntVec3, float>(candidate, score));
                }
            }

            candidates.Sort((a, b) => a.Value.CompareTo(b.Value));

            int pathChecks = 0;
            foreach (var kvp in candidates)
            {
                if (pathChecks++ >= 6) break;
                if (vehicle.CanReachVehicle(new LocalTargetInfo(kvp.Key), PathEndMode.OnCell, Danger.Deadly, TraverseMode.NoPassClosedDoors))
                    return kvp.Key;
            }

            return IntVec3.Invalid;
        }
    }

    public class VRF_JobGiver_VehicleExitMap : ThinkNode_JobGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is VehiclePawn vehicle) || !vehicle.Spawned || vehicle.Map == null) return null;

            var hoverComp = vehicle.GetComp<VehicleRaid.CompVehicleHover>();
            if (hoverComp != null && hoverComp.State == VehicleRaid.HoverState.Hovering) return null;

            if (!CrewManager.CanMove(vehicle))
            {
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 2000, true);
            }

            if (CrewManager.IsAnyPawnBoarding(vehicle) && !CrewManager.HasOperationalDriver(vehicle))
            {
                if (vehicle.CurJob != null && vehicle.CurJob.def == JobDefOf.Wait_Combat) return null;
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 500, true);
            }

            IntVec3 exitCell;
            if (VehicleTrafficManager.TryFindExitCell(vehicle, out exitCell))
            {
                if (!vehicle.CanReachVehicle(new LocalTargetInfo(exitCell), PathEndMode.OnCell, Danger.Deadly, TraverseMode.NoPassClosedDoors))
                {
                    return JobMaker.MakeJob(JobDefOf.Wait_Combat, 300, true);
                }
                return CreateExitJob(exitCell);
            }

            return JobMaker.MakeJob(JobDefOf.Wait_Combat, 300, true);
        }

        private Job CreateExitJob(IntVec3 target)
        {
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("VRF_VehicleExitMap"), target);
            job.exitMapOnArrival = true;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }
    }

    public class JobDriver_VehicleExitMap : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            this.pawn.Map.pawnDestinationReservationManager.Reserve(this.pawn, this.job, this.job.targetA.Cell);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            Toil gotoToil = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            gotoToil.tickAction = () => { if (pawn is VehiclePawn v && v.Position.DistanceToSquared(TargetA.Cell) <= 4) PathingHelper.ExitMapForVehicle(v, job); };
            yield return gotoToil;

            Toil forceExit = ToilMaker.MakeToil();
            forceExit.initAction = () => { if (pawn is VehiclePawn v) PathingHelper.ExitMapForVehicle(v, job); };
            yield return forceExit;
        }
    }

    public class LordToil_VehicleHoldPosition : LordToil
    {
        private IntVec3 holdSpot = IntVec3.Invalid;

        private Dictionary<int, IntVec3> vehicleSlots = new Dictionary<int, IntVec3>();
        private Dictionary<int, float> airplaneOrbitAngles = new Dictionary<int, float>();

        private const float InfantryDefendRadius = 28f;
        private const float ReturnThreshold      = 12f;
        private const float SlotSeparation       = 7f;
        private const int   OrbitUpdateInterval  = 30;

        public override bool ForceHighStoryDanger  => true;
        public override bool AllowSatisfyLongNeeds => false;
        public override IntVec3 FlagLoc => holdSpot.IsValid ? holdSpot : base.FlagLoc;

        public override void Init()
        {
            base.Init();
            holdSpot = ComputeHoldSpot();
            LessonAutoActivator.TeachOpportunity(ConceptDefOf.Drafting, OpportunityType.Critical);
        }

        private IntVec3 ComputeHoldSpot()
        {
            var vJob = this.lord?.LordJob as LordJob_VehicleRaid;
            if (vJob?.naturalRaidLord != null)
            {
                LordToil parentToil = vJob.naturalRaidLord.CurLordToil;
                if (parentToil != null)
                {
                    IntVec3 flag = parentToil.FlagLoc;
                    if (flag.IsValid && flag.InBounds(this.lord.Map))
                    {
                        Log.Message($"[VRF_Debug] ComputeHoldSpot — using parent FlagLoc={flag} (toil={parentToil.GetType().Name})");
                        return flag;
                    }
                }
            }

            if (this.lord == null || this.lord.ownedPawns.Count == 0)
                return IntVec3.Invalid;

            long sumX = 0, sumZ = 0;
            int  count = 0;
            foreach (Pawn p in this.lord.ownedPawns)
            {
                if (!p.Spawned || p.Dead) continue;
                sumX += p.Position.x;
                sumZ += p.Position.z;
                count++;
            }
            IntVec3 centroid = count > 0
                ? new IntVec3((int)(sumX / count), 0, (int)(sumZ / count))
                : this.lord.ownedPawns[0].Position;
            Log.Message($"[VRF_Debug] ComputeHoldSpot — using centroid={centroid} (no parent FlagLoc)");
            return centroid;
        }

        private IntVec3 AssignSlot(VehiclePawn vehicle, List<IntVec3> takenSlots)
        {
            if (!holdSpot.IsValid) return IntVec3.Invalid;
            Map map = this.lord.Map;

            var hoverComp = vehicle.GetComp<VehicleRaid.CompVehicleHover>();
            bool isHover = hoverComp != null;

            if (isHover)
            {
                IntVec3 spawnCell = vehicle.Position;
                if (spawnCell.IsValid && spawnCell.InBounds(map))
                    return spawnCell;
                return holdSpot;
            }

            int   vHalf  = Mathf.Max(vehicle.def.size.x, vehicle.def.size.z) / 2 + 1;
            float minSep = Mathf.Max(SlotSeparation, vHalf * 2f + 2f);

            for (float radius = minSep; radius <= 40f; radius += minSep)
            {
                int   samples = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * radius / minSep));
                float offset  = (Mathf.RoundToInt(radius / minSep) % 2 == 0) ? 0f : Mathf.PI / samples;

                for (int i = 0; i < samples; i++)
                {
                    float   angle     = offset + 2f * Mathf.PI * i / samples;
                    IntVec3 candidate = holdSpot + new IntVec3(
                        Mathf.RoundToInt(Mathf.Cos(angle) * radius),
                        0,
                        Mathf.RoundToInt(Mathf.Sin(angle) * radius));

                    if (!candidate.InBounds(map)) continue;

                    if (!vehicle.DrivableRectOnCell(candidate,
                            Ext_Vehicles.DestinationHitboxReq.AnyRotation)) continue;

                    bool tooClose = false;
                    foreach (IntVec3 taken in takenSlots)
                    {
                        if (candidate.DistanceTo(taken) < minSep) { tooClose = true; break; }
                    }
                    if (tooClose) continue;

                    if (!vehicle.CanReachVehicle(new LocalTargetInfo(candidate),
                            PathEndMode.OnCell, Danger.Deadly,
                            TraverseMode.NoPassClosedDoors)) continue;

                    return candidate;
                }
            }
            return IntVec3.Invalid;
        }

        public override void UpdateAllDuties()
        {
            if (!holdSpot.IsValid)
                holdSpot = ComputeHoldSpot();

            Log.Message($"[VRF_Debug] LordToil_VehicleHoldPosition.UpdateAllDuties — lord={this.lord?.faction?.def?.defName} holdSpot={holdSpot} pawnCount={this.lord?.ownedPawns?.Count}");

            var vJob = this.lord.LordJob as LordJob_VehicleRaid;
            if (vJob != null) vJob.updatingDuties = true;

            try
            {
                DutyDef vehicleHoldDuty =
                    VRF_DutyDefOf.VRF_VehicleHoldStrict
                    ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleHoldStrict", false)
                    ?? VRF_DutyDefOf.VRF_VehicleDefendBase
                    ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleDefendBase", false);

                foreach (Pawn pawn in this.lord.ownedPawns)
                {
                    if (pawn.Dead || pawn.Downed) continue;

                    if (pawn is VehiclePawn v)
                    {
                        if (v.mindState.duty?.def == DutyDefOf.ExitMapBest ||
                            v.mindState.duty?.def?.defName == "VRF_VehicleExitMap")
                            continue;

                        if (!vehicleSlots.TryGetValue(v.thingIDNumber, out IntVec3 slot) || !slot.IsValid)
                        {
                            var takenSlots = new List<IntVec3>(vehicleSlots.Values.Where(s => s.IsValid));
                            slot = AssignSlot(v, takenSlots);
                            if (!slot.IsValid) slot = holdSpot; 
                            vehicleSlots[v.thingIDNumber] = slot;
                        }

                        var hoverComp = v.GetComp<VehicleRaid.CompVehicleHover>();
                        bool isHoverAirborne = hoverComp?.IsAirborne == true;

                        if (isHoverAirborne)
                        {
                            DutyDef hoverHoldDuty =
                                VRF_DutyDefOf.VRF_VehicleHoldStrict
                                ?? DefDatabase<DutyDef>.GetNamed("VRF_VehicleHoldStrict", false)
                                ?? VRF_DutyDefOf.VRF_VehicleDefendBase;

                            if (hoverHoldDuty != null &&
                                (v.mindState.duty?.def != hoverHoldDuty ||
                                 v.mindState.duty?.focus.Cell != slot))
                            {
                                var duty = new PawnDuty(hoverHoldDuty, new LocalTargetInfo(slot));
                                duty.radius = 24f;
                                v.mindState.duty = duty;

                                bool isAirplane = hoverComp.FlightType == VehicleRaid.FlightType.Airplane;
                                if (!isAirplane)
                                {
                                    hoverComp.SetTarget(slot.ToVector3Shifted());
                                }
                                else if (!airplaneOrbitAngles.ContainsKey(v.thingIDNumber))
                                {
                                    airplaneOrbitAngles[v.thingIDNumber] = hoverComp.currentFlyAngle;
                                }

                                Log.Message($"[VRF_Debug]   Assigning hover hold duty '{hoverHoldDuty.defName}' slot={slot} isAirplane={isAirplane} to {v.LabelShort}");
                            }
                        }
                        else if (vehicleHoldDuty != null &&
                            (v.mindState.duty?.def != vehicleHoldDuty ||
                             v.mindState.duty?.focus.Cell != slot))
                        {
                            var duty = new PawnDuty(vehicleHoldDuty, new LocalTargetInfo(slot));
                            duty.radius = 8f;
                            v.mindState.duty = duty;
                            Log.Message($"[VRF_Debug]   Assigning hold duty '{vehicleHoldDuty.defName}' slot={slot} to {v.LabelShort}");

                            if (slot.IsValid && CrewManager.CanMove(v) &&
                                v.Position.DistanceTo(slot) > 8f)
                            {
                                IntVec3 dest = VehicleRaidUtility.FixDestination(v, slot);
                                if (dest.IsValid && v.CanReachVehicle(
                                        new LocalTargetInfo(dest), PathEndMode.OnCell,
                                        Danger.Deadly, TraverseMode.NoPassClosedDoors))
                                {
                                    Job moveJob = JobMaker.MakeJob(JobDefOf.Goto, dest);
                                    moveJob.expiryInterval = 3000;
                                    moveJob.checkOverrideOnExpire = true;
                                    v.jobs?.StartJob(moveJob, JobCondition.InterruptForced,
                                        null, resumeCurJobAfterwards: false, cancelBusyStances: true);
                                    Log.Message($"[VRF_Debug]   → Moving {v.LabelShort} to slot={dest}");
                                }
                            }
                        }
                        else
                        {
                            Log.Message($"[VRF_Debug]   {v.LabelShort} OK at slot={slot} duty={v.mindState?.duty?.def?.defName} job={v.CurJobDef?.defName}");
                        }
                    }
                    else
                    {
                        if (pawn.mindState.duty?.def != DutyDefOf.Defend ||
                            pawn.mindState.duty.focus.Cell != holdSpot)
                        {
                            pawn.mindState.duty = new PawnDuty(
                                DutyDefOf.Defend,
                                new LocalTargetInfo(holdSpot),
                                InfantryDefendRadius);
                        }
                    }
                }
            }
            finally
            {
                if (vJob != null) vJob.updatingDuties = false;
            }
        }

        public override void Notify_PawnLost(Pawn p, PawnLostCondition condition)
        {
            base.Notify_PawnLost(p, condition);
            vehicleSlots.Remove(p.thingIDNumber);
            var vJob = this.lord.LordJob as LordJob_VehicleRaid;
            if (p is VehiclePawn && !(vJob?.updatingDuties ?? false))
                UpdateAllDuties();
        }

        public override void LordToilTick()
        {
            base.LordToilTick();

            int tick = this.lord.ticksInToil;

            if (tick % OrbitUpdateInterval == 0)
            {
                foreach (Pawn pawn in this.lord.ownedPawns)
                {
                    if (!(pawn is VehiclePawn v)) continue;
                    if (!v.Spawned || v.Dead) continue;

                    var hoverComp = v.GetComp<VehicleRaid.CompVehicleHover>();
                    if (hoverComp == null || !hoverComp.IsAirborne) continue;
                    if (hoverComp.FlightType != VehicleRaid.FlightType.Airplane) continue;

                    if (v.mindState.duty?.def?.defName != "VRF_VehicleHoldStrict" &&
                        v.mindState.duty?.def?.defName != "VRF_VehicleDefendBase") continue;

                    IntVec3 orbitCenter = vehicleSlots.TryGetValue(v.thingIDNumber, out IntVec3 s) && s.IsValid
                        ? s : (holdSpot.IsValid ? holdSpot : v.Position);

                    float speedPerTick = hoverComp.Props.hoverMoveSpeed / 60f;
                    float rotSpeedRad  = hoverComp.Props.hoverRotationSpeed * Mathf.Deg2Rad / 60f;
                    float minRadius    = rotSpeedRad > 0f ? speedPerTick / rotSpeedRad : 20f;
                    float orbitRadius  = Mathf.Max(minRadius * 1.5f, 18f); 

                    float arcPerInterval = (speedPerTick * OrbitUpdateInterval) / orbitRadius; 
                    float arcDeg = arcPerInterval * Mathf.Rad2Deg;

                    if (!airplaneOrbitAngles.TryGetValue(v.thingIDNumber, out float orbitAngle))
                    {
                        orbitAngle = hoverComp.currentFlyAngle;
                        airplaneOrbitAngles[v.thingIDNumber] = orbitAngle;
                    }

                    orbitAngle = (orbitAngle + arcDeg) % 360f;
                    airplaneOrbitAngles[v.thingIDNumber] = orbitAngle;

                    float rad = orbitAngle * Mathf.Deg2Rad;
                    Vector3 nextWaypoint = new Vector3(
                        orbitCenter.x + 0.5f + Mathf.Sin(rad) * orbitRadius,
                        0f,
                        orbitCenter.z + 0.5f + Mathf.Cos(rad) * orbitRadius);

                    if (v.Map != null)
                    {
                        float mg = 6f;
                        nextWaypoint.x = Mathf.Clamp(nextWaypoint.x, mg, v.Map.Size.x - mg);
                        nextWaypoint.z = Mathf.Clamp(nextWaypoint.z, mg, v.Map.Size.z - mg);
                    }

                    hoverComp.SetTarget(nextWaypoint);
                }
            }

            if (tick % 120 != 0) return;

            var vJob = this.lord.LordJob as LordJob_VehicleRaid;
            if (vJob?.naturalRaidLord == null)
            {
                if (tick % 600 == 0)
                    Log.Message($"[VRF_Debug] HoldTick — naturalRaidLord is NULL, ticksInToil={tick}");
                return;
            }

            LordToil parentToil = vJob.naturalRaidLord.CurLordToil;
            string toilName = parentToil?.GetType().Name ?? "NULL";

            if (tick % 600 == 0)
                Log.Message($"[VRF_Debug] HoldTick — ticksInToil={tick} parentLord={vJob.naturalRaidLord.LordJob?.GetType().Name} parentToil={toilName}");

            if (parentToil == null) return;

            bool parentIsAssaulting =
                toilName.Contains("Assault") ||
                toilName.Contains("Attack")  ||
                toilName.Contains("Exit")    ||
                toilName.Contains("Leave")   ||
                toilName.Contains("Flee")    ||
                toilName.Contains("Sapper")  ||
                toilName.Contains("Breach");

            if (parentIsAssaulting)
            {
                Log.Message($"[VRF_Debug] HoldTick — parent is assaulting ({toilName}), sending VRF_ParentAssaulting memo");
                this.lord.ReceiveMemo("VRF_ParentAssaulting");
                return;
            }

            if (!holdSpot.IsValid) return;
            foreach (Pawn pawn in this.lord.ownedPawns)
            {
                if (!(pawn is VehiclePawn v)) continue;
                if (!v.Spawned || v.Dead || !CrewManager.CanMove(v)) continue;
                if (v.mindState.duty?.def?.defName == "VRF_VehicleExitMap") continue;

                IntVec3 target = vehicleSlots.TryGetValue(v.thingIDNumber, out IntVec3 s) && s.IsValid
                    ? s : holdSpot;
                if (!target.IsValid) continue;

                var hoverComp = v.GetComp<VehicleRaid.CompVehicleHover>();
                bool isHoverAirborne = hoverComp?.IsAirborne == true;

                if (isHoverAirborne)
                {
                    bool isAirplane = hoverComp.FlightType == VehicleRaid.FlightType.Airplane;
                    if (!isAirplane)
                    {
                        Vector2 realPos = hoverComp.realPos;
                        Vector2 targetV2 = new Vector2(target.x + 0.5f, target.z + 0.5f);
                        float dist = Vector2.Distance(realPos, targetV2);
                        if (dist > 4f)
                        {
                            hoverComp.SetTarget(target.ToVector3Shifted());
                            Log.Message($"[VRF_Debug] HoldTick — redirecting hover {v.LabelShort} back to slot={target} (dist={dist:F1})");
                        }
                    }
                }
                else
                {
                    if (v.CurJobDef == JobDefOf.Goto && v.vehiclePather?.Moving == true) continue;

                    float distToTarget = v.Position.DistanceTo(target);

                    if (distToTarget <= ReturnThreshold) continue;

                    if (v.CurJobDef != JobDefOf.Goto)
                    {
                        IntVec3 snapped = VehicleRaidUtility.FixDestination(v, v.Position);
                        if (!snapped.IsValid) snapped = v.Position;
                        vehicleSlots[v.thingIDNumber] = snapped;

                        if (v.mindState.duty != null)
                            v.mindState.duty = new PawnDuty(v.mindState.duty.def,
                                new LocalTargetInfo(snapped)) { radius = 8f };

                        Log.Message($"[VRF_Debug] HoldTick — {v.LabelShort} could not reach slot={target}, snapping to pos={snapped}");
                        continue;
                    }

                    IntVec3 dest = VehicleRaidUtility.FixDestination(v, target);
                    if (!dest.IsValid) continue;
                    if (!v.CanReachVehicle(new LocalTargetInfo(dest), PathEndMode.OnCell,
                            Danger.Deadly, TraverseMode.NoPassClosedDoors)) continue;

                    Job returnJob = JobMaker.MakeJob(JobDefOf.Goto, dest);
                    returnJob.expiryInterval = 3000;
                    returnJob.checkOverrideOnExpire = true;
                    v.jobs?.StartJob(returnJob, JobCondition.InterruptForced,
                        null, resumeCurJobAfterwards: false, cancelBusyStances: true);
                }
            }
        }
    }
}