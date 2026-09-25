using System;
using System.Linq;
using Verse;
using RimWorld;
using Vehicles;

namespace VehicleRaidFramework.VehicleMapFramework
{
    /// <summary>
    /// Utilidades para detectar y gestionar vehículos de Vehicle Map Framework (VMF).
    /// </summary>
    public static class VRF_VMFUtility
    {
        private static bool? _isVMFActive;

        public static bool IsVMFActive
        {
            get
            {
                if (!_isVMFActive.HasValue)
                {
                    _isVMFActive = LoadedModManager.RunningModsListForReading.Any(m =>
                        m.PackageIdPlayerFacing.IndexOf("VehicleMapFramework", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.PackageIdPlayerFacing.IndexOf("VehicleMap", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                return _isVMFActive.Value;
            }
        }

        /// <summary>
        /// Determina si un VehicleDef o PawnKindDef tiene un mapa interior generado por Vehicle Map Framework.
        /// </summary>
        public static bool HasInteriorMap(VehicleDef vDef, PawnKindDef kind = null)
        {
            if (vDef == null) return false;

            // 1. Verificar si su ThingClass hereda de VehiclePawnWithMap
            if (typeof(global::VehicleMapFramework.VehiclePawnWithMap).IsAssignableFrom(vDef.thingClass))
                return true;

            // 2. Verificar si tiene componentes o propiedades de VehicleMapFramework
            if (vDef.comps != null && vDef.comps.Any(c => c != null && c.GetType().FullName.Contains("VehicleMapFramework")))
                return true;

            // 3. Comprobar por defName en caso de naves gravitacionales o vehículos modded
            if (vDef.defName.IndexOf("grav", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (kind?.defName != null && kind.defName.IndexOf("grav", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        /// <summary>
        /// Comprueba si una instancia concreta de VehiclePawn tiene mapa interior activo.
        /// </summary>
        public static bool HasInteriorMap(VehiclePawn vehicle)
        {
            if (vehicle == null) return false;
            return vehicle is global::VehicleMapFramework.VehiclePawnWithMap || HasInteriorMap(vehicle.VehicleDef);
        }
    }
}
