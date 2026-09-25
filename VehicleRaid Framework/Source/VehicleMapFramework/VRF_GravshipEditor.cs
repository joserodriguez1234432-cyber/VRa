using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using HarmonyLib;
using Vehicles;

namespace VehicleRaidFramework.VehicleMapFramework
{
    /// <summary>
    /// Harmony patch for Building_GravEngine.GetGizmos.
    /// Adds a Dev-mode button to save the custom Gravship design when VehicleMap Framework & Odyssey are active.
    /// </summary>
    [HarmonyPatch(typeof(Building_GravEngine), nameof(Building_GravEngine.GetGizmos))]
    public static class Patch_Building_GravEngine_EditorGizmo
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building_GravEngine __instance)
        {
            foreach (var g in __result)
            {
                yield return g;
            }

            // Show dev gizmo only if Dev Mode is enabled and Odyssey DLC is active
            if (DebugSettings.ShowDevGizmos && ModsConfig.OdysseyActive && __instance != null && __instance.Spawned)
            {
                var saveCommand = new Command_Action
                {
                    defaultLabel = "DEV: Save Gravship Design",
                    defaultDesc = "Saves the current connected Gravship structure, components, and layout to a JSON preset for raids.",
                    action = () =>
                    {
                        VRF_GravshipPresetUtility.SaveGravshipPreset(__instance);
                    }
                };
                yield return saveCommand;
            }
        }
    }

    /// <summary>
    /// Inject a dev-mode designator into the Zone DesignationCategoryDef (Arquitectura > Zonas/Áreas)
    /// </summary>
    [HarmonyPatch(typeof(DesignationCategoryDef), nameof(DesignationCategoryDef.ResolvedAllowedDesignators), MethodType.Getter)]
    public static class Patch_DesignationCategoryDef_GravshipPicker
    {
        private static Designator_SpawnGravshipPreset cachedDesignator;

        [HarmonyPostfix]
        public static IEnumerable<Designator> Postfix(IEnumerable<Designator> __result, DesignationCategoryDef __instance)
        {
            foreach (var d in __result)
            {
                yield return d;
            }

            if (DebugSettings.ShowDevGizmos && ModsConfig.OdysseyActive && __instance == DesignationCategoryDefOf.Zone)
            {
                if (cachedDesignator == null)
                {
                    cachedDesignator = new Designator_SpawnGravshipPreset();
                }
                yield return cachedDesignator;
            }
        }
    }

    /// <summary>
    /// Designator button in Zone menu that opens the Gravship preset picker dialog.
    /// </summary>
    public class Designator_SpawnGravshipPreset : Designator
    {
        public Designator_SpawnGravshipPreset()
        {
            defaultLabel = "DEV: Spawn Gravship Preset";
            defaultDesc = "Opens a list of all custom Gravship presets to test and spawn on the map.";
            useMouseIcon = false;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            return false;
        }

        public override void ProcessInput(Event ev)
        {
            if (!DebugSettings.ShowDevGizmos || !ModsConfig.OdysseyActive) return;
            Find.WindowStack.Add(new Dialog_GravshipPresetPicker());
        }
    }

    /// <summary>
    /// Window listing all available Gravship presets with a button to spawn them at the center of the current map.
    /// </summary>
    public class Dialog_GravshipPresetPicker : Window
    {
        private Vector2 scrollPosition;
        private List<string> presetFiles;

        public override Vector2 InitialSize => new Vector2(550f, 600f);

        public Dialog_GravshipPresetPicker()
        {
            doCloseButton = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            presetFiles = VRF_GravshipPresetUtility.FindAllGravshipPresetFiles();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "Select Gravship Preset");
            Text.Font = GameFont.Small;

            Rect outRect = new Rect(0f, 45f, inRect.width, inRect.height - 95f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, presetFiles.Count * 65f);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float curY = 0f;

            if (presetFiles.Count == 0)
            {
                Widgets.Label(new Rect(10f, 10f, viewRect.width, 30f), "No Gravship presets found in Mods/ or Config/ folders.");
            }

            for (int i = 0; i < presetFiles.Count; i++)
            {
                string filePath = presetFiles[i];
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                Rect rowRect = new Rect(0f, curY, viewRect.width, 55f);
                Widgets.DrawBoxSolid(rowRect, i % 2 == 0 ? new Color(0.2f, 0.2f, 0.2f, 0.4f) : new Color(0.25f, 0.25f, 0.25f, 0.4f));

                Rect labelRect = new Rect(10f, curY + 12f, rowRect.width - 140f, 30f);
                Widgets.Label(labelRect, fileName);

                Rect spawnBtnRect = new Rect(rowRect.width - 120f, curY + 10f, 110f, 35f);
                if (Widgets.ButtonText(spawnBtnRect, "Spawn Here"))
                {
                    var data = VRF_GravshipPresetUtility.LoadGravshipPresetFromFile(filePath);
                    if (data != null)
                    {
                        VRF_GravshipPresetUtility.SpawnGravshipPresetOnMap(data, Find.CurrentMap, UI.MouseCell().IsValid ? UI.MouseCell() : Find.CurrentMap.Center);
                        Close();
                    }
                    else
                    {
                        Messages.Message($"Failed to load preset file: {fileName}", MessageTypeDefOf.RejectInput, false);
                    }
                }

                curY += 60f;
            }

            Widgets.EndScrollView();
        }
    }

    /// <summary>
    /// Handles serialization and saving of custom Gravship blueprints/presets.
    /// </summary>
    public static class VRF_GravshipPresetUtility
    {
        public static System.Reflection.PropertyInfo GetPropertySafe(System.Type type, string name)
        {
            if (type == null) return null;
            System.Type current = type;
            while (current != null && current != typeof(object))
            {
                var prop = current.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (prop != null) return prop;
                current = current.BaseType;
            }
            return null;
        }

        public static System.Reflection.FieldInfo GetFieldSafe(System.Type type, string name)
        {
            if (type == null) return null;
            System.Type current = type;
            while (current != null && current != typeof(object))
            {
                var field = current.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                if (field != null) return field;
                current = current.BaseType;
            }
            return null;
        }

        public static bool IsVMFActive => ModsConfig.OdysseyActive && LoadedModManager.RunningModsListForReading.Any(m => m.PackageIdPlayerFacing.ToLower().Contains("vehiclemap") || m.Name.ToLower().Contains("vehicle map"));

        public static string PresetsFolder => Path.Combine(GenFilePaths.ConfigFolderPath, "VehicleRaidFramework", "GravshipPresets");

        public static void SaveGravshipPreset(Building_GravEngine engine)
        {
            if (engine == null || !engine.Spawned)
            {
                Messages.Message("Cannot save gravship: Engine is invalid or unspawned.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                if (!Directory.Exists(PresetsFolder))
                {
                    Directory.CreateDirectory(PresetsFolder);
                }

                string shipName = engine.RenamableLabel ?? "CustomGravship";
                string fileName = $"{SanitizeFileName(shipName)}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string fullPath = Path.Combine(PresetsFolder, fileName);

                var presetData = ExtractPresetData(engine, shipName);
                string json = json_Serialize(presetData);

                File.WriteAllText(fullPath, json);

                Messages.Message($"Gravship preset saved to: {fileName}", MessageTypeDefOf.PositiveEvent, false);
                VRF_Log.Msg($"Saved Gravship preset successfully to {fullPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[VehicleRaidFramework] Error saving Gravship preset: {ex}");
            }
        }

        /// <summary>
        /// Creates an unspawned VehiclePawnWithMap from a gravship preset.
        /// The vehicle's interior map is populated with the preset's terrain and buildings.
        /// Returns null if creation fails. The caller must GenSpawn.Spawn the result.
        /// </summary>
        public static global::VehicleMapFramework.VehiclePawnWithMap CreateGravshipVehicleFromPreset(
            VRF_GravshipPresetData data,
            Faction faction,
            Vehicles.VehicleDef baseDef = null)
        {
            if (data == null || data.cells.Count == 0)
            {
                Log.Warning("[VehicleRaidFramework] CreateGravshipVehicleFromPreset: null data or empty cells.");
                return null;
            }

            try
            {
                // --- 1. Calculate bounding rect from preset cells ---
                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;
                var cellSet = new HashSet<long>(); // packed (x,z) for fast lookup

                foreach (var c in data.cells)
                {
                    if (c.offsetX < minX) minX = c.offsetX;
                    if (c.offsetX > maxX) maxX = c.offsetX;
                    if (c.offsetZ < minZ) minZ = c.offsetZ;
                    if (c.offsetZ > maxZ) maxZ = c.offsetZ;
                    cellSet.Add(PackCoord(c.offsetX, c.offsetZ));
                }

                int sizeX = maxX - minX + 1;
                int sizeZ = maxZ - minZ + 1;

                VRF_Log.Msg($"CreateGravshipVehicleFromPreset: preset='{data.presetName}', cells={data.cells.Count}, buildings={data.buildings.Count}, boundingSize=({sizeX},{sizeZ}), offset=({minX},{minZ})");

                // --- 2. Calculate outOfBoundsCells (cells in bounding rect NOT in preset) ---
                var outOfBounds = new List<IntVec2>();
                for (int x = 0; x < sizeX; x++)
                {
                    for (int z = 0; z < sizeZ; z++)
                    {
                        if (!cellSet.Contains(PackCoord(x + minX, z + minZ)))
                        {
                            outOfBounds.Add(new IntVec2(x, z));
                        }
                    }
                }

                // --- 3. Resolve base VehicleDef ---
                Faction targetFaction = faction ?? Faction.OfPlayer;
                Vehicles.VehicleDef resolvedBaseDef = baseDef;
                global::VehicleMapFramework.VehiclePawnWithMap vehiclePawn = null;

                // Check whether this vehicle needs dynamic gravship def generation (VMF_GravshipVehicleBase)
                // or if it is already a concrete VehicleDef with interior map (like TMC_Dreadnought).
                bool hasPlaceholders = resolvedBaseDef != null &&
                    global::VehicleMapFramework.UniqueVehicleManager.PlaceholderDefs != null &&
                    global::VehicleMapFramework.UniqueVehicleManager.PlaceholderDefs.ContainsKey(resolvedBaseDef);

                bool isConcreteVehicle = resolvedBaseDef != null &&
                    resolvedBaseDef.defName != "VMF_GravshipVehicleBase" &&
                    typeof(global::VehicleMapFramework.VehiclePawnWithMap).IsAssignableFrom(((ThingDef)resolvedBaseDef).thingClass) &&
                    !hasPlaceholders;

                if (isConcreteVehicle)
                {
                    // --- 4a. Concrete vehicle with interior map: spawn directly with its own intact graphic and def ---
                    VRF_Log.Msg($"CreateGravshipVehicleFromPreset: Spawning concrete vehicle def {resolvedBaseDef.defName} directly.");
                    vehiclePawn = Vehicles.VehicleSpawner.GenerateVehicle(resolvedBaseDef, targetFaction) as global::VehicleMapFramework.VehiclePawnWithMap;
                    if (vehiclePawn == null)
                    {
                        Log.Error($"[VehicleRaidFramework] VehicleSpawner.GenerateVehicle returned null for {resolvedBaseDef.defName}.");
                        return null;
                    }
                }
                else
                {
                    // --- 4b. Procedural Gravship: uses VMF_GravshipVehicleBase with dynamic placeholder defs ---
                    if (resolvedBaseDef == null || resolvedBaseDef.defName != "VMF_GravshipVehicleBase")
                    {
                        resolvedBaseDef = DefDatabase<Vehicles.VehicleDef>.GetNamedSilentFail("VMF_GravshipVehicleBase");
                    }
                    if (resolvedBaseDef == null)
                    {
                        Log.Error("[VehicleRaidFramework] Cannot find VMF_GravshipVehicleBase VehicleDef. Vehicle Map Framework may not be loaded correctly.");
                        return null;
                    }

                    var props = new global::VehicleMapFramework.VehicleMapProps_Gravship();
                    props.baseDef = resolvedBaseDef;
                    props.size = new IntVec2(sizeX, sizeZ);
                    props.offset = new UnityEngine.Vector3(0f, 0f, 0.25f);
                    props.outOfBoundsCells = outOfBounds;

                    Vehicles.VehicleDef vehicleDef = global::VehicleMapFramework.GravshipVehicleUtility.GenerateGravshipVehicleDef(props);
                    if (vehicleDef == null)
                    {
                        Log.Error("[VehicleRaidFramework] GenerateGravshipVehicleDef returned null.");
                        return null;
                    }

                    // Force immediate initialization of graphicData so VehiclePawn.GenerateGraphic never hits a null Graphic
                    try
                    {
                        vehicleDef.graphicData?.Init(vehicleDef);
                    }
                    catch (Exception gEx)
                    {
                        VRF_Log.Msg($"Notice: graphicData.Init: {gEx.Message}");
                    }

                    // --- 5. Generate VehiclePawnWithMap (unspawned) ---
                    vehiclePawn = (global::VehicleMapFramework.VehiclePawnWithMap)Vehicles.VehicleSpawner.GenerateVehicle(vehicleDef, targetFaction);
                    if (vehiclePawn == null)
                    {
                        Log.Error("[VehicleRaidFramework] VehicleSpawner.GenerateVehicle returned null.");
                        return null;
                    }
                }

                // --- 6. Access interior map (triggers GenerateVehicleMap) ---
                Map vehicleMap = vehiclePawn.VehicleMap;
                if (vehicleMap == null)
                {
                    Log.Error("[VehicleRaidFramework] VehiclePawnWithMap.VehicleMap is null after generation.");
                    return null;
                }

                // --- 7. Populate interior map with preset data ---
                // Interior map has a 1-cell border. Content starts at (1, 0, 1).
                IntVec3 interiorOrigin = new IntVec3(1, 0, 1);

                // 7a. Set terrain/floors
                foreach (var cellData in data.cells)
                {
                    IntVec3 cell = interiorOrigin + new IntVec3(cellData.offsetX - minX, 0, cellData.offsetZ - minZ);
                    if (!cell.InBounds(vehicleMap)) continue;

                    TerrainDef tDef = DefDatabase<TerrainDef>.GetNamedSilentFail(cellData.terrainDef);
                    if (tDef == null) continue;

                    try
                    {
                        if (tDef.isFoundation)
                        {
                            int idx = vehicleMap.cellIndices.CellToIndex(cell);
                            if (vehicleMap.terrainGrid.UnderTerrainAt(idx) != null)
                                vehicleMap.terrainGrid.SetUnderTerrain(cell, null);
                            vehicleMap.terrainGrid.SetFoundation(cell, tDef);
                        }
                        else
                        {
                            vehicleMap.terrainGrid.SetTerrain(cell, tDef);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Non-fatal: skip terrain that can't be placed on vehicle map
                        VRF_Log.Msg($"  Terrain skip at ({cell.x},{cell.z}): {ex.Message}");
                    }
                }

                // 7b. Spawn buildings
                int spawnedCount = 0;
                foreach (var bData in data.buildings)
                {
                    IntVec3 cell = interiorOrigin + new IntVec3(bData.offsetX - minX, 0, bData.offsetZ - minZ);
                    if (!cell.InBounds(vehicleMap)) continue;

                    ThingDef bDef = DefDatabase<ThingDef>.GetNamedSilentFail(bData.defName);
                    if (bDef == null)
                    {
                        VRF_Log.Msg($"  Building def not found: {bData.defName}");
                        continue;
                    }

                    // Skip PlaceWorker_ForbidOnVehicle buildings
                    if (bDef.placeWorkers != null && bDef.placeWorkers.Any(pw => pw.GetType().Name == "PlaceWorker_ForbidOnVehicle"))
                    {
                        VRF_Log.Msg($"  Skipping ForbidOnVehicle building: {bData.defName}");
                        continue;
                    }

                    ThingDef stuffDef = bData.stuffDef != null ? DefDatabase<ThingDef>.GetNamedSilentFail(bData.stuffDef) : null;

                    try
                    {
                        Rot4 rot = new Rot4(bData.rotation);
                        Thing created = ThingMaker.MakeThing(bDef, stuffDef);
                        if (bDef.CanHaveFaction)
                            created.SetFaction(targetFaction);

                        GenSpawn.Spawn(created, cell, vehicleMap, rot, WipeMode.Vanish);
                        spawnedCount++;

                        // Fill batteries to full capacity
                        if (created is ThingWithComps twc)
                        {
                            var batteryComp = twc.GetComp<CompPowerBattery>();
                            if (batteryComp != null)
                                batteryComp.SetStoredEnergyPct(1f);
                        }

                        // Restore PipeSystem storage if saved
                        if (bData.pipeStoredResource > 0f)
                            TryRestorePipeStorage(created, bData.pipeStoredResource);
                    }
                    catch (Exception ex)
                    {
                        VRF_Log.Msg($"  Building spawn error '{bData.defName}' at ({cell.x},{cell.z}): {ex.Message}");
                    }
                }

                // --- 8. Force correct faction on ALL things in the vehicle map ---
                // This ensures enemy structures can't be reclaimed by the player
                foreach (Thing t in vehicleMap.listerThings.AllThings.ToList())
                {
                    if (t.def.CanHaveFaction && t.Faction != targetFaction)
                    {
                        t.SetFaction(targetFaction);
                    }
                }

                // --- 9. Force connection for GravEngine ---
                // Find the engine and force it to recognize the substructure
                Building_GravEngine gravEngine = null;
                foreach (Thing t in vehicleMap.listerThings.AllThings)
                {
                    if (t is Building_GravEngine eng)
                    {
                        eng.ForceSubstructureDirty();
                        gravEngine = eng;
                        break;
                    }
                }

                // --- 10. Fill fuel tanks directly ---
                // VGE tanks use PipeSystem.CompResourceStorage (not CompRefuelable).
                // Vanilla tanks use CompRefuelable.
                // We fill both here before spawn so the engine reads correct TotalFuel
                // when CompFueledTravelGravship.PostSpawnSetup syncs the vehicle field.
                float totalFuelAdded = 0f;

                foreach (Thing t in vehicleMap.listerThings.AllThings)
                {
                    if (!(t is ThingWithComps twcFuel)) continue;

                    // --- A. PipeSystem CompResourceStorage (VGE astrofuel tanks) ---
                    // Detect by type name to avoid a hard assembly dependency on PipeSystem.
                    foreach (var comp in twcFuel.AllComps)
                    {
                        if (comp.GetType().Name != "CompResourceStorage") continue;

                        var compType = comp.GetType();

                        // Read storageCapacity via Props
                        float capacity = 0f;
                        float stored   = 0f;
                        try
                        {
                            object storageProps = comp.props ?? GetPropertySafe(compType, "Props")?.GetValue(comp);
                            if (storageProps != null)
                            {
                                var capProp = GetPropertySafe(storageProps.GetType(), "storageCapacity");
                                var capField = GetFieldSafe(storageProps.GetType(), "storageCapacity");
                                if (capProp != null)
                                    capacity = Convert.ToSingle(capProp.GetValue(storageProps));
                                else if (capField != null)
                                    capacity = Convert.ToSingle(capField.GetValue(storageProps));
                            }

                            var amountProp = GetPropertySafe(compType, "AmountStored");
                            if (amountProp != null)
                                stored = Convert.ToSingle(amountProp.GetValue(comp));
                        }
                        catch { break; }

                        if (capacity <= 0f) break;

                        float toAdd = capacity - stored;
                        if (toAdd > 0f)
                        {
                            try
                            {
                                // Call AddResource(float) to fill
                                var addMethod = compType.GetMethod("AddResource",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                    null, new[] { typeof(float) }, null);
                                if (addMethod != null)
                                {
                                    addMethod.Invoke(comp, new object[] { toAdd });
                                    totalFuelAdded += toAdd;
                                    VRF_Log.Msg($"  Filled PipeSystem tank '{t.def.defName}': +{toAdd:F0} (cap={capacity:F0})");
                                }
                            }
                            catch { }
                        }
                        break; // only one storage comp per tank
                    }

                    // --- B. Vanilla CompRefuelable (ChemfuelTank, LargeChemfuelTank) ---
                    CompRefuelable refuelable = twcFuel.GetComp<CompRefuelable>();
                    if (refuelable == null) continue;

                    // Only fill if the building has providesFuel on its CompGravshipFacility
                    bool isFuelProvider = false;
                    foreach (var comp in twcFuel.AllComps)
                    {
                        try
                        {
                            var compType = comp.GetType();
                            if (compType.Name != "CompGravshipFacility" && compType.Name != "CompGravshipFacilityPossibly")
                                continue;
                            object facProps = comp.props ?? GetPropertySafe(compType, "Props")?.GetValue(comp);
                            if (facProps != null)
                            {
                                var fuelField = GetFieldSafe(facProps.GetType(), "providesFuel");
                                var fuelProp = GetPropertySafe(facProps.GetType(), "providesFuel");
                                object fuelVal = fuelField?.GetValue(facProps) ?? fuelProp?.GetValue(facProps);
                                if (fuelVal != null && Convert.ToBoolean(fuelVal))
                                    isFuelProvider = true;
                            }
                            break;
                        }
                        catch { }
                    }
                    // Fallback name-based detection
                    if (!isFuelProvider)
                    {
                        string dn = twcFuel.def?.defName ?? "";
                        isFuelProvider = dn.Contains("Tank") || dn.Contains("Fuel") || dn.Contains("fuel");
                    }

                    if (!isFuelProvider) continue;

                    float refCap = refuelable.Props?.fuelCapacity ?? 0f;
                    if (refCap <= 0f) continue;

                    float refToAdd = refCap - refuelable.Fuel;
                    if (refToAdd > 0f)
                    {
                        refuelable.Refuel(refToAdd);
                        totalFuelAdded += refToAdd;
                        VRF_Log.Msg($"  Filled CompRefuelable tank '{t.def.defName}': +{refToAdd:F0} (cap={refCap:F0})");
                    }
                }

                // Sync the vehicle's CompFueledTravel.Fuel.
                // gravEngine.TotalFuel is the ground-truth (VGE patches it to include PipeSystem tanks).
                // If engine is null before spawn, use the raw sum; PostSpawnSetup will re-sync anyway.
                {
                    var vehicleFuelComp = vehiclePawn.GetComp<CompFueledTravel>();
                    if (vehicleFuelComp != null)
                    {
                        float engineTotal = gravEngine != null ? gravEngine.TotalFuel : totalFuelAdded;
                        float curFuel = vehicleFuelComp.Fuel;
                        if (curFuel > 0f)
                        {
                            try { vehicleFuelComp.ConsumeFuel(curFuel); } catch { }
                        }
                        if (engineTotal > 0f)
                        {
                            try { vehicleFuelComp.Refuel(engineTotal); } catch { }
                        }
                        VRF_Log.Msg($"  Gravship fuel synced: vehicleField={engineTotal:F0} (rawTanks={totalFuelAdded:F0})");
                    }
                }

                return vehiclePawn;
            }
            catch (Exception ex)
            {
                Log.Error($"[VehicleRaidFramework] Error in CreateGravshipVehicleFromPreset: {ex}");
                return null;
            }
        }

        private static long PackCoord(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }

        /// <summary>
        /// Spawns a gravship preset on the specified map at the target location.
        /// If convertToVehicleMode is true AND VMF is active, creates a VehiclePawnWithMap
        /// with the preset structure inside its interior map.
        /// Otherwise, spawns the raw GravEngine + structures directly on the map.
        /// </summary>
        public static Thing SpawnGravshipPresetOnMap(VRF_GravshipPresetData data, Map map, IntVec3 targetLocation, Faction faction = null, Vehicles.VehicleDef baseDef = null, bool convertToVehicleMode = true)
        {
            if (data == null || map == null) return null;

            Faction targetFaction = faction ?? Faction.OfPlayer;

            // --- Vehicle Mode: create VehiclePawnWithMap and spawn it ---
            if (convertToVehicleMode && IsVMFActive)
            {
                try
                {
                    var vehiclePawn = CreateGravshipVehicleFromPreset(data, targetFaction, baseDef);
                    if (vehiclePawn != null)
                    {
                        GenSpawn.Spawn(vehiclePawn, targetLocation, map, Rot4.North, WipeMode.Vanish);

                        // Post-spawn: force faction on all interior buildings and sync fuel
                        if (targetFaction != null)
                        {
                            Map intMap = vehiclePawn.VehicleMap;
                            if (intMap != null)
                            {
                                foreach (Thing t in intMap.listerThings.AllThings.ToList())
                                {
                                    if (t.def.CanHaveFaction && t.Faction != targetFaction)
                                        t.SetFaction(targetFaction);
                                }
                            }
                        }

                        // Post-spawn fuel sync: engine is now accessible, re-sync vehicle fuel field
                        SyncGravshipFuelPostSpawn(vehiclePawn);

                        VRF_Log.Msg($"Spawned Gravship vehicle preset '{data.presetName}' at {targetLocation}");
                        Messages.Message($"Spawned Gravship preset '{data.presetName}' (Vehicle Mode) at {targetLocation}", MessageTypeDefOf.PositiveEvent, false);
                        return vehiclePawn;
                    }
                    else
                    {
                        Log.Warning("[VehicleRaidFramework] CreateGravshipVehicleFromPreset returned null, falling back to structure mode.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[VehicleRaidFramework] Vehicle mode spawn failed, falling back to structure mode: {ex.Message}");
                }
            }

            // --- Structure Mode fallback: spawn raw engine + buildings on the main map ---
            try
            {
                ThingDef engineDef = ThingDefOf.GravEngine;
                Building_GravEngine engine = (Building_GravEngine)GenSpawn.Spawn(engineDef, targetLocation, map, WipeMode.Vanish);
                if (engine != null && engine.def.CanHaveFaction)
                    engine.SetFaction(targetFaction);

                foreach (var cellData in data.cells)
                {
                    IntVec3 cell = targetLocation + new IntVec3(cellData.offsetX, 0, cellData.offsetZ);
                    if (!cell.InBounds(map)) continue;

                    TerrainDef tDef = DefDatabase<TerrainDef>.GetNamedSilentFail(cellData.terrainDef);
                    if (tDef == null) continue;

                    if (tDef.isFoundation)
                    {
                        int index = map.cellIndices.CellToIndex(cell);
                        if (map.terrainGrid.UnderTerrainAt(index) != null)
                            map.terrainGrid.SetUnderTerrain(cell, null);
                        map.terrainGrid.SetFoundation(cell, tDef);
                    }
                    else
                    {
                        map.terrainGrid.SetTerrain(cell, tDef);
                    }
                }

                foreach (var bData in data.buildings)
                {
                    IntVec3 cell = targetLocation + new IntVec3(bData.offsetX, 0, bData.offsetZ);
                    if (!cell.InBounds(map)) continue;

                    ThingDef bDef = DefDatabase<ThingDef>.GetNamedSilentFail(bData.defName);
                    ThingDef stuffDef = bData.stuffDef != null ? DefDatabase<ThingDef>.GetNamedSilentFail(bData.stuffDef) : null;
                    if (bDef == null) continue;

                    Rot4 rot = new Rot4(bData.rotation);
                    Thing created = ThingMaker.MakeThing(bDef, stuffDef);
                    if (bDef.CanHaveFaction)
                        created.SetFaction(targetFaction);
                    GenSpawn.Spawn(created, cell, map, rot, WipeMode.Vanish);

                    if (bData.pipeStoredResource > 0f)
                        TryRestorePipeStorage(created, bData.pipeStoredResource);
                }

                engine.ForceSubstructureDirty();

                Messages.Message($"Spawned Gravship preset '{data.presetName}' at {targetLocation}", MessageTypeDefOf.PositiveEvent, false);
                VRF_Log.Msg($"Spawned Gravship preset '{data.presetName}' (structure mode) at {targetLocation}");
                return engine;
            }
            catch (Exception ex)
            {
                Log.Error($"[VehicleRaidFramework] Error spawning Gravship preset on map: {ex}");
                return null;
            }
        }

        /// <summary>
        /// After a gravship VehiclePawnWithMap has been spawned, the Building_GravEngine
        /// inside its vehicle map is now accessible. Re-sync the vehicle's CompFueledTravel
        /// field to match the actual engine TotalFuel (which includes VGE PipeSystem tanks).
        /// Called immediately after GenSpawn.Spawn.
        /// </summary>
        public static void SyncGravshipFuelPostSpawnPublic(global::VehicleMapFramework.VehiclePawnWithMap vehiclePawn)
        {
            SyncGravshipFuelPostSpawn(vehiclePawn);
        }

        private static void SyncGravshipFuelPostSpawn(global::VehicleMapFramework.VehiclePawnWithMap vehiclePawn)
        {
            if (vehiclePawn == null) return;
            try
            {
                CompFueledTravel fuelComp = vehiclePawn.GetComp<CompFueledTravel>();
                if (fuelComp == null) return;

                Map vehicleMap = vehiclePawn.VehicleMap;
                if (vehicleMap == null) return;

                // Sum fuel directly from all tank buildings in the interior map.
                // We bypass CompFueledTravelGravship.Engine (which only works for player faction)
                // by reading CompRefuelable (vanilla) and CompResourceStorage (VGE PipeSystem) directly.
                // --- FUEL FIX ---
                // Read every tank on the interior map, ignoring faction, using the shared helper
                // (VehicleRaidFramework.VRF_GravshipFuelHelper, see Patch_GravshipFuel_Faction.cs).
                // The old inline loop duplicated this logic and could not be reused by the
                // Harmony patches that fix Building_GravEngine.TotalFuel for enemy gravships.
                float totalFuel;
                float totalCapacity;
                global::VehicleRaidFramework.VRF_GravshipFuelHelper.SumTanks(vehicleMap, out totalFuel, out totalCapacity);

                // Rebuild the engine's facility/substructure cache so the non-player engine
                // registers its own fuel tanks instead of reporting TotalFuel = 0.
                Building_GravEngine engineForRefresh = global::VehicleRaidFramework.VRF_GravshipFuelHelper.FindEngine(vehicleMap);
                if (engineForRefresh != null)
                {
                    try { engineForRefresh.ForceSubstructureDirty(); } catch { }
                }

                if (totalCapacity <= 0f) return;

                // Use base.Refuel() via reflection to bypass CompFueledTravelGravship.Refuel()
                // which requires a non-null Engine (player-only method).
                float cur = fuelComp.Fuel;
                if (cur > 0f)
                {
                    // Call base ConsumeFuel directly on CompFueledTravel
                    var baseConsume = typeof(CompFueledTravel).GetMethod("ConsumeFuel",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, new[] { typeof(float) }, null);
                    try { baseConsume?.Invoke(fuelComp, new object[] { cur }); } catch { }
                }

                if (totalFuel > 0f)
                {
                    var baseRefuel = typeof(CompFueledTravel).GetMethod("Refuel",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                        null, new[] { typeof(float) }, null);
                    try { baseRefuel?.Invoke(fuelComp, new object[] { totalFuel }); } catch { }
                }

                VRF_Log.Msg($"SyncGravshipFuelPostSpawn: fuel={totalFuel:F0} / cap={totalCapacity:F0}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[VehicleRaidFramework] SyncGravshipFuelPostSpawn failed: {ex.Message}");
            }
        }

        private static void TryRestorePipeStorage(Thing b, float amount)
        {
            if (b == null || amount <= 0f) return;
            try
            {
                if (b is ThingWithComps twc)
                {
                    foreach (var comp in twc.AllComps)
                    {
                        if (comp.GetType().Name == "CompResourceStorage")
                        {
                            var method = comp.GetType().GetMethod("AddResource", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            if (method != null)
                            {
                                method.Invoke(comp, new object[] { amount });
                            }
                            else
                            {
                                var prop = GetPropertySafe(comp.GetType(), "AmountStored");
                                if (prop != null && prop.CanWrite)
                                {
                                    prop.SetValue(comp, amount, null);
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[VehicleRaidFramework] Failed to restore PipeSystem storage: {ex.Message}");
            }
        }

        private static float TryExtractPipeStorage(Thing b)
        {
            if (b == null) return 0f;
            try
            {
                if (b is ThingWithComps twc)
                {
                    foreach (var comp in twc.AllComps)
                    {
                        if (comp.GetType().Name == "CompResourceStorage")
                        {
                            var prop = GetPropertySafe(comp.GetType(), "AmountStored");
                            if (prop != null)
                            {
                                object val = prop.GetValue(comp, null);
                                if (val is float f) return f;
                            }
                        }
                    }
                }
            }
            catch { }
            return 0f;
        }

        private static VRF_GravshipPresetData ExtractPresetData(Building_GravEngine engine, string shipName)
        {
            var data = new VRF_GravshipPresetData
            {
                presetName = shipName,
                savedAt = DateTime.Now.ToString("o"),
                substructureCount = engine.ValidSubstructure.Count
            };

            var map = engine.Map;
            var origin = engine.Position;

            foreach (IntVec3 cell in engine.ValidSubstructure)
            {
                IntVec3 relOffset = cell - origin;

                // Capture Terrain/Floor
                var terrain = map.terrainGrid.TerrainAt(cell);
                if (terrain != null)
                {
                    data.cells.Add(new VRF_GravshipCellData
                    {
                        offsetX = relOffset.x,
                        offsetZ = relOffset.z,
                        terrainDef = terrain.defName
                    });
                }

                // Capture Buildings and Things on cell
                var thingList = map.thingGrid.ThingsListAt(cell);
                foreach (var thing in thingList)
                {
                    if (thing is Building b)
                    {
                        // Check if this building was already captured (for multi-cell buildings)
                        IntVec3 bRelPos = b.Position - origin;
                        bool alreadyCaptured = data.buildings.Any(bd => 
                            bd.defName == b.def.defName && 
                            bd.offsetX == bRelPos.x && 
                            bd.offsetZ == bRelPos.z);

                        if (!alreadyCaptured)
                        {
                            float storedPipeRes = TryExtractPipeStorage(b);
                            data.buildings.Add(new VRF_GravshipBuildingData
                            {
                                defName = b.def.defName,
                                offsetX = bRelPos.x,
                                offsetZ = bRelPos.z,
                                rotation = b.Rotation.AsInt,
                                stuffDef = b.Stuff?.defName,
                                pipeStoredResource = storedPipeRes
                            });
                        }
                    }
                }
            }

            return data;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(" ", "_");
        }

        private static string json_Serialize(VRF_GravshipPresetData data)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"presetName\": \"{data.presetName}\",");
            sb.AppendLine($"  \"savedAt\": \"{data.savedAt}\",");
            sb.AppendLine($"  \"substructureCount\": {data.substructureCount},");
            sb.AppendLine("  \"cells\": [");
            for (int i = 0; i < data.cells.Count; i++)
            {
                var c = data.cells[i];
                sb.Append($"    {{\"x\": {c.offsetX}, \"z\": {c.offsetZ}, \"terrain\": \"{c.terrainDef}\"}}");
                if (i < data.cells.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ],");
            sb.AppendLine("  \"buildings\": [");
            for (int i = 0; i < data.buildings.Count; i++)
            {
                var b = data.buildings[i];
                string stuff = b.stuffDef != null ? $"\"{b.stuffDef}\"" : "null";
                sb.Append($"    {{\"x\": {b.offsetX}, \"z\": {b.offsetZ}, \"def\": \"{b.defName}\", \"rot\": {b.rotation}, \"stuff\": {stuff}, \"pipeRes\": {b.pipeStoredResource.ToString("F1")}}}");
                if (i < data.buildings.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private const string GravshipFolder = "GravshipPresets";

        public static List<string> FindAllGravshipPresetFiles()
        {
            var files = new List<string>();

            // 1. Scan root directory of all running mods (e.g. Mods/YourMod/GravshipPresets/*.json)
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                string modFolder = Path.Combine(mod.RootDir, GravshipFolder);
                if (Directory.Exists(modFolder))
                {
                    foreach (string f in Directory.GetFiles(modFolder, "*.json"))
                        files.Add(f);
                }
            }

            // 2. Scan User Config directory (%appdata%/.../VehicleRaidFramework/GravshipPresets/*.json)
            if (Directory.Exists(PresetsFolder))
            {
                foreach (string f in Directory.GetFiles(PresetsFolder, "*.json"))
                    files.Add(f);
            }

            return files.Distinct().ToList();
        }

        public static VRF_GravshipPresetData LoadGravshipPresetFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                string json = File.ReadAllText(filePath);
                return json_Deserialize(json);
            }
            catch (Exception ex)
            {
                Log.Warning($"[VehicleRaidFramework] Failed to load Gravship preset '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static VRF_GravshipPresetData json_Deserialize(string json)
        {
            // Simple lightweight JSON parser tailored to VRF_GravshipPresetData schema
            var data = new VRF_GravshipPresetData();
            if (string.IsNullOrEmpty(json)) return data;

            data.presetName = ExtractJsonString(json, "presetName");
            data.savedAt = ExtractJsonString(json, "savedAt");
            int.TryParse(ExtractJsonNumber(json, "substructureCount"), out data.substructureCount);

            int cellsIndex = json.IndexOf("\"cells\":");
            int buildingsIndex = json.IndexOf("\"buildings\":");

            if (cellsIndex != -1)
            {
                string cellsSection = buildingsIndex > cellsIndex
                    ? json.Substring(cellsIndex, buildingsIndex - cellsIndex)
                    : json.Substring(cellsIndex);

                foreach (string block in ExtractJsonBlocks(cellsSection))
                {
                    int.TryParse(ExtractJsonNumber(block, "x"), out int x);
                    int.TryParse(ExtractJsonNumber(block, "z"), out int z);
                    string t = ExtractJsonString(block, "terrain");
                    data.cells.Add(new VRF_GravshipCellData { offsetX = x, offsetZ = z, terrainDef = t });
                }
            }

            if (buildingsIndex != -1)
            {
                string buildingsSection = json.Substring(buildingsIndex);

                foreach (string block in ExtractJsonBlocks(buildingsSection))
                {
                    int.TryParse(ExtractJsonNumber(block, "x"), out int x);
                    int.TryParse(ExtractJsonNumber(block, "z"), out int z);
                    string def = ExtractJsonString(block, "def");
                    int.TryParse(ExtractJsonNumber(block, "rot"), out int rot);
                    string stuff = ExtractJsonString(block, "stuff");
                    float.TryParse(ExtractJsonNumber(block, "pipeRes"), out float pipeRes);

                    data.buildings.Add(new VRF_GravshipBuildingData
                    {
                        offsetX = x,
                        offsetZ = z,
                        defName = def,
                        rotation = rot,
                        stuffDef = string.IsNullOrEmpty(stuff) || stuff == "null" ? null : stuff,
                        pipeStoredResource = pipeRes
                    });
                }
            }

            return data;
        }

        private static string ExtractJsonString(string text, string key)
        {
            string pattern = $"\"{key}\": \"";
            int start = text.IndexOf(pattern);
            if (start == -1) return null;
            start += pattern.Length;
            int end = text.IndexOf("\"", start);
            return end == -1 ? null : text.Substring(start, end - start);
        }

        private static string ExtractJsonNumber(string text, string key)
        {
            string pattern = $"\"{key}\": ";
            int start = text.IndexOf(pattern);
            if (start == -1) return "0";
            start += pattern.Length;
            int end = text.IndexOfAny(new[] { ',', '}', '\r', '\n' }, start);
            return end == -1 ? text.Substring(start).Trim() : text.Substring(start, end - start).Trim();
        }

        private static List<string> ExtractJsonBlocks(string text)
        {
            var list = new List<string>();
            int index = 0;
            while ((index = text.IndexOf('{', index)) != -1)
            {
                int end = text.IndexOf('}', index);
                if (end == -1) break;
                list.Add(text.Substring(index, end - index + 1));
                index = end + 1;
            }
            return list;
        }
    }

    [Serializable]
    public class VRF_GravshipPresetData
    {
        public string presetName;
        public string savedAt;
        public int substructureCount;
        public List<VRF_GravshipCellData> cells = new List<VRF_GravshipCellData>();
        public List<VRF_GravshipBuildingData> buildings = new List<VRF_GravshipBuildingData>();
    }

    [Serializable]
    public class VRF_GravshipCellData
    {
        public int offsetX;
        public int offsetZ;
        public string terrainDef;
    }

    [Serializable]
    public class VRF_GravshipBuildingData
    {
        public string defName;
        public int offsetX;
        public int offsetZ;
        public int rotation;
        public string stuffDef;
        public float pipeStoredResource;
    }
}
