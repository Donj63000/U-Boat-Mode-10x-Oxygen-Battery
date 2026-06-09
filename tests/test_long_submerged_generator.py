from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from openpyxl import Workbook, load_workbook

import build_uboat_long_submerged_mod as generator


def make_general_workbook(path: Path, oxygen_value: float) -> None:
    wb = Workbook()
    ws = wb.active
    ws.title = "Settings"

    rows = [
        ["/Discipline", "Normal", "Hard", "Very Hard"],
        ["Underwater Discipline Loss", 0.000015, 0.000016, 0.000017],
        ["Fatigue - Per Day", -0.0000021, -0.0000022, -0.0000023],
        ["Fatigue - Max Penalty", -0.00012, -0.000155, -0.0002],
        [None, None, None, None],
        ["/Resources", "Value", None, None],
        ["Oxygen Consumption Per Character", oxygen_value, None, None],
        ["Energy Base Scale", 0.32, None, None],
    ]

    for row in rows:
        ws.append(row)

    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)


def make_entities_workbook(path: Path) -> None:
    wb = Workbook()
    ws = wb.active
    ws.title = "Equipment"

    headers = [f"Column {i}" for i in range(1, 16)] + ["Parameters"]
    headers[0] = "Id"
    headers[1] = "Name"
    ws.append(headers)

    def append_equipment(row_id: str, row_name: str, parameters: str) -> None:
        row = [None] * 16
        row[0] = row_id
        row[1] = row_name
        row[15] = parameters
        ws.append(row)

    append_equipment("Accumulators I", "Accumulators, Electric Engines Upgrade", "EnergyCapacityGain=25%")
    append_equipment("Electric Engines", "Engines, Electric Engines", "EnergyUsage = 0.000035, Noise = 0.52")
    append_equipment("Diesel Engines", "Engines, Diesel Engines", "EnergyUsage = -1.0, Noise = 0.7")
    append_equipment("Atmosphere Tank", "Player U-boat Atmosphere Air", "AirCapacity = 100")
    append_equipment("Ventilation", "Ventilation", "EnergyUsage = 0.0001, OxygenGain = 0.00011, RegenerationLimit = 0.5")
    append_equipment(
        "G7a Torpedo T1 - Pi1",
        "Torpedo",
        (
            "Range1 = 5000, Speed1 = 22.63555, DudChance = 0.19, "
            "Damage = 7.0, CrewDamage = 0.8, DamageRadius = 6.5, "
            "DamageEffectsRadius = 7.0, DamageEffectsIntensity = 1.0, "
            "MagneticExplosionOnArm = 0.1, MagneticExplosionAfterArm = 0.005, "
            "MagneticExplosionFail = 0.1"
        ),
    )
    append_equipment("Bow Torpedo Launcher", "Torpedo Launcher", "ReloadTime = 180, AimPerformance = 0.55")

    ws_types = wb.create_sheet("Types")
    ws_types.append([None, None, None, "Displacement (t)"])
    ws_types.append(["/", "Category", "Speed (km/h)", "Standard", "Full"])
    ws_types.append(["Type VIIC", "Submarine", 32.8, 769, 871])
    ws_types.append(["Type VIIC (Player)", "Submarine", 32.8, 769, 871])
    ws_types.append(["F-Class", "Destroyer", 65.7, 1428, 1970])

    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)


def make_type_ix_dlc_entities_workbook(path: Path) -> None:
    wb = Workbook()
    ws_types = wb.active
    ws_types.title = "Types"
    ws_types.append([None, None, None, "Displacement (t)"])
    ws_types.append(["/", "Category", "Speed (km/h)", "Standard", "Full"])
    ws_types.append(["Type IXC", "Submarine", 32.8, 1120, 1232])
    ws_types.append(["Type IXC (Player)", "Submarine", 32.8, 1120, 1232])

    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)


def make_crew_workbook(path: Path) -> None:
    wb = Workbook()
    ws = wb.active
    ws.title = "Statements"

    ws.append(["/", "Tags", "Queue Behaviour", "Queue Priority", "Show on Portrait", "Ignore Injuries", "Spatial Blend", "Spatial Blend (FPP)", "Volume", "Min Distance", "Max Distance"])
    ws.append(["Voice 426", "Air Quality Down To 25%", "Queue", -1, True, False, 0.84, 0.84, 1, 1, 100000])

    path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(path)


