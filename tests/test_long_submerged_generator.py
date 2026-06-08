from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from openpyxl import Workbook, load_workbook

import build_uboat_long_submerged_mod as generator


def make_general_workbook(path: Path) -> None:
    wb = Workbook()
    ws = wb.active
    ws.title = "Settings"

    # Je reproduis le cas important du jeu : Discipline a trois colonnes de difficulté.
    rows = [
        ["/Discipline", "Normal", "Hard", "Very Hard"],
        ["Underwater Discipline Loss", 0.00001, 0.00002, 0.00003],
        ["Fatigue - Per Day", -0.0000021, -0.0000022, -0.0000023],
        ["Fatigue - Max Penalty", -0.00012, -0.000155, -0.0002],
        [None, None, None, None],
        ["/Resources", "Value", None, None],
        ["Oxygen Consumption Per Character", -0.000009, None, None],
        ["Energy Base Scale", 0.32, None, None],
        ["Energy Recharge Rate", 1.0, None, None],
    ]

    for row in rows:
        ws.append(row)

    wb.save(path)


def make_entities_workbook(path: Path) -> None:
    wb = Workbook()
    ws = wb.active
    ws.title = "Equipment"

    headers = [f"Column {i}" for i in range(1, 16)] + ["Parameters"]
    headers[0] = "Id"
    ws.append(headers)

    def append_equipment(row_id: str, parameters: str) -> None:
        row = [None] * 16
        row[0] = row_id
        row[15] = parameters
        ws.append(row)

    append_equipment("Potassium Absorbers", "OxygenGain = 0.0008, Duration = 18")
    append_equipment(
        "Ventilation",
        "EnergyUsage = 0.0001, Noise = 0.08, OxygenGain = 0.00011, RegenerationLimit = 0.5",
    )
    append_equipment("Trim Pump", "LitresPerSecond = 0.06, Noise = 0.15, EnergyUsage = 0.0002")

    wb.save(path)


def find_row_by_id(ws, row_id: str) -> int:
    for row in range(1, ws.max_row + 1):
        if ws.cell(row, 1).value == row_id:
            return row
    raise AssertionError(f"Ligne introuvable : {row_id}")


class LongSubmergedGeneratorTests(unittest.TestCase):
    def test_parameter_scaling_keeps_unknown_parameters(self) -> None:
        result, changes = generator.scale_parameter_string(
            "EnergyUsage = 0,0001, Noise = 0.08, OxygenGain = 1.5%",
            {
                "EnergyUsage": 0.1,
                "OxygenGain": 10.0,
            },
        )

        self.assertIn("EnergyUsage = 1e-05", result)
        self.assertIn("Noise = 0.08", result)
        self.assertIn("OxygenGain = 15%", result)
        self.assertEqual(len(changes), 2)

    def test_generate_mod_creates_minimal_datasheet_overrides(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            uboat_root = tmp_path / "UBOAT"
            data_sheets = uboat_root / "UBOAT_Data" / "Data Sheets"
            out_mod = tmp_path / "LongSubmerged10x"
            data_sheets.mkdir(parents=True)

            make_general_workbook(data_sheets / "General.xlsx")
            make_entities_workbook(data_sheets / "Entities.xlsx")

            report = generator.generate_mod(
                uboat_root=uboat_root,
                out_mod_dir=out_mod,
                force=False,
                clear_cache=False,
            )

            self.assertTrue((out_mod / "Manifest.json").exists())
            self.assertTrue((out_mod / "Data Sheets" / "General.xlsx").exists())
            self.assertTrue((out_mod / "Data Sheets" / "Entities.xlsx").exists())
            self.assertEqual(report.warnings, [])

            general_ws = load_workbook(out_mod / "Data Sheets" / "General.xlsx", data_only=False)["Settings"]

            underwater_row = find_row_by_id(general_ws, "Underwater Discipline Loss")
            self.assertEqual(general_ws.cell(underwater_row, 2).value, 0.000001)
            self.assertEqual(general_ws.cell(underwater_row, 3).value, 0.000002)
            self.assertEqual(general_ws.cell(underwater_row, 4).value, 0.000003)

            fatigue_row = find_row_by_id(general_ws, "Fatigue - Max Penalty")
            self.assertAlmostEqual(general_ws.cell(fatigue_row, 2).value, -0.000012)
            self.assertAlmostEqual(general_ws.cell(fatigue_row, 3).value, -0.0000155)
            self.assertAlmostEqual(general_ws.cell(fatigue_row, 4).value, -0.00002)

            oxygen_row = find_row_by_id(general_ws, "Oxygen Consumption Per Character")
            energy_row = find_row_by_id(general_ws, "Energy Base Scale")
            self.assertAlmostEqual(general_ws.cell(oxygen_row, 2).value, -0.0000009)
            self.assertAlmostEqual(general_ws.cell(energy_row, 2).value, 3.2)

            entities_ws = load_workbook(out_mod / "Data Sheets" / "Entities.xlsx", data_only=False)["Equipment"]

            ids = [entities_ws.cell(row, 1).value for row in range(1, entities_ws.max_row + 1)]
            self.assertIn("Potassium Absorbers", ids)
            self.assertIn("Ventilation", ids)
            self.assertNotIn("Trim Pump", ids)

            absorbers_row = find_row_by_id(entities_ws, "Potassium Absorbers")
            ventilation_row = find_row_by_id(entities_ws, "Ventilation")

            self.assertIn("OxygenGain = 0.008", entities_ws.cell(absorbers_row, 16).value)
            ventilation_parameters = entities_ws.cell(ventilation_row, 16).value
            self.assertIn("EnergyUsage = 1e-05", ventilation_parameters)
            self.assertIn("OxygenGain = 0.0011", ventilation_parameters)
            self.assertIn("RegenerationLimit = 5", ventilation_parameters)


if __name__ == "__main__":
    unittest.main()
