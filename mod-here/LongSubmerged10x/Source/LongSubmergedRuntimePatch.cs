using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Core.AI.GroupBehaviours;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Tasks;
using UBOAT.Game.Scene.Utilities;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Missions;
using UBOAT.Game.UI.Notifications;
using UBOAT.Game.UI.Resources;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongSubmerged10x
{
    // DonJ : point d'entree du runtime UBOAT. Cette classe ne porte pas la logique gameplay elle-meme ;
    // elle charge les reglages, cree le menu F10, installe les hooks Harmony et lance une premiere passe runtime.
    public sealed class LongSubmergedRuntimePatchMod : IUserMod
    {
        private const string RuntimeVersion = "1.4.16";

        public string Name
        {
            get { return "Long Submerged 10x+ AirFix"; }
        }

        public void OnLoaded()
        {
            try
            {
                // DonJ : je charge les reglages PlayerPrefs et je cree le menu avant Harmony.
                // Si un hook Harmony casse apres une mise a jour UBOAT, le menu et le heartbeat batterie existent quand meme.
                LongSubmergedRuntimeSettings.Load();
                LongSubmergedMenuController.Ensure();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            // DonJ : je patche chaque hook un par un. Un patch rate ne doit jamais empecher la batterie,
            // l'oxygene, les torpilles ou le menu de continuer a fonctionner avec les autres hooks valides.
            LongSubmergedRuntimePatcher.PatchSafely(new Harmony("donj.longsubmerged10x.runtimefix1416"));

            try
            {
                // DonJ : premiere application directe. Elle couvre le cas ou des objets existent deja
                // avant que leurs hooks Awake/Start aient pu etre interceptes.
                LongSubmergedRuntimeApplier.ApplyAll("mod loaded");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            Debug.Log("[LongSubmerged10x] Runtime patch loaded v" + RuntimeVersion + ". F10 ouvre le menu Long Submerged.");
        }
    }

    // DonJ : liste centralisee des hooks Harmony du mod. Garder cette liste explicite rend le chargement
    // robuste : on voit exactement quelles zones du jeu sont touchees et on peut ignorer un hook incompatible.
    internal static class LongSubmergedRuntimePatcher
    {
        private static readonly Type[] PatchTypes = new Type[]
        {
            typeof(PlayerShipAwakePatch),
            typeof(PlayerShipOnAfterDeserializePatch),
            typeof(PlayerShipUpdatePatch),
            typeof(ResourceUpdateAmountBatteryPatch),
            typeof(PlayerShipValidateTargetVelocityPatch),
            typeof(PlayerShipValidateOxygenBreathModifierPatch),
            typeof(DeepDivePlayerShipTargetDepthSetterPatch),
            typeof(DeepDiveHullCrushControllerDoUpdatePatch),
            typeof(DeepDivePlayerShipUpdateStressAndDisciplineGainPatch),
            typeof(PlayerShipSavesManagerOnLoadedPatch),
            typeof(PlayerShipCrewAddedPatch),
            typeof(PlayerShipCrewRemovedPatch),
            typeof(PlayerShipEngineAwakePatch),
            typeof(PlayerShipEngineOnAfterDeserializePatch),
            typeof(PlayerShipEngineSavesManagerOnLoadedPatch),
            typeof(AccumulatorsUpgradeStartPatch),
            typeof(DivingPlanesStationAwakePatch),
            typeof(DivingPlanesStationUpdateModifiersPatch),
            typeof(GyrocompassApplyModifiersPatch),
            typeof(TrimPumpOnEnablePatch),
            typeof(StoredTorpedoStartPatch),
            typeof(StoredTorpedoApplyWarmUpModifierPatch),
            typeof(TorpedoAwakePatch),
            typeof(TorpedoFixedUpdatePatch),
            typeof(TorpedoDetonatePatch),
            typeof(MegaSonarHydrophoneRefreshPatch),
            typeof(SuperStealthEntityUpdateDetectabilityPatch),
            typeof(SuperStealthAirCompressorOnEnablePatch),
            typeof(SuperStealthAirCompressorOnDisablePatch),
            typeof(SuperStealthVentilationOnEnablePatch),
            typeof(SuperStealthVentilationOnDisablePatch),
            typeof(SuperStealthPropellerPowerPatch),
            typeof(SuperStealthPropellerPowerMultiplierPatch),
            typeof(SuperStealthSnorkelUpdatePatch),
            typeof(SuperStealthPeriscopeUpdatePatch),
            typeof(HeavyArmorHullAddDamagePatch),
            typeof(HeavyArmorEquipmentAddDamagePatch),
            typeof(HeavyArmorEquipmentAddWaterDamagePatch),
            typeof(HeavyArmorPlayableCharacterAddDamagePatch),
            typeof(HeavyArmorDamageUtilityDoApplyDamagePatch),
            typeof(HeavyArmorDamageUtilityApplyDamageToComponentsPatch),
            typeof(HeavyArmorApplyWaterDamageToPlayerShipDoDamageTickPatch),
            typeof(HeavyArmorHullEffectsRendererApplyImpactPatch),
            typeof(ResourceGuiGetTooltipContentsPatch),
            typeof(ResourceGuiUpdateDisplayedValuePatch),
            typeof(InteriorLightingPlayerShipInteriorLightingAwakePatch),
            typeof(InteriorLightingPlayerShipInteriorLightingStartPatch),
            typeof(InteriorLightingPlayerShipInteriorLightingApplyPresetsPatch),
            typeof(InteriorLightingLightControllerUpdatePresetsPatch),
            typeof(InteriorLightingFillLightUpdatePresetsPatch)
        };

        public static void PatchSafely(Harmony harmony)
        {
            if (harmony == null)
                return;

            foreach (Type patchType in PatchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(patchType).Patch();
                    Debug.Log("[LongSubmerged10x] Harmony patch active: " + patchType.Name + ".");
                }
                catch (Exception ex)
                {
                    // DonJ : une seule methode renommee dans UBOAT ne doit plus neutraliser tout le mod.
                    Debug.LogWarning("[LongSubmerged10x] Harmony patch skipped: " + patchType.Name + " -> " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }

    internal static class InteriorLightingColorPatcher
    {
        private static readonly Color AlarmOrangeColor = new Color(1f, 0.55f, 0.12f, 1f);
        private static readonly Color SilentRunGreenColor = new Color(0.12f, 0.78f, 0.28f, 1f);

        private static readonly FieldInfo AlarmInteriorFogColorField =
            AccessTools.Field(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "alarmInteriorFogColor");

        private static readonly FieldInfo SilentRunInteriorFogColorField =
            AccessTools.Field(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "silentRunInteriorFogColor");

        private static readonly FieldInfo AlarmLightsColorMultiplierField =
            AccessTools.Field(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "alarmLightsColorMultiplier");

        private static readonly FieldInfo SilentRunLightsColorMultiplierField =
            AccessTools.Field(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "silentRunLightsColorMultiplier");

        private static readonly MethodInfo ApplyColorMultiplierMethod =
            AccessTools.Method(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "ApplyColorMultiplier");

        private static readonly HashSet<string> MissingMemberWarnings = new HashSet<string>();

        private static readonly ConditionalWeakTable<object, InteriorLightingObjectPatchData> ObjectColorPatches =
            new ConditionalWeakTable<object, InteriorLightingObjectPatchData>();

        private static bool refreshingInteriorLighting;

        public static bool IsEnabled()
        {
            return LongSubmergedRuntimeSettings.InteriorLightingColors;
        }

        public static void ApplyAll(string reason)
        {
            try
            {
                foreach (UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting lighting in UnityEngine.Object.FindObjectsOfType<UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting>())
                    ApplyInteriorLighting(lighting, reason + ".PlayerShipInteriorLighting", true);

                foreach (UBOAT.Game.Scene.Effects.LightController controller in UnityEngine.Object.FindObjectsOfType<UBOAT.Game.Scene.Effects.LightController>())
                    ApplyLightController(controller, reason + ".LightController");

                foreach (UBOAT.Game.Scene.Effects.FillLight fillLight in UnityEngine.Object.FindObjectsOfType<UBOAT.Game.Scene.Effects.FillLight>())
                    ApplyFillLight(fillLight, reason + ".FillLight");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyInteriorLighting(
            UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting lighting,
            string reason,
            bool refreshPresets
        )
        {
            if (lighting == null)
                return;

            try
            {
                if (!IsEnabled())
                {
                    RestoreInteriorLighting(lighting, reason, refreshPresets);
                    return;
                }

                ApplyPrivateInteriorColors(lighting);
                ApplyLightControllers(lighting, reason);
                ApplyColorMultiplier(lighting);

                if (refreshPresets && !refreshingInteriorLighting)
                {
                    refreshingInteriorLighting = true;
                    try
                    {
                        lighting.ApplyLightControllersPresets();
                    }
                    finally
                    {
                        refreshingInteriorLighting = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyLightController(
            UBOAT.Game.Scene.Effects.LightController controller,
            string reason
        )
        {
            if (controller == null)
                return;

            try
            {
                if (!IsEnabled())
                {
                    RestoreLightController(controller, reason);
                    return;
                }

                SetColorProperty(controller, "AlarmColor", controller.AlarmColor, AlarmOrangeColor);
                SetColorProperty(controller, "BlueColor", controller.BlueColor, SilentRunGreenColor);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyFillLight(
            UBOAT.Game.Scene.Effects.FillLight fillLight,
            string reason
        )
        {
            if (fillLight == null)
                return;

            try
            {
                if (!IsEnabled())
                {
                    RestoreFillLight(fillLight, reason);
                    return;
                }

                SetColorProperty(fillLight, "RedColor", fillLight.RedColor, AlarmOrangeColor);
                SetColorProperty(fillLight, "BlueColor", fillLight.BlueColor, SilentRunGreenColor);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void ApplyPrivateInteriorColors(
            UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting lighting
        )
        {
            SetColorField(lighting, AlarmInteriorFogColorField, "alarmInteriorFogColor", AlarmOrangeColor);
            SetColorField(lighting, SilentRunInteriorFogColorField, "silentRunInteriorFogColor", SilentRunGreenColor);
            SetColorField(lighting, AlarmLightsColorMultiplierField, "alarmLightsColorMultiplier", Color.white);
            SetColorField(lighting, SilentRunLightsColorMultiplierField, "silentRunLightsColorMultiplier", Color.white);
        }

        private static void ApplyLightControllers(
            UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting lighting,
            string reason
        )
        {
            UBOAT.Game.Scene.Effects.LightController[] controllers =
                lighting.GetComponentsInChildren<UBOAT.Game.Scene.Effects.LightController>(true);
            for (int index = 0; index < controllers.Length; index++)
                ApplyLightController(controllers[index], reason);

            UBOAT.Game.Scene.Effects.FillLight[] fillLights =
                lighting.GetComponentsInChildren<UBOAT.Game.Scene.Effects.FillLight>(true);
            for (int index = 0; index < fillLights.Length; index++)
                ApplyFillLight(fillLights[index], reason);
        }

        private static void ApplyColorMultiplier(
            UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting lighting
        )
        {
            if (ApplyColorMultiplierMethod == null)
            {
                WarnMissingMember("ApplyColorMultiplier");
                return;
            }

            ApplyColorMultiplierMethod.Invoke(lighting, null);
        }

        private static void RestoreInteriorLighting(
            UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting lighting,
            string reason,
            bool refreshPresets
        )
        {
            RestoreColorField(lighting, AlarmInteriorFogColorField, "alarmInteriorFogColor");
            RestoreColorField(lighting, SilentRunInteriorFogColorField, "silentRunInteriorFogColor");
            RestoreColorField(lighting, AlarmLightsColorMultiplierField, "alarmLightsColorMultiplier");
            RestoreColorField(lighting, SilentRunLightsColorMultiplierField, "silentRunLightsColorMultiplier");
            ApplyLightControllers(lighting, reason);
            ApplyColorMultiplier(lighting);

            if (refreshPresets && !refreshingInteriorLighting)
            {
                refreshingInteriorLighting = true;
                try
                {
                    lighting.ApplyLightControllersPresets();
                }
                finally
                {
                    refreshingInteriorLighting = false;
                }
            }
        }

        private static void RestoreLightController(
            UBOAT.Game.Scene.Effects.LightController controller,
            string reason
        )
        {
            RestoreColorProperty(controller, "AlarmColor", controller.AlarmColor, delegate(Color value) { controller.AlarmColor = value; });
            RestoreColorProperty(controller, "BlueColor", controller.BlueColor, delegate(Color value) { controller.BlueColor = value; });
        }

        private static void RestoreFillLight(
            UBOAT.Game.Scene.Effects.FillLight fillLight,
            string reason
        )
        {
            RestoreColorProperty(fillLight, "RedColor", fillLight.RedColor, delegate(Color value) { fillLight.RedColor = value; });
            RestoreColorProperty(fillLight, "BlueColor", fillLight.BlueColor, delegate(Color value) { fillLight.BlueColor = value; });
        }

        private static void SetColorField(object target, FieldInfo field, string memberName, Color value)
        {
            if (field == null)
            {
                WarnMissingMember(memberName);
                return;
            }

            object current = field.GetValue(target);
            if (current is Color && ColorsEqual((Color)current, value))
                return;

            if (current is Color)
                RememberColorPatch(target, memberName, (Color)current, value);

            field.SetValue(target, value);
        }

        private static void SetColorProperty(object target, string memberName, Color current, Color value)
        {
            if (ColorsEqual(current, value))
                return;

            RememberColorPatch(target, memberName, current, value);
            if (target is UBOAT.Game.Scene.Effects.LightController)
            {
                UBOAT.Game.Scene.Effects.LightController controller =
                    (UBOAT.Game.Scene.Effects.LightController)target;
                if (memberName == "AlarmColor")
                    controller.AlarmColor = value;
                else if (memberName == "BlueColor")
                    controller.BlueColor = value;
            }
            else if (target is UBOAT.Game.Scene.Effects.FillLight)
            {
                UBOAT.Game.Scene.Effects.FillLight fillLight =
                    (UBOAT.Game.Scene.Effects.FillLight)target;
                if (memberName == "RedColor")
                    fillLight.RedColor = value;
                else if (memberName == "BlueColor")
                    fillLight.BlueColor = value;
            }
        }

        private static void RestoreColorField(object target, FieldInfo field, string memberName)
        {
            if (field == null)
            {
                WarnMissingMember(memberName);
                return;
            }

            object current = field.GetValue(target);
            if (!(current is Color))
                return;

            Color original;
            if (!TryConsumeColorPatch(target, memberName, (Color)current, out original))
                return;

            field.SetValue(target, original);
        }

        private static void RestoreColorProperty(
            object target,
            string memberName,
            Color current,
            Action<Color> setter
        )
        {
            Color original;
            if (!TryConsumeColorPatch(target, memberName, current, out original))
                return;

            setter(original);
        }

        private static void RememberColorPatch(object target, string memberName, Color original, Color patched)
        {
            InteriorLightingObjectPatchData data;
            if (!ObjectColorPatches.TryGetValue(target, out data))
            {
                data = new InteriorLightingObjectPatchData();
                ObjectColorPatches.Add(target, data);
            }

            InteriorLightingColorPatchValue stored;
            if (!data.Values.TryGetValue(memberName, out stored))
            {
                data.Values.Add(memberName, new InteriorLightingColorPatchValue(original, patched));
                return;
            }

            stored.PatchedValue = patched;
        }

        private static bool TryConsumeColorPatch(
            object target,
            string memberName,
            Color current,
            out Color original
        )
        {
            original = Color.clear;

            InteriorLightingObjectPatchData data;
            if (!ObjectColorPatches.TryGetValue(target, out data))
                return false;

            InteriorLightingColorPatchValue stored;
            if (!data.Values.TryGetValue(memberName, out stored))
                return false;

            data.Values.Remove(memberName);
            if (data.Values.Count == 0)
                ObjectColorPatches.Remove(target);

            if (!ColorsEqual(current, stored.PatchedValue))
                return false;

            original = stored.OriginalValue;
            return true;
        }

        private static bool ColorsEqual(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.0001f
                && Mathf.Abs(left.g - right.g) < 0.0001f
                && Mathf.Abs(left.b - right.b) < 0.0001f
                && Mathf.Abs(left.a - right.a) < 0.0001f;
        }

        private static void WarnMissingMember(string memberName)
        {
            if (!MissingMemberWarnings.Add(memberName))
                return;

            Debug.LogWarning("[LongSubmerged10x] Interior lighting color patch skipped missing member: " + memberName + ".");
        }
    }

    internal sealed class InteriorLightingObjectPatchData
    {
        public readonly Dictionary<string, InteriorLightingColorPatchValue> Values =
            new Dictionary<string, InteriorLightingColorPatchValue>();
    }

    internal sealed class InteriorLightingColorPatchValue
    {
        public readonly Color OriginalValue;
        public Color PatchedValue;

        public InteriorLightingColorPatchValue(Color originalValue, Color patchedValue)
        {
            OriginalValue = originalValue;
            PatchedValue = patchedValue;
        }
    }

    internal static class ReinforcementRuntimeController
    {
        private const float ReinforcementCooldownSeconds = 300f;
        private const float ReinforcementActiveTrackingSeconds = 900f;
        private const int RequiredPrimaryAirPatrolCalls = 2;
        private const int RequiredPrimaryWarshipCalls = 2;
        private const int DesiredFallbackUboatCount = 2;
        private const float FallbackMinimumPlayerDistance = 8f;
        private const float FallbackGroupClearance = 2.5f;
        private const float FallbackRallyDistance = 6f;
        private static readonly string[] FallbackSubmarineTypePriority = new string[]
        {
            "Type VIIC",
            "Type VIIB",
            "Type VIIC41",
            "Type IID",
            "Type IIB",
            "Type IIA"
        };

        private static readonly float[] FallbackSpawnDistances = new float[] { 10f, 12f, 14f, 16f };
        private static readonly float[] FallbackSpawnAngles = new float[] { 110f, -110f, 130f, -130f, 150f, -150f, 90f, -90f };

        private static readonly List<SandboxGroup> ActiveReinforcementGroups = new List<SandboxGroup>();
        private static readonly List<float> ActiveReinforcementGroupTrackedAt = new List<float>();
        private static readonly FieldInfo SandboxGroupWorldNavMeshField = AccessTools.Field(typeof(SandboxGroup), "worldNavMesh");

        private static bool reinforcementCallInProgress;
        private static float nextAllowedReinforcementCallTime;
        private static bool warnedMissingWorldNavMeshField;
        private static bool warnedWorldNavMeshValidationFailure;

        public static string GetStatusText()
        {
            CleanupActiveGroups();

            if (ActiveReinforcementGroups.Count > 0)
                return "Renforts deja actifs";

            float remainingSeconds = GetCooldownRemainingSeconds();
            if (remainingSeconds > 0f)
                return "Cooldown " + Mathf.CeilToInt(remainingSeconds) + "s";

            return "Pret";
        }

        public static string CallReinforcements(string reason)
        {
            CleanupActiveGroups();

            if (reinforcementCallInProgress)
            {
                Debug.LogWarning("[LongSubmerged10x] Reinforcement call skipped: already running.");
                return "Appel deja en cours";
            }

            if (ActiveReinforcementGroups.Count > 0)
            {
                Debug.LogWarning("[LongSubmerged10x] Reinforcement call skipped: active reinforcement groups still exist.");
                return "Renforts deja actifs";
            }

            float remainingSeconds = GetCooldownRemainingSeconds();
            if (remainingSeconds > 0f)
            {
                Debug.LogWarning("[LongSubmerged10x] Reinforcement call skipped: cooldown active for " + remainingSeconds + "s.");
                return "Cooldown " + Mathf.CeilToInt(remainingSeconds) + "s";
            }

            reinforcementCallInProgress = true;
            try
            {
                Debug.Log("[LongSubmerged10x] Reinforcement call requested: " + SafeReason(reason) + ".");

                PlayerShip playerShip = UnityEngine.Object.FindObjectOfType<PlayerShip>();
                if (playerShip == null)
                    return FailWithoutCooldown("Aucun sous-marin joueur", "player ship missing");

                SandboxGroup playerGroup = ResolvePlayerGroup(playerShip);
                if (playerGroup == null)
                    return FailWithoutCooldown("Groupe joueur introuvable", "player sandbox group missing");

                Country playerCountry = ResolvePlayerCountry(playerShip, playerGroup);
                if (playerCountry == null)
                    return FailWithoutCooldown("Pays joueur introuvable", "player country missing");

                Sandbox sandbox = ResolveSandbox();
                List<Country> friendlyCountries = BuildFriendlyCountries(sandbox, playerCountry);
                if (friendlyCountries.Count == 0)
                    return FailWithoutCooldown("Aucun pays ami", "no friendly country found");

                List<SandboxGroup> primaryGroups = new List<SandboxGroup>();
                int airGroups = SpawnFriendlyPatrols(
                    "LongSubmerged Air Reinforcement",
                    "Entities/Air Patrol",
                    "Air Patrol",
                    true,
                    RequiredPrimaryAirPatrolCalls,
                    friendlyCountries,
                    playerCountry,
                    playerGroup,
                    primaryGroups
                );
                int warshipGroups = SpawnFriendlyPatrols(
                    "LongSubmerged Warship Reinforcement",
                    "Entities/Warships",
                    "Warships",
                    false,
                    RequiredPrimaryWarshipCalls,
                    friendlyCountries,
                    playerCountry,
                    playerGroup,
                    primaryGroups
                );

                if (airGroups >= RequiredPrimaryAirPatrolCalls && warshipGroups >= RequiredPrimaryWarshipCalls)
                {
                    TrackCreatedGroups(primaryGroups);
                    StartReinforcementCooldown();
                    Debug.Log("[LongSubmerged10x] Reinforcement call spawned primary groups: air=" + airGroups + ", warships=" + warshipGroups + ".");
                    return "Renforts appeles";
                }

                DestroyCreatedGroups(primaryGroups, "primary incomplete");
                Debug.LogWarning("[LongSubmerged10x] Reinforcement call primary fallback: air=" + airGroups + ", warships=" + warshipGroups + ".");

                List<SandboxGroup> fallbackGroups = new List<SandboxGroup>();
                int submarineGroups = CreateManualFriendlyUboats(
                    sandbox,
                    friendlyCountries,
                    playerCountry,
                    playerGroup,
                    fallbackGroups
                );

                if (submarineGroups > 0)
                {
                    TrackCreatedGroups(fallbackGroups);
                    StartReinforcementCooldown();
                    Debug.Log("[LongSubmerged10x] Reinforcement call spawned manual fallback U-boats: submarines=" + submarineGroups + ".");
                    return submarineGroups == 1 ? "1 U-boat appele" : submarineGroups + " U-boats appeles";
                }

                DestroyCreatedGroups(fallbackGroups, "fallback failed");
                Debug.LogWarning("[LongSubmerged10x] Reinforcement call failed: no friendly U-boat fallback was available.");
                return "Aucun U-boat ami disponible";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return "Erreur renforts";
            }
            finally
            {
                reinforcementCallInProgress = false;
            }
        }

        private static int SpawnFriendlyPatrols(
            string namePrefix,
            string displayNameKey,
            string patrolType,
            bool airborne,
            int requiredCalls,
            List<Country> friendlyCountries,
            Country playerCountry,
            SandboxGroup playerGroup,
            List<SandboxGroup> createdGroups
        )
        {
            int spawnedCount = 0;
            for (int attemptIndex = 0; attemptIndex < requiredCalls; attemptIndex++)
            {
                SandboxGroup group = SpawnOneFriendlyPatrol(
                    namePrefix + " " + (attemptIndex + 1),
                    displayNameKey,
                    patrolType,
                    airborne,
                    attemptIndex,
                    friendlyCountries,
                    playerCountry,
                    playerGroup
                );

                if (group == null)
                    continue;

                createdGroups.Add(group);
                spawnedCount++;
            }

            return spawnedCount;
        }

        private static SandboxGroup SpawnOneFriendlyPatrol(
            string groupName,
            string displayNameKey,
            string patrolType,
            bool airborne,
            int attemptIndex,
            List<Country> friendlyCountries,
            Country playerCountry,
            SandboxGroup playerGroup
        )
        {
            Vector2 preferredDirection = GetPreferredDirection(playerGroup, attemptIndex, airborne);
            for (int countryIndex = 0; countryIndex < friendlyCountries.Count; countryIndex++)
            {
                Country country = friendlyCountries[countryIndex];
                if (!IsFriendlyCountry(country, playerCountry))
                    continue;

                SandboxMobileGroup group = null;
                try
                {
                    group = MissionUtility.SpawnPatrol(
                        groupName,
                        new LocalizedString(displayNameKey),
                        patrolType,
                        false,
                        country,
                        preferredDirection,
                        airborne,
                        true
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[LongSubmerged10x] Reinforcement patrol spawn failed for " + patrolType + "/" + SafeCountryCode(country) + ": " + ex.GetType().Name + ": " + ex.Message);
                }

                if (group == null)
                    continue;

                if (!IsFriendlyGroup(group, playerCountry))
                {
                    Debug.LogWarning("[LongSubmerged10x] Reinforcement patrol rejected non-friendly group: " + patrolType + "/" + SafeCountryCode(group.Country) + ".");
                    DestroyCreatedGroup(group, "non-friendly group");
                    continue;
                }

                Debug.Log("[LongSubmerged10x] Reinforcement patrol spawned: " + patrolType + "/" + SafeCountryCode(group.Country) + " at " + group.Position + ".");
                return group;
            }

            Debug.LogWarning("[LongSubmerged10x] Reinforcement patrol unavailable for friendly countries: " + patrolType + ".");
            return null;
        }

        private static int CreateManualFriendlyUboats(
            Sandbox sandbox,
            List<Country> friendlyCountries,
            Country playerCountry,
            SandboxGroup playerGroup,
            List<SandboxGroup> createdGroups
        )
        {
            if (sandbox == null)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback skipped: sandbox missing.");
                return 0;
            }

            List<Country> fallbackCountries = BuildFallbackSubmarineCountries(sandbox, friendlyCountries, playerCountry);
            if (fallbackCountries.Count == 0)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback skipped: no friendly submarine country.");
                return 0;
            }

            int createdCount = 0;
            for (int reinforcementIndex = 0; reinforcementIndex < DesiredFallbackUboatCount; reinforcementIndex++)
            {
                SandboxMobileGroup group = CreateOneManualFriendlyUboat(sandbox, fallbackCountries, playerCountry, playerGroup, reinforcementIndex);
                if (group == null)
                    continue;

                createdGroups.Add(group);
                createdCount++;
            }

            return createdCount;
        }

        private static SandboxMobileGroup CreateOneManualFriendlyUboat(
            Sandbox sandbox,
            List<Country> fallbackCountries,
            Country playerCountry,
            SandboxGroup playerGroup,
            int reinforcementIndex
        )
        {
            Vector2 spawnPosition;
            if (!TryGetFallbackSpawnPosition(sandbox, playerGroup, reinforcementIndex, out spawnPosition))
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback skipped: no clear horizon position.");
                return null;
            }

            Vector2 rallyPoint = GetFallbackRallyPoint(playerGroup, reinforcementIndex);
            for (int countryIndex = 0; countryIndex < fallbackCountries.Count; countryIndex++)
            {
                Country country = fallbackCountries[countryIndex];
                if (!IsFriendlyCountry(country, playerCountry))
                    continue;

                for (int typeIndex = 0; typeIndex < FallbackSubmarineTypePriority.Length; typeIndex++)
                {
                    SandboxMobileGroup group = TryCreateManualUboat(
                        sandbox,
                        country,
                        playerCountry,
                        playerGroup,
                        reinforcementIndex,
                        FallbackSubmarineTypePriority[typeIndex],
                        spawnPosition,
                        rallyPoint
                    );

                    if (group != null)
                        return group;
                }
            }

            Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback failed: no friendly submarine blueprint worked.");
            return null;
        }

        private static SandboxMobileGroup TryCreateManualUboat(
            Sandbox sandbox,
            Country country,
            Country playerCountry,
            SandboxGroup playerGroup,
            int reinforcementIndex,
            string submarineTypeName,
            Vector2 spawnPosition,
            Vector2 rallyPoint
        )
        {
            SandboxMobileGroup group = null;
            SandboxEntity entity = null;
            bool entityAttachedToGroup = false;
            try
            {
                group = SandboxGroup.Create<SandboxMobileGroup>(
                    "LongSubmerged U-boat Reinforcement " + (reinforcementIndex + 1),
                    "Submarine",
                    spawnPosition,
                    country
                );

                if (group == null)
                    return null;

                CharacterAI ai = EnsureCharacterAi(group);
                entity = SandboxEntity.Create(submarineTypeName, country);
                if (entity == null)
                {
                    DestroyCreatedGroup(group, "manual U-boat entity missing");
                    return null;
                }

                entity.Position = spawnPosition;
                entity.FormationPosition = Vector2.zero;
                entity.RandomizeSpawnPosition = false;
                entity.SpawnsInstantly = true;

                group.Position = spawnPosition;
                group.Up = GetDirectionTowards(spawnPosition, rallyPoint, ResolvePlayerForward(playerGroup));
                group.AddEntity(entity);
                entityAttachedToGroup = true;
                group.Velocity = Mathf.Max(0f, group.MaxVelocity * 0.55f);
                AddFallbackSailToBehaviour(ai, rallyPoint);

                sandbox.AddGroup(group);
                RefreshCreatedGroup(group);
                AddFallbackObservations(playerGroup, group);

                if (!IsFriendlyGroup(group, playerCountry))
                {
                    Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback rejected non-friendly group: " + SafeCountryCode(group.Country) + ".");
                    DestroyCreatedGroup(group, "manual non-friendly group");
                    return null;
                }

                Debug.Log("[LongSubmerged10x] Manual U-boat fallback spawned: type=" + submarineTypeName + ", country=" + SafeCountryCode(country) + ", position=" + group.Position + ".");
                return group;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback failed for " + submarineTypeName + "/" + SafeCountryCode(country) + ": " + ex.GetType().Name + ": " + ex.Message);
                if (group != null)
                    DestroyCreatedGroup(group, "manual U-boat exception");
                if (!entityAttachedToGroup && entity != null)
                    DestroyCreatedEntity(entity, "manual U-boat orphan");

                return null;
            }
        }

        private static List<Country> BuildFallbackSubmarineCountries(Sandbox sandbox, List<Country> friendlyCountries, Country playerCountry)
        {
            List<Country> countries = new List<Country>();
            AddFriendlyCountry(countries, FindCountryByCode(sandbox, "DE"), playerCountry);
            AddFriendlyCountry(countries, playerCountry, playerCountry);

            if (friendlyCountries != null)
            {
                for (int index = 0; index < friendlyCountries.Count; index++)
                    AddFriendlyCountry(countries, friendlyCountries[index], playerCountry);
            }

            return countries;
        }

        private static Country FindCountryByCode(Sandbox sandbox, string countryCode)
        {
            if (sandbox == null || sandbox.Countries == null || string.IsNullOrEmpty(countryCode))
                return null;

            Country[] countries = sandbox.Countries;
            for (int index = 0; index < countries.Length; index++)
            {
                Country country = countries[index];
                if (country != null && string.Equals(country.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase))
                    return country;
            }

            return null;
        }

        private static bool TryGetFallbackSpawnPosition(Sandbox sandbox, SandboxGroup playerGroup, int reinforcementIndex, out Vector2 position)
        {
            position = Vector2.zero;
            if (playerGroup == null)
                return false;

            Vector2 origin = playerGroup.Position;
            Vector2 forward = ResolvePlayerForward(playerGroup);
            WorldNavMesh worldNavMesh = ResolveWorldNavMesh();
            int angleOffset = (reinforcementIndex * 2) % FallbackSpawnAngles.Length;
            for (int distanceIndex = 0; distanceIndex < FallbackSpawnDistances.Length; distanceIndex++)
            {
                for (int angleIndex = 0; angleIndex < FallbackSpawnAngles.Length; angleIndex++)
                {
                    float angle = FallbackSpawnAngles[(angleIndex + angleOffset) % FallbackSpawnAngles.Length];
                    float distance = FallbackSpawnDistances[distanceIndex];
                    Vector2 candidate = origin + Rotate(forward, angle) * distance;
                    candidate = SnapFallbackSpawnPosition(worldNavMesh, candidate);
                    if (!IsFallbackSpawnPositionClear(sandbox, playerGroup, worldNavMesh, candidate))
                        continue;

                    position = candidate;
                    return true;
                }
            }

            return false;
        }

        private static WorldNavMesh ResolveWorldNavMesh()
        {
            try
            {
                if (SandboxGroupWorldNavMeshField == null)
                {
                    if (!warnedMissingWorldNavMeshField)
                    {
                        warnedMissingWorldNavMeshField = true;
                        Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback navmesh check skipped: SandboxGroup.worldNavMesh field missing.");
                    }

                    return null;
                }

                return SandboxGroupWorldNavMeshField.GetValue(null) as WorldNavMesh;
            }
            catch (Exception ex)
            {
                WarnWorldNavMeshValidationSkipped(ex);
                return null;
            }
        }

        private static Vector2 SnapFallbackSpawnPosition(WorldNavMesh worldNavMesh, Vector2 position)
        {
            if (worldNavMesh == null)
                return position;

            try
            {
                return worldNavMesh.SnapWorld(position);
            }
            catch (Exception ex)
            {
                WarnWorldNavMeshValidationSkipped(ex);
                return position;
            }
        }

        private static bool IsFallbackSpawnPositionClear(Sandbox sandbox, SandboxGroup playerGroup, WorldNavMesh worldNavMesh, Vector2 position)
        {
            if (playerGroup != null)
            {
                Vector2 fromPlayer = position - playerGroup.Position;
                if (fromPlayer.sqrMagnitude < FallbackMinimumPlayerDistance * FallbackMinimumPlayerDistance)
                    return false;
            }

            if (!IsFallbackSpawnPositionOnNavMesh(worldNavMesh, playerGroup, position))
                return false;

            if (sandbox == null)
                return true;

            try
            {
                List<SandboxGroup> nearbyGroups = sandbox.GetGroupsInRange(position, FallbackGroupClearance, false);
                if (nearbyGroups == null)
                    return true;

                for (int index = 0; index < nearbyGroups.Count; index++)
                {
                    SandboxGroup group = nearbyGroups[index];
                    if (group != null && group != playerGroup && !ActiveReinforcementGroups.Contains(group))
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback group clearance check skipped: " + ex.GetType().Name + ": " + ex.Message);
            }

            return true;
        }

        private static bool IsFallbackSpawnPositionOnNavMesh(WorldNavMesh worldNavMesh, SandboxGroup playerGroup, Vector2 position)
        {
            if (worldNavMesh == null)
                return true;

            try
            {
                if (!worldNavMesh.IsOnNavMesh(position))
                    return false;

                if (playerGroup != null)
                {
                    Vector2 hit;
                    if (worldNavMesh.RaycastLandsNavMesh(position, playerGroup.Position, out hit))
                        return false;
                }
            }
            catch (Exception ex)
            {
                WarnWorldNavMeshValidationSkipped(ex);
                return true;
            }

            return true;
        }

        private static void WarnWorldNavMeshValidationSkipped(Exception ex)
        {
            if (warnedWorldNavMeshValidationFailure)
                return;

            warnedWorldNavMeshValidationFailure = true;
            Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback navmesh check skipped: " + ex.GetType().Name + ": " + ex.Message);
        }

        private static Vector2 GetFallbackRallyPoint(SandboxGroup playerGroup, int reinforcementIndex)
        {
            if (playerGroup == null)
                return Vector2.zero;

            Vector2 forward = ResolvePlayerForward(playerGroup);
            float rallyAngle = reinforcementIndex % 2 == 0 ? 70f : -70f;
            return playerGroup.Position + Rotate(forward, rallyAngle) * FallbackRallyDistance;
        }

        private static Vector2 ResolvePlayerForward(SandboxGroup playerGroup)
        {
            Vector2 forward = Vector2.up;
            if (playerGroup != null && playerGroup.Up.sqrMagnitude > 0.0001f)
                forward = playerGroup.Up.normalized;

            return forward;
        }

        private static Vector2 GetDirectionTowards(Vector2 from, Vector2 to, Vector2 fallback)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude <= 0.0001f)
                direction = fallback;

            if (direction.sqrMagnitude <= 0.0001f)
                direction = Vector2.up;

            return direction.normalized;
        }

        private static CharacterAI EnsureCharacterAi(SandboxMobileGroup group)
        {
            if (group == null)
                return null;

            try
            {
                CharacterAI ai = group.GetComponent<CharacterAI>();
                if (ai != null)
                    return ai;

                return group.gameObject.AddComponent<CharacterAI>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback AI creation skipped: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static void AddFallbackSailToBehaviour(CharacterAI ai, Vector2 rallyPoint)
        {
            if (ai == null)
                return;

            try
            {
                SailToBehaviour sailToBehaviour = new SailToBehaviour(ai, 1.5f, rallyPoint);
                sailToBehaviour.Flags = AIBehaviourFlags.OneShot;
                ai.AddBehaviour(sailToBehaviour);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback sail behaviour skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void RefreshCreatedGroup(SandboxMobileGroup group)
        {
            if (group == null)
                return;

            try
            {
                group.UpdateGroup();
                group.UpdateGroupLowFrequency(false);

                if (group.AI != null)
                {
                    for (int index = 0; index < 3; index++)
                        group.AI.UpdateAI(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback refresh skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void AddFallbackObservations(SandboxGroup playerGroup, SandboxGroup reinforcementGroup)
        {
            if (playerGroup == null || reinforcementGroup == null)
                return;

            try
            {
                playerGroup.AddObservation(reinforcementGroup, GroupDetectionMethod.IndirectObservation);
                reinforcementGroup.AddObservation(playerGroup, GroupDetectionMethod.IndirectObservation);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Manual U-boat fallback observations skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static SandboxGroup ResolvePlayerGroup(PlayerShip playerShip)
        {
            if (playerShip == null)
                return null;

            if (playerShip.SandboxGroup != null)
                return playerShip.SandboxGroup;

            SandboxPlayerWolfpack wolfpack = playerShip.SandboxPlayerShip;
            return wolfpack as SandboxGroup;
        }

        private static Country ResolvePlayerCountry(PlayerShip playerShip, SandboxGroup playerGroup)
        {
            if (playerGroup != null && playerGroup.Country != null)
                return playerGroup.Country;

            if (playerShip != null && playerShip.SandboxEntity != null && playerShip.SandboxEntity.Country != null)
                return playerShip.SandboxEntity.Country;

            if (playerShip != null)
                return playerShip.Country;

            return null;
        }

        private static Sandbox ResolveSandbox()
        {
            if (Sandbox.Instance != null)
                return Sandbox.Instance;

            return UnityEngine.Object.FindObjectOfType<Sandbox>();
        }

        private static List<Country> BuildFriendlyCountries(Sandbox sandbox, Country playerCountry)
        {
            List<Country> friendlyCountries = new List<Country>();
            AddFriendlyCountry(friendlyCountries, playerCountry, playerCountry);

            if (sandbox != null && sandbox.Countries != null)
            {
                Country[] countries = sandbox.Countries;
                for (int index = 0; index < countries.Length; index++)
                    AddFriendlyCountry(friendlyCountries, countries[index], playerCountry);
            }

            return friendlyCountries;
        }

        private static void AddFriendlyCountry(List<Country> countries, Country candidate, Country playerCountry)
        {
            if (!IsFriendlyCountry(candidate, playerCountry))
                return;

            for (int index = 0; index < countries.Count; index++)
            {
                if (countries[index] == candidate)
                    return;
            }

            countries.Add(candidate);
        }

        private static bool IsFriendlyGroup(SandboxGroup group, Country playerCountry)
        {
            return group != null && IsFriendlyCountry(group.Country, playerCountry);
        }

        private static bool IsFriendlyCountry(Country candidate, Country playerCountry)
        {
            if (candidate == null || playerCountry == null)
                return false;

            if (candidate == playerCountry)
                return true;

            return playerCountry.GetRelationWith(candidate) == Country.Relation.Ally;
        }

        private static Vector2 GetPreferredDirection(SandboxGroup playerGroup, int attemptIndex, bool airborne)
        {
            Vector2 direction = Vector2.up;
            if (playerGroup != null && playerGroup.Up.sqrMagnitude > 0.0001f)
                direction = playerGroup.Up.normalized;

            float baseAngle = airborne ? 28f : -28f;
            float stepAngle = attemptIndex % 2 == 0 ? 18f : -18f;
            return Rotate(direction, baseAngle + stepAngle).normalized;
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
        }

        private static void TrackCreatedGroups(List<SandboxGroup> groups)
        {
            for (int index = 0; index < groups.Count; index++)
            {
                SandboxGroup group = groups[index];
                if (group != null && !ActiveReinforcementGroups.Contains(group))
                {
                    ActiveReinforcementGroups.Add(group);
                    ActiveReinforcementGroupTrackedAt.Add(Time.unscaledTime);
                }
            }
        }

        private static void CleanupActiveGroups()
        {
            while (ActiveReinforcementGroupTrackedAt.Count < ActiveReinforcementGroups.Count)
                ActiveReinforcementGroupTrackedAt.Add(Time.unscaledTime);

            while (ActiveReinforcementGroupTrackedAt.Count > ActiveReinforcementGroups.Count)
                ActiveReinforcementGroupTrackedAt.RemoveAt(ActiveReinforcementGroupTrackedAt.Count - 1);

            for (int index = ActiveReinforcementGroups.Count - 1; index >= 0; index--)
            {
                SandboxGroup group = ActiveReinforcementGroups[index];
                float trackedAt = ActiveReinforcementGroupTrackedAt[index];
                bool trackingExpired = Time.unscaledTime - trackedAt >= ReinforcementActiveTrackingSeconds;
                if (group == null || trackingExpired)
                {
                    ActiveReinforcementGroups.RemoveAt(index);
                    ActiveReinforcementGroupTrackedAt.RemoveAt(index);
                    if (trackingExpired && group != null)
                        Debug.Log("[LongSubmerged10x] Reinforcement group tracking expired; cooldown now controls new calls.");
                }
            }
        }

        private static void DestroyCreatedGroups(List<SandboxGroup> groups, string reason)
        {
            for (int index = 0; index < groups.Count; index++)
                DestroyCreatedGroup(groups[index], reason);

            groups.Clear();
        }

        private static void DestroyCreatedGroup(SandboxGroup group, string reason)
        {
            if (group == null)
                return;

            try
            {
                group.DestroyGroup();
                Debug.Log("[LongSubmerged10x] Reinforcement group removed after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Reinforcement group cleanup failed after " + reason + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void DestroyCreatedEntity(SandboxEntity entity, string reason)
        {
            if (entity == null)
                return;

            try
            {
                entity.Destroy(false);
                Debug.Log("[LongSubmerged10x] Reinforcement entity removed after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Reinforcement entity cleanup failed after " + reason + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static float GetCooldownRemainingSeconds()
        {
            return Mathf.Max(0f, nextAllowedReinforcementCallTime - Time.unscaledTime);
        }

        private static void StartReinforcementCooldown()
        {
            nextAllowedReinforcementCallTime = Time.unscaledTime + ReinforcementCooldownSeconds;
        }

        private static string FailWithoutCooldown(string uiMessage, string logReason)
        {
            Debug.LogWarning("[LongSubmerged10x] Reinforcement call failed: " + logReason + ".");
            return uiMessage;
        }

        private static string SafeCountryCode(Country country)
        {
            if (country == null)
                return "none";

            return string.IsNullOrEmpty(country.CountryCode) ? "unknown" : country.CountryCode;
        }

        private static string SafeReason(string reason)
        {
            return string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }
    }

    internal static class LongSubmergedRuntimeSettings
    {
        private const string PrefPrefix = "LongSubmerged10x.";
        private const int RuntimeSettingsVersion = 18;

        public const float MinRuntimeFactor = 1f;
        public const float BatteryMaxFactor = 100f;
        public const float OxygenMaxFactor = 100f;
        public const float SpeedMaxFactor = 20f;
        public const float TorpedoMaxFactor = 10f;
        public const float SonarMaxFactor = 10f;
        public const float HeavyArmorDamageFactor = 3f;
        public const float SuperStealthFactor = 3f;

        // Compatibilite interne : les anciens blocs utilisaient MaxRuntimeFactor pour Batterie/Oxygene.
        // Les sliders vitesse, torpilles et sonar ont maintenant leurs propres bornes.
        public const float MaxRuntimeFactor = BatteryMaxFactor;

        private const bool DefaultMegaBattery = true;
        private const bool DefaultMegaOxygen = true;
        private const bool DefaultSuperSpeed = true;
        private const bool DefaultMegaTorpedoes = true;
        private const bool DefaultMegaSonar = true;
        private const bool DefaultHeavyArmor = false;
        private const bool DefaultSuperStealth = false;
        private const bool DefaultDeepDive = true;
        private const bool DefaultInteriorLightingColors = true;

        // DonJ: readable in-game defaults. Mega Batterie now means a fully infinite battery.
        // The battery slider is kept only as a saved legacy value and no longer gates infinity.
        // Oxygen 100 is calibrated around 90 days, speed defaults to x8, torpedoes to x10,
        // and sonar defaults to x3 while remaining adjustable up to x10.
        private const float DefaultBatteryFactor = 10f;
        private const float DefaultOxygenFactor = 100f;
        private const float DefaultSpeedFactor = 8f;
        private const float DefaultTorpedoFactor = 10f;
        private const float DefaultSonarFactor = 3f;

        public static bool MegaBattery = DefaultMegaBattery;
        public static bool MegaOxygen = DefaultMegaOxygen;
        public static bool SuperSpeed = DefaultSuperSpeed;
        public static bool MegaTorpedoes = DefaultMegaTorpedoes;
        public static bool MegaSonar = DefaultMegaSonar;
        public static bool HeavyArmor = DefaultHeavyArmor;
        public static bool SuperStealth = DefaultSuperStealth;
        public static bool DeepDive = DefaultDeepDive;
        public static bool InteriorLightingColors = DefaultInteriorLightingColors;
        public static float BatteryFactor = DefaultBatteryFactor;
        public static float OxygenFactor = DefaultOxygenFactor;
        public static float SpeedFactor = DefaultSpeedFactor;
        public static float TorpedoFactor = DefaultTorpedoFactor;
        public static float SonarFactor = DefaultSonarFactor;

        public static void Load()
        {
            int savedVersion = PlayerPrefs.GetInt(PrefPrefix + "RuntimeSettingsVersion", 0);

            MegaBattery = ReadBool("MegaBattery", DefaultMegaBattery);
            MegaOxygen = ReadBool("MegaOxygen", DefaultMegaOxygen);
            SuperSpeed = ReadBool("SuperSpeed", DefaultSuperSpeed);
            MegaTorpedoes = ReadBool("MegaTorpedoes", DefaultMegaTorpedoes);
            MegaSonar = ReadBool("MegaSonar", DefaultMegaSonar);
            HeavyArmor = ReadBool("HeavyArmor", DefaultHeavyArmor);
            SuperStealth = ReadBool("SuperStealth", DefaultSuperStealth);
            DeepDive = ReadBool("DeepDive", DefaultDeepDive);
            InteriorLightingColors = ReadBool("InteriorLightingColors", DefaultInteriorLightingColors);

            BatteryFactor = ReadFactor("BatteryFactor", DefaultBatteryFactor, BatteryMaxFactor);
            OxygenFactor = ReadFactor("OxygenFactor", DefaultOxygenFactor, OxygenMaxFactor);
            SpeedFactor = ReadFactor("SpeedFactor", DefaultSpeedFactor, SpeedMaxFactor);
            TorpedoFactor = ReadFactor("TorpedoFactor", DefaultTorpedoFactor, TorpedoMaxFactor);
            SonarFactor = ReadFactor("SonarFactor", DefaultSonarFactor, SonarMaxFactor);

            if (savedVersion < RuntimeSettingsVersion)
            {
                // Keep existing runtime choices, but force Heavy Armor off once after its default changed.
                if (savedVersion < 16)
                    HeavyArmor = false;

                Save();
                Debug.Log("[LongSubmerged10x] Runtime settings migrated to v" + RuntimeSettingsVersion + ".");
            }
        }

        public static void Save()
        {
            BatteryFactor = ClampBatteryFactor(BatteryFactor);
            OxygenFactor = ClampOxygenFactor(OxygenFactor);
            SpeedFactor = ClampSpeedFactor(SpeedFactor);
            TorpedoFactor = ClampTorpedoFactor(TorpedoFactor);
            SonarFactor = ClampSonarFactor(SonarFactor);

            PlayerPrefs.SetInt(PrefPrefix + "MegaBattery", MegaBattery ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaOxygen", MegaOxygen ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "SuperSpeed", SuperSpeed ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaTorpedoes", MegaTorpedoes ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaSonar", MegaSonar ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "HeavyArmor", HeavyArmor ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "SuperStealth", SuperStealth ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "DeepDive", DeepDive ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "InteriorLightingColors", InteriorLightingColors ? 1 : 0);
            PlayerPrefs.SetFloat(PrefPrefix + "BatteryFactor", BatteryFactor);
            PlayerPrefs.SetFloat(PrefPrefix + "OxygenFactor", OxygenFactor);
            PlayerPrefs.SetFloat(PrefPrefix + "SpeedFactor", SpeedFactor);
            PlayerPrefs.SetFloat(PrefPrefix + "TorpedoFactor", TorpedoFactor);
            PlayerPrefs.SetFloat(PrefPrefix + "SonarFactor", SonarFactor);
            PlayerPrefs.SetInt(PrefPrefix + "RuntimeSettingsVersion", RuntimeSettingsVersion);
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            MegaBattery = DefaultMegaBattery;
            MegaOxygen = DefaultMegaOxygen;
            SuperSpeed = DefaultSuperSpeed;
            MegaTorpedoes = DefaultMegaTorpedoes;
            MegaSonar = DefaultMegaSonar;
            HeavyArmor = DefaultHeavyArmor;
            SuperStealth = DefaultSuperStealth;
            DeepDive = DefaultDeepDive;
            InteriorLightingColors = DefaultInteriorLightingColors;
            BatteryFactor = DefaultBatteryFactor;
            OxygenFactor = DefaultOxygenFactor;
            SpeedFactor = DefaultSpeedFactor;
            TorpedoFactor = DefaultTorpedoFactor;
            SonarFactor = DefaultSonarFactor;
        }

        public static float ClampFactor(float value)
        {
            return ClampFactor(value, MaxRuntimeFactor);
        }

        public static float ClampBatteryFactor(float value)
        {
            return ClampFactor(value, BatteryMaxFactor);
        }

        public static float ClampOxygenFactor(float value)
        {
            return ClampFactor(value, OxygenMaxFactor);
        }

        public static float ClampSpeedFactor(float value)
        {
            return ClampFactor(value, SpeedMaxFactor);
        }

        public static float ClampTorpedoFactor(float value)
        {
            return ClampFactor(value, TorpedoMaxFactor);
        }

        public static float ClampSonarFactor(float value)
        {
            return ClampFactor(value, SonarMaxFactor);
        }

        public static float ClampFactor(float value, float maxValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return MinRuntimeFactor;

            return Mathf.Clamp(value, MinRuntimeFactor, Mathf.Max(MinRuntimeFactor, maxValue));
        }

        private static bool ReadBool(string key, bool fallback)
        {
            return PlayerPrefs.GetInt(PrefPrefix + key, fallback ? 1 : 0) != 0;
        }

        private static float ReadFactor(string key, float fallback, float maxValue)
        {
            return ClampFactor(PlayerPrefs.GetFloat(PrefPrefix + key, fallback), maxValue);
        }
    }

    // DonJ : vrai menu Unity UI en ScreenSpaceOverlay. Je n'utilise plus l'ancien rendu IMGUI,
    // car UBOAT pouvait figer ou masquer ce rendu. F10 ouvre/ferme, Escape ferme, et les changements s'appliquent en jeu.
    internal sealed class LongSubmergedMenuController : MonoBehaviour
    {
        private const KeyCode MenuKey = KeyCode.F10;
        private const int CanvasSortingOrder = 32000;
        private const float BatteryMaintenanceIntervalSeconds = 0.20f;
        private const float MegaSonarMaintenanceIntervalSeconds = 1.00f;
        private static LongSubmergedMenuController instance;
        private static Font cachedFont;

        private GameObject panelObject;
        private Toggle megaBatteryToggle;
        private Toggle megaOxygenToggle;
        private Toggle superSpeedToggle;
        private Toggle megaTorpedoesToggle;
        private Toggle megaSonarToggle;
        private Toggle heavyArmorToggle;
        private Toggle superStealthToggle;
        private Toggle deepDiveToggle;
        private Toggle interiorLightingToggle;
        private Slider batteryFactorSlider;
        private Slider oxygenFactorSlider;
        private Slider speedFactorSlider;
        private Slider torpedoFactorSlider;
        private Slider sonarFactorSlider;
        private Button callReinforcementsButton;
        private Text batteryFactorValueText;
        private Text oxygenFactorValueText;
        private Text speedFactorValueText;
        private Text torpedoFactorValueText;
        private Text sonarFactorValueText;
        private Text reinforcementsStatusText;
        private bool visible;
        private bool suppressToggleEvents;
        private float nextBatteryMaintenanceTime;
        private float nextMegaSonarMaintenanceTime;
        private string reinforcementStatusOverride;
        private float reinforcementStatusOverrideUntil;
        private bool cursorCaptured;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLockState;

        public static void Ensure()
        {
            if (instance != null)
            {
                instance.EnsureUi();
                return;
            }

            instance = UnityEngine.Object.FindObjectOfType<LongSubmergedMenuController>();
            if (instance != null)
            {
                instance.EnsureUi();
                return;
            }

            GameObject go = new GameObject("LongSubmerged10x Runtime Menu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<LongSubmergedMenuController>();
        }

        private void Awake()
        {
            instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            EnsureUi();
        }

        private void OnDestroy()
        {
            RestoreCursorIfNeeded();

            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(MenuKey))
                SetVisible(!visible, "F10");

            if (visible && Input.GetKeyDown(KeyCode.Escape))
                SetVisible(false, "Escape");

            RunBatteryMaintenanceTick();
            RunMegaSonarMaintenanceTick();

            if (visible)
                RefreshReinforcementsStatus();
        }

        private void RunBatteryMaintenanceTick()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return;

            // DonJ : le heartbeat tourne meme menu ferme. UBOAT peut recalculer la batterie apres chargement,
            // changement d'equipement ou equipage ; je reapplique donc le mode nucleaire regulierement.
            float now = Time.unscaledTime;
            if (now < nextBatteryMaintenanceTime)
                return;

            nextBatteryMaintenanceTime = now + BatteryMaintenanceIntervalSeconds;
            LongSubmergedRuntimeApplier.MaintainBatteryRuntime("runtime heartbeat");
        }

        private void RunMegaSonarMaintenanceTick()
        {
            if (!LongSubmergedRuntimeSettings.MegaSonar)
                return;

            float now = Time.unscaledTime;
            if (now < nextMegaSonarMaintenanceTime)
                return;

            nextMegaSonarMaintenanceTime = now + MegaSonarMaintenanceIntervalSeconds;
            MegaSonarRuntimePatcher.ApplyAll("runtime sonar heartbeat");
        }

        private void SaveAndApplyCurrentControlsNow(string reason)
        {
            ReadControlStateIntoSettings();
            RefreshFactorLabels();
            LongSubmergedRuntimeSettings.Save();
            nextBatteryMaintenanceTime = 0f;
            nextMegaSonarMaintenanceTime = 0f;
            LongSubmergedRuntimeApplier.ApplyAll(string.IsNullOrEmpty(reason) ? "unity ui change" : reason);
        }

        private void EnsureUi()
        {
            if (panelObject != null)
                return;

            try
            {
                // DonJ : Canvas overlay avec ordre tres haut pour passer au-dessus de l'UI du jeu.
                Canvas canvas = gameObject.GetComponent<Canvas>();
                if (canvas == null)
                    canvas = gameObject.AddComponent<Canvas>();

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = CanvasSortingOrder;
                canvas.overrideSorting = true;

                CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
                if (scaler == null)
                    scaler = gameObject.AddComponent<CanvasScaler>();

                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                if (gameObject.GetComponent<GraphicRaycaster>() == null)
                    gameObject.AddComponent<GraphicRaycaster>();

                EnsureEventSystem();
                BuildPanel();
                RefreshControlState();
                panelObject.SetActive(false);
                Debug.Log("[LongSubmerged10x] Runtime Unity UI menu controller ready on F10.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void BuildPanel()
        {
            // DonJ : panneau compact de test runtime. Tous les controles modifient les valeurs sauvegardees
            // et rappellent ApplyAll pour voir le resultat directement dans la partie.
            panelObject = CreateUiObject("LongSubmerged10x Panel", transform);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.05f, 0.06f, 0.96f);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(28f, -82f);
            panelRect.sizeDelta = new Vector2(470f, 815f);

            CreateText(panelObject.transform, "Title", "Long Submerged 10x+", 20, FontStyle.Bold, new Vector2(18f, -16f), new Vector2(410f, 30f));
            CreateText(panelObject.transform, "Hint", "F10 ferme. Les reglages sont sauvegardes et appliques en partie.", 13, FontStyle.Normal, new Vector2(18f, -48f), new Vector2(430f, 24f));

            megaBatteryToggle = CreateToggle(panelObject.transform, "Mega Batterie", new Vector2(20f, -82f));
            batteryFactorSlider = CreateFactorSlider(panelObject.transform, "Batterie", LongSubmergedRuntimeSettings.BatteryMaxFactor, new Vector2(20f, -118f), out batteryFactorValueText);

            megaOxygenToggle = CreateToggle(panelObject.transform, "Mega Oxygene", new Vector2(20f, -158f));
            oxygenFactorSlider = CreateFactorSlider(panelObject.transform, "Oxygene", LongSubmergedRuntimeSettings.OxygenMaxFactor, new Vector2(20f, -194f), out oxygenFactorValueText);

            superSpeedToggle = CreateToggle(panelObject.transform, "SuperVitesse", new Vector2(20f, -234f));
            speedFactorSlider = CreateFactorSlider(panelObject.transform, "Vitesses rapides", LongSubmergedRuntimeSettings.SpeedMaxFactor, new Vector2(20f, -270f), out speedFactorValueText);

            megaTorpedoesToggle = CreateToggle(panelObject.transform, "Mega Torpilles", new Vector2(20f, -310f));
            torpedoFactorSlider = CreateFactorSlider(panelObject.transform, "Torpilles", LongSubmergedRuntimeSettings.TorpedoMaxFactor, new Vector2(20f, -346f), out torpedoFactorValueText);

            megaSonarToggle = CreateToggle(panelObject.transform, "Mega Sonar", new Vector2(20f, -386f));
            sonarFactorSlider = CreateFactorSlider(panelObject.transform, "Hydrophone portee", LongSubmergedRuntimeSettings.SonarMaxFactor, new Vector2(20f, -422f), out sonarFactorValueText);

            heavyArmorToggle = CreateToggle(panelObject.transform, "Blindage lourd x3", new Vector2(20f, -462f));
            superStealthToggle = CreateToggle(panelObject.transform, "Super discrétion", new Vector2(20f, -498f));

            deepDiveToggle = CreateToggle(panelObject.transform, "Plongée x2", new Vector2(20f, -534f));
            interiorLightingToggle = CreateToggle(panelObject.transform, "Lumières orange/vert", new Vector2(20f, -570f));

            callReinforcementsButton = CreateButton(panelObject.transform, "Appeler renforts", new Vector2(20f, -614f), new Vector2(180f, 38f));
            callReinforcementsButton.onClick.AddListener(OnCallReinforcementsClicked);

            reinforcementsStatusText = CreateText(panelObject.transform, "Renforts Status", "Pret", 13, FontStyle.Normal, new Vector2(216f, -614f), new Vector2(230f, 38f));
            reinforcementsStatusText.alignment = TextAnchor.MiddleLeft;

            Button defaultsButton = CreateButton(panelObject.transform, "Par defaut", new Vector2(20f, -710f), new Vector2(140f, 38f));
            defaultsButton.onClick.AddListener(OnDefaultsClicked);

            Button refreshButton = CreateButton(panelObject.transform, "Reappliquer maintenant", new Vector2(176f, -710f), new Vector2(220f, 38f));
            refreshButton.onClick.AddListener(OnRefreshClicked);
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject("LongSubmerged10x EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
            Debug.Log("[LongSubmerged10x] Runtime menu created fallback EventSystem.");
        }

        private void SetVisible(bool value, string source)
        {
            EnsureUi();

            if (panelObject == null || visible == value)
                return;

            visible = value;
            panelObject.SetActive(visible);

            if (visible)
            {
                RefreshControlState();
                RefreshReinforcementsStatus();
                CaptureCursor();
            }
            else
            {
                RestoreCursorIfNeeded();
            }

            Debug.Log("[LongSubmerged10x] Runtime menu " + (visible ? "opened" : "closed") + " by " + source + ".");
        }

        private void OnToggleChanged(bool ignored)
        {
            if (suppressToggleEvents)
                return;

            SaveAndApplyCurrentControlsNow("unity ui toggle");
        }

        private void OnFactorSliderChanged(float ignored)
        {
            if (suppressToggleEvents)
                return;

            SaveAndApplyCurrentControlsNow("unity ui slider");
        }

        private void ReadControlStateIntoSettings()
        {
            // UI changes must update the runtime state before every immediate apply.
            LongSubmergedRuntimeSettings.MegaBattery = megaBatteryToggle != null && megaBatteryToggle.isOn;
            LongSubmergedRuntimeSettings.MegaOxygen = megaOxygenToggle != null && megaOxygenToggle.isOn;
            LongSubmergedRuntimeSettings.SuperSpeed = superSpeedToggle != null && superSpeedToggle.isOn;
            LongSubmergedRuntimeSettings.MegaTorpedoes = megaTorpedoesToggle != null && megaTorpedoesToggle.isOn;
            LongSubmergedRuntimeSettings.MegaSonar = megaSonarToggle != null && megaSonarToggle.isOn;
            LongSubmergedRuntimeSettings.HeavyArmor = heavyArmorToggle != null && heavyArmorToggle.isOn;
            LongSubmergedRuntimeSettings.SuperStealth = superStealthToggle != null && superStealthToggle.isOn;
            LongSubmergedRuntimeSettings.DeepDive = deepDiveToggle != null && deepDiveToggle.isOn;
            LongSubmergedRuntimeSettings.InteriorLightingColors = interiorLightingToggle != null && interiorLightingToggle.isOn;
            LongSubmergedRuntimeSettings.BatteryFactor = ReadSliderFactor(batteryFactorSlider, LongSubmergedRuntimeSettings.BatteryMaxFactor);
            LongSubmergedRuntimeSettings.OxygenFactor = ReadSliderFactor(oxygenFactorSlider, LongSubmergedRuntimeSettings.OxygenMaxFactor);
            LongSubmergedRuntimeSettings.SpeedFactor = ReadSliderFactor(speedFactorSlider, LongSubmergedRuntimeSettings.SpeedMaxFactor);
            LongSubmergedRuntimeSettings.TorpedoFactor = ReadSliderFactor(torpedoFactorSlider, LongSubmergedRuntimeSettings.TorpedoMaxFactor);
            LongSubmergedRuntimeSettings.SonarFactor = ReadSliderFactor(sonarFactorSlider, LongSubmergedRuntimeSettings.SonarMaxFactor);
        }

        private void OnDefaultsClicked()
        {
            LongSubmergedRuntimeSettings.ResetToDefaults();
            LongSubmergedRuntimeSettings.Save();
            RefreshControlState();
            nextBatteryMaintenanceTime = 0f;
            nextMegaSonarMaintenanceTime = 0f;
            LongSubmergedRuntimeApplier.ApplyAll("unity ui defaults");
        }

        private void OnRefreshClicked()
        {
            SaveAndApplyCurrentControlsNow("unity ui refresh");
        }

        private void OnCallReinforcementsClicked()
        {
            SaveAndApplyCurrentControlsNow("unity ui call reinforcements");
            SetReinforcementsStatusOverride("Appel...", 1f);
            string status = ReinforcementRuntimeController.CallReinforcements("unity ui call reinforcements");
            SetReinforcementsStatusOverride(status, 4f);
            RefreshReinforcementsStatus();
        }

        private void RefreshReinforcementsStatus()
        {
            string availabilityStatus = ReinforcementRuntimeController.GetStatusText();
            string displayStatus = availabilityStatus;

            if (!string.IsNullOrEmpty(reinforcementStatusOverride) && Time.unscaledTime < reinforcementStatusOverrideUntil)
                displayStatus = reinforcementStatusOverride;
            else
                reinforcementStatusOverride = null;

            SetReinforcementsStatus(displayStatus);

            if (callReinforcementsButton != null)
                callReinforcementsButton.interactable = availabilityStatus == "Pret";
        }

        private void SetReinforcementsStatusOverride(string status, float seconds)
        {
            reinforcementStatusOverride = string.IsNullOrEmpty(status) ? null : status;
            reinforcementStatusOverrideUntil = Time.unscaledTime + Mathf.Max(0f, seconds);
            SetReinforcementsStatus(status);
        }

        private void SetReinforcementsStatus(string status)
        {
            if (reinforcementsStatusText != null)
                reinforcementsStatusText.text = string.IsNullOrEmpty(status) ? "Pret" : status;
        }

        private void RefreshControlState()
        {
            suppressToggleEvents = true;

            try
            {
                if (megaBatteryToggle != null)
                    megaBatteryToggle.isOn = LongSubmergedRuntimeSettings.MegaBattery;

                if (megaOxygenToggle != null)
                    megaOxygenToggle.isOn = LongSubmergedRuntimeSettings.MegaOxygen;

                if (superSpeedToggle != null)
                    superSpeedToggle.isOn = LongSubmergedRuntimeSettings.SuperSpeed;

                if (megaTorpedoesToggle != null)
                    megaTorpedoesToggle.isOn = LongSubmergedRuntimeSettings.MegaTorpedoes;

                if (megaSonarToggle != null)
                    megaSonarToggle.isOn = LongSubmergedRuntimeSettings.MegaSonar;

                if (heavyArmorToggle != null)
                    heavyArmorToggle.isOn = LongSubmergedRuntimeSettings.HeavyArmor;

                if (superStealthToggle != null)
                    superStealthToggle.isOn = LongSubmergedRuntimeSettings.SuperStealth;

                if (deepDiveToggle != null)
                    deepDiveToggle.isOn = LongSubmergedRuntimeSettings.DeepDive;

                if (interiorLightingToggle != null)
                    interiorLightingToggle.isOn = LongSubmergedRuntimeSettings.InteriorLightingColors;

                SetSliderValue(batteryFactorSlider, LongSubmergedRuntimeSettings.BatteryFactor, LongSubmergedRuntimeSettings.BatteryMaxFactor);
                SetSliderValue(oxygenFactorSlider, LongSubmergedRuntimeSettings.OxygenFactor, LongSubmergedRuntimeSettings.OxygenMaxFactor);
                SetSliderValue(speedFactorSlider, LongSubmergedRuntimeSettings.SpeedFactor, LongSubmergedRuntimeSettings.SpeedMaxFactor);
                SetSliderValue(torpedoFactorSlider, LongSubmergedRuntimeSettings.TorpedoFactor, LongSubmergedRuntimeSettings.TorpedoMaxFactor);
                SetSliderValue(sonarFactorSlider, LongSubmergedRuntimeSettings.SonarFactor, LongSubmergedRuntimeSettings.SonarMaxFactor);
                RefreshFactorLabels();
            }
            finally
            {
                suppressToggleEvents = false;
            }
        }

        private void RefreshFactorLabels()
        {
            SetFactorLabel(
                batteryFactorValueText,
                batteryFactorSlider,
                LongSubmergedRuntimeSettings.BatteryMaxFactor,
                "x",
                LongSubmergedRuntimeSettings.MegaBattery ? "inf" : null
            );

            SetFactorLabel(
                oxygenFactorValueText,
                oxygenFactorSlider,
                LongSubmergedRuntimeSettings.OxygenMaxFactor,
                "x",
                oxygenFactorSlider != null && oxygenFactorSlider.value >= LongSubmergedRuntimeSettings.OxygenMaxFactor ? "90j" : null
            );

            SetFactorLabel(speedFactorValueText, speedFactorSlider, LongSubmergedRuntimeSettings.SpeedMaxFactor, "x", null);
            SetFactorLabel(torpedoFactorValueText, torpedoFactorSlider, LongSubmergedRuntimeSettings.TorpedoMaxFactor, "x", null);
            SetFactorLabel(sonarFactorValueText, sonarFactorSlider, LongSubmergedRuntimeSettings.SonarMaxFactor, "x", null);
        }

        private static void SetSliderValue(Slider slider, float value, float maxValue)
        {
            if (slider == null)
                return;

            slider.minValue = LongSubmergedRuntimeSettings.MinRuntimeFactor;
            slider.maxValue = maxValue;
            slider.wholeNumbers = true;
            slider.value = LongSubmergedRuntimeSettings.ClampFactor(value, maxValue);
        }

        private static float ReadSliderFactor(Slider slider, float maxValue)
        {
            return slider == null
                ? LongSubmergedRuntimeSettings.MinRuntimeFactor
                : LongSubmergedRuntimeSettings.ClampFactor(slider.value, maxValue);
        }

        private static void SetFactorLabel(Text text, Slider slider, float maxValue, string prefix, string suffixOverride)
        {
            if (text == null || slider == null)
                return;

            float value = LongSubmergedRuntimeSettings.ClampFactor(slider.value, maxValue);
            text.text = suffixOverride == null ? prefix + value.ToString("0") : suffixOverride;
        }

        private void CaptureCursor()
        {
            if (cursorCaptured)
                return;

            previousCursorVisible = Cursor.visible;
            previousCursorLockState = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            cursorCaptured = true;
        }

        private void RestoreCursorIfNeeded()
        {
            if (!cursorCaptured)
                return;

            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockState;
            cursorCaptured = false;
        }

        private Slider CreateFactorSlider(Transform parent, string label, float maxValue, Vector2 anchoredPosition, out Text valueText)
        {
            GameObject root = CreateUiObject(label + " Factor", parent);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = new Vector2(420f, 30f);

            Text labelText = CreateText(root.transform, "Label", label, 13, FontStyle.Bold, new Vector2(0f, -1f), new Vector2(116f, 24f));
            labelText.alignment = TextAnchor.MiddleLeft;

            valueText = CreateText(root.transform, "Value", "x1", 13, FontStyle.Bold, new Vector2(362f, -1f), new Vector2(58f, 24f));
            valueText.alignment = TextAnchor.MiddleRight;

            GameObject sliderObject = CreateUiObject("Slider", root.transform);
            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(0f, 0.5f);
            sliderRect.pivot = new Vector2(0f, 0.5f);
            sliderRect.anchoredPosition = new Vector2(124f, -3f);
            sliderRect.sizeDelta = new Vector2(230f, 18f);

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = LongSubmergedRuntimeSettings.MinRuntimeFactor;
            slider.maxValue = maxValue;
            slider.wholeNumbers = true;

            GameObject background = CreateUiObject("Background", sliderObject.transform);
            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = new Color(0.12f, 0.13f, 0.15f, 1f);
            RectTransform backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(0f, 6f);

            GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(5f, 0f);
            fillAreaRect.offsetMax = new Vector2(-5f, 0f);

            GameObject fill = CreateUiObject("Fill", fillArea.transform);
            Image fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.18f, 0.85f, 0.52f, 1f);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(1f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(0f, 6f);

            GameObject handleArea = CreateUiObject("Handle Slide Area", sliderObject.transform);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(5f, 0f);
            handleAreaRect.offsetMax = new Vector2(-5f, 0f);

            GameObject handle = CreateUiObject("Handle", handleArea.transform);
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.92f, 0.95f, 0.98f, 1f);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16f, 16f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.onValueChanged.AddListener(OnFactorSliderChanged);

            return slider;
        }

        private Toggle CreateToggle(Transform parent, string label, Vector2 anchoredPosition)
        {
            GameObject root = CreateUiObject(label + " Toggle", parent);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = new Vector2(330f, 30f);

            Toggle toggle = root.AddComponent<Toggle>();
            Image rowHitImage = root.AddComponent<Image>();
            rowHitImage.color = new Color(1f, 1f, 1f, 0f);
            rowHitImage.raycastTarget = true;

            GameObject box = CreateUiObject("Box", root.transform);
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = new Color(0.16f, 0.18f, 0.2f, 1f);
            RectTransform boxRect = box.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0f, 0.5f);
            boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.pivot = new Vector2(0f, 0.5f);
            boxRect.anchoredPosition = new Vector2(0f, 0f);
            boxRect.sizeDelta = new Vector2(24f, 24f);

            GameObject checkmark = CreateUiObject("Checkmark", box.transform);
            Image checkmarkImage = checkmark.AddComponent<Image>();
            checkmarkImage.color = new Color(0.18f, 0.85f, 0.52f, 1f);
            RectTransform checkRect = checkmark.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.anchoredPosition = Vector2.zero;
            checkRect.sizeDelta = new Vector2(14f, 14f);

            Text labelText = CreateText(root.transform, "Label", label, 16, FontStyle.Normal, new Vector2(34f, -2f), new Vector2(280f, 28f));
            labelText.alignment = TextAnchor.MiddleLeft;

            toggle.targetGraphic = boxImage;
            toggle.graphic = checkmarkImage;
            toggle.onValueChanged.AddListener(OnToggleChanged);

            return toggle;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject buttonObject = CreateUiObject(label + " Button", parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.13f, 0.26f, 0.42f, 1f);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText(buttonObject.transform, "Label", label, 15, FontStyle.Bold, Vector2.zero, size);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;

            return button;
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize, FontStyle fontStyle, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject textObject = CreateUiObject(name, parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = UiFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static Font UiFont
        {
            get
            {
                if (cachedFont == null)
                    cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

                return cachedFont;
            }
        }
    }

    // DonJ : coeur gameplay du mod. Cette classe applique les valeurs runtime sans reecrire les fichiers XLSX :
    // elle pose des modifiers sur les Parametres du jeu, garde la batterie pleine et ajuste torpilles/vitesse/oxygene.
    internal static class LongSubmergedRuntimeApplier
    {
        // DonJ : constantes du profil livre. Le joueur peut ensuite ajuster en F10 sans regenerer le mod.
        private const float OxygenRuntimeMaxFactor = 250f;
        private const float BatteryCapacityDataFactor = 10f;
        private const float EnergyUsageDataFactor = 0.1f;
        private const float BatteryCapacityVanillaRestoreScale = 1f / 10f;
        private const float EnergyUsageVanillaRestoreScale = 1f / 0.1f;
        private const float TorpedoDamageScale = 10f;
        private const float TorpedoCrewDamageScale = 10f;
        private const float TorpedoExplosionRadiusScale = 3f;
        private const float TorpedoExplosionIntensityScale = 3f;
        private const bool PerfectTorpedoReliability = true;
        private const float TorpedoGuidanceLeadSeconds = 4f;
        private const float TorpedoGuidanceMinimumDetonationDistance = 20f;
        private const float TorpedoGuidanceMaximumDetonationDistance = 80f;
        private const float TorpedoGuidanceDetonationRadiusRatio = 0.75f;
        private const string RuntimeScaleModifierName = "LongSubmerged10x Runtime Toggle";
        private const string RuntimeBatteryGainModifierName = "LongSubmerged10x Battery Gain Runtime";
        private const string RuntimeNuclearBatteryCapacityModifierName = "LongSubmerged10x Nuclear Battery Capacity Runtime";
        private const float NuclearBatteryCapacityFloor = 100000f;

        private static readonly FieldInfo OxygenBreathModifierField =
            AccessTools.Field(typeof(PlayerShip), "oxygenBreathModifier");

        private static readonly FieldInfo ResourcePlayerShipField =
            AccessTools.Field(typeof(Resource), "playerShip");

        private static readonly FieldInfo AirCompressorEnergyModifierField =
            AccessTools.Field(typeof(AirCompressor), "energyModifier");

        private static readonly FieldInfo GyrocompassEnergyGainModifierField =
            AccessTools.Field(typeof(Gyrocompass), "energyGainModifier");

        private static readonly FieldInfo TrimPumpEnergyGainModifierField =
            AccessTools.Field(typeof(TrimPump), "energyGainModifier");

        private static readonly FieldInfo VentilationEnergyModifierField =
            AccessTools.Field(typeof(Ventilation), "energyModifier");

        private static readonly FieldInfo ResourceGuiResourceField =
            AccessTools.Field(typeof(ResourceGUI), "resource");

        private static readonly FieldInfo DepletingResourceNotificationResourceField =
            AccessTools.Field(typeof(DepletingResourceNotification), "resource");

        private static readonly FieldInfo TorpedoHomingTargetField =
            AccessTools.Field(typeof(Torpedo), "homingTarget");

        private static readonly FieldInfo TorpedoRotatedField =
            AccessTools.Field(typeof(Torpedo), "rotated");

        private static readonly FieldInfo TorpedoSumOfAnglesField =
            AccessTools.Field(typeof(Torpedo), "sumOfAngles");

        private static readonly FieldInfo TorpedoHitEntityField =
            AccessTools.Field(typeof(Torpedo), "hitEntity");

        private static readonly FieldInfo TorpedoPassedDistanceField =
            AccessTools.Field(typeof(Torpedo), "passedDistance");

        private static readonly FieldInfo TorpedoArmDistanceField =
            AccessTools.Field(typeof(Torpedo), "armDistance");

        private static readonly MethodInfo TorpedoDoExplosionHitMethod =
            AccessTools.Method(typeof(Torpedo), "DoExplosionHit");

        private static readonly MethodInfo TorpedoDetonateMethod =
            AccessTools.Method(typeof(Torpedo), "Detonate", new Type[] { typeof(bool) });

        private static readonly ConditionalWeakTable<Parameter, ParameterScalePatchData> ParameterScaleData =
            new ConditionalWeakTable<Parameter, ParameterScalePatchData>();

        // DonJ : ConditionalWeakTable evite de garder en memoire des objets Unity detruits.
        // Chaque Parameter recoit un seul modifier DonJ, ensuite je change juste sa valeur.
        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> BatteryGainDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> NuclearBatteryCapacityDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<Torpedo, TorpedoGuidancePatchData> TorpedoGuidanceData =
            new ConditionalWeakTable<Torpedo, TorpedoGuidancePatchData>();

        private static readonly ConditionalWeakTable<Modifier, OxygenModifierPatchData> OxygenModifierData =
            new ConditionalWeakTable<Modifier, OxygenModifierPatchData>();

        private static readonly HashSet<int> InfiniteBatteryLoggedShipIds = new HashSet<int>();

        private static readonly HashSet<int> BatteryGainRuntimeLoggedResourceIds = new HashSet<int>();

        private static readonly HashSet<int> NuclearBatteryCapacityLoggedResourceIds = new HashSet<int>();

        private static readonly HashSet<int> BatteryTooltipRuntimeLoggedResourceIds = new HashSet<int>();

        // SurfaceSafe 1.4.7 :
        // Les callbacks de certains équipements peuvent être relancés pendant que l'on ajoute/modifie
        // leurs modifiers. Sans garde, un EnergyUsage_Changed déclenché par notre propre SetScale peut
        // entrer en récursion pendant la transition immersion -> surface.
        private static readonly HashSet<int> BatteryObjectApplicationGuardIds = new HashSet<int>();

        private static readonly Dictionary<Type, FieldInfo[]> GenericEnergyUsageFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> GenericParameterCollectionFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> GenericEnergyModifierFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        public static void ApplyAll(string reason)
        {
            try
            {
                // DonJ : passe globale volontairement defensive. Elle resynchronise le menu,
                // le PlayerShip, les consommateurs batterie et toutes les torpilles visibles.
                LongSubmergedMenuController.Ensure();
                InteriorLightingColorPatcher.ApplyAll(reason + ".InteriorLighting");
                ApplyPlayerShip(UnityEngine.Object.FindObjectOfType<PlayerShip>(), reason);
                DeepDiveRuntimePatcher.ApplyAll(reason + ".DeepDive");
                ApplyBatteryConsumers(reason);
                MegaSonarRuntimePatcher.ApplyAll(reason + ".MegaSonar");

                foreach (StoredTorpedo item in UnityEngine.Object.FindObjectsOfType<StoredTorpedo>())
                    ApplyStoredTorpedo(item, reason + ".StoredTorpedo");

                foreach (Torpedo item in UnityEngine.Object.FindObjectsOfType<Torpedo>())
                    ApplyLaunchedTorpedo(item, reason + ".Torpedo");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void MaintainBatteryRuntime(string reason)
        {
            try
            {
                // DonJ : tick leger appele toutes les 0.20s. Il ne rescane pas toute la scene,
                // il remet seulement la ressource batterie du sous-marin dans l'etat attendu.
                ApplyBatteryResource(UnityEngine.Object.FindObjectOfType<PlayerShip>(), reason);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyBatteryConsumers(string reason)
        {
            try
            {
                // DonJ : UBOAT disperse les consommations electriques entre plusieurs composants.
                // Je traite les types connus puis je lance un scan generique pour les champs renommes ou caches.
                foreach (AccumulatorsUpgrade item in UnityEngine.Object.FindObjectsOfType<AccumulatorsUpgrade>())
                    ApplyBatteryObject(item, reason + ".AccumulatorsUpgrade");

                foreach (PlayerShipEngine item in UnityEngine.Object.FindObjectsOfType<PlayerShipEngine>())
                    ApplyBatteryObject(item, reason + ".PlayerShipEngine");

                foreach (DivingPlanesStation item in UnityEngine.Object.FindObjectsOfType<DivingPlanesStation>())
                    ApplyBatteryObject(item, reason + ".DivingPlanesStation");

                // SurfaceSafe 1.4.7 :
                // On ne touche plus AirCompressor ni Ventilation. Ces composants appartiennent au circuit
                // air/recharge de surface ; les modifier au moment où le bateau reprend l'air peut provoquer
                // une boucle EnergyUsage_Changed / modifier UI. La batterie infinie reste assurée par
                // ApplyBatteryResource(PlayerShip.Energy), donc il n'y a pas besoin de neutraliser leur coût.
                foreach (Gyrocompass item in UnityEngine.Object.FindObjectsOfType<Gyrocompass>())
                    ApplyBatteryObject(item, reason + ".Gyrocompass");

                foreach (TrimPump item in UnityEngine.Object.FindObjectsOfType<TrimPump>())
                    ApplyBatteryObject(item, reason + ".TrimPump");

                foreach (Equipment item in UnityEngine.Object.FindObjectsOfType<Equipment>())
                    ApplyBatteryEquipment(item, reason + ".Equipment");

                ApplyGenericBatteryConsumers(reason + ".Generic");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyPlayerShip(PlayerShip ship, string reason)
        {
            LongSubmergedMenuController.Ensure();

            if (ship == null)
                return;

            OxygenBreathRecalculator.Recalculate(ship, reason);
            ApplyBatteryResource(ship, reason);
            EngineFastSpeedPatcher.PatchPlayerShip(ship, reason);
            DeepDiveRuntimePatcher.ApplyPlayerShip(ship, reason);
            SuperStealthRuntimePatcher.ApplyPlayerShip(ship, reason);
        }

        public static void ApplyBatteryResource(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            ApplyBatteryRuntimeToResource(ship.Energy, reason);
        }

        public static void MaintainInfiniteBatteryCharge(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            ApplyBatteryRuntimeToResource(ship.Energy, reason);
        }

        private static void ApplyBatteryRuntimeToResource(Resource energy, string reason)
        {
            if (energy == null)
                return;

            // DonJ : pipeline batterie unique. Capacite nucleaire, gain/drain et remplissage passent ici,
            // ce qui evite d'avoir plusieurs comportements batterie qui divergent.
            ApplyNuclearBatteryCapacityOverride(energy, reason);
            ApplyBatteryGainModifiers(energy, reason);

            if (IsInfiniteBatteryRuntimeActive())
                FillBatteryToCapacity(energy, reason);
            else
                ClampBatteryAmountToCapacity(energy);
        }

        private static void ApplyNuclearBatteryCapacityOverride(Resource energy, string reason)
        {
            if (energy == null || energy.Capacity == null)
                return;

            // DonJ: Mega Batterie does not merely reduce consumption; it adds a huge
            // capacity so the UI and gameplay see a nuclear battery immediately.
            float baseCapacity = energy.Capacity.GetValueExcludingModifier(RuntimeNuclearBatteryCapacityModifierName);
            float targetCapacity = baseCapacity;

            if (IsInfiniteBatteryRuntimeActive())
                targetCapacity = Math.Max(baseCapacity, NuclearBatteryCapacityFloor);

            float delta = targetCapacity - baseCapacity;
            SetDelta(
                energy.Capacity,
                NuclearBatteryCapacityDeltaData,
                RuntimeNuclearBatteryCapacityModifierName,
                delta
            );

            if (IsInfiniteBatteryRuntimeActive())
            {
                int resourceId = RuntimeHelpers.GetHashCode(energy);
                if (NuclearBatteryCapacityLoggedResourceIds.Add(resourceId))
                    Debug.Log("[LongSubmerged10x] Mega Batterie nuclear capacity active after " + reason + ".");
            }
        }

        private static void ClampBatteryAmountToCapacity(Resource energy)
        {
            if (energy == null)
                return;

            double capacity = GetResourceCapacity(energy);
            if (!IsUsableResourceValue(capacity) || capacity <= 0.0)
                return;

            if (energy.Amount > capacity)
                energy.Amount = capacity;
            else if (energy.Amount < 0.0)
                energy.Amount = 0.0;
        }

        public static bool TryMaintainBatteryResource(Resource resource, string reason)
        {
            try
            {
                if (!IsPlayerShipEnergyResource(resource))
                    return false;

                ApplyBatteryRuntimeToResource(resource, reason);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        public static bool TryFreezeInfiniteBatteryResource(Resource resource, string reason)
        {
            try
            {
                if (!IsInfiniteBatteryRuntimeActive())
                    return false;

                if (!IsPlayerShipEnergyResource(resource))
                    return false;

                double capacity = GetResourceCapacity(resource);
                if (!IsUsableResourceValue(capacity) || capacity <= 0.0)
                    return false;

                ApplyBatteryRuntimeToResource(resource, reason);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        public static Resource GetResourceFromGui(ResourceGUI gui)
        {
            try
            {
                return gui != null && ResourceGuiResourceField != null
                    ? ResourceGuiResourceField.GetValue(gui) as Resource
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static Resource GetResourceFromDepletingNotification(DepletingResourceNotification notification)
        {
            try
            {
                return notification != null && DepletingResourceNotificationResourceField != null
                    ? DepletingResourceNotificationResourceField.GetValue(notification) as Resource
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool ShouldSuppressBatteryDepletionUi(Resource resource, string reason)
        {
            if (!IsInfiniteBatteryRuntimeActive())
                return false;

            if (!TryMaintainBatteryResource(resource, reason))
                return false;

            int resourceId = RuntimeHelpers.GetHashCode(resource);
            if (BatteryTooltipRuntimeLoggedResourceIds.Add(resourceId))
                Debug.Log("[LongSubmerged10x] Mega Batterie depletion UI guard active after " + reason + ".");

            return true;
        }

        public static string BuildInfiniteBatteryTooltip(Resource resource)
        {
            // DonJ : ne pas appeler resource.PrintInfo ici.
            // PrintInfo repasse par les calculs vanilla de duree/recharge et peut toucher des etats UI
            // sensibles pendant la transition immersion -> surface. Le tooltip batterie infinie reste donc statique.
            if (resource == null)
                return "Mega Batterie : batterie infinie active.";

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Capacite de la batterie 100 %");
            builder.AppendLine("Mega Batterie : batterie nucleaire active.");
            builder.AppendLine("La batterie est maintenue au maximum par Long Submerged 10x+.");
            builder.AppendLine("Decoche Mega Batterie dans F10 pour revenir a la batterie vanilla.");
            return builder.ToString();
        }

        private static void FillBatteryToCapacity(Resource energy, string reason)
        {
            if (energy == null)
                return;

            double capacity = GetResourceCapacity(energy);
            if (!IsUsableResourceValue(capacity) || capacity <= 0.0)
                return;

            if (Math.Abs(energy.Amount - capacity) > 0.0001)
            {
                // DonJ : je garde la batterie au maximum avec le setter Amount pour forcer aussi le refresh UI.
                energy.Amount = capacity;

                int resourceId = RuntimeHelpers.GetHashCode(energy);
                if (InfiniteBatteryLoggedShipIds.Add(resourceId))
                    Debug.Log("[LongSubmerged10x] Mega Batterie infinite hold active after " + reason + ".");
            }
        }

        private static void ApplyBatteryGainModifiers(Resource energy, string reason)
        {
            if (energy == null)
                return;

            float factor = GetEffectiveBatteryGainFactor();
            ApplyBatteryGainParameter(energy.Gain, factor);
            ApplyBatteryGainParameter(energy.GainSandboxTimeScale, factor);

            if (factor >= LongSubmergedRuntimeSettings.BatteryMaxFactor - 0.0001f)
            {
                int resourceId = RuntimeHelpers.GetHashCode(energy);
                if (BatteryGainRuntimeLoggedResourceIds.Add(resourceId))
                    Debug.Log("[LongSubmerged10x] Mega Batterie infinite gain guard active after " + reason + ".");
            }
        }

        private static float GetEffectiveBatteryGainFactor()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return LongSubmergedRuntimeSettings.MinRuntimeFactor;

            return LongSubmergedRuntimeSettings.BatteryMaxFactor;
        }

        private static void ApplyBatteryGainParameter(Parameter parameter, float factor)
        {
            if (parameter == null)
                return;

            float baseValue = parameter.GetValueExcludingModifier(RuntimeBatteryGainModifierName);
            float desiredValue = baseValue;

            // DonJ: Mega Batterie is the single switch for infinity, so every negative
            // battery gain is neutralized without depending on the legacy slider value.
            if (factor >= LongSubmergedRuntimeSettings.BatteryMaxFactor - 0.0001f && baseValue < 0f)
                desiredValue = 0f;

            SetDelta(
                parameter,
                BatteryGainDeltaData,
                RuntimeBatteryGainModifierName,
                desiredValue - baseValue
            );
        }

        private static void SetDelta(
            Parameter parameter,
            ConditionalWeakTable<Parameter, ParameterDeltaPatchData> table,
            string modifierName,
            float delta
        )
        {
            if (parameter == null || table == null)
                return;

            ParameterDeltaPatchData data;
            if (!table.TryGetValue(parameter, out data))
            {
                data = new ParameterDeltaPatchData(parameter.AddDeltaModifier(modifierName, false));
                table.Add(parameter, data);
            }

            if (data.DeltaModifier == null)
                return;

            if (Math.Abs(data.DeltaModifier.Value - delta) > 0.000001f)
                data.DeltaModifier.Value = delta;
        }

        private static bool IsPlayerShipEnergyResource(Resource resource)
        {
            if (resource == null)
                return false;

            PlayerShip owner = null;
            if (ResourcePlayerShipField != null)
                owner = ResourcePlayerShipField.GetValue(resource) as PlayerShip;

            if (owner == null)
                owner = UnityEngine.Object.FindObjectOfType<PlayerShip>();

            // DonJ : securite anti-faux-positif. Si je trouve le PlayerShip, je n'accepte que sa vraie ressource Energy.
            // Le fallback par nom sert seulement quand UBOAT ne donne pas encore le lien owner.
            if (owner != null)
                return object.ReferenceEquals(owner.Energy, resource);

            return IsEnergyResourceName(resource.Name);
        }

        private static bool IsEnergyResourceName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.Equals("Energy", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("Battery", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Batterie", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static double GetResourceCapacity(Resource resource)
        {
            if (resource == null || resource.Capacity == null)
                return double.NaN;

            return resource.Capacity.Value;
        }

        private static bool IsUsableResourceValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static void ApplyOxygenBreathModifier(PlayerShip ship, string reason)
        {
            if (ship == null || OxygenBreathModifierField == null)
                return;

            try
            {
                Modifier oxygenModifier = OxygenBreathModifierField.GetValue(ship) as Modifier;
                if (oxygenModifier == null)
                    return;

                float currentValue = oxygenModifier.Value;
                if (!IsFinite(currentValue))
                    return;

                OxygenModifierPatchData data;
                if (!OxygenModifierData.TryGetValue(oxygenModifier, out data))
                {
                    data = new OxygenModifierPatchData(currentValue);
                    OxygenModifierData.Add(oxygenModifier, data);
                }

                float factor = GetEffectiveOxygenRuntimeFactor();

                // Surface and recharge states use zero or positive values; keep them vanilla.
                if (!LongSubmergedRuntimeSettings.MegaOxygen || factor <= 1.0001f || currentValue >= 0f)
                {
                    if (data.LastAppliedFactor > 1.0001f
                        && Math.Abs(currentValue - data.LastPatchedValue) <= 0.000001f)
                    {
                        oxygenModifier.Value = data.OriginalValue;
                    }
                    else
                    {
                        data.OriginalValue = currentValue;
                    }

                    data.LastAppliedFactor = 1f;
                    data.LastPatchedValue = oxygenModifier.Value;
                    return;
                }

                if (data.LastAppliedFactor <= 1.0001f
                    || Math.Abs(currentValue - data.LastPatchedValue) > 0.000001f)
                {
                    data.OriginalValue = currentValue;
                }

                float desiredValue = data.OriginalValue / factor;
                if (Math.Abs(oxygenModifier.Value - desiredValue) > 0.000000000001f)
                    oxygenModifier.Value = desiredValue;

                data.LastAppliedFactor = factor;
                data.LastPatchedValue = desiredValue;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyBatteryObject(object target, string reason)
        {
            if (target == null || IsSurfaceAirRuntimeObject(target))
                return;

            if (!TryEnterBatteryObjectApplication(target))
                return;

            try
            {
                Equipment equipment = target as Equipment;
                if (equipment != null)
                    ApplyBatteryEquipment(equipment, reason);

                ApplyBatteryCapacityParameter(GetParameterField(target, "energyCapacityGain"));
                Parameter energyUsage = GetParameterField(target, "energyUsage");
                ApplyEnergyUsageParameter(energyUsage);
                ApplyDirectEnergyGainModifier(target, energyUsage, reason);
            }
            finally
            {
                ExitBatteryObjectApplication(target);
            }
        }

        public static void ApplyBatteryEquipment(Equipment equipment, string reason)
        {
            if (equipment == null || equipment.Parameters == null || IsSurfaceAirRuntimeObject(equipment))
                return;

            ApplyBatteryCapacityParameter(GetParameter(equipment.Parameters, "EnergyCapacityGain"));
            ApplyEnergyUsageParameter(GetParameter(equipment.Parameters, "EnergyUsage"));
        }

        public static void ApplyStoredTorpedo(StoredTorpedo storedTorpedo, string reason)
        {
            if (storedTorpedo == null)
                return;

            float reliabilityScale = IsMegaTorpedoRuntimeActive() && PerfectTorpedoReliability ? 0f : 1f;
            SetScale(storedTorpedo.DudChance, reliabilityScale);
        }

        public static void ApplyLaunchedTorpedo(Torpedo torpedo, string reason)
        {
            if (torpedo == null)
                return;

            if (torpedo.Parameters != null)
            {
                // DonJ : les torpilles sont reglees au runtime. A 1 elles redeviennent vanilla ;
                // a 10 elles utilisent le profil mega par defaut ; a 100 elles deviennent extremes.
                float torpedoFactor = GetEffectiveTorpedoFactor();
                float damageScale = torpedoFactor;
                float crewDamageScale = torpedoFactor;
                // DonJ : les degats peuvent rester x10, mais les effets visuels/particules sont bornes.
                // Des rayons/intensites x10 creent trop de surfaces feu/fumee et peuvent declencher
                // un crash natif Unity/particules pendant les phases surface + alarme.
                float radiusScale = Mathf.Min(torpedoFactor, 3f);
                float intensityScale = Mathf.Min(torpedoFactor, 3f);
                float reliabilityScale = IsMegaTorpedoRuntimeActive() && PerfectTorpedoReliability ? 0f : 1f;

                SetScale(GetParameter(torpedo.Parameters, "Damage"), damageScale);
                SetScale(GetParameter(torpedo.Parameters, "CrewDamage"), crewDamageScale);
                SetScale(GetParameter(torpedo.Parameters, "DamageRadius"), radiusScale);
                SetScale(GetParameter(torpedo.Parameters, "DamageEffectsRadius"), radiusScale);
                SetScale(GetParameter(torpedo.Parameters, "DamageEffectsIntensity"), intensityScale);
                SetScale(GetParameter(torpedo.Parameters, "MagneticExplosionOnArm"), reliabilityScale);
                SetScale(GetParameter(torpedo.Parameters, "MagneticExplosionAfterArm"), reliabilityScale);
                SetScale(GetParameter(torpedo.Parameters, "MagneticExplosionFail"), reliabilityScale);
            }

            // DonJ : stabilite surface/alarme.
            // Je garde les degats/fiabilite des Mega Torpilles, mais je desactive le guidage runtime
            // et la detonation forcee. Ces deux actions s'executaient en FixedUpdate avec des valeurs NaN
            // et pouvaient laisser une torpille/detonation dans un etat fragile pendant l'alarme.
            RestoreLockedTargetGuidance(torpedo);
        }

        private static void ApplyLockedTargetGuidance(Torpedo torpedo, string reason)
        {
            Entity target = torpedo.TargetEntity;
            if (target == null)
            {
                RestoreLockedTargetGuidance(torpedo);
                return;
            }

            TorpedoGuidancePatchData data = GetTorpedoGuidanceData(torpedo);
            if (!data.HasOriginalValues)
            {
                data.OriginalGyroAngle = torpedo.GyroAngle;
                data.OriginalTargetPosition = torpedo.TargetPosition;
                data.OriginalTargetPositionForReports = torpedo.TargetPositionForReports;
                data.HasOriginalValues = true;
            }

            Vector3 targetPoint = PredictLockedTargetPoint(torpedo, target);
            if (!IsFinite(targetPoint))
                return;

            // DonJ : je transforme le tir verrouille en visee cartesienne dynamique.
            // L'objectif est qu'une torpille tiree sur une cible correctement verrouillee corrige son angle pendant le vol.
            torpedo.GyroAngle = float.NaN;
            torpedo.TargetPosition = targetPoint;
            torpedo.TargetPositionForReports = targetPoint;
            data.GuidanceApplied = true;

            ResetCartesianTurnLimiter(torpedo);
            ApplyHomingPropeller(torpedo, target);
            TryForceLockedTargetDetonation(torpedo, target);

            if (!data.GuidanceLogged)
            {
                Debug.Log("[LongSubmerged10x] Mega torpedo locked-target guidance active after " + reason + ".");
                data.GuidanceLogged = true;
            }
        }

        private static void RestoreLockedTargetGuidance(Torpedo torpedo)
        {
            TorpedoGuidancePatchData data;
            if (!TorpedoGuidanceData.TryGetValue(torpedo, out data) || !data.HasOriginalValues || !data.GuidanceApplied)
                return;

            try
            {
                torpedo.GyroAngle = data.OriginalGyroAngle;
                torpedo.TargetPosition = data.OriginalTargetPosition;
                torpedo.TargetPositionForReports = data.OriginalTargetPositionForReports;

                if (TorpedoHomingTargetField != null)
                    TorpedoHomingTargetField.SetValue(torpedo, null);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            data.GuidanceApplied = false;
        }

        private static Vector3 PredictLockedTargetPoint(Torpedo torpedo, Entity target)
        {
            Vector3 targetPoint = target.transform.position;
            Ship targetShip = target as Ship;
            if (targetShip != null && targetShip.RigidBody != null)
                targetPoint += targetShip.RigidBody.velocity * TorpedoGuidanceLeadSeconds;

            Vector3 torpedoPosition = torpedo.transform.position;
            targetPoint.y = torpedoPosition.y;
            return targetPoint;
        }

        private static void ResetCartesianTurnLimiter(Torpedo torpedo)
        {
            try
            {
                if (TorpedoRotatedField != null)
                    TorpedoRotatedField.SetValue(torpedo, false);

                if (TorpedoSumOfAnglesField != null)
                    TorpedoSumOfAnglesField.SetValue(torpedo, 0f);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void ApplyHomingPropeller(Torpedo torpedo, Entity target)
        {
            if (TorpedoHomingTargetField == null)
                return;

            Ship targetShip = target as Ship;
            if (targetShip == null)
                return;

            Propeller[] propellers = targetShip.Propellers;
            if (propellers == null || propellers.Length == 0)
                return;

            for (int i = 0; i < propellers.Length; i++)
            {
                if (propellers[i] == null)
                    continue;

                TorpedoHomingTargetField.SetValue(torpedo, propellers[i]);
                return;
            }
        }

        private static void TryForceLockedTargetDetonation(Torpedo torpedo, Entity target)
        {
            if (target == null || torpedo.Detonated || TorpedoDoExplosionHitMethod == null || TorpedoDetonateMethod == null)
                return;

            TorpedoGuidancePatchData data = GetTorpedoGuidanceData(torpedo);
            if (data.ForcingDetonation)
                return;

            if (!IsTorpedoArmedForAssist(torpedo))
                return;

            Vector3 torpedoPosition = torpedo.transform.position;
            Vector3 targetPosition = target.transform.position;
            Vector2 delta = new Vector2(torpedoPosition.x - targetPosition.x, torpedoPosition.z - targetPosition.z);
            float detonationDistance = GetAssistDetonationDistance(torpedo);

            if (delta.sqrMagnitude > detonationDistance * detonationDistance)
                return;

            try
            {
                data.ForcingDetonation = true;

                if (TorpedoHitEntityField != null)
                    TorpedoHitEntityField.SetValue(torpedo, target);

                TorpedoDoExplosionHitMethod.Invoke(torpedo, new object[] { target });
                TorpedoDetonateMethod.Invoke(torpedo, new object[] { true });
                Debug.Log("[LongSubmerged10x] Mega torpedo forced locked-target detonation inside " + detonationDistance.ToString("0.0") + "m.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                data.ForcingDetonation = false;
            }
        }

        private static bool IsTorpedoArmedForAssist(Torpedo torpedo)
        {
            if (TorpedoPassedDistanceField == null || TorpedoArmDistanceField == null)
                return true;

            try
            {
                float passedDistance = (float)TorpedoPassedDistanceField.GetValue(torpedo);
                Parameter armDistance = TorpedoArmDistanceField.GetValue(torpedo) as Parameter;
                return armDistance == null || passedDistance >= armDistance.Value;
            }
            catch
            {
                return true;
            }
        }

        private static float GetAssistDetonationDistance(Torpedo torpedo)
        {
            Parameter damageRadius = torpedo.Parameters == null ? null : GetParameter(torpedo.Parameters, "DamageRadius");
            float scaledDamageRadius = damageRadius == null ? 0f : damageRadius.Value * GetEffectiveTorpedoFactor();
            // DonJ : detonateur de secours proche cible. Il reste borne pour ne pas exploser trop loin,
            // mais suit le rayon mega afin de fiabiliser les impacts verrouilles.
            return Mathf.Clamp(
                scaledDamageRadius * TorpedoGuidanceDetonationRadiusRatio,
                TorpedoGuidanceMinimumDetonationDistance,
                TorpedoGuidanceMaximumDetonationDistance
            );
        }

        private static bool IsMegaTorpedoRuntimeActive()
        {
            return LongSubmergedRuntimeSettings.MegaTorpedoes && GetEffectiveTorpedoFactor() > 1.0001f;
        }

        private static float GetEffectiveTorpedoFactor()
        {
            return LongSubmergedRuntimeSettings.MegaTorpedoes
                ? LongSubmergedRuntimeSettings.ClampTorpedoFactor(LongSubmergedRuntimeSettings.TorpedoFactor)
                : 1f;
        }

        private static float GetEffectiveOxygenRuntimeFactor()
        {
            if (!LongSubmergedRuntimeSettings.MegaOxygen)
                return 1f;

            float sliderValue = LongSubmergedRuntimeSettings.ClampOxygenFactor(LongSubmergedRuntimeSettings.OxygenFactor);
            if (sliderValue <= LongSubmergedRuntimeSettings.MinRuntimeFactor)
                return 1f;

            float normalized = (sliderValue - LongSubmergedRuntimeSettings.MinRuntimeFactor)
                / (LongSubmergedRuntimeSettings.OxygenMaxFactor - LongSubmergedRuntimeSettings.MinRuntimeFactor);

            float maxFactor = Mathf.Max(1f, OxygenRuntimeMaxFactor);
            return 1f + normalized * (maxFactor - 1f);
        }

        private static float GetEffectiveBatteryCapacityScale()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return BatteryCapacityVanillaRestoreScale;

            float factor = LongSubmergedRuntimeSettings.ClampBatteryFactor(LongSubmergedRuntimeSettings.BatteryFactor);
            return factor / BatteryCapacityDataFactor;
        }

        private static float GetEffectiveBatteryEnergyUsageScale()
        {
            // DonJ: Mega Batterie is now fully infinite as soon as the toggle is on.
            // With the toggle off, restore the XLSX fallback x0.1 back to vanilla.
            if (IsInfiniteBatteryRuntimeActive())
                return 0f;

            return EnergyUsageVanillaRestoreScale;
        }

        private static bool IsInfiniteBatteryRuntimeActive()
        {
            return LongSubmergedRuntimeSettings.MegaBattery;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static TorpedoGuidancePatchData GetTorpedoGuidanceData(Torpedo torpedo)
        {
            TorpedoGuidancePatchData data;
            if (!TorpedoGuidanceData.TryGetValue(torpedo, out data))
            {
                data = new TorpedoGuidancePatchData();
                TorpedoGuidanceData.Add(torpedo, data);
            }

            return data;
        }

        private static void ApplyBatteryCapacityParameter(Parameter parameter)
        {
            if (parameter == null)
                return;

            SetScale(
                parameter,
                GetEffectiveBatteryCapacityScale()
            );
        }

        private static void ApplyEnergyUsageParameter(Parameter parameter)
        {
            if (parameter == null)
                return;

            // DonJ : ne pas tester parameter.Value ici : en mode infini mon scale vaut 0.
            // Quand le joueur redescend le slider, je dois pouvoir restaurer le drain vanilla.
            float baseValue = parameter.GetValueExcludingModifier(RuntimeScaleModifierName);
            if (baseValue <= 0f)
                return;

            SetScale(
                parameter,
                GetEffectiveBatteryEnergyUsageScale()
            );
        }

        private static void ApplyDirectEnergyGainModifier(object target, Parameter energyUsage, string reason)
        {
            if (target == null || energyUsage == null || IsSurfaceAirRuntimeObject(target))
                return;

            if (target is AirCompressor)
            {
                ApplyDirectEnergyGainModifierField(target, AirCompressorEnergyModifierField, energyUsage);
                return;
            }

            if (target is Gyrocompass)
            {
                ApplyDirectEnergyGainModifierField(target, GyrocompassEnergyGainModifierField, energyUsage);
                return;
            }

            if (target is TrimPump)
            {
                ApplyDirectEnergyGainModifierField(target, TrimPumpEnergyGainModifierField, energyUsage);
                return;
            }

            if (target is Ventilation)
                ApplyDirectEnergyGainModifierField(target, VentilationEnergyModifierField, energyUsage);
        }

        private static void ApplyDirectEnergyGainModifierField(object target, FieldInfo modifierField, Parameter energyUsage)
        {
            if (modifierField == null || energyUsage == null)
                return;

            Modifier modifier = modifierField.GetValue(target) as Modifier;
            if (modifier == null)
                return;

            float usage = energyUsage.Value;
            if (usage < 0f)
                return;

            float desiredGain = -usage;
            if (Math.Abs(modifier.Value - desiredGain) > 0.0001f)
                modifier.Value = desiredGain;
        }

        private static void ApplyGenericBatteryConsumers(string reason)
        {
            // DonJ : filet de securite. Si UBOAT renomme un composant electrique,
            // je cherche quand meme les champs Parameter nommes EnergyUsage dans tous les MonoBehaviour.
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null || behaviour is LongSubmergedMenuController)
                    continue;

                ApplyGenericBatteryConsumer(behaviour, reason);
            }
        }

        private static void ApplyGenericBatteryConsumer(object target, string reason)
        {
            if (target == null || IsSurfaceAirRuntimeObject(target))
                return;

            if (!TryEnterBatteryObjectApplication(target))
                return;

            try
            {
                Type type = target.GetType();

                foreach (FieldInfo field in GetGenericEnergyUsageFields(type))
                {
                    Parameter energyUsage = GetParameterFromField(target, field);
                    if (energyUsage == null)
                        continue;

                    ApplyEnergyUsageParameter(energyUsage);
                    ApplyDirectEnergyGainModifier(target, energyUsage, reason);
                    ApplyGenericEnergyModifierFields(target, energyUsage);
                }

                foreach (FieldInfo field in GetGenericParameterCollectionFields(type))
                {
                    ParameterCollection parameters = GetParameterCollectionFromField(target, field);
                    if (parameters == null)
                        continue;

                    ApplyBatteryCapacityParameter(GetParameter(parameters, "EnergyCapacityGain"));
                    Parameter energyUsage = GetParameter(parameters, "EnergyUsage");
                    ApplyEnergyUsageParameter(energyUsage);
                    ApplyDirectEnergyGainModifier(target, energyUsage, reason);
                    ApplyGenericEnergyModifierFields(target, energyUsage);
                }
            }
            finally
            {
                ExitBatteryObjectApplication(target);
            }
        }

        private static FieldInfo[] GetGenericEnergyUsageFields(Type type)
        {
            FieldInfo[] cached;
            if (GenericEnergyUsageFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(Parameter), true);
            cached = fields.ToArray();
            GenericEnergyUsageFieldCache[type] = cached;
            return cached;
        }

        private static FieldInfo[] GetGenericParameterCollectionFields(Type type)
        {
            FieldInfo[] cached;
            if (GenericParameterCollectionFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(ParameterCollection), false);
            cached = fields.ToArray();
            GenericParameterCollectionFieldCache[type] = cached;
            return cached;
        }

        private static FieldInfo[] GetGenericEnergyModifierFields(Type type)
        {
            FieldInfo[] cached;
            if (GenericEnergyModifierFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(Modifier), false);
            cached = fields.ToArray();
            GenericEnergyModifierFieldCache[type] = cached;
            return cached;
        }

        private static void CollectFields(Type type, List<FieldInfo> fields, Type requiredFieldType, bool energyUsageNameOnly)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] declaredFields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in declaredFields)
                {
                    if (field == null || !requiredFieldType.IsAssignableFrom(field.FieldType))
                        continue;

                    if (energyUsageNameOnly && !IsEnergyUsageMemberName(field.Name))
                        continue;

                    if (!energyUsageNameOnly && requiredFieldType == typeof(Modifier) && !IsEnergyModifierMemberName(field.Name))
                        continue;

                    fields.Add(field);
                }
            }
        }

        private static bool IsEnergyUsageMemberName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("EnergyUsage", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsEnergyModifierMemberName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("Energy", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("Modifier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSurfaceAirRuntimeObject(object target)
        {
            if (target == null)
                return false;

            // AirCompressor et Ventilation sont volontairement laissés vanilla.
            // Ils s'activent autour du retour en surface et peuvent recalculer EnergyUsage en cascade.
            if (target is AirCompressor || target is Ventilation)
                return true;

            Type type = target.GetType();
            if (type != null && IsSurfaceAirName(type.Name))
                return true;

            UnityEngine.Object unityObject = target as UnityEngine.Object;
            if (unityObject != null && IsSurfaceAirName(unityObject.name))
                return true;

            return false;
        }

        private static bool IsSurfaceAirName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.IndexOf("AirCompressor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Ventilation", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Atmosphere", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Oxygen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryEnterBatteryObjectApplication(object target)
        {
            if (target == null)
                return false;

            return BatteryObjectApplicationGuardIds.Add(RuntimeHelpers.GetHashCode(target));
        }

        private static void ExitBatteryObjectApplication(object target)
        {
            if (target == null)
                return;

            BatteryObjectApplicationGuardIds.Remove(RuntimeHelpers.GetHashCode(target));
        }

        private static Parameter GetParameterFromField(object target, FieldInfo field)
        {
            try
            {
                return field == null ? null : field.GetValue(target) as Parameter;
            }
            catch
            {
                return null;
            }
        }

        private static ParameterCollection GetParameterCollectionFromField(object target, FieldInfo field)
        {
            try
            {
                return field == null ? null : field.GetValue(target) as ParameterCollection;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyGenericEnergyModifierFields(object target, Parameter energyUsage)
        {
            if (target == null || energyUsage == null || IsSurfaceAirRuntimeObject(target))
                return;

            float usage = energyUsage.Value;
            if (usage < 0f)
                return;

            float desiredGain = -usage;
            foreach (FieldInfo field in GetGenericEnergyModifierFields(target.GetType()))
            {
                try
                {
                    Modifier modifier = field.GetValue(target) as Modifier;
                    if (modifier != null && Math.Abs(modifier.Value - desiredGain) > 0.0001f)
                        modifier.Value = desiredGain;
                }
                catch
                {
                }
            }
        }

        private static Parameter GetParameter(ParameterCollection parameters, string key)
        {
            try
            {
                return parameters.GetParameter(key);
            }
            catch
            {
                return null;
            }
        }

        private static Parameter GetParameterField(object target, string fieldName)
        {
            try
            {
                FieldInfo field = AccessTools.Field(target.GetType(), fieldName);
                return field == null ? null : field.GetValue(target) as Parameter;
            }
            catch
            {
                return null;
            }
        }

        private static void SetScale(Parameter parameter, float scale)
        {
            if (parameter == null)
                return;

            ParameterScalePatchData data;
            if (!ParameterScaleData.TryGetValue(parameter, out data))
            {
                data = new ParameterScalePatchData(parameter.AddScaleModifier(RuntimeScaleModifierName, false));
                ParameterScaleData.Add(parameter, data);
            }

            if (data.ScaleModifier == null)
                return;

            if (Math.Abs(data.ScaleModifier.Value - scale) > 0.0001f)
                data.ScaleModifier.Value = scale;
        }
    }

    internal static class MegaSonarRuntimePatcher
    {
        private const string MegaSonarScaleModifierName = "LongSubmerged10x Mega Sonar Runtime";

        private static readonly string[] HydrophoneParameterKeys = new string[]
        {
            "HydrophoneRange",
            "GroupHydrophoneRange",
            "DirectHydrophoneRange",
            "HydrophoneDirectRange",
            "HydrophoneDetectionRange",
            "NoiseHydrophoneRange",
            "HydrophoneNoiseRange",
            "ListeningRange",
            "PassiveSonarRange"
        };

        private static readonly string[] DirectRefreshMethodNames = new string[]
        {
            "Awake",
            "Start",
            "OnEnable",
            "OnAfterDeserialize",
            "SavesManagerOnLoaded",
            "Update",
            "FixedUpdate",
            "UpdateModifiers",
            "ApplyModifiers",
            "Refresh",
            "Recalculate",
            "Validate"
        };

        private static readonly ConditionalWeakTable<Parameter, ParameterScalePatchData> SonarScaleData =
            new ConditionalWeakTable<Parameter, ParameterScalePatchData>();

        private static readonly ConditionalWeakTable<object, MegaSonarObjectPatchData> ObjectPatchData =
            new ConditionalWeakTable<object, MegaSonarObjectPatchData>();

        private static readonly Dictionary<Type, FieldInfo[]> ParameterFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> ParameterCollectionFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, FieldInfo[]> FloatFieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static readonly Dictionary<Type, PropertyInfo[]> FloatPropertyCache =
            new Dictionary<Type, PropertyInfo[]>();

        private static readonly HashSet<int> ApplicationGuardIds = new HashSet<int>();
        private static readonly HashSet<string> TargetMethodLogIds = new HashSet<string>();

        public static void ApplyAll(string reason)
        {
            try
            {
                Equipment[] equipmentItems = UnityEngine.Object.FindObjectsOfType<Equipment>();
                foreach (Equipment equipment in equipmentItems)
                    ApplyEquipment(equipment, reason + ".Equipment");

                MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour == null || behaviour is LongSubmergedMenuController || behaviour is Equipment)
                        continue;

                    if (!IsPotentialHydrophoneObject(behaviour))
                        continue;

                    ApplyObject(behaviour, reason + ".HydrophoneObject");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyObject(object target, string reason)
        {
            if (target == null || target is LongSubmergedMenuController)
                return;

            if (!TryEnter(target))
                return;

            try
            {
                Equipment equipment = target as Equipment;
                if (equipment != null)
                {
                    ApplyEquipment(equipment, reason);
                    return;
                }

                bool ownerLooksHydrophone = IsPotentialHydrophoneObject(target);
                if (!ownerLooksHydrophone)
                    return;

                Type type = target.GetType();
                ApplyParameterFields(target, type, ownerLooksHydrophone);
                ApplyParameterCollections(target, type);
                ApplyDirectFloatMembers(target, type, ownerLooksHydrophone);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Mega Sonar skipped on " + SafeObjectName(target) + " -> " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Exit(target);
            }
        }

        public static IEnumerable<MethodBase> FindHydrophoneTargetMethods()
        {
            HashSet<string> emitted = new HashSet<string>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                Type[] types = GetTypesSafely(assembly);
                if (types == null)
                    continue;

                foreach (Type type in types)
                {
                    if (!IsPotentialHydrophoneType(type))
                        continue;

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (MethodInfo method in methods)
                    {
                        if (method == null || method.IsAbstract || method.ContainsGenericParameters)
                            continue;

                        if (!IsHydrophoneRefreshMethod(method.Name))
                            continue;

                        string id = type.FullName + "::" + method.Name + "#" + method.GetParameters().Length;
                        if (!emitted.Add(id))
                            continue;

                        if (TargetMethodLogIds.Add(id))
                            Debug.Log("[LongSubmerged10x] Mega Sonar will refresh after " + id + ".");

                        yield return method;
                    }
                }
            }
        }

        private static void ApplyEquipment(Equipment equipment, string reason)
        {
            if (equipment == null || equipment.Parameters == null)
                return;

            bool hasHydrophoneRanges = ApplyParameterCollection(equipment.Parameters);
            bool equipmentLooksHydrophone = hasHydrophoneRanges || IsHydrophoneName(equipment.name);

            if (equipmentLooksHydrophone)
                ApplyDirectFloatMembers(equipment, equipment.GetType(), true);
        }

        private static void ApplyParameterFields(object target, Type type, bool ownerLooksHydrophone)
        {
            foreach (FieldInfo field in GetParameterFields(type))
            {
                if (!IsHydrophoneRangeMemberName(field.Name, ownerLooksHydrophone))
                    continue;

                try
                {
                    Parameter parameter = field.GetValue(target) as Parameter;
                    ApplyParameterScale(parameter);
                }
                catch
                {
                }
            }
        }

        private static void ApplyParameterCollections(object target, Type type)
        {
            foreach (FieldInfo field in GetParameterCollectionFields(type))
            {
                try
                {
                    ParameterCollection parameters = field.GetValue(target) as ParameterCollection;
                    ApplyParameterCollection(parameters);
                }
                catch
                {
                }
            }
        }

        private static bool ApplyParameterCollection(ParameterCollection parameters)
        {
            if (parameters == null)
                return false;

            bool foundAny = false;
            foreach (string key in HydrophoneParameterKeys)
            {
                Parameter parameter = GetParameter(parameters, key);
                if (parameter == null)
                    continue;

                ApplyParameterScale(parameter);
                foundAny = true;
            }

            return foundAny;
        }

        private static void ApplyParameterScale(Parameter parameter)
        {
            if (parameter == null)
                return;

            ParameterScalePatchData data;
            if (!SonarScaleData.TryGetValue(parameter, out data))
            {
                data = new ParameterScalePatchData(parameter.AddScaleModifier(MegaSonarScaleModifierName, false));
                SonarScaleData.Add(parameter, data);
            }

            if (data.ScaleModifier == null)
                return;

            float scale = GetEffectiveSonarFactor();
            if (Math.Abs(data.ScaleModifier.Value - scale) > 0.0001f)
                data.ScaleModifier.Value = scale;
        }

        private static void ApplyDirectFloatMembers(object target, Type type, bool ownerLooksHydrophone)
        {
            foreach (FieldInfo field in GetFloatFields(type))
            {
                if (!IsHydrophoneRangeMemberName(field.Name, ownerLooksHydrophone))
                    continue;

                ApplyFloatField(target, field);
            }

            foreach (PropertyInfo property in GetFloatProperties(type))
            {
                if (!IsHydrophoneRangeMemberName(property.Name, ownerLooksHydrophone))
                    continue;

                ApplyFloatProperty(target, property);
            }
        }

        private static void ApplyFloatField(object target, FieldInfo field)
        {
            try
            {
                object rawValue = field.GetValue(target);
                if (!(rawValue is float))
                    return;

                float currentValue = (float)rawValue;
                float desiredValue;
                if (!TryGetDesiredFloatValue(target, GetMemberKey(field), currentValue, out desiredValue))
                    return;

                if (Math.Abs(currentValue - desiredValue) > GetFloatTolerance(desiredValue))
                    field.SetValue(target, desiredValue);
            }
            catch
            {
            }
        }

        private static void ApplyFloatProperty(object target, PropertyInfo property)
        {
            try
            {
                object rawValue = property.GetValue(target, null);
                if (!(rawValue is float))
                    return;

                float currentValue = (float)rawValue;
                float desiredValue;
                if (!TryGetDesiredFloatValue(target, GetMemberKey(property), currentValue, out desiredValue))
                    return;

                if (Math.Abs(currentValue - desiredValue) > GetFloatTolerance(desiredValue))
                    property.SetValue(target, desiredValue, null);
            }
            catch
            {
            }
        }

        private static bool TryGetDesiredFloatValue(object target, string memberKey, float currentValue, out float desiredValue)
        {
            desiredValue = currentValue;

            if (!IsFinite(currentValue) || currentValue <= 0f)
                return false;

            float factor = GetEffectiveSonarFactor();
            MegaSonarObjectPatchData objectData = GetObjectPatchData(target);

            MegaSonarFloatMemberPatchData memberData;
            if (!objectData.FloatMembers.TryGetValue(memberKey, out memberData))
            {
                memberData = new MegaSonarFloatMemberPatchData(currentValue, 1f, currentValue);
                objectData.FloatMembers.Add(memberKey, memberData);
            }

            if (factor <= 1.0001f)
            {
                if (memberData.LastAppliedFactor > 1.0001f
                    && Math.Abs(currentValue - memberData.LastPatchedValue) <= GetFloatTolerance(memberData.LastPatchedValue))
                {
                    desiredValue = memberData.OriginalValue;
                }
                else
                {
                    memberData.OriginalValue = currentValue;
                    desiredValue = currentValue;
                }

                memberData.LastAppliedFactor = 1f;
                memberData.LastPatchedValue = desiredValue;
                return IsFinite(desiredValue) && desiredValue > 0f;
            }

            if (memberData.LastAppliedFactor <= 1.0001f
                || Math.Abs(currentValue - memberData.LastPatchedValue) > GetFloatTolerance(memberData.LastPatchedValue))
            {
                memberData.OriginalValue = currentValue;
            }

            desiredValue = memberData.OriginalValue * factor;
            memberData.LastAppliedFactor = factor;
            memberData.LastPatchedValue = desiredValue;
            return IsFinite(desiredValue) && desiredValue > 0f;
        }

        private static MegaSonarObjectPatchData GetObjectPatchData(object target)
        {
            MegaSonarObjectPatchData data;
            if (!ObjectPatchData.TryGetValue(target, out data))
            {
                data = new MegaSonarObjectPatchData();
                ObjectPatchData.Add(target, data);
            }

            return data;
        }

        private static float GetEffectiveSonarFactor()
        {
            if (!LongSubmergedRuntimeSettings.MegaSonar)
                return 1f;

            return LongSubmergedRuntimeSettings.ClampSonarFactor(LongSubmergedRuntimeSettings.SonarFactor);
        }

        private static FieldInfo[] GetParameterFields(Type type)
        {
            FieldInfo[] cached;
            if (ParameterFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(Parameter));
            cached = fields.ToArray();
            ParameterFieldCache[type] = cached;
            return cached;
        }

        private static FieldInfo[] GetParameterCollectionFields(Type type)
        {
            FieldInfo[] cached;
            if (ParameterCollectionFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();
            CollectFields(type, fields, typeof(ParameterCollection));
            cached = fields.ToArray();
            ParameterCollectionFieldCache[type] = cached;
            return cached;
        }

        private static FieldInfo[] GetFloatFields(Type type)
        {
            FieldInfo[] cached;
            if (FloatFieldCache.TryGetValue(type, out cached))
                return cached;

            List<FieldInfo> fields = new List<FieldInfo>();

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] declaredFields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in declaredFields)
                {
                    if (field == null || field.FieldType != typeof(float) || field.IsInitOnly || field.IsLiteral)
                        continue;

                    fields.Add(field);
                }
            }

            cached = fields.ToArray();
            FloatFieldCache[type] = cached;
            return cached;
        }

        private static PropertyInfo[] GetFloatProperties(Type type)
        {
            PropertyInfo[] cached;
            if (FloatPropertyCache.TryGetValue(type, out cached))
                return cached;

            List<PropertyInfo> properties = new List<PropertyInfo>();

            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                PropertyInfo[] declaredProperties = current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in declaredProperties)
                {
                    if (property == null || property.PropertyType != typeof(float))
                        continue;

                    if (property.GetIndexParameters().Length != 0)
                        continue;

                    if (property.GetGetMethod(true) == null || property.GetSetMethod(true) == null)
                        continue;

                    properties.Add(property);
                }
            }

            cached = properties.ToArray();
            FloatPropertyCache[type] = cached;
            return cached;
        }

        private static void CollectFields(Type type, List<FieldInfo> fields, Type requiredFieldType)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo[] declaredFields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in declaredFields)
                {
                    if (field == null || !requiredFieldType.IsAssignableFrom(field.FieldType))
                        continue;

                    fields.Add(field);
                }
            }
        }

        private static bool IsPotentialHydrophoneObject(object target)
        {
            if (target == null)
                return false;

            Type type = target.GetType();
            if (IsPotentialHydrophoneType(type))
                return true;

            UnityEngine.Object unityObject = target as UnityEngine.Object;
            if (unityObject != null && IsHydrophoneName(unityObject.name))
                return true;

            Equipment equipment = target as Equipment;
            if (equipment != null && equipment.Parameters != null)
                return HasHydrophoneParameter(equipment.Parameters);

            return false;
        }

        private static bool IsPotentialHydrophoneType(Type type)
        {
            if (type == null || type.IsAbstract || type.ContainsGenericParameters)
                return false;

            if (type.Namespace != null && type.Namespace.IndexOf("LongSubmerged10x", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            string name = type.FullName;
            if (string.IsNullOrEmpty(name))
                name = type.Name;

            return IsHydrophoneName(name);
        }

        private static bool IsHydrophoneName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.IndexOf("Hydrophone", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Horch", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Listening", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("PassiveSonar", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Gruppenhorch", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Balkon", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(name.Trim(), "GHG", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name.Trim(), "KDB", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasHydrophoneParameter(ParameterCollection parameters)
        {
            if (parameters == null)
                return false;

            foreach (string key in HydrophoneParameterKeys)
            {
                if (GetParameter(parameters, key) != null)
                    return true;
            }

            return false;
        }

        private static bool IsHydrophoneRefreshMethod(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
                return false;

            if (methodName.StartsWith("get_", StringComparison.Ordinal) || methodName.StartsWith("set_", StringComparison.Ordinal))
                return false;

            if (methodName.StartsWith("add_", StringComparison.Ordinal) || methodName.StartsWith("remove_", StringComparison.Ordinal))
                return false;

            foreach (string directName in DirectRefreshMethodNames)
            {
                if (string.Equals(methodName, directName, StringComparison.Ordinal))
                    return true;
            }

            bool looksLikeRefresh =
                methodName.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Apply", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Refresh", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Recalculate", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Calculate", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Validate", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksLikeRefresh)
                return false;

            return methodName.IndexOf("Hydrophone", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Range", StringComparison.OrdinalIgnoreCase) >= 0
                || methodName.IndexOf("Modifier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHydrophoneRangeMemberName(string name, bool ownerLooksHydrophone)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (string key in HydrophoneParameterKeys)
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (IsExcludedRangeName(name))
                return false;

            bool explicitHydrophone =
                name.IndexOf("Hydrophone", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("PassiveSonar", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Listening", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Horch", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!explicitHydrophone && !ownerLooksHydrophone)
                return false;

            return name.IndexOf("Range", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Radius", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsExcludedRangeName(string name)
        {
            return name.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Angle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Bearing", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Heading", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Direction", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Rotation", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Fov", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Frequency", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Delay", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Time", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Cooldown", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Duration", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Explosion", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Torpedo", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Accuracy", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Performance", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Penalty", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Modifier", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Parameter GetParameter(ParameterCollection parameters, string key)
        {
            try
            {
                return parameters.GetParameter(key);
            }
            catch
            {
                return null;
            }
        }

        private static Type[] GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types;
            }
            catch
            {
                return null;
            }
        }

        private static string GetMemberKey(MemberInfo member)
        {
            if (member == null)
                return string.Empty;

            string declaringType = member.DeclaringType == null ? string.Empty : member.DeclaringType.FullName;
            return declaringType + "." + member.Name;
        }

        private static string SafeObjectName(object target)
        {
            if (target == null)
                return "null";

            UnityEngine.Object unityObject = target as UnityEngine.Object;
            if (unityObject != null)
                return target.GetType().Name + "(" + unityObject.name + ")";

            return target.GetType().Name;
        }

        private static float GetFloatTolerance(float reference)
        {
            return Math.Max(0.0001f, Math.Abs(reference) * 0.0001f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool TryEnter(object target)
        {
            if (target == null)
                return false;

            return ApplicationGuardIds.Add(RuntimeHelpers.GetHashCode(target));
        }

        private static void Exit(object target)
        {
            if (target == null)
                return;

            ApplicationGuardIds.Remove(RuntimeHelpers.GetHashCode(target));
        }
    }

    internal sealed class MegaSonarObjectPatchData
    {
        public readonly Dictionary<string, MegaSonarFloatMemberPatchData> FloatMembers =
            new Dictionary<string, MegaSonarFloatMemberPatchData>();
    }

    internal sealed class MegaSonarFloatMemberPatchData
    {
        public float OriginalValue;
        public float LastAppliedFactor;
        public float LastPatchedValue;

        public MegaSonarFloatMemberPatchData(float originalValue, float lastAppliedFactor, float lastPatchedValue)
        {
            OriginalValue = originalValue;
            LastAppliedFactor = lastAppliedFactor;
            LastPatchedValue = lastPatchedValue;
        }
    }

    [HarmonyPatch]
    internal static class MegaSonarHydrophoneRefreshPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return MegaSonarRuntimePatcher.FindHydrophoneTargetMethods();
        }

        private static void Postfix(object __instance)
        {
            MegaSonarRuntimePatcher.ApplyObject(__instance, "hydrophone refresh hook");
        }
    }

    internal sealed class ParameterScalePatchData
    {
        public readonly Modifier ScaleModifier;

        public ParameterScalePatchData(Modifier scaleModifier)
        {
            ScaleModifier = scaleModifier;
        }
    }

    internal sealed class ParameterDeltaPatchData
    {
        public readonly Modifier DeltaModifier;

        public ParameterDeltaPatchData(Modifier deltaModifier)
        {
            DeltaModifier = deltaModifier;
        }
    }

    internal sealed class OxygenModifierPatchData
    {
        public float OriginalValue;
        public float LastAppliedFactor;
        public float LastPatchedValue;

        public OxygenModifierPatchData(float originalValue)
        {
            OriginalValue = originalValue;
            LastAppliedFactor = 1f;
            LastPatchedValue = originalValue;
        }
    }

    internal sealed class TorpedoGuidancePatchData
    {
        public bool HasOriginalValues;
        public bool GuidanceApplied;
        public bool GuidanceLogged;
        public bool ForcingDetonation;
        public float OriginalGyroAngle;
        public Vector3 OriginalTargetPosition;
        public Vector3 OriginalTargetPositionForReports;
    }

    internal static class SuperStealthRuntimePatcher
    {
        private const string SuperStealthScaleModifierName = "LongSubmerged10x Super Stealth Runtime";

        private static readonly ConditionalWeakTable<Parameter, ParameterScalePatchData> StealthScaleData =
            new ConditionalWeakTable<Parameter, ParameterScalePatchData>();

        private static readonly HashSet<int> ApplicationGuardIds = new HashSet<int>();

        public static void ApplyPlayerShip(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            if (!TryEnter(ship))
                return;

            try
            {
                float scale = GetEffectiveStealthScale();

                ApplyParameter(ship.CrewNoiseModifier, scale);
                ApplyParameter(ship.StationaryNoise, scale);
                ApplyEntityDetectability(ship, scale);
                ApplySandboxDetectability(ship, scale);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Super discrétion skipped after " + reason + " -> " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Exit(ship);
            }
        }

        public static void ApplyEntity(Entity entity, string reason)
        {
            ApplyPlayerShip(entity as PlayerShip, reason);
        }

        public static void ApplyEquipment(Component component, string reason)
        {
            PlayerShip owner = GetPlayerShipOwner(component);
            if (owner == null)
                return;

            // Equipment noise feeds the player's final detectability parameters. Reapply the player-level
            // scale after vanilla component updates instead of scaling the same contribution twice.
            ApplyPlayerShip(owner, reason);
        }

        private static void ApplyEntityDetectability(Entity entity, float scale)
        {
            if (entity == null)
                return;

            ApplyParameter(entity.HydrophoneDetectability, scale);
            ApplyParameter(entity.SonarDetectability, scale);
            ApplyParameter(entity.VisualDetectability, scale);
            ApplyParameter(entity.RadarDetectorDetectability, scale);
        }

        private static void ApplySandboxDetectability(Entity entity, float scale)
        {
            if (entity == null)
                return;

            SandboxEntity sandboxEntity = null;

            try
            {
                sandboxEntity = entity.SandboxEntity;
            }
            catch
            {
                sandboxEntity = null;
            }

            if (sandboxEntity == null)
                return;

            ApplyParameter(sandboxEntity.HydrophoneDetectability, scale);
            ApplyParameter(sandboxEntity.RadarDetectability, scale);
            ApplyParameter(sandboxEntity.IndirectVisualDetectability, scale);
            ApplyParameter(sandboxEntity.SignatureRadius, scale);
        }

        private static void ApplyParameter(Parameter parameter, float scale)
        {
            if (parameter == null)
                return;

            ParameterScalePatchData data;
            if (!StealthScaleData.TryGetValue(parameter, out data))
            {
                data = new ParameterScalePatchData(parameter.AddScaleModifier(SuperStealthScaleModifierName, false));
                StealthScaleData.Add(parameter, data);
            }

            if (data.ScaleModifier == null)
                return;

            if (Math.Abs(data.ScaleModifier.Value - scale) > 0.0001f)
                data.ScaleModifier.Value = scale;
        }

        private static PlayerShip GetPlayerShipOwner(Component component)
        {
            if (component == null)
                return null;

            try
            {
                return component.GetComponentInParent<PlayerShip>();
            }
            catch
            {
                return null;
            }
        }

        private static float GetEffectiveStealthScale()
        {
            if (!LongSubmergedRuntimeSettings.SuperStealth)
                return 1f;

            return 1f / LongSubmergedRuntimeSettings.SuperStealthFactor;
        }

        private static bool TryEnter(object target)
        {
            if (target == null)
                return false;

            return ApplicationGuardIds.Add(RuntimeHelpers.GetHashCode(target));
        }

        private static void Exit(object target)
        {
            if (target == null)
                return;

            ApplicationGuardIds.Remove(RuntimeHelpers.GetHashCode(target));
        }
    }

    internal struct HeavyArmorDamageScaleState
    {
        public bool ScaledDamage;
        public bool PreserveDistributionBudget;
        public float OriginalDamage;

        public HeavyArmorDamageScaleState(bool scaledDamage, bool preserveDistributionBudget, float originalDamage)
        {
            ScaledDamage = scaledDamage;
            PreserveDistributionBudget = preserveDistributionBudget;
            OriginalDamage = originalDamage;
        }
    }

    internal static class HeavyArmorRuntimePatcher
    {
        public static readonly Type[] AddDamageParameterTypes = new Type[]
        {
            typeof(float),
            typeof(Entity),
            typeof(Vector3),
            typeof(Vector3),
            typeof(float),
            typeof(float),
            typeof(DamageType),
            typeof(float),
            typeof(float),
            typeof(bool).MakeByRefType()
        };

        public static readonly Type[] AddWaterDamageParameterTypes = new Type[]
        {
            typeof(float),
            typeof(bool)
        };

        public static readonly Type[] DamageUtilityDoApplyDamageParameterTypes = new Type[]
        {
            typeof(Entity),
            typeof(PlayableCharacterData[]),
            typeof(Entity),
            typeof(Vector3),
            typeof(Vector3),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(Entity),
            typeof(DamageType),
            typeof(Action<DamageEvent>),
            typeof(Ship),
            typeof(float)
        };

        public static readonly Type[] DamageUtilityApplyDamageToComponentsParameterTypes = new Type[]
        {
            typeof(Entity),
            typeof(Vector3),
            typeof(Vector3),
            typeof(float),
            typeof(DamageType),
            typeof(float),
            typeof(float).MakeByRefType(),
            typeof(float)
        };

        public static readonly Type[] HullEffectsApplyImpactParameterTypes = new Type[]
        {
            typeof(Vector3),
            typeof(float),
            typeof(float)
        };

        [ThreadStatic]
        private static int damageScaleScopeDepth;

        [ThreadStatic]
        private static int componentDamageDistributionScopeDepth;

        [ThreadStatic]
        private static int pressureWaterDamageScopeDepth;

        public static void ScalePlayerEquipmentDamage(Equipment equipment, ref float damage)
        {
            TryScalePlayerEquipmentDamage(equipment, ref damage);
        }

        public static HeavyArmorDamageScaleState TryScalePlayerEquipmentDamage(Equipment equipment, ref float damage)
        {
            float ignoredFlawProbabilityFactor = 0f;
            float ignoredFireChance = 0f;
            return TryScalePlayerEquipmentDamage(equipment, ref damage, ref ignoredFlawProbabilityFactor, ref ignoredFireChance);
        }

        public static HeavyArmorDamageScaleState TryScalePlayerEquipmentDamage(
            Equipment equipment,
            ref float damage,
            ref float flawProbabilityFactor,
            ref float fireChance
        )
        {
            if (!ShouldScaleDamage(damage) || !IsPlayerShipEquipment(equipment))
                return new HeavyArmorDamageScaleState(false, false, 0f);

            HeavyArmorDamageScaleState state = new HeavyArmorDamageScaleState(
                true,
                IsComponentDamageDistributionScopeActive(),
                damage
            );

            damage = ScaleDamage(damage);

            if (ShouldScaleRawValue(flawProbabilityFactor))
                flawProbabilityFactor = ScaleDamage(flawProbabilityFactor);

            if (ShouldScaleRawValue(fireChance))
                fireChance = ScaleDamage(fireChance);

            return state;
        }

        public static HeavyArmorDamageScaleState TryScalePlayerWaterDamage(Equipment equipment, ref float damage)
        {
            if (IsPressureWaterDamageScopeActive())
                return new HeavyArmorDamageScaleState(false, false, 0f);

            return TryScalePlayerEquipmentDamage(equipment, ref damage);
        }

        public static void ScalePlayerCharacterDamage(PlayableCharacter character, ref float damage)
        {
            if (!ShouldScaleDamage(damage) || !IsPlayerCrewMember(character))
                return;

            damage = ScaleDamage(damage);
        }

        public static void ScalePlayerCrewDamage(Ship target, ref float crewDamage)
        {
            if (!IsHeavyArmorActive() || !IsPlayerEntity(target) || !ShouldScaleRawValue(crewDamage))
                return;

            crewDamage = ScaleDamage(crewDamage);
        }

        public static void ScalePlayerHullImpact(HullEffectsRenderer renderer, ref float intensity)
        {
            if (!IsHeavyArmorActive() || !IsPlayerHullRenderer(renderer) || !ShouldScaleRawValue(intensity))
                return;

            intensity = ScaleDamage(intensity);
        }

        public static bool BeginComponentDamageDistributionScope()
        {
            if (!IsHeavyArmorActive())
                return false;

            componentDamageDistributionScopeDepth++;
            return true;
        }

        public static void EndComponentDamageDistributionScope(bool entered)
        {
            if (!entered)
                return;

            if (componentDamageDistributionScopeDepth > 0)
                componentDamageDistributionScopeDepth--;
        }

        public static bool BeginPressureWaterDamageScope()
        {
            pressureWaterDamageScopeDepth++;
            return true;
        }

        public static void EndPressureWaterDamageScope(bool entered)
        {
            if (!entered)
                return;

            if (pressureWaterDamageScopeDepth > 0)
                pressureWaterDamageScopeDepth--;
        }

        public static void BeginDamageScaleScope(HeavyArmorDamageScaleState state)
        {
            if (state.ScaledDamage)
                damageScaleScopeDepth++;
        }

        public static void EndDamageScaleScope(HeavyArmorDamageScaleState state)
        {
            if (!state.ScaledDamage)
                return;

            if (damageScaleScopeDepth > 0)
                damageScaleScopeDepth--;
        }

        public static void RestoreComponentDistributionBudget(HeavyArmorDamageScaleState state, ref float result)
        {
            if (!state.PreserveDistributionBudget || !ShouldScaleRawValue(result))
                return;

            float restoredResult = result * LongSubmergedRuntimeSettings.HeavyArmorDamageFactor;
            if (ShouldScaleRawValue(state.OriginalDamage))
                result = Mathf.Min(state.OriginalDamage, restoredResult);
            else
                result = restoredResult;
        }

        private static bool ShouldScaleDamage(float damage)
        {
            return IsHeavyArmorActive() && !IsDamageScaleScopeActive() && ShouldScaleRawValue(damage);
        }

        private static bool IsDamageScaleScopeActive()
        {
            return damageScaleScopeDepth > 0;
        }

        private static bool IsComponentDamageDistributionScopeActive()
        {
            return componentDamageDistributionScopeDepth > 0;
        }

        private static bool IsPressureWaterDamageScopeActive()
        {
            return pressureWaterDamageScopeDepth > 0;
        }

        private static bool ShouldScaleRawValue(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static float ScaleDamage(float value)
        {
            return value / LongSubmergedRuntimeSettings.HeavyArmorDamageFactor;
        }

        private static bool IsHeavyArmorActive()
        {
            return LongSubmergedRuntimeSettings.HeavyArmor
                && LongSubmergedRuntimeSettings.HeavyArmorDamageFactor > 1.0001f;
        }

        private static bool IsPlayerShipEquipment(Equipment equipment)
        {
            if (equipment == null)
                return false;

            try
            {
                if (IsPlayerEntity(equipment.ParentEntity))
                    return true;
            }
            catch
            {
            }

            try
            {
                return equipment.GetComponentInParent<PlayerShip>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerCrewMember(PlayableCharacter character)
        {
            if (character == null)
                return false;

            try
            {
                if (IsPlayerEntity(character.ParentEntity))
                    return true;
            }
            catch
            {
            }

            try
            {
                return character.GetComponentInParent<PlayerShip>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerHullRenderer(HullEffectsRenderer renderer)
        {
            if (renderer == null)
                return false;

            try
            {
                if (IsPlayerEntity(renderer.ParentEntity))
                    return true;
            }
            catch
            {
            }

            try
            {
                return renderer.GetComponentInParent<PlayerShip>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPlayerEntity(Entity entity)
        {
            if (entity == null)
                return false;

            if (entity is PlayerShip)
                return true;

            try
            {
                SandboxEntity sandboxEntity = entity.SandboxEntity;
                return sandboxEntity != null && sandboxEntity.IsPlayerShip;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    // DonJ : profondeur x2 sans falsifier le profondimetre reel.
    // Les ordres de profondeur > 10 m sont transformes en profondeur reelle :
    // 20->40, 40->80, 150->300, 300->600. Le crush vanilla est neutralise sous 700 m.
    internal static class DeepDiveRuntimePatcher
    {
        public const float DisplayedDepthCommandFactor = 2f;
        public const float ShallowDepthPassthroughMeters = 10f;
        public const float MaxDisplayedCommandDepthMeters = 300f;
        public const float MaxRealCommandDepthMeters = 600f;
        public const float CrushDepthMeters = 700f;

        private const float MetersPerAtmosphere = 10f;
        private const float SeaLevelPressureAtmospheres = 1f;
        private const float Epsilon = 0.01f;
        private const float FullScanIntervalSeconds = 2f;
        private const int MaxObjectPatchLogs = 20;
        private const string HullCrushDepthDeltaModifierName = "LongSubmerged10x DeepDive Crush Depth";

        private static readonly BindingFlags InstanceMemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static readonly Type[] PlayerShipSetTargetDepthParameterTypes =
            new Type[] { typeof(float), typeof(bool), typeof(bool) };

        private static readonly MethodInfo PlayerShipSetTargetDepthMethod =
            AccessTools.Method(typeof(PlayerShip), "SetTargetDepth", PlayerShipSetTargetDepthParameterTypes);

        private static readonly MethodInfo PlayerShipUpdateStressAndDisciplineGainMethod =
            AccessTools.Method(typeof(PlayerShip), "UpdateStressAndDisciplineGain", new Type[] { });

        private static readonly FieldInfo DepthStressModifierField =
            AccessTools.Field(typeof(PlayerShip), "depthStressModifier");

        private static readonly Dictionary<Type, DepthMemberCache> MemberCache =
            new Dictionary<Type, DepthMemberCache>();

        private static readonly HashSet<string> MissingTypeWarnings =
            new HashSet<string>();

        private static readonly HashSet<int> ObjectPatchLogIds =
            new HashSet<int>();

        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> CrushDepthDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<object, DepthObjectPatchData> DepthObjectPatches =
            new ConditionalWeakTable<object, DepthObjectPatchData>();

        private static float nextFullScanTime;
        private static int objectPatchLogCount;

        public static bool IsEnabled()
        {
            return LongSubmergedRuntimeSettings.DeepDive;
        }

        public static void ApplyAll(string reason)
        {
            try
            {
                ApplyPlayerShip(UnityEngine.Object.FindObjectOfType<PlayerShip>(), reason + ".PlayerShip");
                ApplyNearbyDepthObjects(reason + ".Objects");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void ApplyPlayerShip(PlayerShip ship, string reason)
        {
            if (ship == null)
                return;

            try
            {
                if (!IsEnabled())
                {
                    RestoreDepthObject(ship, reason);
                    return;
                }

                ApplyDepthObject(ship, reason);
                ClampPlayerShipTargetDepth(ship, reason);

                if (Time.unscaledTime >= nextFullScanTime)
                {
                    nextFullScanTime = Time.unscaledTime + FullScanIntervalSeconds;
                    ApplyNearbyDepthObjects(reason + ".Scan");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public static void UpdatePlayerShipRuntime(PlayerShip ship, string reason)
        {
            ApplyPlayerShip(ship, reason);
        }

        public static void ApplyDepthObject(object target, string reason)
        {
            if (target == null)
                return;

            try
            {
                if (!IsEnabled())
                {
                    RestoreDepthObject(target, reason);
                    return;
                }

                Type type = target.GetType();
                DepthMemberCache cache = GetDepthMemberCache(type);
                int changed = 0;

                for (int index = 0; index < cache.Fields.Length; index++)
                {
                    if (TryPatchField(target, cache.Fields[index]))
                        changed++;
                }

                for (int index = 0; index < cache.Properties.Length; index++)
                {
                    if (TryPatchProperty(target, cache.Properties[index]))
                        changed++;
                }

                if (changed > 0)
                    LogPatchedObjectOnce(target, type, changed, reason);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[LongSubmerged10x] DeepDive reflection skipped for "
                    + target.GetType().Name
                    + " after "
                    + SafeReason(reason)
                    + ": "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message
                );
            }
        }

        public static void ScaleTargetDepthCommand(ref float value, string reason)
        {
            if (!IsEnabled())
                return;

            if (!IsFinite(value) || value <= ShallowDepthPassthroughMeters)
                return;

            float original = value;
            float scaled = value <= MaxDisplayedCommandDepthMeters + Epsilon
                ? value * DisplayedDepthCommandFactor
                : value;

            scaled = Mathf.Clamp(scaled, 0f, MaxRealCommandDepthMeters);

            if (Mathf.Abs(scaled - original) <= Epsilon)
                return;

            value = scaled;
            Debug.Log(
                "[LongSubmerged10x] DeepDive target depth "
                + original.ToString("0.#")
                + " m -> "
                + scaled.ToString("0.#")
                + " m after "
                + SafeReason(reason)
                + "."
            );
        }

        public static void ApplyDepthStressModifier(PlayerShip ship, string reason)
        {
            if (ship == null || !IsEnabled())
                return;

            try
            {
                if (DepthStressModifierField == null)
                {
                    WarnMissingTypeOnce(
                        "PlayerShip.depthStressModifier",
                        "champ depthStressModifier introuvable, stress de profondeur vanilla conserve."
                    );
                    return;
                }

                Modifier depthStressModifier = DepthStressModifierField.GetValue(ship) as Modifier;
                if (depthStressModifier == null)
                {
                    WarnMissingTypeOnce(
                        "PlayerShip.depthStressModifier.Value",
                        "modificateur stress profondeur introuvable, stress de profondeur vanilla conserve."
                    );
                    return;
                }

                float deckDepth = ship.DeckDepth;
                float targetDepth = ship.TargetDepth;
                if (!IsFinite(deckDepth) && !IsFinite(targetDepth))
                    return;

                if (!IsFinite(deckDepth))
                    deckDepth = 0f;

                if (!IsFinite(targetDepth))
                    targetDepth = 0f;

                float realDepth = Mathf.Max(Mathf.Max(0f, deckDepth), Mathf.Max(0f, targetDepth));
                float vanillaTier = GetDepthStressTier(realDepth);
                if (vanillaTier <= 0f)
                    return;

                float effectiveDepth = realDepth / DisplayedDepthCommandFactor;
                float effectiveTier = GetDepthStressTier(effectiveDepth);
                float currentValue = depthStressModifier.Value;
                if (!IsFinite(currentValue))
                    return;

                float desiredValue = currentValue * (effectiveTier / vanillaTier);
                if (Math.Abs(currentValue - desiredValue) <= 0.000001f)
                    return;

                depthStressModifier.Value = desiredValue;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[LongSubmerged10x] DeepDive stress patch skipped after "
                    + SafeReason(reason)
                    + ": "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message
                );
            }
        }

        public static MethodBase FindMethodOnKnownType(string simpleTypeName, string methodName)
        {
            Type type = FindKnownType(simpleTypeName);
            if (type == null)
                return null;

            MethodBase exactNoArgs = AccessTools.Method(type, methodName, new Type[] { });
            if (exactNoArgs != null)
                return exactNoArgs;

            return AccessTools.Method(type, methodName);
        }

        public static MethodBase FindPlayerShipSetTargetDepthMethod()
        {
            return PlayerShipSetTargetDepthMethod;
        }

        public static MethodBase FindPlayerShipUpdateStressAndDisciplineGainMethod()
        {
            return PlayerShipUpdateStressAndDisciplineGainMethod;
        }

        public static bool ShouldRunHullCrushDoUpdate(object controller, ref float result, string reason)
        {
            if (!IsEnabled())
            {
                RestoreDepthObject(controller, reason);
                TryPatchHullCrushControllerData(controller, reason);
                return true;
            }

            ApplyDepthObject(controller, reason);

            if (TryPatchHullCrushControllerData(controller, reason))
                return true;

            float depth = GetPlayerDeckDepthMeters();
            if (!IsFinite(depth))
            {
                WarnMissingTypeOnce(
                    "PlayerShipDepth",
                    "profondeur joueur introuvable, HullCrushController vanilla conserve."
                );
                return true;
            }

            // Sous 700 m, on neutralise le crush vanilla. A 700 m et plus, on laisse le jeu executer
            // son chemin original de destruction pour garder la vraie logique de game over.
            if (depth < CrushDepthMeters - Epsilon)
            {
                result = 1f;
                return false;
            }

            return true;
        }

        public static bool ShouldSkipPressureWaterDamageTick(object controller, string reason)
        {
            if (!IsEnabled())
            {
                RestoreDepthObject(controller, reason);
                return false;
            }

            ApplyDepthObject(controller, reason);

            float depth = GetPlayerDeckDepthMeters();
            if (!IsFinite(depth))
                return false;

            // Jusqu'a 600 m reels, la profondeur demandee est consideree comme operationnelle.
            // Au-dessus, la pression vanilla peut recommencer a infliger des avaries avant le crush a 700 m.
            return depth < MaxRealCommandDepthMeters - Epsilon;
        }

        private static void ApplyNearbyDepthObjects(string reason)
        {
            foreach (DivingPlanesStation station in UnityEngine.Object.FindObjectsOfType<DivingPlanesStation>())
                ApplyDepthObject(station, reason + ".DivingPlanesStation");

            foreach (Equipment equipment in UnityEngine.Object.FindObjectsOfType<Equipment>())
                ApplyDepthObject(equipment, reason + ".Equipment");

            ApplyObjectsByTypeName("HullCrushController", reason + ".HullCrushController");
            ApplyObjectsByTypeName("ApplyWaterDamageToPlayerShip", reason + ".ApplyWaterDamageToPlayerShip");
        }

        private static void ApplyObjectsByTypeName(string simpleTypeName, string reason)
        {
            Type type = FindKnownType(simpleTypeName);
            if (type == null)
            {
                WarnMissingTypeOnce(
                    simpleTypeName,
                    "type " + simpleTypeName + " introuvable, patch profondeur partiel."
                );
                return;
            }

            UnityEngine.Object[] objects = UnityEngine.Object.FindObjectsOfType(type);
            for (int index = 0; index < objects.Length; index++)
            {
                ApplyDepthObject(objects[index], reason);

                if (simpleTypeName == "HullCrushController")
                    TryPatchHullCrushControllerData(objects[index], reason);
            }
        }

        private static bool TryPatchHullCrushControllerData(object controller, string reason)
        {
            if (controller == null)
                return false;

            try
            {
                Type controllerType = controller.GetType();
                MethodInfo parseMethod = AccessTools.Method(controllerType, "ParseNewEntities", new Type[] { });
                if (parseMethod != null)
                    parseMethod.Invoke(controller, null);

                FieldInfo dataField = AccessTools.Field(controllerType, "hullCrushData");
                if (dataField == null)
                    return false;

                Array data = dataField.GetValue(controller) as Array;
                if (data == null)
                    return false;

                int changed = 0;
                for (int index = 0; index < data.Length; index++)
                {
                    object item = data.GetValue(index);
                    if (item == null)
                        continue;

                    Type itemType = item.GetType();
                    FieldInfo entityField = AccessTools.Field(itemType, "Entity");
                    object entity = entityField == null ? null : entityField.GetValue(item);
                    if (!(entity is PlayerShip))
                        continue;

                    FieldInfo crushDepthField = AccessTools.Field(itemType, "CrushDepth");
                    Parameter crushDepth = crushDepthField == null ? null : crushDepthField.GetValue(item) as Parameter;
                    if (crushDepth == null)
                        continue;

                    if (ApplyCrushDepthParameter(crushDepth))
                        changed++;
                }

                if (changed > 0)
                {
                    if (IsEnabled())
                    {
                        Debug.Log(
                            "[LongSubmerged10x] DeepDive raised player crush depth to "
                            + CrushDepthMeters.ToString("0.#")
                            + " m after "
                            + SafeReason(reason)
                            + "."
                        );
                    }
                    else
                    {
                        Debug.Log(
                            "[LongSubmerged10x] DeepDive restored player crush depth after "
                            + SafeReason(reason)
                            + "."
                        );
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[LongSubmerged10x] DeepDive hull crush data patch skipped after "
                    + SafeReason(reason)
                    + ": "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message
                );
                return false;
            }
        }

        private static bool ApplyCrushDepthParameter(Parameter parameter)
        {
            if (parameter == null)
                return false;

            float baseValue = parameter.GetValueExcludingModifier(HullCrushDepthDeltaModifierName);
            if (!IsFinite(baseValue))
                return false;

            float target = GetHullCrushDepthTarget();
            float desiredDelta = IsEnabled()
                ? (baseValue <= target + Epsilon ? 0f : target - baseValue)
                : 0f;

            ParameterDeltaPatchData data;
            if (!CrushDepthDeltaData.TryGetValue(parameter, out data))
            {
                if (Math.Abs(desiredDelta) <= 0.0001f)
                    return false;

                data = new ParameterDeltaPatchData(parameter.AddDeltaModifier(HullCrushDepthDeltaModifierName, false));
                CrushDepthDeltaData.Add(parameter, data);
            }

            if (data.DeltaModifier == null)
                return false;

            if (Math.Abs(data.DeltaModifier.Value - desiredDelta) <= 0.0001f)
                return false;

            data.DeltaModifier.Value = desiredDelta;
            return true;
        }

        private static float GetHullCrushDepthTarget()
        {
            return -CrushDepthMeters;
        }

        private static Type FindKnownType(string simpleTypeName)
        {
            if (string.IsNullOrEmpty(simpleTypeName))
                return null;

            Type type = AccessTools.TypeByName(simpleTypeName);
            if (type != null)
                return type;

            string[] namespaces = new string[]
            {
                "UBOAT.Game",
                "UBOAT.Game.Scene",
                "UBOAT.Game.Scene.Tasks",
                "UBOAT.Game.Scene.Entities",
                "UBOAT.Game.Scene.Utilities",
                "UBOAT.Game.Core"
            };

            for (int index = 0; index < namespaces.Length; index++)
            {
                type = AccessTools.TypeByName(namespaces[index] + "." + simpleTypeName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static void ClampPlayerShipTargetDepth(PlayerShip ship, string reason)
        {
            try
            {
                float targetDepth = ship.TargetDepth;
                if (!IsFinite(targetDepth) || targetDepth <= MaxRealCommandDepthMeters + Epsilon)
                    return;

                if (TrySetPlayerShipTargetDepth(ship, MaxRealCommandDepthMeters, reason))
                {
                    Debug.Log(
                        "[LongSubmerged10x] DeepDive target depth clamped from "
                        + targetDepth.ToString("0.#")
                        + " m to "
                        + MaxRealCommandDepthMeters.ToString("0.#")
                        + " m after "
                        + SafeReason(reason)
                        + "."
                    );
                }
            }
            catch
            {
                float targetDepth;
                if (!TryReadFloatMember(ship, "TargetDepth", out targetDepth))
                    return;

                if (!IsFinite(targetDepth) || targetDepth <= MaxRealCommandDepthMeters + Epsilon)
                    return;

                TrySetPlayerShipTargetDepth(ship, MaxRealCommandDepthMeters, reason);
            }
        }

        private static bool TrySetPlayerShipTargetDepth(PlayerShip ship, float value, string reason)
        {
            if (ship == null)
                return false;

            try
            {
                if (PlayerShipSetTargetDepthMethod != null)
                {
                    PlayerShipSetTargetDepthMethod.Invoke(ship, new object[] { value, true, false });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[LongSubmerged10x] DeepDive SetTargetDepth invoke skipped after "
                    + SafeReason(reason)
                    + ": "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message
                );
            }

            if (TryWriteFloatMember(ship, "targetDepth", value))
                return true;

            if (TryWriteFloatMember(ship, "TargetDepth", value))
                return true;

            WarnMissingTypeOnce(
                "PlayerShip.SetTargetDepth",
                "PlayerShip.SetTargetDepth introuvable, clamp profondeur impossible."
            );
            return false;
        }

        private static float GetPlayerDeckDepthMeters()
        {
            return GetPlayerDeckDepthMeters(UnityEngine.Object.FindObjectOfType<PlayerShip>());
        }

        private static float GetPlayerDeckDepthMeters(PlayerShip ship)
        {
            if (ship == null)
                return float.NaN;

            try
            {
                if (IsFinite(ship.DeckDepth))
                    return Mathf.Max(0f, ship.DeckDepth);
            }
            catch
            {
                // Fallback reflection ci-dessous.
            }

            float value;
            if (TryReadFloatMember(ship, "DeckDepth", out value))
                return Mathf.Max(0f, value);

            if (TryReadFloatMember(ship, "KeelDepth", out value))
                return Mathf.Max(0f, value);

            if (TryReadFloatMember(ship, "Depth", out value))
                return Mathf.Max(0f, value);

            return float.NaN;
        }

        private static bool TryReadFloatMember(object target, string memberName, out float value)
        {
            value = 0f;
            if (target == null || string.IsNullOrEmpty(memberName))
                return false;

            Type type = target.GetType();

            FieldInfo field = AccessTools.Field(type, memberName);
            if (field != null && TryObjectToFloat(field.GetValue(target), out value))
                return true;

            PropertyInfo property = AccessTools.Property(type, memberName);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return TryObjectToFloat(property.GetValue(target, null), out value);
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool TryWriteFloatMember(object target, string memberName, float value)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
                return false;

            Type type = target.GetType();

            FieldInfo field = AccessTools.Field(type, memberName);
            if (field != null && !field.IsInitOnly && !field.IsLiteral)
            {
                if (field.FieldType == typeof(float))
                {
                    field.SetValue(target, value);
                    return true;
                }

                if (field.FieldType == typeof(double))
                {
                    field.SetValue(target, (double)value);
                    return true;
                }

                if (field.FieldType == typeof(int))
                {
                    field.SetValue(target, Mathf.RoundToInt(value));
                    return true;
                }
            }

            PropertyInfo property = AccessTools.Property(type, memberName);
            if (
                property != null
                && property.CanWrite
                && property.GetIndexParameters().Length == 0
                && property.GetSetMethod(true) != null
            )
            {
                if (property.PropertyType == typeof(float))
                {
                    property.SetValue(target, value, null);
                    return true;
                }

                if (property.PropertyType == typeof(double))
                {
                    property.SetValue(target, (double)value, null);
                    return true;
                }

                if (property.PropertyType == typeof(int))
                {
                    property.SetValue(target, Mathf.RoundToInt(value), null);
                    return true;
                }
            }

            return false;
        }

        private static DepthMemberCache GetDepthMemberCache(Type type)
        {
            DepthMemberCache cache;
            if (MemberCache.TryGetValue(type, out cache))
                return cache;

            List<FieldInfo> fields = new List<FieldInfo>();
            List<PropertyInfo> properties = new List<PropertyInfo>();

            FieldInfo[] allFields = type.GetFields(InstanceMemberFlags);
            for (int index = 0; index < allFields.Length; index++)
            {
                FieldInfo field = allFields[index];
                if (CanPatchNumericType(field.FieldType) && !field.IsInitOnly && !field.IsLiteral && IsDepthLimitMemberName(field.Name))
                    fields.Add(field);
            }

            PropertyInfo[] allProperties = type.GetProperties(InstanceMemberFlags);
            for (int index = 0; index < allProperties.Length; index++)
            {
                PropertyInfo property = allProperties[index];
                if (
                    CanPatchNumericType(property.PropertyType)
                    && property.CanRead
                    && property.CanWrite
                    && property.GetIndexParameters().Length == 0
                    && property.GetSetMethod(true) != null
                    && IsDepthLimitMemberName(property.Name)
                )
                {
                    properties.Add(property);
                }
            }

            cache = new DepthMemberCache(fields.ToArray(), properties.ToArray());
            MemberCache[type] = cache;
            return cache;
        }

        private static bool TryPatchField(object target, FieldInfo field)
        {
            object rawValue = field.GetValue(target);
            float current;
            if (!TryObjectToFloat(rawValue, out current))
                return false;

            float patched;
            if (!TryBuildPatchedLimit(field.Name, current, out patched))
                return false;

            object patchedValue;
            if (!TryBuildTypedNumericValue(field.FieldType, patched, out patchedValue))
                return false;

            RememberPatchedMember(target, GetMemberPatchKey(field), rawValue, patchedValue);
            field.SetValue(target, patchedValue);
            return true;
        }

        private static bool TryPatchProperty(object target, PropertyInfo property)
        {
            object rawValue = property.GetValue(target, null);
            float current;
            if (!TryObjectToFloat(rawValue, out current))
                return false;

            float patched;
            if (!TryBuildPatchedLimit(property.Name, current, out patched))
                return false;

            object patchedValue;
            if (!TryBuildTypedNumericValue(property.PropertyType, patched, out patchedValue))
                return false;

            RememberPatchedMember(target, GetMemberPatchKey(property), rawValue, patchedValue);
            property.SetValue(target, patchedValue, null);
            return true;
        }

        private static void RestoreDepthObject(object target, string reason)
        {
            if (target == null)
                return;

            DepthObjectPatchData data;
            if (!DepthObjectPatches.TryGetValue(target, out data) || data.Values.Count == 0)
                return;

            try
            {
                Type type = target.GetType();
                DepthMemberCache cache = GetDepthMemberCache(type);
                int restored = 0;

                for (int index = 0; index < cache.Fields.Length; index++)
                {
                    if (TryRestoreField(target, cache.Fields[index], data))
                        restored++;
                }

                for (int index = 0; index < cache.Properties.Length; index++)
                {
                    if (TryRestoreProperty(target, cache.Properties[index], data))
                        restored++;
                }

                if (data.Values.Count == 0)
                    DepthObjectPatches.Remove(target);

                if (restored > 0)
                {
                    Debug.Log(
                        "[LongSubmerged10x] DeepDive restored "
                        + restored
                        + " depth/pressure limits on "
                        + type.Name
                        + " after "
                        + SafeReason(reason)
                        + "."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[LongSubmerged10x] DeepDive restore skipped for "
                    + target.GetType().Name
                    + " after "
                    + SafeReason(reason)
                    + ": "
                    + ex.GetType().Name
                    + ": "
                    + ex.Message
                );
            }
        }

        private static bool TryRestoreField(object target, FieldInfo field, DepthObjectPatchData data)
        {
            string key = GetMemberPatchKey(field);
            DepthOriginalValue originalValue;
            if (!data.Values.TryGetValue(key, out originalValue))
                return false;

            object currentValue = field.GetValue(target);
            if (!ShouldRestoreMemberValue(currentValue, originalValue.PatchedValue))
            {
                data.Values.Remove(key);
                return false;
            }

            field.SetValue(target, originalValue.OriginalValue);
            data.Values.Remove(key);
            return true;
        }

        private static bool TryRestoreProperty(object target, PropertyInfo property, DepthObjectPatchData data)
        {
            string key = GetMemberPatchKey(property);
            DepthOriginalValue originalValue;
            if (!data.Values.TryGetValue(key, out originalValue))
                return false;

            object currentValue = property.GetValue(target, null);
            if (!ShouldRestoreMemberValue(currentValue, originalValue.PatchedValue))
            {
                data.Values.Remove(key);
                return false;
            }

            property.SetValue(target, originalValue.OriginalValue, null);
            data.Values.Remove(key);
            return true;
        }

        private static void RememberPatchedMember(object target, string key, object originalValue, object patchedValue)
        {
            DepthObjectPatchData data;
            if (!DepthObjectPatches.TryGetValue(target, out data))
            {
                data = new DepthObjectPatchData();
                DepthObjectPatches.Add(target, data);
            }

            DepthOriginalValue storedValue;
            if (!data.Values.TryGetValue(key, out storedValue))
            {
                storedValue = new DepthOriginalValue(originalValue, patchedValue);
                data.Values.Add(key, storedValue);
                return;
            }

            storedValue.PatchedValue = patchedValue;
        }

        private static bool ShouldRestoreMemberValue(object currentValue, object patchedValue)
        {
            float current;
            float patched;
            if (TryObjectToFloat(currentValue, out current) && TryObjectToFloat(patchedValue, out patched))
                return Math.Abs(current - patched) <= Epsilon;

            return object.Equals(currentValue, patchedValue);
        }

        private static string GetMemberPatchKey(MemberInfo member)
        {
            return member.MemberType.ToString() + ":" + member.DeclaringType.FullName + ":" + member.Name;
        }

        private static bool TryBuildTypedNumericValue(Type type, float value, out object typedValue)
        {
            typedValue = null;

            if (type == typeof(float))
            {
                typedValue = value;
                return true;
            }

            if (type == typeof(double))
            {
                typedValue = (double)value;
                return true;
            }

            if (type == typeof(int))
            {
                typedValue = Mathf.RoundToInt(value);
                return true;
            }

            return false;
        }

        private static bool TryBuildPatchedLimit(string memberName, float current, out float patched)
        {
            patched = current;

            if (!IsFinite(current) || Math.Abs(current) <= Epsilon)
                return false;

            string name = memberName.ToLowerInvariant();
            float wanted;

            if (IsPressureMemberName(name) && !name.Contains("depth"))
            {
                if (current <= 0f)
                    return false;

                wanted = DepthMetersToAtmospheres(IsCrushMemberName(name) ? CrushDepthMeters : MaxRealCommandDepthMeters);
            }
            else if (IsCrushMemberName(name))
                wanted = GetSignedDepthLimit(current, CrushDepthMeters);
            else
                wanted = GetSignedDepthLimit(current, MaxRealCommandDepthMeters);

            // Never make an existing limit from the game or another mod stricter.
            if (current < 0f)
            {
                if (current <= wanted + Epsilon)
                    return false;
            }
            else if (current >= wanted - Epsilon)
            {
                return false;
            }

            // Avoid changing tiny multipliers/probabilities with ambiguous depth-like names.
            if (Math.Abs(wanted) > 50f && Math.Abs(current) < 1f)
                return false;

            patched = wanted;
            return true;
        }

        private static float GetSignedDepthLimit(float current, float positiveMeters)
        {
            return current < 0f ? -positiveMeters : positiveMeters;
        }

        private static bool IsDepthLimitMemberName(string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
                return false;

            string name = memberName.ToLowerInvariant();

            bool depthLike =
                name.Contains("depth")
                || name.Contains("pressure")
                || name.Contains("atm");

            if (!depthLike)
                return false;

            // Protection : on ne touche pas la profondeur courante du bateau.
            if (
                name.Contains("current")
                || name.Contains("actual")
                || name.Contains("deck")
                || name.Contains("keel")
                || name.Contains("real")
            )
            {
                return false;
            }

            return
                name.Contains("max")
                || name.Contains("maximum")
                || name.Contains("limit")
                || name.Contains("allowed")
                || name.Contains("operational")
                || name.Contains("safe")
                || name.Contains("danger")
                || name.Contains("warning")
                || name.Contains("test")
                || name.Contains("design")
                || IsCrushMemberName(name);
        }

        private static bool IsCrushMemberName(string name)
        {
            return name.Contains("crush")
                || name.Contains("implosion")
                || name.Contains("collapse")
                || name.Contains("destroy")
                || name.Contains("destruct")
                || name.Contains("breakdepth");
        }

        private static bool IsPressureMemberName(string name)
        {
            return name.Contains("pressure") || name.Contains("atm");
        }

        private static float DepthMetersToAtmospheres(float depthMeters)
        {
            return SeaLevelPressureAtmospheres + Mathf.Max(0f, depthMeters) / MetersPerAtmosphere;
        }

        private static float GetDepthStressTier(float depthMeters)
        {
            if (!IsFinite(depthMeters))
                return 0f;

            if (depthMeters > 300f)
                return 6f;

            if (depthMeters > 250f)
                return 5f;

            if (depthMeters > 200f)
                return 4f;

            if (depthMeters > 150f)
                return 3f;

            if (depthMeters > 100f)
                return 2f;

            if (depthMeters > 25f)
                return 1f;

            return 0f;
        }

        private static bool CanPatchNumericType(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(int);
        }

        private static bool TryObjectToFloat(object rawValue, out float value)
        {
            value = 0f;

            if (rawValue is float)
            {
                value = (float)rawValue;
                return true;
            }

            if (rawValue is double)
            {
                value = (float)(double)rawValue;
                return true;
            }

            if (rawValue is int)
            {
                value = (int)rawValue;
                return true;
            }

            return false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void LogPatchedObjectOnce(object target, Type type, int changedCount, string reason)
        {
            if (objectPatchLogCount >= MaxObjectPatchLogs)
                return;

            int id = GetObjectId(target);
            if (ObjectPatchLogIds.Contains(id))
                return;

            ObjectPatchLogIds.Add(id);
            objectPatchLogCount++;

            Debug.Log(
                "[LongSubmerged10x] DeepDive patched "
                + changedCount
                + " depth/pressure limits on "
                + type.Name
                + " after "
                + SafeReason(reason)
                + "."
            );
        }

        private static int GetObjectId(object target)
        {
            UnityEngine.Object unityObject = target as UnityEngine.Object;
            if (unityObject != null)
                return unityObject.GetInstanceID();

            return RuntimeHelpers.GetHashCode(target);
        }

        private static void WarnMissingTypeOnce(string key, string message)
        {
            if (MissingTypeWarnings.Contains(key))
                return;

            MissingTypeWarnings.Add(key);
            Debug.LogWarning("[LongSubmerged10x] DeepDive: " + message);
        }

        private static string SafeReason(string reason)
        {
            return string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }
    }

    internal sealed class DepthMemberCache
    {
        public readonly FieldInfo[] Fields;
        public readonly PropertyInfo[] Properties;

        public DepthMemberCache(FieldInfo[] fields, PropertyInfo[] properties)
        {
            Fields = fields ?? new FieldInfo[0];
            Properties = properties ?? new PropertyInfo[0];
        }
    }

    internal sealed class DepthObjectPatchData
    {
        public readonly Dictionary<string, DepthOriginalValue> Values =
            new Dictionary<string, DepthOriginalValue>();
    }

    internal sealed class DepthOriginalValue
    {
        public readonly object OriginalValue;
        public object PatchedValue;

        public DepthOriginalValue(object originalValue, object patchedValue)
        {
            OriginalValue = originalValue;
            PatchedValue = patchedValue;
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
                // SurfaceSafe 1.4.7 :
                // UBOAT recalcule d'abord sa respiration vanilla.
                // Ensuite seulement, le mod réduit le drain négatif si Mega Oxygène est actif.
                // On ne touche pas aux valeurs nulles/positives utilisées pendant la surface/recharge.
                ValidateOxygenBreathModifierMethod.Invoke(ship, null);
                LongSubmergedRuntimeApplier.ApplyOxygenBreathModifier(ship, reason);
                Debug.Log("[LongSubmerged10x] Oxygen runtime breath modifier applied after " + reason + ".");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    // DonJ : SuperVitesse ne change pas toutes les allures. Je booste seulement les deux derniers crans avant,
    // le plafond du sous-marin joueur et le multiplicateur de propulseur quand ces crans rapides sont actifs.
    internal static class EngineFastSpeedPatcher
    {
        private const float FastSpeedFactor = 8f;
        private const float FastSpeedFuelFactor = 8f;
        private const float PlayerSubmarineMaxSpeed = 45f;
        private const float LegacyDataSheetPlayerSubmarineMaxSpeed = 45f;
        private const float VanillaPlayerSubmarineMaxSpeedFallback = 32.8f;
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
                // DonJ : appele regulierement par PlayerShip.Update/ValidateTargetVelocity.
                // Les propulseurs sont une valeur runtime, donc il faut la remettre quand le joueur change de cran.
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
                // DonJ : les champs moteur sont prives dans UBOAT, donc je passe par reflection.
                // Si une version du jeu renomme un champ, je log une seule alerte et je laisse le moteur vanilla.
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

                float speedFactor = GetEffectiveFastSpeedFactor();
                float fuelFactor = GetEffectiveFastFuelFactor(speedFactor);

                // DonJ : je garde une copie des valeurs originales, puis je recalcule depuis ces bases.
                // Comme ca le slider F10 peut monter/descendre sans empiler les multiplicateurs.
                ApplyTopGearBasePower(forwardPresets, data.ForwardBasePower, speedFactor);
                ApplyTopGearFuelConsumption(forwardPresets, data.ForwardFuelConsumption, fuelFactor);
                ApplyTopGearFloatArray(expectedVelocityPerGear, data.ExpectedVelocityPerGear, speedFactor);
                ApplyTopGearFloatArray(expectedVelocityPerGearUnderwater, data.ExpectedVelocityPerGearUnderwater, speedFactor);

                Debug.Log("[LongSubmerged10x] Fast speed patch applied after " + reason + " with x" + speedFactor + ".");
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

        private static void ApplyTopGearBasePower(Array forwardPresets, float[] originalBasePower, float speedFactor)
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

                BasePowerField.SetValue(preset, originalBasePower[index] * speedFactor);
                forwardPresets.SetValue(preset, index);
            }
        }

        private static void ApplyTopGearFuelConsumption(Array forwardPresets, float[] originalFuelConsumption, float fuelFactor)
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

                FuelConsumptionField.SetValue(preset, originalFuelConsumption[index] * fuelFactor);
                forwardPresets.SetValue(preset, index);
            }
        }

        private static void ApplyTopGearFloatArray(float[] target, float[] original, float speedFactor)
        {
            if (target == null || original == null)
                return;

            int patchCount = Math.Min(FastForwardGearCount, Math.Min(target.Length, original.Length));
            int firstPatchedGear = target.Length - patchCount;

            for (int index = firstPatchedGear; index < target.Length; index++)
                target[index] = original[index] * speedFactor;
        }

        private static void PatchShipVelocityCap(PlayerShip ship, string reason, bool verboseLog)
        {
            if (ship == null || ship.Blueprint == null || ship.Blueprint.Velocity == null)
                return;

            ShipRuntimePatchData data;
            if (!ShipRuntimeData.TryGetValue(ship, out data))
            {
                float originalVelocity = ship.Blueprint.Velocity;
                if (!IsFinite(originalVelocity) || originalVelocity <= 0f)
                    return;

                // DonJ : on cree toujours le modifier, meme si le vieux XLSX a deja mis 45 km/h.
                // Ca permet au slider x1 de revenir a la vitesse normale estimee au lieu de rester a 45.
                Modifier modifier = ship.Blueprint.Velocity.AddDeltaModifier(RuntimeVelocityModifierName, false);
                data = new ShipRuntimePatchData(originalVelocity, modifier);
                ShipRuntimeData.Add(ship, data);
            }

            if (data.VelocityModifier == null)
                return;

            float baseVelocity = GetRuntimeBaseVelocity(data.OriginalVelocity);
            float effectiveSpeedFactor = GetEffectiveFastSpeedFactor();
            float desiredMaxSpeed = baseVelocity * effectiveSpeedFactor;
            float desiredDelta = desiredMaxSpeed - data.OriginalVelocity;

            if (Math.Abs(data.VelocityModifier.Value - desiredDelta) > 0.001f)
                data.VelocityModifier.Value = desiredDelta;

            if (verboseLog)
            {
                Debug.Log(
                    "[LongSubmerged10x] Player ship speed cap patched after "
                    + reason
                    + ": base "
                    + baseVelocity
                    + " km/h, x"
                    + effectiveSpeedFactor
                    + " -> "
                    + desiredMaxSpeed
                    + " km/h."
                );
            }
        }

        private static float GetRuntimeBaseVelocity(float originalVelocity)
        {
            if (!IsFinite(originalVelocity) || originalVelocity <= 0f)
                return 1f;

            // DonJ : compatibilite avec les builds 1.4.7 deja installes.
            // Ces builds ecrivaient 45 km/h dans Entities.xlsx, ce qui empechait x1 d'etre vanilla.
            // Les types joueur vanilla VIIC/IXC utilises par le mod sont a 32.8 km/h dans les Data Sheets.
            if (Math.Abs(originalVelocity - LegacyDataSheetPlayerSubmarineMaxSpeed) <= 0.05f)
                return VanillaPlayerSubmarineMaxSpeedFallback;

            return originalVelocity;
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
            float appliedFactor = fastForwardGear ? GetEffectiveFastSpeedFactor() : 1f;
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

        private static float GetEffectiveFastSpeedFactor()
        {
            if (!LongSubmergedRuntimeSettings.SuperSpeed)
                return 1f;

            return LongSubmergedRuntimeSettings.ClampSpeedFactor(LongSubmergedRuntimeSettings.SpeedFactor);
        }

        private static float GetEffectiveFastFuelFactor(float speedFactor)
        {
            if (!LongSubmergedRuntimeSettings.SuperSpeed || speedFactor <= 1.0001f)
                return 1f;

            float referenceSpeedFactor = Math.Max(1.0001f, FastSpeedFactor);
            float normalized = (speedFactor - 1f) / (referenceSpeedFactor - 1f);
            return Math.Max(1f, 1f + normalized * (FastSpeedFuelFactor - 1f));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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

    // DonJ : hooks Harmony courts et delegues. Chaque hook appelle une methode robuste du runtime,
    // ce qui limite le risque de casser UBOAT si un objet arrive partiellement initialise.
    [HarmonyPatch(typeof(PlayerShip), "Awake")]
    internal static class PlayerShipAwakePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "PlayerShip.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "OnAfterDeserialize")]
    internal static class PlayerShipOnAfterDeserializePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "PlayerShip.OnAfterDeserialize");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Update")]
    internal static class PlayerShipUpdatePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            // DonJ: I keep the battery full after the submarine frame.
            // Resource.UpdateAmount is also guarded below, but only for PlayerShip.Energy.
            LongSubmergedRuntimeApplier.ApplyBatteryResource(__instance, "PlayerShip.Update");

            // DonJ : la vitesse est un etat runtime du bateau et du cran moteur actif.
            // On la remet ici pour que le slider F10 et les changements de cran soient visibles immediatement.
            EngineFastSpeedPatcher.UpdatePlayerShipRuntime(__instance, "PlayerShip.Update");

            DeepDiveRuntimePatcher.UpdatePlayerShipRuntime(__instance, "PlayerShip.Update");

            SuperStealthRuntimePatcher.ApplyPlayerShip(__instance, "PlayerShip.Update");
        }
    }

    [HarmonyPatch(typeof(Resource), "UpdateAmount")]
    internal static class ResourceUpdateAmountBatteryPatch
    {
        private static bool Prefix(Resource __instance)
        {
            if (LongSubmergedRuntimeApplier.TryFreezeInfiniteBatteryResource(
                __instance,
                "Resource.UpdateAmount.Prefix"
            ))
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ResourceGUI), "GetTooltipContents")]
    internal static class ResourceGuiGetTooltipContentsPatch
    {
        private static bool Prefix(ResourceGUI __instance, ref string __result)
        {
            Resource resource = LongSubmergedRuntimeApplier.GetResourceFromGui(__instance);
            if (!LongSubmergedRuntimeApplier.ShouldSuppressBatteryDepletionUi(resource, "ResourceGUI.GetTooltipContents"))
                return true;

            __result = LongSubmergedRuntimeApplier.BuildInfiniteBatteryTooltip(resource);
            return false;
        }
    }

    [HarmonyPatch(typeof(ResourceGUI), "UpdateDisplayedValue")]
    internal static class ResourceGuiUpdateDisplayedValuePatch
    {
        private static void Prefix(ResourceGUI __instance)
        {
            LongSubmergedRuntimeApplier.TryMaintainBatteryResource(
                LongSubmergedRuntimeApplier.GetResourceFromGui(__instance),
                "ResourceGUI.UpdateDisplayedValue"
            );
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "Awake")]
    internal static class InteriorLightingPlayerShipInteriorLightingAwakePatch
    {
        private static void Postfix(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting __instance)
        {
            InteriorLightingColorPatcher.ApplyInteriorLighting(
                __instance,
                "PlayerShipInteriorLighting.Awake",
                false
            );
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "Start")]
    internal static class InteriorLightingPlayerShipInteriorLightingStartPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting __instance)
        {
            InteriorLightingColorPatcher.ApplyInteriorLighting(
                __instance,
                "PlayerShipInteriorLighting.Start",
                false
            );
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting), "ApplyLightControllersPresets")]
    internal static class InteriorLightingPlayerShipInteriorLightingApplyPresetsPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Effects.PlayerShipInteriorLighting __instance)
        {
            InteriorLightingColorPatcher.ApplyInteriorLighting(
                __instance,
                "PlayerShipInteriorLighting.ApplyLightControllersPresets",
                false
            );
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.LightController), "UpdatePresets", new Type[] { typeof(float[]), typeof(float[]) })]
    internal static class InteriorLightingLightControllerUpdatePresetsPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Effects.LightController __instance)
        {
            InteriorLightingColorPatcher.ApplyLightController(__instance, "LightController.UpdatePresets");
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FillLight), "UpdatePresets", new Type[] { typeof(float[]) })]
    internal static class InteriorLightingFillLightUpdatePresetsPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Effects.FillLight __instance)
        {
            InteriorLightingColorPatcher.ApplyFillLight(__instance, "FillLight.UpdatePresets");
        }
    }

    [HarmonyPatch(typeof(DepletingResourceNotification), "DoUpdate")]
    internal static class DepletingResourceNotificationDoUpdatePatch
    {
        private static bool Prefix(DepletingResourceNotification __instance, ref float __result)
        {
            Resource resource = LongSubmergedRuntimeApplier.GetResourceFromDepletingNotification(__instance);
            if (!LongSubmergedRuntimeApplier.ShouldSuppressBatteryDepletionUi(resource, "DepletingResourceNotification.DoUpdate"))
                return true;

            __result = 5f;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class DeepDivePlayerShipTargetDepthSetterPatch
    {
        private static MethodBase TargetMethod()
        {
            return DeepDiveRuntimePatcher.FindPlayerShipSetTargetDepthMethod();
        }

        private static void Prefix(ref float __0)
        {
            DeepDiveRuntimePatcher.ScaleTargetDepthCommand(ref __0, "PlayerShip.SetTargetDepth");
        }
    }

    [HarmonyPatch]
    internal static class DeepDiveHullCrushControllerDoUpdatePatch
    {
        private static MethodBase TargetMethod()
        {
            return DeepDiveRuntimePatcher.FindMethodOnKnownType("HullCrushController", "DoUpdate");
        }

        private static bool Prefix(object __instance, ref float __result)
        {
            return DeepDiveRuntimePatcher.ShouldRunHullCrushDoUpdate(__instance, ref __result, "HullCrushController.DoUpdate");
        }
    }

    [HarmonyPatch]
    internal static class DeepDivePlayerShipUpdateStressAndDisciplineGainPatch
    {
        private static MethodBase TargetMethod()
        {
            return DeepDiveRuntimePatcher.FindPlayerShipUpdateStressAndDisciplineGainMethod();
        }

        private static void Postfix(PlayerShip __instance)
        {
            DeepDiveRuntimePatcher.ApplyDepthStressModifier(__instance, "PlayerShip.UpdateStressAndDisciplineGain");
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

    [HarmonyPatch(typeof(PlayerShip), "ValidateOxygenBreathModifier")]
    internal static class PlayerShipValidateOxygenBreathModifierPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyOxygenBreathModifier(__instance, "PlayerShip.ValidateOxygenBreathModifier");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SavesManagerOnLoaded")]
    internal static class PlayerShipSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShip __instance, Queue<Action> __0)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "SavesManagerOnLoaded");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Crew_Added")]
    internal static class PlayerShipCrewAddedPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "Crew_Added");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "Crew_Removed")]
    internal static class PlayerShipCrewRemovedPatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            LongSubmergedRuntimeApplier.ApplyPlayerShip(__instance, "Crew_Removed");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "Awake")]
    internal static class PlayerShipEngineAwakePatch
    {
        private static void Postfix(PlayerShipEngine __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "PlayerShipEngine.Awake");
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "OnAfterDeserialize")]
    internal static class PlayerShipEngineOnAfterDeserializePatch
    {
        private static void Postfix(PlayerShipEngine __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "PlayerShipEngine.OnAfterDeserialize");
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.OnAfterDeserialize");
        }
    }

    [HarmonyPatch(typeof(PlayerShipEngine), "SavesManagerOnLoaded")]
    internal static class PlayerShipEngineSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShipEngine __instance, Queue<Action> __0)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "PlayerShipEngine.SavesManagerOnLoaded");
            EngineFastSpeedPatcher.PatchEngine(__instance, "PlayerShipEngine.SavesManagerOnLoaded");
        }
    }

    [HarmonyPatch(typeof(AccumulatorsUpgrade), "Start")]
    internal static class AccumulatorsUpgradeStartPatch
    {
        private static void Postfix(AccumulatorsUpgrade __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "AccumulatorsUpgrade.Start");
        }
    }

    [HarmonyPatch(typeof(DivingPlanesStation), "Awake")]
    internal static class DivingPlanesStationAwakePatch
    {
        private static void Postfix(DivingPlanesStation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "DivingPlanesStation.Awake");
            DeepDiveRuntimePatcher.ApplyDepthObject(__instance, "DivingPlanesStation.Awake");
        }
    }

    [HarmonyPatch(typeof(DivingPlanesStation), "UpdateModifiers")]
    internal static class DivingPlanesStationUpdateModifiersPatch
    {
        private static void Postfix(DivingPlanesStation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "DivingPlanesStation.UpdateModifiers");
            DeepDiveRuntimePatcher.ApplyDepthObject(__instance, "DivingPlanesStation.UpdateModifiers");
        }
    }

    [HarmonyPatch(typeof(Gyrocompass), "ApplyModifiers")]
    internal static class GyrocompassApplyModifiersPatch
    {
        private static void Postfix(Gyrocompass __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "Gyrocompass.ApplyModifiers");
        }
    }

    [HarmonyPatch(typeof(TrimPump), "OnEnable")]
    internal static class TrimPumpOnEnablePatch
    {
        private static void Postfix(TrimPump __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "TrimPump.OnEnable");
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "Start")]
    internal static class StoredTorpedoStartPatch
    {
        private static void Postfix(StoredTorpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyStoredTorpedo(__instance, "StoredTorpedo.Start");
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "ApplyWarmUpModifier")]
    internal static class StoredTorpedoApplyWarmUpModifierPatch
    {
        private static void Postfix(StoredTorpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyStoredTorpedo(__instance, "StoredTorpedo.ApplyWarmUpModifier");
        }
    }

    [HarmonyPatch(typeof(Torpedo), "Awake")]
    internal static class TorpedoAwakePatch
    {
        private static void Postfix(Torpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyLaunchedTorpedo(__instance, "Torpedo.Awake");
        }
    }

    [HarmonyPatch(typeof(Torpedo), "FixedUpdate")]
    internal static class TorpedoFixedUpdatePatch
    {
        private static void Prefix(Torpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyLaunchedTorpedo(__instance, "Torpedo.FixedUpdate");
        }
    }

    [HarmonyPatch(typeof(Torpedo), "Detonate")]
    internal static class TorpedoDetonatePatch
    {
        private static void Prefix(Torpedo __instance)
        {
            LongSubmergedRuntimeApplier.ApplyLaunchedTorpedo(__instance, "Torpedo.Detonate");
        }
    }

    [HarmonyPatch(typeof(Entity), "UpdateDetectability")]
    internal static class SuperStealthEntityUpdateDetectabilityPatch
    {
        private static void Postfix(Entity __instance)
        {
            SuperStealthRuntimePatcher.ApplyEntity(__instance, "Entity.UpdateDetectability");
        }
    }

    [HarmonyPatch(typeof(AirCompressor), "OnEnable")]
    internal static class SuperStealthAirCompressorOnEnablePatch
    {
        private static void Postfix(AirCompressor __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "AirCompressor.OnEnable");
        }
    }

    [HarmonyPatch(typeof(AirCompressor), "OnDisable")]
    internal static class SuperStealthAirCompressorOnDisablePatch
    {
        private static void Postfix(AirCompressor __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "AirCompressor.OnDisable");
        }
    }

    [HarmonyPatch(typeof(Ventilation), "OnEnable")]
    internal static class SuperStealthVentilationOnEnablePatch
    {
        private static void Postfix(Ventilation __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "Ventilation.OnEnable");
        }
    }

    [HarmonyPatch(typeof(Ventilation), "OnDisable")]
    internal static class SuperStealthVentilationOnDisablePatch
    {
        private static void Postfix(Ventilation __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "Ventilation.OnDisable");
        }
    }

    [HarmonyPatch(typeof(Propeller), "set_Power")]
    internal static class SuperStealthPropellerPowerPatch
    {
        private static void Postfix(Propeller __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "Propeller.set_Power");
        }
    }

    [HarmonyPatch(typeof(Propeller), "set_PowerMultiplier")]
    internal static class SuperStealthPropellerPowerMultiplierPatch
    {
        private static void Postfix(Propeller __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "Propeller.set_PowerMultiplier");
        }
    }

    [HarmonyPatch(typeof(Snorkel), "Update")]
    internal static class SuperStealthSnorkelUpdatePatch
    {
        private static void Postfix(Snorkel __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "Snorkel.Update");
        }
    }

    [HarmonyPatch(typeof(Periscope), "Update")]
    internal static class SuperStealthPeriscopeUpdatePatch
    {
        private static void Postfix(Periscope __instance)
        {
            SuperStealthRuntimePatcher.ApplyEquipment(__instance, "Periscope.Update");
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorHullAddDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Hull), "AddDamage", HeavyArmorRuntimePatcher.AddDamageParameterTypes);
        }

        private static void Prefix(
            Hull __instance,
            ref float damage,
            ref float flawProbabilityFactor,
            ref float fireChance,
            out HeavyArmorDamageScaleState __state
        )
        {
            __state = HeavyArmorRuntimePatcher.TryScalePlayerEquipmentDamage(
                __instance,
                ref damage,
                ref flawProbabilityFactor,
                ref fireChance
            );
            HeavyArmorRuntimePatcher.BeginDamageScaleScope(__state);
        }

        private static void Postfix(HeavyArmorDamageScaleState __state, ref float __result)
        {
            HeavyArmorRuntimePatcher.RestoreComponentDistributionBudget(__state, ref __result);
        }

        private static void Finalizer(HeavyArmorDamageScaleState __state)
        {
            HeavyArmorRuntimePatcher.EndDamageScaleScope(__state);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorEquipmentAddDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Equipment), "AddDamage", HeavyArmorRuntimePatcher.AddDamageParameterTypes);
        }

        private static void Prefix(
            Equipment __instance,
            ref float damage,
            ref float flawProbabilityFactor,
            ref float fireChance,
            out HeavyArmorDamageScaleState __state
        )
        {
            __state = HeavyArmorRuntimePatcher.TryScalePlayerEquipmentDamage(
                __instance,
                ref damage,
                ref flawProbabilityFactor,
                ref fireChance
            );
            HeavyArmorRuntimePatcher.BeginDamageScaleScope(__state);
        }

        private static void Postfix(HeavyArmorDamageScaleState __state, ref float __result)
        {
            HeavyArmorRuntimePatcher.RestoreComponentDistributionBudget(__state, ref __result);
        }

        private static void Finalizer(HeavyArmorDamageScaleState __state)
        {
            HeavyArmorRuntimePatcher.EndDamageScaleScope(__state);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorEquipmentAddWaterDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Equipment), "AddWaterDamage", HeavyArmorRuntimePatcher.AddWaterDamageParameterTypes);
        }

        private static void Prefix(Equipment __instance, ref float damage, out HeavyArmorDamageScaleState __state)
        {
            __state = HeavyArmorRuntimePatcher.TryScalePlayerWaterDamage(__instance, ref damage);
            HeavyArmorRuntimePatcher.BeginDamageScaleScope(__state);
        }

        private static void Finalizer(HeavyArmorDamageScaleState __state)
        {
            HeavyArmorRuntimePatcher.EndDamageScaleScope(__state);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorPlayableCharacterAddDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PlayableCharacter), "AddDamage", HeavyArmorRuntimePatcher.AddDamageParameterTypes);
        }

        private static void Prefix(PlayableCharacter __instance, ref float damage)
        {
            HeavyArmorRuntimePatcher.ScalePlayerCharacterDamage(__instance, ref damage);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorDamageUtilityDoApplyDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(DamageUtility),
                "DoApplyDamage",
                HeavyArmorRuntimePatcher.DamageUtilityDoApplyDamageParameterTypes
            );
        }

        private static void Prefix(Ship target, ref float crewDamage)
        {
            HeavyArmorRuntimePatcher.ScalePlayerCrewDamage(target, ref crewDamage);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorDamageUtilityApplyDamageToComponentsPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(DamageUtility),
                "ApplyDamageToComponents",
                HeavyArmorRuntimePatcher.DamageUtilityApplyDamageToComponentsParameterTypes
            );
        }

        private static void Prefix(out bool __state)
        {
            __state = HeavyArmorRuntimePatcher.BeginComponentDamageDistributionScope();
        }

        private static void Finalizer(bool __state)
        {
            HeavyArmorRuntimePatcher.EndComponentDamageDistributionScope(__state);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorApplyWaterDamageToPlayerShipDoDamageTickPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ApplyWaterDamageToPlayerShip), "DoDamageTick", new Type[] { });
        }

        private static bool Prefix(ApplyWaterDamageToPlayerShip __instance, out bool __state)
        {
            __state = HeavyArmorRuntimePatcher.BeginPressureWaterDamageScope();
            return !DeepDiveRuntimePatcher.ShouldSkipPressureWaterDamageTick(
                __instance,
                "ApplyWaterDamageToPlayerShip.DoDamageTick"
            );
        }

        private static void Finalizer(bool __state)
        {
            HeavyArmorRuntimePatcher.EndPressureWaterDamageScope(__state);
        }
    }

    [HarmonyPatch]
    internal static class HeavyArmorHullEffectsRendererApplyImpactPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(HullEffectsRenderer),
                "ApplyImpact",
                HeavyArmorRuntimePatcher.HullEffectsApplyImpactParameterTypes
            );
        }

        private static void Prefix(HullEffectsRenderer __instance, ref float intensity)
        {
            HeavyArmorRuntimePatcher.ScalePlayerHullImpact(__instance, ref intensity);
        }
    }
}