def find_row_by_id(ws, row_id: str) -> int:
    for row in range(1, ws.max_row + 1):
        if ws.cell(row, 1).value == row_id:
            return row
    raise AssertionError(f"Ligne introuvable : {row_id}")


class LongSubmergedGeneratorTests(unittest.TestCase):
    def test_generator_keeps_ventilation_vanilla_and_patches_air_capacity(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            uboat_root = tmp_path / "UBOAT"
            data_sheets = uboat_root / "UBOAT_Data" / "Data Sheets"
            out_mod = tmp_path / "LongSubmerged10x"

            make_general_workbook(data_sheets / "General.xlsx", -0.000009)
            make_general_workbook(data_sheets / "Realistic Travel" / "General.xlsx", -0.00000133)
            make_entities_workbook(data_sheets / "Entities.xlsx")
            make_type_ix_dlc_entities_workbook(
                uboat_root
                / "UBOAT_Data"
                / "StreamingAssets"
                / "Packages"
                / "uboat.dlc.type-ix"
                / "Data Sheets"
                / "Entities.xlsx"
            )
            make_crew_workbook(data_sheets / "Crew.xlsx")

            exit_code = generator.main(
                [
                    "--uboat",
                    str(uboat_root),
                    "--out",
                    str(out_mod),
                    "--force",
                    "--game-version",
                    "2026.1 Patch 20",
                ]
            )

            self.assertEqual(exit_code, 0)

            manifest = json.loads((out_mod / "Manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(manifest["version"], "1.4.2")
            self.assertEqual(manifest["assemblyName"], "LongSubmerged10xPatch_1_4_2")
            self.assertIn("Reflection", manifest["permissions"])
            self.assertIn("2026.1 Patch 20", manifest["supportedGameVersions"])
            self.assertFalse((out_mod / "Data Sheets" / "Crew.xlsx").exists())

            runtime_patch = out_mod / "Source" / "LongSubmergedRuntimePatch.cs"
            runtime_patch_text = runtime_patch.read_text(encoding="utf-8")
            self.assertIn("IUserMod", runtime_patch_text)
            self.assertIn("DonJ : point d'entree du runtime UBOAT", runtime_patch_text)
            self.assertIn("DonJ : vrai menu Unity UI", runtime_patch_text)
            self.assertIn("DonJ : coeur gameplay du mod", runtime_patch_text)
            self.assertIn("DonJ : le cran Batterie 100", runtime_patch_text)
            self.assertIn("DonJ : les torpilles sont reglees au runtime", runtime_patch_text)
            self.assertIn("DonJ : SuperVitesse ne change pas toutes les allures", runtime_patch_text)
            self.assertIn("DonJ : hooks Harmony courts et delegues", runtime_patch_text)
            self.assertIn("ValidateOxygenBreathModifier", runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShip), "SavesManagerOnLoaded")', runtime_patch_text)
            self.assertIn("private const float FastSpeedFactor = 3.5f;", runtime_patch_text)
            self.assertIn("private const float FastSpeedFuelFactor = 8f;", runtime_patch_text)
            self.assertIn("private const float PlayerSubmarineMaxSpeed = 45f;", runtime_patch_text)
            self.assertIn("private const int FastForwardGearCount = 2;", runtime_patch_text)
            self.assertIn("EngineFastSpeedPatcher", runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShip), "OnAfterDeserialize")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShip), "ValidateTargetVelocity")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShipEngine), "Awake")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShipEngine), "OnAfterDeserialize")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShipEngine), "SavesManagerOnLoaded")', runtime_patch_text)
            self.assertIn("expectedVelocityPerGear", runtime_patch_text)
            self.assertIn("expectedVelocityPerGearUnderwater", runtime_patch_text)
            self.assertIn("basePower", runtime_patch_text)
            self.assertIn("fuelConsumptionInLitersPerHour", runtime_patch_text)
            self.assertIn("PowerMultiplier", runtime_patch_text)
            self.assertIn("AddDeltaModifier", runtime_patch_text)
            self.assertIn("LongSubmergedMenuController", runtime_patch_text)
            self.assertIn("KeyCode.F10", runtime_patch_text)
            self.assertNotIn("KeyCode.F11", runtime_patch_text)
            self.assertIn("MenuKey = KeyCode.F10", runtime_patch_text)
            self.assertIn('private const string RuntimeVersion = "1.4.2";', runtime_patch_text)
            self.assertIn("Runtime patch loaded v", runtime_patch_text)
            self.assertIn("LongSubmergedRuntimePatcher.PatchSafely", runtime_patch_text)
            self.assertIn("Harmony patch active", runtime_patch_text)
            self.assertIn("Harmony patch skipped", runtime_patch_text)
            self.assertNotIn("PatchAll()", runtime_patch_text)
            self.assertIn("Runtime Unity UI menu controller ready on F10", runtime_patch_text)
            self.assertIn("using UnityEngine.UI;", runtime_patch_text)
            self.assertIn("Canvas", runtime_patch_text)
            self.assertIn("RenderMode.ScreenSpaceOverlay", runtime_patch_text)
            self.assertIn("CanvasScaler", runtime_patch_text)
            self.assertIn("GraphicRaycaster", runtime_patch_text)
            self.assertIn("Button", runtime_patch_text)
            self.assertIn("Toggle", runtime_patch_text)
            self.assertIn("Slider", runtime_patch_text)
            self.assertIn("CreateFactorSlider", runtime_patch_text)
            self.assertIn("OnFactorSliderChanged", runtime_patch_text)
            self.assertIn("Par defaut", runtime_patch_text)
            self.assertIn("SuperVitesse", runtime_patch_text)
            self.assertIn("Vitesses rapides", runtime_patch_text)
            self.assertNotIn("private void OnGUI", runtime_patch_text)
            self.assertNotIn("GUI.Window", runtime_patch_text)
            self.assertNotIn("GUILayout", runtime_patch_text)
            self.assertIn("PlayerPrefs", runtime_patch_text)
            self.assertIn("MegaBattery", runtime_patch_text)
            self.assertIn("MegaOxygen", runtime_patch_text)
            self.assertIn("SuperSpeed", runtime_patch_text)
            self.assertIn("MegaTorpedoes", runtime_patch_text)
            self.assertIn("BatteryFactor", runtime_patch_text)
            self.assertIn("OxygenFactor", runtime_patch_text)
            self.assertIn("SpeedFactor", runtime_patch_text)
            self.assertIn("TorpedoFactor", runtime_patch_text)
            self.assertIn("MinRuntimeFactor = 1f", runtime_patch_text)
            self.assertIn("MaxRuntimeFactor = 100f", runtime_patch_text)
            self.assertIn("ResetToDefaults", runtime_patch_text)
            self.assertIn("RuntimeSettingsVersion = 7", runtime_patch_text)
            self.assertIn("RunBatteryMaintenanceTick", runtime_patch_text)
            self.assertIn("ApplyBatteryObject", runtime_patch_text)
            self.assertIn("ApplyBatteryResource", runtime_patch_text)
            self.assertIn("MaintainBatteryRuntime", runtime_patch_text)
            self.assertIn("ApplyBatteryConsumers", runtime_patch_text)
            self.assertIn("ApplyGenericBatteryConsumers", runtime_patch_text)
            self.assertIn("GenericEnergyUsageFieldCache", runtime_patch_text)
            self.assertIn("IsEnergyUsageMemberName", runtime_patch_text)
            self.assertIn("ResourceGuiGetTooltipContentsPatch", runtime_patch_text)
            self.assertIn("ResourceGuiUpdateDisplayedValuePatch", runtime_patch_text)
            self.assertIn("DepletingResourceNotificationDoUpdatePatch", runtime_patch_text)
            self.assertIn("ShouldSuppressBatteryDepletionUi", runtime_patch_text)
            self.assertIn("ApplyBatteryRuntimeToResource", runtime_patch_text)
            self.assertIn("ApplyNuclearBatteryCapacityOverride", runtime_patch_text)
            self.assertIn("RuntimeNuclearBatteryCapacityModifierName", runtime_patch_text)
            self.assertIn("NuclearBatteryCapacityFloor = 1000000000f", runtime_patch_text)
            self.assertIn("NuclearBatteryCapacityDeltaData", runtime_patch_text)
            self.assertIn("NuclearBatteryCapacityLoggedResourceIds", runtime_patch_text)
            self.assertIn("Mega Batterie nuclear capacity active", runtime_patch_text)
            self.assertIn("ClampBatteryAmountToCapacity", runtime_patch_text)
            self.assertIn("SetDelta", runtime_patch_text)
            self.assertIn("BuildInfiniteBatteryTooltip", runtime_patch_text)
            self.assertIn("Mega Batterie : batterie infinie active.", runtime_patch_text)
            self.assertIn("MaintainInfiniteBatteryCharge", runtime_patch_text)
            self.assertIn("IsInfiniteBatteryRuntimeActive", runtime_patch_text)
            self.assertIn("TryUpdateBatteryResourceAmount", runtime_patch_text)
            self.assertIn("ResourcePlayerShipField", runtime_patch_text)
            self.assertIn("return object.ReferenceEquals(owner.Energy, resource);", runtime_patch_text)
            self.assertIn("energy.Amount = capacity", runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(Resource), "UpdateAmount")', runtime_patch_text)
            self.assertIn("RuntimeBatteryGainModifierName", runtime_patch_text)
            self.assertIn("ApplyBatteryGainModifiers", runtime_patch_text)
            self.assertIn("ApplyBatteryGainParameter", runtime_patch_text)
            self.assertIn("BatteryGainDeltaData", runtime_patch_text)
            self.assertIn("ParameterDeltaPatchData", runtime_patch_text)
            self.assertIn("GetValueExcludingModifier", runtime_patch_text)
            self.assertIn("energy.GainSandboxTimeScale", runtime_patch_text)
            self.assertIn("Mega Batterie infinite gain guard active", runtime_patch_text)
            self.assertIn("1 = vanilla, 4 = x4, 99 = x99", runtime_patch_text)
            self.assertIn("baseValue = parameter.GetValueExcludingModifier(RuntimeScaleModifierName)", runtime_patch_text)
            self.assertNotIn("SetAmountQuiet", runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(PlayerShip), "Update")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(AirCompressor), "EnergyUsage_Changed")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(Ventilation), "OnEnable")', runtime_patch_text)
            self.assertIn("ApplyDirectEnergyGainModifier", runtime_patch_text)
            self.assertIn("private const float OxygenVanillaRestoreFactor = 1800f;", runtime_patch_text)
            self.assertIn("GetEffectiveOxygenDataFactor", runtime_patch_text)
            self.assertIn("GetEffectiveBatteryCapacityScale", runtime_patch_text)
            self.assertIn("GetEffectiveBatteryEnergyUsageScale", runtime_patch_text)
            self.assertIn("ApplyStoredTorpedo", runtime_patch_text)
            self.assertIn("ApplyLaunchedTorpedo", runtime_patch_text)
            self.assertIn("ApplyLockedTargetGuidance", runtime_patch_text)
            self.assertIn("RestoreLockedTargetGuidance", runtime_patch_text)
            self.assertIn("PredictLockedTargetPoint", runtime_patch_text)
            self.assertIn("TryForceLockedTargetDetonation", runtime_patch_text)
            self.assertIn("TorpedoGuidanceLeadSeconds = 4f", runtime_patch_text)
            self.assertIn("TorpedoGuidanceMinimumDetonationDistance = 20f", runtime_patch_text)
            self.assertIn("TorpedoGuidanceMaximumDetonationDistance = 80f", runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(Torpedo), "FixedUpdate")', runtime_patch_text)
            self.assertIn("torpedo.GyroAngle = float.NaN", runtime_patch_text)
            self.assertIn("torpedo.TargetPosition = targetPoint", runtime_patch_text)
            self.assertIn("TorpedoHomingTargetField", runtime_patch_text)
            self.assertIn("TorpedoDoExplosionHitMethod.Invoke", runtime_patch_text)
            self.assertIn("TorpedoDetonateMethod.Invoke", runtime_patch_text)
            self.assertIn("ForcingDetonation", runtime_patch_text)
            self.assertIn("GetEffectiveTorpedoFactor", runtime_patch_text)
            self.assertIn("IsMegaTorpedoRuntimeActive", runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(AccumulatorsUpgrade), "Start")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(StoredTorpedo), "Start")', runtime_patch_text)
            self.assertIn('HarmonyPatch(typeof(Torpedo), "Detonate")', runtime_patch_text)
            self.assertIn("private const float TorpedoDamageScale = 10f;", runtime_patch_text)
            self.assertIn("private const float TorpedoCrewDamageScale = 10f;", runtime_patch_text)
            self.assertIn("private const float TorpedoExplosionRadiusScale = 10f;", runtime_patch_text)
            self.assertIn("private const float TorpedoExplosionIntensityScale = 10f;", runtime_patch_text)
            self.assertIn("private const bool PerfectTorpedoReliability = true;", runtime_patch_text)
            self.assertIn("private const bool DefaultMegaTorpedoes = true;", runtime_patch_text)
            self.assertIn("GetEffectiveFastSpeedFactor", runtime_patch_text)
            self.assertIn("GetEffectiveFastFuelFactor", runtime_patch_text)

            root_ws = load_workbook(out_mod / "Data Sheets" / "General.xlsx", data_only=False)["Settings"]
            realistic_ws = load_workbook(out_mod / "Data Sheets" / "Realistic Travel" / "General.xlsx", data_only=False)["Settings"]

            root_oxygen_row = find_row_by_id(root_ws, "Oxygen Consumption Per Character")
            realistic_oxygen_row = find_row_by_id(realistic_ws, "Oxygen Consumption Per Character")
            discipline_row = find_row_by_id(root_ws, "Underwater Discipline Loss")

            self.assertAlmostEqual(root_ws.cell(root_oxygen_row, 2).value, -0.000009 / 1800)
            self.assertAlmostEqual(realistic_ws.cell(realistic_oxygen_row, 2).value, -0.00000133 / 1800)
            self.assertAlmostEqual(root_ws.cell(discipline_row, 2).value, 0.000015 / 15)
            self.assertAlmostEqual(root_ws.cell(discipline_row, 3).value, 0.000016 / 15)
            self.assertAlmostEqual(root_ws.cell(discipline_row, 4).value, 0.000017 / 15)

            entities_ws = load_workbook(out_mod / "Data Sheets" / "Entities.xlsx", data_only=False)["Equipment"]
            ids = [entities_ws.cell(row, 1).value for row in range(1, entities_ws.max_row + 1)]

            self.assertIn("Accumulators I", ids)
            self.assertIn("Electric Engines", ids)
            self.assertNotIn("Diesel Engines", ids)
            self.assertIn("Atmosphere Tank", ids)
            self.assertNotIn("Ventilation", ids)
            self.assertNotIn("G7a Torpedo T1 - Pi1", ids)
            self.assertNotIn("Bow Torpedo Launcher", ids)

            accumulator_row = find_row_by_id(entities_ws, "Accumulators I")
            engine_row = find_row_by_id(entities_ws, "Electric Engines")
            air_row = find_row_by_id(entities_ws, "Atmosphere Tank")

            self.assertIn("250%", entities_ws.cell(accumulator_row, 16).value)
            self.assertIn("EnergyUsage = 3.5e-06", entities_ws.cell(engine_row, 16).value)
            self.assertIn("AirCapacity = 180000", entities_ws.cell(air_row, 16).value)

            report_text = (out_mod / "LongSubmerged10x_generation_report.txt").read_text(encoding="utf-8")
            self.assertIn("air_capacity_parameter_rows: 1", report_text)
            self.assertIn("Mega Batterie : runtime F10 reglable 1-100", report_text)
            self.assertIn("SuperVitesse : runtime F10 reglable 1-100", report_text)
            self.assertIn("Mega torpilles : runtime F10 reglable 1-100, defaut x10", report_text)
            self.assertNotIn("mega_torpedo_rows:", report_text)
            self.assertNotIn("perfect_torpedo_reliability_rows:", report_text)
            self.assertNotIn("energy_recharge_rows:", report_text)
            self.assertIn("player_submarine_speed_rows: 2", report_text)
            self.assertIn("DLC Type IX detecte", report_text)

            types_ws = load_workbook(out_mod / "Data Sheets" / "Entities.xlsx", data_only=False)["Types"]
            player_type_row = find_row_by_id(types_ws, "Type VIIC (Player)")
            type_ix_player_row = find_row_by_id(types_ws, "Type IXC (Player)")
            self.assertEqual(types_ws.cell(player_type_row, 3).value, 45)
            self.assertEqual(types_ws.cell(type_ix_player_row, 3).value, 45)
            generated_type_ids = [types_ws.cell(row, 1).value for row in range(3, types_ws.max_row + 1)]
            self.assertNotIn("Type VIIC", generated_type_ids)
            self.assertNotIn("Type IXC", generated_type_ids)


if __name__ == "__main__":
    unittest.main()
