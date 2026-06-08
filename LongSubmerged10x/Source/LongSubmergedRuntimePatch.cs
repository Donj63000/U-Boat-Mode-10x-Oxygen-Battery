using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core.Data;
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
        private const float FastSpeedFactor = 3.5f;
        private const float FastSpeedFuelFactor = 8f;
        private const float PlayerSubmarineMaxSpeed = 45f;
        private const int FastForwardGearCount = 2;
        private const string RuntimeVelocityModifierName = "LongSubmerged10x Player Speed Cap";

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

        private static readonly FieldInfo FuelConsumptionField =
            EngineSpeedPresetType == null ? null : AccessTools.Field(EngineSpeedPresetType, "fuelConsumptionInLitersPerHour");

        private static readonly FieldInfo ShipPropellersField =
            AccessTools.Field(typeof(Ship), "propellers");

        private static readonly ConditionalWeakTable<PlayerShipEngine, EngineSpeedPatchData> OriginalData =
            new ConditionalWeakTable<PlayerShipEngine, EngineSpeedPatchData>();

        private static readonly ConditionalWeakTable<PlayerShip, ShipRuntimePatchData> ShipRuntimeData =
            new ConditionalWeakTable<PlayerShip, ShipRuntimePatchData>();

        private static readonly ConditionalWeakTable<Propeller, PropellerPatchData> PropellerRuntimeData =
            new ConditionalWeakTable<Propeller, PropellerPatchData>();

        private static readonly HashSet<int> WarnedEngines = new HashSet<int>();

        public static void PatchPlayerShip(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            try
            {
                PatchEngine(ship.DieselEngine, reason + ".DieselEngine");
                PatchEngine(ship.ElectricEngine, reason + ".ElectricEngine");
                PatchShipVelocityCap(ship, reason, true);
                ApplyPropellerSpeedMultiplier(ship, reason, true);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void UpdatePlayerShipRuntime(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            try
            {
                PatchShipVelocityCap(ship, reason, false);
                ApplyPropellerSpeedMultiplier(ship, reason, false);
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
                        BasePowerField,
                        FuelConsumptionField
                    );
                    OriginalData.Add(engine, data);
                }

                ApplyTopGearBasePower(forwardPresets, data.ForwardBasePower);
                ApplyTopGearFuelConsumption(forwardPresets, data.ForwardFuelConsumption);
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
                && BasePowerField != null
                && FuelConsumptionField != null;
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

        private static void ApplyTopGearFuelConsumption(Array forwardPresets, float[] originalFuelConsumption)
        {
            if (forwardPresets == null || originalFuelConsumption == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(forwardPresets.Length, originalFuelConsumption.Length));
            int firstPatchedGear = forwardPresets.Length - patchCount;

            for (int index = firstPatchedGear; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                FuelConsumptionField.SetValue(preset, originalFuelConsumption[index] * FastSpeedFuelFactor);
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

        private static void PatchShipVelocityCap(PlayerShip ship, string reason, bool verboseLog)
        {
            if (ship == null || ship.Blueprint == null || ship.Blueprint.Velocity == null)
                return;

            ShipRuntimePatchData data;
            if (!ShipRuntimeData.TryGetValue(ship, out data))
            {
                float originalVelocity = ship.Blueprint.Velocity;
                Modifier modifier = null;

                if (originalVelocity < PlayerSubmarineMaxSpeed)
                    modifier = ship.Blueprint.Velocity.AddDeltaModifier(RuntimeVelocityModifierName, false);

                data = new ShipRuntimePatchData(originalVelocity, modifier);
                ShipRuntimeData.Add(ship, data);
            }

            if (data.VelocityModifier == null)
                return;

            float desiredDelta = PlayerSubmarineMaxSpeed - data.OriginalVelocity;
            if (desiredDelta < 0f)
                desiredDelta = 0f;

            if (Math.Abs(data.VelocityModifier.Value - desiredDelta) > 0.001f)
                data.VelocityModifier.Value = desiredDelta;

            if (verboseLog)
            {
                Debug.Log(
                    "[LongSubmerged10x] Player ship speed cap patched after "
                    + reason
                    + ": "
                    + data.OriginalVelocity
                    + " -> "
                    + PlayerSubmarineMaxSpeed
                    + " km/h."
                );
            }
        }

        private static void ApplyPropellerSpeedMultiplier(PlayerShip ship, string reason, bool verboseLog)
        {
            if (ship == null)
                return;

            Propeller[] propellers = ShipPropellersField == null
                ? ship.Propellers
                : ShipPropellersField.GetValue(ship) as Propeller[];

            if (propellers == null || propellers.Length == 0)
                return;

            bool fastForwardGear = IsActiveEngineInFastForwardGear(ship);
            float appliedFactor = fastForwardGear ? FastSpeedFactor : 1f;
            int changedCount = 0;

            foreach (Propeller propeller in propellers)
            {
                if (propeller == null)
                    continue;

                PropellerPatchData data;
                if (!PropellerRuntimeData.TryGetValue(propeller, out data))
                {
                    data = new PropellerPatchData(propeller.PowerMultiplier);
                    PropellerRuntimeData.Add(propeller, data);
                }

                float desiredMultiplier = data.OriginalPowerMultiplier * appliedFactor;

                if (Math.Abs(propeller.PowerMultiplier - desiredMultiplier) > 0.001f)
                {
                    propeller.PowerMultiplier = desiredMultiplier;
                    changedCount++;
                }
            }

            if (verboseLog && changedCount > 0)
            {
                Debug.Log(
                    "[LongSubmerged10x] Propeller multiplier "
                    + appliedFactor
                    + " applied after "
                    + reason
                    + "."
                );
            }
        }

        private static bool IsActiveEngineInFastForwardGear(PlayerShip ship)
        {
            PlayerShipEngine engine = ship.ActiveEngine;
            if (engine == null || engine.GearIndex <= 0 || ForwardPresetsField == null)
                return false;

            Array forwardPresets = ForwardPresetsField.GetValue(engine) as Array;
            if (forwardPresets == null || forwardPresets.Length < FastForwardGearCount)
                return false;

            int firstFastGearIndex = forwardPresets.Length - FastForwardGearCount + 1;
            return engine.GearIndex >= firstFastGearIndex;
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
        public readonly float[] ForwardFuelConsumption;
        public readonly float[] ExpectedVelocityPerGear;
        public readonly float[] ExpectedVelocityPerGearUnderwater;

        private EngineSpeedPatchData(
            float[] forwardBasePower,
            float[] forwardFuelConsumption,
            float[] expectedVelocityPerGear,
            float[] expectedVelocityPerGearUnderwater)
        {
            ForwardBasePower = forwardBasePower;
            ForwardFuelConsumption = forwardFuelConsumption;
            ExpectedVelocityPerGear = expectedVelocityPerGear;
            ExpectedVelocityPerGearUnderwater = expectedVelocityPerGearUnderwater;
        }

        public static EngineSpeedPatchData Capture(
            Array forwardPresets,
            float[] expectedVelocityPerGear,
            float[] expectedVelocityPerGearUnderwater,
            FieldInfo basePowerField,
            FieldInfo fuelConsumptionField)
        {
            float[] basePower = new float[forwardPresets.Length];
            float[] fuelConsumption = new float[forwardPresets.Length];

            for (int index = 0; index < forwardPresets.Length; index++)
            {
                object preset = forwardPresets.GetValue(index);
                if (preset == null)
                    continue;

                object rawValue = basePowerField.GetValue(preset);
                if (rawValue is float)
                    basePower[index] = (float)rawValue;

                object rawFuelConsumption = fuelConsumptionField.GetValue(preset);
                if (rawFuelConsumption is float)
                    fuelConsumption[index] = (float)rawFuelConsumption;
            }

            return new EngineSpeedPatchData(
                basePower,
                fuelConsumption,
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

    internal sealed class ShipRuntimePatchData
    {
        public readonly float OriginalVelocity;
        public readonly Modifier VelocityModifier;

        public ShipRuntimePatchData(float originalVelocity, Modifier velocityModifier)
        {
            OriginalVelocity = originalVelocity;
            VelocityModifier = velocityModifier;
        }
    }

    internal sealed class PropellerPatchData
    {
        public readonly float OriginalPowerMultiplier;

        public PropellerPatchData(float originalPowerMultiplier)
        {
            OriginalPowerMultiplier = originalPowerMultiplier;
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

    [HarmonyPatch(typeof(PlayerShip), "OnAfterDeserialize")]
    internal static class PlayerShipOnAfterDeserializePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            EngineFastSpeedPatcher.PatchPlayerShip(__instance, "PlayerShip.OnAfterDeserialize");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "ValidateTargetVelocity")]
    internal static class PlayerShipValidateTargetVelocityPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            EngineFastSpeedPatcher.UpdatePlayerShipRuntime(__instance, "PlayerShip.ValidateTargetVelocity");
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
