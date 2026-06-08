using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Scene.Entities;
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

    [HarmonyPatch(typeof(PlayerShip), "Awake")]
    internal static class PlayerShipAwakePatch
    {
        private static void Postfix(PlayerShip __instance)
        {
            OxygenBreathRecalculator.Recalculate(__instance, "PlayerShip.Awake");
        }
    }

    [HarmonyPatch(typeof(PlayerShip), "SavesManagerOnLoaded")]
    internal static class PlayerShipSavesManagerOnLoadedPatch
    {
        private static void Postfix(PlayerShip __instance, Queue<Action> __0)
        {
            OxygenBreathRecalculator.Recalculate(__instance, "SavesManagerOnLoaded");
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
}
