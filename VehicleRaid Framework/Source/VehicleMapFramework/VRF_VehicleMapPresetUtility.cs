using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Vehicles;

namespace VehicleRaidFramework.VehicleMapFramework
{
    /// <summary>
    /// Gestión integral de presets interiores para cualquier vehículo compatible con Vehicle Map Framework.
    /// </summary>
    public static class VRF_VehicleMapPresetUtility
    {
        public const string PresetsSubfolder = "VehiclePresets";

        /// <summary>
        /// Guarda el plano interior de cualquier vehículo con VehicleMap en un preset JSON.
        /// </summary>
        public static void SaveVehicleInteriorPreset(global::VehicleMapFramework.VehiclePawnWithMap vehicle, string presetName)
        {
            if (vehicle?.VehicleMap == null)
            {
                Messages.Message("No se puede guardar: El vehículo no tiene un mapa interior generado.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            try
            {
                Map vMap = vehicle.VehicleMap;
                string cleanName = string.IsNullOrWhiteSpace(presetName) ? vehicle.LabelCap : presetName.Trim();

                var data = new VRF_GravshipPresetData
                {
                    presetName = cleanName,
                    savedAt = DateTime.Now.ToString("o")
                };

                IntVec3 origin = vMap.Center;

                foreach (IntVec3 cell in vMap.AllCells)
                {
                    var terrain = vMap.terrainGrid.TerrainAt(cell);
                    var things = vMap.thingGrid.ThingsListAt(cell);

                    bool hasBuildings = things.Any(t => t is Building && !(t is Pawn));
                    if (terrain == null && !hasBuildings) continue;

                    IntVec3 relOffset = cell - origin;

                    if (terrain != null)
                    {
                        data.cells.Add(new VRF_GravshipCellData
                        {
                            offsetX = relOffset.x,
                            offsetZ = relOffset.z,
                            terrainDef = terrain.defName
                        });
                    }

                    foreach (Thing thing in things)
                    {
                        if (thing is Building b && !(thing is Pawn))
                        {
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

                data.substructureCount = data.cells.Count;

                // Carpeta de guardado en Config
                string configFolder = Path.Combine(GenFilePaths.ConfigFolderPath, PresetsSubfolder);
                if (!Directory.Exists(configFolder))
                    Directory.CreateDirectory(configFolder);

                string safeName = SanitizeFileName(cleanName);
                string fullPath = Path.Combine(configFolder, safeName + ".json");

                string json = json_Serialize(data);
                File.WriteAllText(fullPath, json);

                Messages.Message($"Preset de interior guardado exitosamente: {safeName}", MessageTypeDefOf.PositiveEvent, false);
                VRF_Log.Msg($"[VRF] Guardado preset VMF interior en {fullPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"[VehicleRaidFramework] Error al guardar preset interior de vehículo: {ex}");
            }
        }

        /// <summary>
        /// Crea e inicializa un VehiclePawn de VMF con la estructura interior del preset cargada.
        /// </summary>
        public static VehiclePawn CreateVMFVehicleFromPreset(
            VRF_GravshipPresetData data,
            Faction faction,
            VehicleDef vDef)
        {
            if (data == null) return null;
            Faction targetFaction = faction ?? Faction.OfPlayer;

            // Si es un Gravship dinámico generado sobre VMF_GravshipVehicleBase:
            if (vDef == null || vDef.defName == "VMF_GravshipVehicleBase" || vDef.defName.Contains("Gravship"))
            {
                return VRF_GravshipPresetUtility.CreateGravshipVehicleFromPreset(data, targetFaction, vDef);
            }

            // Para vehículos normales con interior VMF
            var vehiclePawn = (global::VehicleMapFramework.VehiclePawnWithMap)VehicleSpawner.GenerateVehicle(vDef, targetFaction);
            if (vehiclePawn == null) return null;

            Map vehicleMap = vehiclePawn.VehicleMap;
            if (vehicleMap != null)
            {
                IntVec3 origin = vehicleMap.Center;

                // 1. Suelos
                foreach (var cellData in data.cells)
                {
                    IntVec3 cell = origin + new IntVec3(cellData.offsetX, 0, cellData.offsetZ);
                    if (!cell.InBounds(vehicleMap)) continue;

                    TerrainDef tDef = DefDatabase<TerrainDef>.GetNamedSilentFail(cellData.terrainDef);
                    if (tDef != null)
                        vehicleMap.terrainGrid.SetTerrain(cell, tDef);
                }

                // 2. Estructuras y Edificios
                foreach (var bData in data.buildings)
                {
                    IntVec3 cell = origin + new IntVec3(bData.offsetX, 0, bData.offsetZ);
                    if (!cell.InBounds(vehicleMap)) continue;

                    ThingDef bDef = DefDatabase<ThingDef>.GetNamedSilentFail(bData.defName);
                    if (bDef == null) continue;

                    ThingDef stuffDef = bData.stuffDef != null ? DefDatabase<ThingDef>.GetNamedSilentFail(bData.stuffDef) : null;
                    Thing created = ThingMaker.MakeThing(bDef, stuffDef);
                    if (bDef.CanHaveFaction)
                        created.SetFaction(targetFaction);

                    GenSpawn.Spawn(created, cell, vehicleMap, new Rot4(bData.rotation), WipeMode.Vanish);

                    if (bData.pipeStoredResource > 0f)
                        TryRestorePipeStorage(created, bData.pipeStoredResource);
                }
            }

            return vehiclePawn;
        }

        /// <summary>
        /// Obtiene todos los archivos de presets (tanto de Mods como de Config).
        /// </summary>
        public static List<string> GetAllVMFPresetFiles()
        {
            var files = VRF_GravshipPresetUtility.FindAllGravshipPresetFiles();

            string configFolder = Path.Combine(GenFilePaths.ConfigFolderPath, PresetsSubfolder);
            if (Directory.Exists(configFolder))
            {
                foreach (string f in Directory.GetFiles(configFolder, "*.json"))
                {
                    if (!files.Contains(f)) files.Add(f);
                }
            }

            return files;
        }

        #region Helper Methods (Auto-contenidos para evitar errores de protección CS0122)

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(" ", "_");
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
                            var prop = VRF_GravshipPresetUtility.GetPropertySafe(comp.GetType(), "AmountStored");
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
                                var prop = VRF_GravshipPresetUtility.GetPropertySafe(comp.GetType(), "AmountStored");
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

        #endregion
    }
}
