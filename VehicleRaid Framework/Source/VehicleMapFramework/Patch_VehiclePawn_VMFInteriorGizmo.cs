using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Vehicles;
using VehicleMapFramework;

namespace VehicleRaidFramework.VehicleMapFramework
{
    /// <summary>
    /// Parche directo sobre VehiclePawnWithMap de Vehicle Map Framework.
    /// Al estar aplicado sobre la clase exacta que hereda de VehiclePawn y sobreescribe GetGizmos(),
    /// garantiza que el botón aparezca siempre que se seleccione cualquier vehículo con mapa interior.
    /// </summary>
    [HarmonyPatch(typeof(global::VehicleMapFramework.VehiclePawnWithMap), nameof(global::VehicleMapFramework.VehiclePawnWithMap.GetGizmos))]
    public static class Patch_VehiclePawnWithMap_Gizmo
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, global::VehicleMapFramework.VehiclePawnWithMap __instance)
        {
            if (__result != null)
            {
                foreach (var g in __result)
                    yield return g;
            }

            if (__instance == null || !__instance.Spawned)
                yield break;

            // Gizmo visible para guardar el interior
            yield return new Command_Action
            {
                defaultLabel = "DEV: Guardar Estructura VMF",
                defaultDesc = "Guarda la estructura, suelos y edificios del interior de este vehículo en un preset JSON para raids.",
                icon = TexCommand.GatherSpotActive,
                action = () =>
                {
                    if (__instance.VehicleMap == null)
                    {
                        Messages.Message("El mapa interior aún no ha sido generado en este vehículo. Abre la vista interior o haz que un colono entre primero.", MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    Find.WindowStack.Add(new Dialog_SaveVehiclePresetName(__instance));
                }
            };
        }
    }

    /// <summary>
    /// Parche de respaldo sobre VehiclePawn genérico por si algún vehículo no hereda directamente de VehiclePawnWithMap.
    /// </summary>
    [HarmonyPatch(typeof(VehiclePawn), nameof(VehiclePawn.GetGizmos))]
    public static class Patch_VehiclePawn_FallbackGizmo
    {
        [HarmonyPrepare]
        public static bool Prepare() => true;

        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, VehiclePawn __instance)
        {
            if (__result != null)
            {
                foreach (var g in __result)
                    yield return g;
            }

            // Si ya es VehiclePawnWithMap, el parche principal ya lo procesó
            if (__instance is global::VehicleMapFramework.VehiclePawnWithMap)
                yield break;

            if (__instance != null && __instance.Spawned && VRF_VMFUtility.HasInteriorMap(__instance))
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Guardar Estructura VMF",
                    defaultDesc = "Guarda el interior de este vehículo compatible con VMF en un preset JSON.",
                    icon = TexCommand.GatherSpotActive,
                    action = () =>
                    {
                        Messages.Message("Este vehículo no posee la instancia VehiclePawnWithMap activa.", MessageTypeDefOf.RejectInput, false);
                    }
                };
            }
        }
    }

    /// <summary>
    /// Diálogo para nombrar el preset al guardar el interior de un vehículo.
    /// </summary>
    public class Dialog_SaveVehiclePresetName : Window
    {
        private readonly global::VehicleMapFramework.VehiclePawnWithMap _vehicle;
        private string _presetName;

        public override Vector2 InitialSize => new Vector2(460f, 180f);

        public Dialog_SaveVehiclePresetName(global::VehicleMapFramework.VehiclePawnWithMap vehicle)
        {
            _vehicle = vehicle;
            _presetName = $"{vehicle.VehicleDef?.defName ?? "VMF_Vehicle"}_Preset_{Find.TickManager.TicksGame}";
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "Guardar Diseño Interior (VMF)");
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 35f, inRect.width, 22f), "Nombre del preset JSON:");
            _presetName = Widgets.TextField(new Rect(0f, 60f, inRect.width, 30f), _presetName);

            float btnW = (inRect.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, 110f, btnW, 35f), "Guardar"))
            {
                VRF_VehicleMapPresetUtility.SaveVehicleInteriorPreset(_vehicle, _presetName);
                Close();
            }

            if (Widgets.ButtonText(new Rect(btnW + 10f, 110f, btnW, 35f), "Cancelar"))
            {
                Close();
            }
        }
    }
}
