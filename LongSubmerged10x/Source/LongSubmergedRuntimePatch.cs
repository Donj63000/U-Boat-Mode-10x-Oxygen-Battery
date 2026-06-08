using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace LongSubmerged10x
{
    public sealed class LongSubmergedRuntimePatchMod : IUserMod
    {
        public string Name
        {
            get { return "Long Submerged 10x+ AirFix"; }
        }

        public void OnLoaded()
        {
            try
            {
                // La je charge mes patches Harmony pour recalculer l'air sur les sauvegardes existantes.
                new Harmony("donj.longsubmerged10x.airfix").PatchAll();
                Debug.Log("[LongSubmerged10x] AirFix runtime patch loaded.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    internal static class OxygenBreathRecalculator
    {
        private static readonly MethodInfo ValidateOxygenBreathModifierMethod =
            AccessTools.Method(typeof(PlayerShip), "ValidateOxygenBreathModifier");

        public static void Recalculate(PlayerShip ship, string reason)
        {
            if (ship == null || ValidateOxygenBreathModifierMethod == null)
                return;

            try
            {
                // La je force le jeu a reprendre ma valeur Oxygen Consumption Per Character du fichier General.xlsx.
                ValidateOxygenBreathModifierMethod.Invoke(ship, null);
                Debug.Log("[LongSubmerged10x] Oxygen breath modifier recalculated after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    internal static class EngineFastSpeedPatcher
    {
        private const float FastSpeedFactor = 2f;
        private const int FastForwardGearCount = 2;

        private static readonly FieldInfo ForwardPresetsField =
            AccessTools.Field(typeof(PlayerShipEngine), "forwardPresets");

        private static readonly FieldInfo ExpectedVelocityPerGearField =
            AccessTools.Field(typeof(PlayerShipEngine), "expectedVelocityPerGear");

        private static readonly FieldInfo ExpectedVelocityPerGearUnderwaterField =
            AccessTools.Field(typeof(PlayerShipEngine), "expectedVelocityPerGearUnderwater");

        private static readonly Type EngineSpeedPresetType =
            typeof(PlayerShipEngine).GetNestedType("EngineSpeedPreset", BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo BasePowerField =
            EngineSpeedPresetType == null ? null : AccessTools.Field(EngineSpeedPresetType, "basePower");

        private static readonly ConditionalWeakTable<PlayerShipEngine, EngineSpeedPatchData> OriginalData =
            new ConditionalWeakTable<PlayerShipEngine, EngineSpeedPatchData>();

        private static readonly HashSet<int> WarnedEngines = new HashSet<int>();

        public static void PatchPlayerShip(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            try
            {
                PatchEngine(ship.DieselEngine, reason + ".DieselEngine");
                PatchEngine(ship.ElectricEngine, reason + ".ElectricEngine");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void PatchEngine(PlayerShipEngine engine, string reason)
        {
            if (engine == null)
                return;

            try
            {
                if (!FieldsReady())
                {
                    WarnOnce(engine, "champs moteur introuvables, patch vitesse ignore.");
                    return;
                }

                Array forwardPresets = ForwardPresetsField.GetValue(engine) as Array;
                float[] expectedVelocityPerGear = ExpectedVelocityPerGearField.GetValue(engine) as float[];
                float[] expectedVelocityPerGearUnderwater =
                    ExpectedVelocityPerGearUnderwaterField.GetValue(engine) as float[];

                if (forwardPresets == null || forwardPresets.Length < FastForwardGearCount)
                {
                    WarnOnce(engine, "moins de " + FastForwardGearCount + " crans avant, patch vitesse ignore.");
                    return;
                }

                EngineSpeedPatchData data;
                if (!OriginalData.TryGetValue(engine, out data))
                {
                    data = EngineSpeedPatchData.Capture(
                        forwardPresets,
                        expectedVelocityPerGear,
                        expectedVelocityPerGearUnderwater,
                        BasePowerField
                    );
                    OriginalData.Add(engine, data);
                }

                ApplyTopGearBasePower(forwardPresets, data.ForwardBasePower);
                ApplyTopGearFloatArray(expectedVelocityPerGear, data.ExpectedVelocityPerGear);
                ApplyTopGearFloatArray(expectedVelocityPerGearUnderwater, data.ExpectedVelocityPerGearUnderwater);

                Debug.Log("[LongSubmerged10x] Fast speed patch applied after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static bool FieldsReady()
        {
            return ForwardPresetsField != null
                && ExpectedVelocityPerGearField != null
                && ExpectedVelocityPerGearUnderwaterField != null
                && BasePowerField != null;
        }

        private static void ApplyTopGearBasePower(Array forwardPresets, float[] originalBasePower)
        {
            if (forwardPresets == null || originalBasePower == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(forwardPresets.Length, originalBasePower.Length));
            int firstPatchedGear = forwardPresets.Length - patchCount;

            for (int index = firstPatchedGear; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                BasePowerField.SetValue(preset, originalBasePower[index] * FastSpeedFactor);
                forwardPresets.SetValue(preset, index);
            }
        }

        private static void ApplyTopGearFloatArray(float[] target, float[] original)
        {
            if (target == null || original == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(target.Length, original.Length));
            int firstPatchedGear = target.Length - patchCount;

            for (int index = firstPatchedGear; index < target.Length; index++)
                target[index] = original[index] * FastSpeedFactor;
        }

        private static void WarnOnce(PlayerShipEngine engine, string message)
        {
            int instanceId = engine.GetInstanceID();
            if (WarnedEngines.Add(instanceId))
                Debug.LogWarning("[LongSubmerged10x] " + message);
        }
    }

    internal sealed class EngineSpeedPatchData
    {
        public readonly float[] ForwardBasePower;
        public readonly float[] ExpectedVelocityPerGear;
        public readonly float[] ExpectedVelocityPerGearUnderwater;

        private EngineSpeedPatchData(
            float[] forwardBasePower,
            float[] expectedVelocityPerGear,
            float[] expectedVelocityPerGearUnderwater)
        {
            ForwardBasePower = forwardBasePower;
            ExpectedVelocityPerGear = expectedVelocityPerGear;
            ExpectedVelocityPerGearUnderwater = expectedVelocityPerGearUnderwater;
        }

        public static EngineSpeedPatchData Capture(
            Array forwardPresets,
            float[] expectedVelocityPerGear,
            float[] expectedVelocityPerGearUnderwater,
            FieldInfo basePowerField)
        {
            float[] basePower = new float[forwardPresets.Length];

            for (int index = 0; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                object rawValue = basePowerField.GetValue(preset);
                if (rawValue is float)
                    basePower[index] = (float)rawValue;
            }

            return new EngineSpeedPatchData(
                basePower,
                CloneFloatArray(expectedVelocityPerGear),
                CloneFloatArray(expectedVelocityPerGearUnderwater)
            );
        }

        private static float[] CloneFloatArray(float[] source)
        {
            if (source == null)
                return null;

            float[] clone = new float[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Awake")]
    internal static class PlayerShipAwakePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            OxygenBreathRecalculator.Recalculate(__instance, "PlayerShip.Awake");
            EngineFastSpeedPatcher.PatchPlayerShip(__instance, "PlayerShip.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SavesManagerOnLoaded")]
    internal static class PlayerShipSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShip __instance, Queue<Action> __0)
        {
            OxygenBreathRecalculator.Recalculate(__instance, "SavesManagerOnLoaded");
            EngineFastSpeedPatcher.PatchPlayerShip(__instance, "SavesManagerOnLoaded");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Crew_Added")]
    internal static class PlayerShipCrewAddedPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            OxygenBreathRecalculator.Recalculate(__instance, "Crew_Added");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Crew_Removed")]
    internal static class PlayerShipCrewRemovedPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            OxygenBreathRecalculator.Recalculate(__instance, "Crew_Removed");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "Awake")]
    internal static class PlayerShipEngineAwakePatch
    {
        private static void Postfix(PlayerShipEngine __instance)
        {
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "OnAfterDeserialize")]
    internal static class PlayerShipEngineOnAfterDeserializePatch
    {
        private static void Postfix(PlayerShipEngine __instance)
        {
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.OnAfterDeserialize");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "SavesManagerOnLoaded")]
    internal static class PlayerShipEngineSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShipEngine __instance, Queue<Action> __0)
        {
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.SavesManagerOnLoaded");
        }
    }
}
