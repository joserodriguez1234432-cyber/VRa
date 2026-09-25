using System.IO;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Vehicles;
using VehicleRaidFramework.VehicleMapFramework;

namespace VehicleRaidFramework
{
    /// <summary>
    /// Métodos de interfaz para listar y renderizar presets de cualquier vehículo compatible con Vehicle Map Framework.
    /// </summary>
    public static class VRF_SettingsUI_VMF
    {
        private static Vector2 _vmfListScrollPos = Vector2.zero;

        /// <summary>
        /// Dibuja la lista de presets con sus planos renderizados proceduralmente para el vehículo seleccionado.
        /// </summary>
        public static void DrawVMFStructuresList(Rect rect, VehicleDef vDef, VRF_NaturalRaidFactionConfig factionConfig, PawnKindDef kind)
        {
            var presetFiles = VRF_VehicleMapPresetUtility.GetAllVMFPresetFiles();
            float y = rect.y;

            Widgets.Label(new Rect(rect.x, y, rect.width, 30f), "Estructuras y Diseños Interiores (VMF)");
            y += 32f;

            if (presetFiles == null || presetFiles.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x, y, rect.width, 26f), "No se encontraron presets. Puedes crear uno en partida usando el Modo Desarrollo en el vehículo.");
                GUI.color = Color.white;
                return;
            }

            float cardH = 90f;
            float cardPad = 6f;
            float totalH = presetFiles.Count * (cardH + cardPad);
            Rect outRect = new Rect(rect.x, y, rect.width, rect.height - 35f);
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalH);

            Widgets.BeginScrollView(outRect, ref _vmfListScrollPos, viewRect);
            float curY = 0f;

            for (int i = 0; i < presetFiles.Count; i++)
            {
                string filePath = presetFiles[i];
                string pName = Path.GetFileNameWithoutExtension(filePath);
                var gEntry = factionConfig.GetOrCreateGravshipEntry(pName);

                Rect cardRect = new Rect(0f, curY, viewRect.width, cardH);
                Widgets.DrawBoxSolid(cardRect, new Color(0.12f, 0.12f, 0.15f, 0.85f));
                Widgets.DrawHighlightIfMouseover(cardRect);

                // 1. Miniatura renderizada proceduralmente del interior / plano
                Rect thumbRect = new Rect(cardRect.x + 6f, cardRect.y + 6f, 78f, 78f);
                VRF_SettingsUI.DrawGravshipPresetStructureThumbnail(thumbRect, filePath, vDef, kind);

                // 2. Información del Preset
                Rect textRect = new Rect(cardRect.x + 95f, cardRect.y + 8f, cardRect.width - 260f, 24f);
                Text.Font = GameFont.Medium;
                Widgets.Label(textRect, pName);
                Text.Font = GameFont.Small;

                Rect infoRect = new Rect(cardRect.x + 95f, cardRect.y + 36f, cardRect.width - 260f, 22f);
                GUI.color = Color.cyan;
                Widgets.Label(infoRect, $"Poder de combate: {(int)gEntry.combatPower} | Puntos: {(int)gEntry.minRaidPoints} - {(int)gEntry.maxRaidPoints}");
                GUI.color = Color.white;

                // 3. Toggle de activación para raid
                Rect checkRect = new Rect(cardRect.xMax - 150f, cardRect.y + 12f, 140f, 26f);
                Widgets.CheckboxLabeled(checkRect, "Habilitado", ref gEntry.enabled);

                // 4. Botón para abrir el editor de balance/puntos
                Rect editBtnRect = new Rect(cardRect.xMax - 150f, cardRect.y + 46f, 140f, 30f);
                if (Widgets.ButtonText(editBtnRect, "Ajustar Puntos"))
                {
                    Find.WindowStack.Add(new Dialog_VRF_GravshipStructureEditor(gEntry, vDef, filePath));
                }

                curY += cardH + cardPad;
            }

            Widgets.EndScrollView();
        }
    }
}
