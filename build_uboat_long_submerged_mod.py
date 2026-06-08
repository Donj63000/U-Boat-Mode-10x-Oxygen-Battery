#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
UBOAT - Long Submerged 10x+
Générateur de mod Data Sheets.

But de cette version :
- Garder la batterie longue durée qui fonctionne déjà.
- Corriger l'air / "qualité de l'air" pour viser environ 7 à 8 jours de base.
- Ne plus casser la ventilation : la ligne Ventilation reste vanilla par défaut.
- Patcher aussi la capacité / réserve de l'atmosphère quand elle est exposée dans les datasheets,
  parce que baisser seulement "Oxygen Consumption Per Character" peut être plafonné par le jeu.

Prérequis :
    py -m pip install openpyxl

Commande conseillée :
    py build_uboat_long_submerged_mod.py --uboat "C:\\Program Files (x86)\\Steam\\steamapps\\common\\UBOAT" --force --clear-cache

Important :
    Teste l'air sur une NOUVELLE carrière après génération.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
import os
import re
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable

from openpyxl import Workbook, load_workbook
from openpyxl.cell import Cell
from openpyxl.worksheet.worksheet import Worksheet


# =============================================================================
# CONFIGURATION
# =============================================================================

MOD_FOLDER_NAME = "LongSubmerged10x"
MOD_DISPLAY_NAME = "Long Submerged 10x+"
MOD_VERSION = "1.2.5"
MOD_AUTHOR = "VotreNomOuVotreEquipe"
MOD_ASSEMBLY_NAME = "LongSubmerged10xPatch"

DEFAULT_GAME_VERSION = "2026.1 Patch 20"

# Ton dernier screenshot donne environ 23 h 27 min d'air à 99 % avec /15 déjà appliqué.
# 23.46 h * 125 / 15 = 195 h, donc environ 8 jours avec Blue Lighting.
DEFAULT_AIR_CAPACITY_FACTOR = 125.0

# On garde aussi la baisse de consommation par personnage, mais ce n'est plus le levier principal :
# certaines versions / configs semblent garder un minimum visible dans l'UI, par exemple "Équipage -4/min".
DEFAULT_OXYGEN_CONSUMPTION_FACTOR = 125.0

# Discipline/fatigue proportionnelles à l'immersion longue.
DEFAULT_DISCIPLINE_FACTOR = 15.0

# Batterie : ces réglages marchent chez toi, donc on les garde.
DEFAULT_BATTERY_CAPACITY_FACTOR = 10.0
DEFAULT_EQUIPMENT_ENERGY_USAGE_FACTOR = 0.10

# Vitesse : seuls les deux derniers crans avant sont boostes.
DEFAULT_FAST_SPEED_FACTOR = 3.5
DEFAULT_FAST_SPEED_TOP_GEARS = 2
DEFAULT_PLAYER_SUBMARINE_MAX_SPEED = 45.0

# IMPORTANT :
# On ne modifie plus la ventilation par défaut.
# Une ancienne tentative modifiait la ligne ventilation deux fois sur EnergyUsage et montait RegenerationLimit trop haut.
# Pour "la ventilation marche normalement", on ne copie plus cette ligne dans l'override.
DEFAULT_PATCH_VENTILATION = False

# Optionnel, désactivé par défaut.
# Si activé, seulement OxygenGain est augmenté et EnergyUsage est réduit UNE seule fois.
# RegenerationLimit reste vanilla pour éviter les comportements bizarres.
DEFAULT_VENTILATION_GAIN_FACTOR = 1.0
DEFAULT_PATCH_POTASSIUM = False
DEFAULT_POTASSIUM_DURATION_FACTOR = 1.0

# Patch optionnel de Energy Base Scale dans General.xlsx. Désactivé car la batterie marche via Entities.xlsx.
DEFAULT_PATCH_ENERGY_BASE_SCALE = False


# =============================================================================
# STRUCTURES
# =============================================================================

@dataclass(frozen=True)
class GeneralPatch:
    row_id: str
    multiplier: float
    expected_category: str | None
    reason: str


@dataclass
class PatchReport:
    changed_files: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    info: list[str] = field(default_factory=list)
    counters: dict[str, int] = field(default_factory=dict)

    def file(self, path: Path) -> None:
        path_text = str(path)
        if path_text not in self.changed_files:
            self.changed_files.append(path_text)

    def warn(self, message: str) -> None:
        self.warnings.append(message)

    def note(self, message: str) -> None:
        self.info.append(message)

    def inc(self, key: str, amount: int = 1) -> None:
        self.counters[key] = self.counters.get(key, 0) + amount


@dataclass
class GeneralRowHit:
    patch: GeneralPatch
    source_row: int
    category_norm: str | None
    category_row: int | None
    value_cols: list[int]


@dataclass
class ModifiedRow:
    source_row: int
    new_values_by_col: dict[int, Any]
    changes: list[str]
    readable_context: str


# =============================================================================
# OUTILS GÉNÉRAUX
# =============================================================================

def norm_text(value: Any) -> str:
    if value is None:
        return ""
    text = str(value).replace("\u00a0", " ").strip().lower()
    return re.sub(r"\s+", " ", text)


def norm_category(value: Any) -> str:
    text = norm_text(value)
    if not text:
        return ""
    if not text.startswith("/"):
        text = "/" + text
    return text


def contains_any(text: str, needles: Iterable[str]) -> bool:
    lowered = text.lower()
    return any(needle.lower() in lowered for needle in needles)


def safe_float(value: Any) -> float | None:
    if value is None or isinstance(value, bool):
        return None

    if isinstance(value, (int, float)):
        number = float(value)
        return number if math.isfinite(number) else None

    text = str(value).strip()
    if not text:
        return None

    if text.endswith("%"):
        text = text[:-1].strip()

    text = text.replace("\u00a0", "")
    text = text.replace(" ", "")
    text = text.replace(",", ".")

    try:
        number = float(text)
    except ValueError:
        return None

    return number if math.isfinite(number) else None


def scale_numeric_excel_value(original_value: Any, multiplier: float) -> float:
    parsed = safe_float(original_value)
    if parsed is None:
        raise ValueError(f"valeur non numérique : {original_value!r}")
    return parsed * multiplier


def copy_cell(src: Cell, dst: Cell) -> None:
    dst.value = src.value

    if src.has_style:
        dst._style = copy.copy(src._style)

    dst.number_format = src.number_format

    if src.font:
        dst.font = copy.copy(src.font)
    if src.fill:
        dst.fill = copy.copy(src.fill)
    if src.border:
        dst.border = copy.copy(src.border)
    if src.alignment:
        dst.alignment = copy.copy(src.alignment)
    if src.protection:
        dst.protection = copy.copy(src.protection)
    if src.comment:
        dst.comment = copy.copy(src.comment)


def copy_row(src_ws: Worksheet, dst_ws: Worksheet, src_row: int, dst_row: int) -> None:
    for col in range(1, src_ws.max_column + 1):
        copy_cell(src_ws.cell(src_row, col), dst_ws.cell(dst_row, col))

    if src_ws.row_dimensions[src_row].height:
        dst_ws.row_dimensions[dst_row].height = src_ws.row_dimensions[src_row].height


def copy_column_widths(src_ws: Worksheet, dst_ws: Worksheet) -> None:
    for key, dim in src_ws.column_dimensions.items():
        dst_ws.column_dimensions[key].width = dim.width


def find_existing_override_row(dst_ws: Worksheet, source_row_id: Any) -> int | None:
    row_id = norm_text(source_row_id)
    if not row_id:
        return None

    for row in range(1, dst_ws.max_row + 1):
        if norm_text(dst_ws.cell(row, 1).value) == row_id:
            return row

    return None


def get_sheet_case_insensitive(workbook: Workbook, wanted_name: str) -> Worksheet | None:
    wanted = norm_text(wanted_name)
    for name in workbook.sheetnames:
        if norm_text(name) == wanted:
            return workbook[name]
    return None


def default_local_uboat_dir() -> Path:
    user_profile = os.environ.get("USERPROFILE")
    if user_profile:
        return Path(user_profile) / "AppData" / "LocalLow" / "Deep Water Studio" / "UBOAT"
    return Path.cwd() / "UBOAT_LocalLow"


def default_local_mods_root() -> Path:
    return default_local_uboat_dir() / "Mods"


def resolve_data_sheets_dir(uboat_root: Path) -> Path:
    data_sheets = uboat_root / "UBOAT_Data" / "Data Sheets"
    if not data_sheets.exists():
        raise FileNotFoundError(
            f"Dossier introuvable : {data_sheets}\n"
            f"--uboat doit pointer vers le dossier d'installation UBOAT."
        )
    return data_sheets


def ensure_clean_directory(path: Path, force: bool) -> None:
    if path.exists():
        if not force:
            raise FileExistsError(
                f"Le dossier existe déjà : {path}\n"
                f"Relance avec --force pour le remplacer."
            )
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def find_datasheet_files(data_sheets_dir: Path, filename: str | None = None) -> list[Path]:
    files: list[Path] = []
    for path in data_sheets_dir.rglob("*.xlsx"):
        if path.name.startswith("~$"):
            continue
        if filename is not None and path.name.lower() != filename.lower():
            continue
        files.append(path)
    return sorted(files, key=lambda p: str(p.relative_to(data_sheets_dir)).lower())


def find_type_ix_dlc_data_sheets_dir(uboat_root: Path) -> Path | None:
    data_sheets = (
        uboat_root
        / "UBOAT_Data"
        / "StreamingAssets"
        / "Packages"
        / "uboat.dlc.type-ix"
        / "Data Sheets"
    )
    return data_sheets if data_sheets.exists() else None


def unique_preserve_order(values: Iterable[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []

    for value in values:
        key = value.strip().lower()
        if not key or key in seen:
            continue
        seen.add(key)
        result.append(value.strip())

    return result


def row_values(ws: Worksheet, row: int) -> list[Any]:
    return [ws.cell(row, col).value for col in range(1, ws.max_column + 1)]


def join_row_text(values: list[Any]) -> str:
    return " | ".join(str(value) for value in values if value is not None)


def readable_row_label(ws: Worksheet, row: int) -> str:
    values = row_values(ws, row)
    parts: list[str] = []
    for index in range(min(len(values), 6)):
        value = values[index]
        if value is not None and str(value).strip():
            parts.append(str(value).strip())
    return " | ".join(parts) if parts else f"row={row}"


def find_header_row(ws: Worksheet) -> int:
    """
    Cherche la première ligne d'en-tête probable.
    Dans les datasheets UBOAT, elle est très souvent en ligne 1.
    """
    best_row = 1
    best_score = -1

    for row in range(1, min(ws.max_row, 25) + 1):
        values = [norm_text(ws.cell(row, col).value) for col in range(1, ws.max_column + 1)]
        score = 0
        for value in values:
            if value in {"id", "name", "parameters", "type", "value", "category"}:
                score += 3
            elif value:
                score += 1

        if score > best_score:
            best_score = score
            best_row = row

    return best_row


def find_columns_by_header(ws: Worksheet, header_row: int) -> dict[str, list[int]]:
    headers: dict[str, list[int]] = {}
    for col in range(1, ws.max_column + 1):
        header = norm_text(ws.cell(header_row, col).value)
        if not header:
            continue
        headers.setdefault(header, []).append(col)
    return headers


# =============================================================================
# GENERAL.XLSX
# =============================================================================

def build_general_patches(args: argparse.Namespace) -> list[GeneralPatch]:
    patches = [
        GeneralPatch(
            row_id="Oxygen Consumption Per Character",
            multiplier=1.0 / args.oxygen_consumption_factor,
            expected_category="/Resources",
            reason=(
                f"Consommation air/qualité d'air divisée par {args.oxygen_consumption_factor:g}. "
                "Ce n'est plus le seul levier, car l'UI peut garder un minimum affiché."
            ),
        ),
        GeneralPatch(
            row_id="Underwater Discipline Loss",
            multiplier=1.0 / args.discipline_factor,
            expected_category="/Discipline",
            reason=f"Discipline sous l'eau environ {args.discipline_factor:g}x moins punitive.",
        ),
        GeneralPatch(
            row_id="Fatigue - Per Day",
            multiplier=1.0 / args.discipline_factor,
            expected_category="/Discipline",
            reason=f"Fatigue longue durée environ {args.discipline_factor:g}x plus lente.",
        ),
        GeneralPatch(
            row_id="Fatigue - Max Penalty",
            multiplier=1.0 / args.discipline_factor,
            expected_category="/Discipline",
            reason="Pénalité maximale de fatigue réduite proportionnellement.",
        ),
    ]

    if args.patch_energy_base_scale:
        patches.append(
            GeneralPatch(
                row_id="Energy Base Scale",
                multiplier=args.battery_capacity_factor,
                expected_category="/Resources",
                reason="Optionnel : réserve d'énergie globale augmentée.",
            )
        )

    return patches


def index_settings_sheet(ws: Worksheet) -> tuple[
    dict[str, int],
    dict[tuple[str, str], int],
    dict[str, tuple[int, str | None]],
]:
    category_rows: dict[str, int] = {}
    rows_by_category_and_id: dict[tuple[str, str], int] = {}
    rows_by_id: dict[str, tuple[int, str | None]] = {}

    current_category: str | None = None

    for row in range(1, ws.max_row + 1):
        first_raw = ws.cell(row, 1).value
        first = str(first_raw).strip() if first_raw is not None else ""

        if not first:
            continue

        if first.startswith("/"):
            current_category = norm_category(first)
            category_rows[current_category] = row
            continue

        row_id_norm = norm_text(first)
        if not row_id_norm:
            continue

        if current_category:
            rows_by_category_and_id[(current_category, row_id_norm)] = row

        rows_by_id.setdefault(row_id_norm, (row, current_category))

    return category_rows, rows_by_category_and_id, rows_by_id


def find_value_cols_for_general_row(ws: Worksheet, row: int) -> list[int]:
    value_cols: list[int] = []

    for col in range(2, ws.max_column + 1):
        if safe_float(ws.cell(row, col).value) is not None:
            value_cols.append(col)

    return value_cols


def find_general_hits(
    ws: Worksheet,
    patches: list[GeneralPatch],
    report: PatchReport,
    source_label: str,
) -> list[GeneralRowHit]:
    category_rows, rows_by_category_and_id, rows_by_id = index_settings_sheet(ws)
    hits: list[GeneralRowHit] = []

    for patch in patches:
        expected_category_norm = norm_category(patch.expected_category) if patch.expected_category else None
        row: int | None = None
        actual_category: str | None = None

        if expected_category_norm:
            row = rows_by_category_and_id.get((expected_category_norm, norm_text(patch.row_id)))
            if row is not None:
                actual_category = expected_category_norm

        if row is None:
            fallback = rows_by_id.get(norm_text(patch.row_id))
            if fallback:
                row, actual_category = fallback

        if row is None:
            report.warn(f"{source_label}: ligne introuvable dans Settings : {patch.row_id!r}")
            continue

        value_cols = find_value_cols_for_general_row(ws, row)

        if not value_cols:
            report.warn(
                f"{source_label}: {patch.row_id!r} trouvé ligne {row}, "
                f"mais aucune colonne numérique n'a été trouvée."
            )
            continue

        hits.append(
            GeneralRowHit(
                patch=patch,
                source_row=row,
                category_norm=actual_category,
                category_row=category_rows.get(actual_category or "") if actual_category else None,
                value_cols=value_cols,
            )
        )

    return hits


def apply_general_hit_values(
    src_ws: Worksheet,
    out_ws: Worksheet,
    hit: GeneralRowHit,
    dest_row: int,
    report: PatchReport,
    source_label: str,
) -> None:
    changes: list[str] = []

    for value_col in hit.value_cols:
        original = src_ws.cell(hit.source_row, value_col).value
        new_value = scale_numeric_excel_value(original, hit.patch.multiplier)
        out_ws.cell(dest_row, value_col).value = new_value

        header = src_ws.cell(hit.category_row or 1, value_col).value
        header_text = str(header).strip() if header is not None else f"colonne {value_col}"
        changes.append(f"{header_text}: {original!r} -> {new_value!r}")

    report.note(
        f"{source_label} / Settings / {hit.patch.row_id}: "
        + "; ".join(changes)
        + f" | {hit.patch.reason}"
    )

    if norm_text(hit.patch.row_id) == "oxygen consumption per character":
        report.inc("general_oxygen_consumption_rows")


def create_general_override(
    vanilla_general: Path,
    output_general: Path,
    patches: list[GeneralPatch],
    report: PatchReport,
    data_sheets_dir: Path,
) -> bool:
    source_label = str(vanilla_general.relative_to(data_sheets_dir))
    src_wb = load_workbook(vanilla_general, data_only=False)
    src_ws = get_sheet_case_insensitive(src_wb, "Settings")

    if src_ws is None:
        report.warn(f"{source_label}: onglet Settings introuvable, fichier ignoré.")
        return False

    hits = find_general_hits(src_ws, patches, report, source_label)
    if not hits:
        report.warn(f"{source_label}: aucune valeur General.xlsx patchée.")
        return False

    out_wb = Workbook()
    out_ws = out_wb.active
    out_ws.title = src_ws.title
    copy_column_widths(src_ws, out_ws)

    dest_row = 1
    written_categories: set[str] = set()

    categorized_hits = [hit for hit in hits if hit.category_norm and hit.category_row]
    uncategorized_hits = [hit for hit in hits if not (hit.category_norm and hit.category_row)]

    for hit in sorted(categorized_hits, key=lambda h: (h.category_row or 0, h.source_row)):
        assert hit.category_norm is not None
        assert hit.category_row is not None

        if hit.category_norm not in written_categories:
            copy_row(src_ws, out_ws, hit.category_row, dest_row)
            dest_row += 1
            written_categories.add(hit.category_norm)

        copy_row(src_ws, out_ws, hit.source_row, dest_row)
        apply_general_hit_values(src_ws, out_ws, hit, dest_row, report, source_label)
        dest_row += 1

    if uncategorized_hits:
        if dest_row > 1:
            dest_row += 1

        first_row_text = " ".join(
            norm_text(src_ws.cell(1, col).value)
            for col in range(1, src_ws.max_column + 1)
        )

        if "value" in first_row_text or "id" in first_row_text:
            copy_row(src_ws, out_ws, 1, dest_row)
            dest_row += 1

        for hit in sorted(uncategorized_hits, key=lambda h: h.source_row):
            copy_row(src_ws, out_ws, hit.source_row, dest_row)
            apply_general_hit_values(src_ws, out_ws, hit, dest_row, report, source_label)
            dest_row += 1

    output_general.parent.mkdir(parents=True, exist_ok=True)
    out_wb.save(output_general)
    report.file(output_general)
    return True


# =============================================================================
# PATCH PARAMÈTRES TEXTUELS, EX: "EnergyUsage = 0.0002"
# =============================================================================

NUMBER_PATTERN = r"[-+]?\d+(?:[.,]\d+)?(?:[eE][-+]?\d+)?"
KEY_VALUE_PATTERN = re.compile(
    r"(?P<prefix>(?P<key>[A-Za-z][A-Za-z0-9_ ]*?)\s*(?P<op>[*+\-/]?=)\s*)"
    r"(?P<number>" + NUMBER_PATTERN + r")(?P<percent>%?)"
)


def format_number(value: float, percent: str = "") -> str:
    return f"{value:.10g}" + percent


def format_csharp_float(value: float) -> str:
    return f"{value:.10g}f"


def normalize_parameter_key(key: str) -> str:
    return re.sub(r"[^a-z0-9]", "", key.lower())


def replace_keyed_parameters(
    parameters: str,
    multiplier_by_key: dict[str, float],
    *,
    only_when: Callable[[str, re.Match[str]], bool] | None = None,
) -> tuple[str, list[str]]:
    """
    Remplace les valeurs dans une chaîne Parameters.

    multiplier_by_key utilise des clés normalisées :
      "EnergyUsage" -> "energyusage"
      "Air Quality Capacity" -> "airqualitycapacity"
    """
    normalized_multipliers = {
        normalize_parameter_key(key): value
        for key, value in multiplier_by_key.items()
    }

    changes: list[str] = []

    def replace(match: re.Match[str]) -> str:
        raw_key = match.group("key")
        normalized_key = normalize_parameter_key(raw_key)
        multiplier = normalized_multipliers.get(normalized_key)

        if multiplier is None:
            return match.group(0)

        if only_when is not None and not only_when(raw_key, match):
            return match.group(0)

        raw_number = match.group("number")
        percent = match.group("percent") or ""
        parsed = safe_float(raw_number + percent)

        if parsed is None:
            return match.group(0)

        scaled = parsed * multiplier
        replacement_number = format_number(scaled, percent)
        changes.append(f"{raw_key.strip()}: {raw_number}{percent} -> {replacement_number}")

        return match.group("prefix") + replacement_number

    new_parameters = KEY_VALUE_PATTERN.sub(replace, parameters)
    return new_parameters, changes


def find_parameters_col(ws: Worksheet, header_row: int) -> int | None:
    for col in range(1, ws.max_column + 1):
        if norm_text(ws.cell(header_row, col).value) == "parameters":
            return col

    # Fallback historique : colonne P.
    if ws.max_column >= 16:
        return 16

    return None


# =============================================================================
# DÉTECTION DES LIGNES À PATCHER
# =============================================================================

VENTILATION_WORDS = (
    "ventilation",
    "ventilator",
    "air recircul",
    "recirculating",
    "potassium",
    "absorber",
    "absorbent",
    "kali",
)

COMPRESSOR_WORDS = (
    "compressor",
    "compresseur",
    "compressed air",
    "compressedair",
    "druckluft",
)

ACCUMULATOR_WORDS = (
    "accumulator",
    "accumulators",
    "akkumulator",
    "akkumulatoren",
    "battery upgrade",
    "battery capacity",
)

PLAYER_SUBMARINE_WORDS = (
    "u-boat",
    "uboat",
    "u boat",
    "type ii",
    "type vii",
    "type viic",
    "type viib",
    "type ix",
    "submarine",
    "player ship",
    "player uboat",
)

AIR_CONTEXT_WORDS = (
    "air quality",
    "airquality",
    "air supply",
    "airsupply",
    "breathable",
    "atmosphere",
    "atmospheric",
    "oxygen capacity",
    "oxygen supply",
    "oxygen amount",
    "oxygen reserve",
    "starting air",
    "start air",
    "initial air",
)

AIR_BANNED_WORDS = (
    "compressed air",
    "compressedair",
    "compressor",
    "compresseur",
    "torpedo",
    "torpille",
    "diesel",
    "fuel",
    "emission",
    "exhaust",
    "noise",
    "visibility",
    "detection",
    "contrail",
)

CONSUMPTION_BANNED_WORDS = (
    "consumption",
    "consume",
    "usage",
    "use",
    "gain",
    "regeneration",
    "recharge",
    "efficiency",
    "noise",
    "rate",
    "persecond",
    "perminute",
    "percharacter",
    "per character",
)


# Clés exactes qui ressemblent à la CAPACITÉ de l'atmosphère, pas à sa consommation.
AIR_CAPACITY_KEYS = {
    "aircapacity",
    "airqualitycapacity",
    "airqualitymax",
    "airqualitymaximum",
    "maxairquality",
    "maxair",
    "airmax",
    "airamount",
    "airqualityamount",
    "airbaseamount",
    "baseairamount",
    "startingair",
    "startair",
    "initialair",
    "atmospherecapacity",
    "atmosphericcapacity",
    "atmospheremax",
    "atmosphereamount",
    "atmosphere",
    "oxygenmax",
    "maxoxygen",
    "oxygencapacity",
    "oxygenamount",
    "oxygenreserve",
    "oxygensupply",
    "oxygenstorage",
    "baseoxygen",
    "oxygenbase",
    "startingoxygen",
    "initialoxygen",
    "breathableair",
    "breathableaircapacity",
    "co2capacity",
    "carbondioxidecapacity",
}


ENERGY_USAGE_KEYS = {
    "energyusage",
    "electricityusage",
    "powerusage",
}


def patch_energy_usage_parameters(
    parameters: str,
    *,
    consumption_factor: float,
    recharge_factor: float,
) -> tuple[str, list[str], dict[str, int]]:
    """
    Je garde deux règles distinctes :
    - valeur positive : l'équipement consomme, donc je réduis la consommation ;
    - valeur négative : l'équipement produit/recharge, donc je compense la batterie x10.
    """
    changes: list[str] = []
    counters = {
        "energy_usage_rows": 0,
        "energy_recharge_rows": 0,
    }

    def replace(match: re.Match[str]) -> str:
        raw_key = match.group("key")
        if normalize_parameter_key(raw_key) not in ENERGY_USAGE_KEYS:
            return match.group(0)

        raw_number = match.group("number")
        percent = match.group("percent") or ""
        parsed = safe_float(raw_number + percent)

        if parsed is None or parsed == 0:
            return match.group(0)

        if parsed < 0:
            multiplier = recharge_factor
            counter_key = "energy_recharge_rows"
        else:
            multiplier = consumption_factor
            counter_key = "energy_usage_rows"

        scaled = parsed * multiplier
        replacement_number = format_number(scaled, percent)
        changes.append(f"{raw_key.strip()}: {raw_number}{percent} -> {replacement_number}")
        counters[counter_key] = 1

        return match.group("prefix") + replacement_number

    new_parameters = KEY_VALUE_PATTERN.sub(replace, parameters)
    return new_parameters, changes, counters


ACCUMULATOR_CAPACITY_KEYS = {
    "energycapacity",
    "batterycapacity",
    "capacitymultiplier",
    "capacityscale",
    "capacity",
    "maxcapacity",
    "energycapacitygain",
    "batterycapacitygain",
    "energystorage",
    "storage",
    "value",
}


def is_ventilation_row(text: str) -> bool:
    lowered = text.lower()
    return contains_any(lowered, VENTILATION_WORDS) or "oxygengain" in lowered or "regenerationlimit" in lowered


def is_compressor_row(text: str) -> bool:
    return contains_any(text.lower(), COMPRESSOR_WORDS)


def is_accumulator_row(text: str) -> bool:
    lowered = text.lower()
    return contains_any(lowered, ACCUMULATOR_WORDS) and not is_compressor_row(lowered)


def is_player_submarine_context(text: str) -> bool:
    return contains_any(text.lower(), PLAYER_SUBMARINE_WORDS)


def has_air_context(text: str) -> bool:
    lowered = text.lower()

    if contains_any(lowered, AIR_BANNED_WORDS):
        return False

    if contains_any(lowered, AIR_CONTEXT_WORDS):
        return True

    # Fallback : ligne de sous-marin + paramètre exact "Oxygen" ou "Air".
    if is_player_submarine_context(lowered) and ("oxygen" in lowered or "air" in lowered):
        return True

    return False


def is_air_capacity_key(key: str, row_text_full: str, value: float) -> bool:
    normalized = normalize_parameter_key(key)

    if value <= 0:
        return False

    if normalized in AIR_CAPACITY_KEYS:
        return True

    # Fallback très contrôlé :
    # "Oxygen = 3200" ou "Air = 3200" peut être le compteur de base.
    # On ne l'accepte que sur une ligne clairement liée au bateau / atmosphère.
    if normalized in {"oxygen", "air"}:
        lowered = row_text_full.lower()
        if is_player_submarine_context(lowered) or contains_any(lowered, AIR_CONTEXT_WORDS):
            return True

    return False


def is_probable_air_capacity_row(ws: Worksheet, row: int, header_row: int, parameters_col: int | None) -> bool:
    values = row_values(ws, row)
    text = join_row_text(values)

    if not has_air_context(text):
        return False

    lowered = text.lower()
    if contains_any(lowered, CONSUMPTION_BANNED_WORDS):
        return False

    if parameters_col is not None and ws.cell(row, parameters_col).value:
        params = str(ws.cell(row, parameters_col).value)
        for match in KEY_VALUE_PATTERN.finditer(params):
            value = safe_float((match.group("number") or "") + (match.group("percent") or ""))
            if value is not None and is_air_capacity_key(match.group("key"), text, value):
                return True

    headers = find_columns_by_header(ws, header_row)
    header_text_by_col = {
        col: norm_text(ws.cell(header_row, col).value)
        for col in range(1, ws.max_column + 1)
    }

    for col in range(2, ws.max_column + 1):
        value = safe_float(ws.cell(row, col).value)
        if value is None or value <= 0:
            continue

        header_text = header_text_by_col.get(col, "")

        if contains_any(header_text, ("capacity", "amount", "maximum", "max", "initial", "start", "base", "value")):
            return True

        # Sans header utile, on n'accepte que de grandes valeurs typiques d'un compteur.
        if value >= 100:
            return True

    return False


# =============================================================================
# PATCH DES LIGNES NON-GENERAL
# =============================================================================

def patch_parameter_cell(
    parameters: str,
    row_text_full: str,
    args: argparse.Namespace,
) -> tuple[str, list[str], dict[str, int]]:
    """
    Patch une cellule Parameters.
    Retourne :
      new_parameters, changes, counters
    """
    counters: dict[str, int] = {}
    new_parameters = parameters
    changes: list[str] = []
    lowered_context = row_text_full.lower()

    # Batterie : je garde l'autonomie validée en jeu.
    if is_accumulator_row(row_text_full):
        accumulator_multipliers = {key: args.battery_capacity_factor for key in ACCUMULATOR_CAPACITY_KEYS}

        patched, local_changes = replace_keyed_parameters(new_parameters, accumulator_multipliers)
        if local_changes:
            new_parameters = patched
            changes.extend(local_changes)
            counters["battery_capacity_rows"] = 1

    # Conso/recharge électrique : je réduis les consommateurs et je compense les rechargeurs.
    # Avec une batterie x10, une recharge vanilla remplirait le plein dix fois trop lentement.
    if (
        not is_ventilation_row(row_text_full)
        and not is_compressor_row(row_text_full)
        and any(key in normalize_parameter_key(new_parameters) for key in ENERGY_USAGE_KEYS)
    ):
        patched, local_changes, local_counters = patch_energy_usage_parameters(
            new_parameters,
            consumption_factor=args.energy_usage_factor,
            recharge_factor=args.battery_capacity_factor,
        )
        if local_changes:
            new_parameters = patched
            changes.extend(local_changes)
            for key, value in local_counters.items():
                if value:
                    counters[key] = value

    # Air / atmosphère de base : c'est le nouveau levier principal.
    # On vise les clés de capacité/stock, jamais les clés Gain/Usage/Consumption.
    if has_air_context(row_text_full) or is_player_submarine_context(row_text_full):
        def only_air_capacity(raw_key: str, match: re.Match[str]) -> bool:
            parsed = safe_float((match.group("number") or "") + (match.group("percent") or ""))
            if parsed is None:
                return False
            return is_air_capacity_key(raw_key, row_text_full, parsed)

        air_capacity_multipliers = {key: args.air_capacity_factor for key in AIR_CAPACITY_KEYS}
        air_capacity_multipliers.update({"Oxygen": args.air_capacity_factor, "Air": args.air_capacity_factor})

        patched, local_changes = replace_keyed_parameters(
            new_parameters,
            air_capacity_multipliers,
            only_when=only_air_capacity,
        )

        if local_changes:
            new_parameters = patched
            changes.extend(local_changes)
            counters["air_capacity_parameter_rows"] = 1

    # Ventilation : désactivé par défaut pour revenir au fonctionnement vanilla.
    # Si tu actives explicitement --patch-ventilation, on ne touche PAS RegenerationLimit.
    if args.patch_ventilation and is_ventilation_row(row_text_full):
        ventilation_multipliers = {
            "OxygenGain": args.ventilation_gain_factor,
            "AirQualityGain": args.ventilation_gain_factor,
            "AirGain": args.ventilation_gain_factor,
        }

        if args.energy_usage_factor != 1.0:
            ventilation_multipliers["EnergyUsage"] = args.energy_usage_factor

        patched, local_changes = replace_keyed_parameters(new_parameters, ventilation_multipliers)
        if local_changes:
            new_parameters = patched
            changes.extend(local_changes)
            counters["ventilation_rows"] = 1

    # Potassium absorbers : désactivé par défaut.
    # Si activé, on allonge seulement les durées, pas la ventilation elle-même.
    if args.patch_potassium and contains_any(lowered_context, ("potassium", "absorber", "absorbent")):
        potassium_multipliers = {
            "Duration": args.potassium_duration_factor,
            "WorkTime": args.potassium_duration_factor,
            "WorkingTime": args.potassium_duration_factor,
            "BurnTime": args.potassium_duration_factor,
            "Time": args.potassium_duration_factor,
        }

        patched, local_changes = replace_keyed_parameters(new_parameters, potassium_multipliers)
        if local_changes:
            new_parameters = patched
            changes.extend(local_changes)
            counters["potassium_rows"] = 1

    return new_parameters, unique_preserve_order(changes), counters


def patch_simple_air_capacity_cells(
    ws: Worksheet,
    row: int,
    header_row: int,
    parameters_col: int | None,
    args: argparse.Namespace,
) -> tuple[dict[int, Any], list[str], dict[str, int]]:
    """
    Patch les cellules numériques d'une ligne qui représente clairement une réserve/capacité d'air,
    même si la ligne n'utilise pas de colonne Parameters.
    """
    if not is_probable_air_capacity_row(ws, row, header_row, parameters_col):
        return {}, [], {}

    values_by_col: dict[int, Any] = {}
    changes: list[str] = []
    header_text_by_col = {
        col: norm_text(ws.cell(header_row, col).value)
        for col in range(1, ws.max_column + 1)
    }

    for col in range(2, ws.max_column + 1):
        if parameters_col is not None and col == parameters_col:
            continue

        original = ws.cell(row, col).value
        parsed = safe_float(original)

        if parsed is None or parsed <= 0:
            continue

        header_text = header_text_by_col.get(col, "")
        accept = False

        if contains_any(header_text, ("capacity", "amount", "maximum", "max", "initial", "start", "base", "value")):
            accept = True

        if parsed >= 100:
            accept = True

        if not accept:
            continue

        new_value = parsed * args.air_capacity_factor
        values_by_col[col] = new_value

        label = header_text if header_text else f"colonne {col}"
        changes.append(f"{label}: {original!r} -> {new_value!r}")

    if not values_by_col:
        return {}, [], {}

    return values_by_col, changes, {"air_capacity_cell_rows": 1}


def patch_player_submarine_speed_cells(
    ws: Worksheet,
    row: int,
    header_row: int,
    args: argparse.Namespace,
) -> tuple[dict[int, Any], list[str], dict[str, int]]:
    if norm_text(ws.title) != "types":
        return {}, [], {}

    row_name_raw = ws.cell(row, 1).value
    row_name = str(row_name_raw or "")
    category = norm_text(ws.cell(row, 2).value)

    if "(player)" not in row_name.lower() or category != "submarine":
        return {}, [], {}

    speed_col = None
    for col in range(1, ws.max_column + 1):
        header = norm_text(ws.cell(header_row, col).value)
        if "speed" in header and "km/h" in header:
            speed_col = col
            break

    if speed_col is None:
        return {}, [], {}

    original = ws.cell(row, speed_col).value
    parsed = safe_float(original)

    if parsed is None or parsed <= 0:
        return {}, [], {}

    target_speed = args.player_submarine_max_speed
    if parsed >= target_speed:
        return {}, [], {}

    return (
        {speed_col: target_speed},
        [f"Speed (km/h): {original!r} -> {target_speed!r}"],
        {"player_submarine_speed_rows": 1},
    )


def patch_sheet_generic(
    src_ws: Worksheet,
    source_label: str,
    sheet_name: str,
    report: PatchReport,
    args: argparse.Namespace,
) -> tuple[Worksheet | None, list[ModifiedRow]]:
    header_row = find_header_row(src_ws)
    parameters_col = find_parameters_col(src_ws, header_row)

    modified_rows_by_source: dict[int, ModifiedRow] = {}

    for row_idx in range(header_row + 1, src_ws.max_row + 1):
        values = row_values(src_ws, row_idx)
        full_text = join_row_text(values)
        readable = readable_row_label(src_ws, row_idx)

        new_values_by_col: dict[int, Any] = {}
        changes: list[str] = []
        counters: dict[str, int] = {}

        if parameters_col is not None:
            params_raw = src_ws.cell(row_idx, parameters_col).value
            if params_raw is not None:
                params = str(params_raw)
                new_params, param_changes, param_counters = patch_parameter_cell(params, full_text, args)

                if param_changes and new_params != params:
                    new_values_by_col[parameters_col] = new_params
                    changes.extend(param_changes)
                    for key, value in param_counters.items():
                        counters[key] = counters.get(key, 0) + value

        simple_values, simple_changes, simple_counters = patch_simple_air_capacity_cells(
            src_ws,
            row_idx,
            header_row,
            parameters_col,
            args,
        )

        if simple_changes:
            new_values_by_col.update(simple_values)
            changes.extend(simple_changes)
            for key, value in simple_counters.items():
                counters[key] = counters.get(key, 0) + value

        speed_values, speed_changes, speed_counters = patch_player_submarine_speed_cells(
            src_ws,
            row_idx,
            header_row,
            args,
        )

        if speed_changes:
            new_values_by_col.update(speed_values)
            changes.extend(speed_changes)
            for key, value in speed_counters.items():
                counters[key] = counters.get(key, 0) + value

        if new_values_by_col:
            modified_rows_by_source[row_idx] = ModifiedRow(
                source_row=row_idx,
                new_values_by_col=new_values_by_col,
                changes=unique_preserve_order(changes),
                readable_context=readable,
            )

            for key, value in counters.items():
                report.inc(key, value)

    return None, list(modified_rows_by_source.values())


def create_generic_xlsx_override(
    vanilla_xlsx: Path,
    output_xlsx: Path,
    report: PatchReport,
    source_root: Path,
    args: argparse.Namespace,
) -> bool:
    """
    Patch générique pour tous les fichiers XLSX sauf General.xlsx/Settings.
    Crée un override minimal avec seulement les feuilles/lignes modifiées.
    """
    source_label = str(vanilla_xlsx.relative_to(source_root))
    src_wb = load_workbook(vanilla_xlsx, data_only=False)

    if output_xlsx.exists():
        out_wb = load_workbook(output_xlsx, data_only=False)
    else:
        out_wb = Workbook()
        out_wb.remove(out_wb.active)

    total_rows_changed = 0

    for sheet_name in src_wb.sheetnames:
        src_ws = src_wb[sheet_name]

        _unused, modified_rows = patch_sheet_generic(src_ws, source_label, sheet_name, report, args)
        if not modified_rows:
            continue

        header_row = find_header_row(src_ws)

        out_ws = get_sheet_case_insensitive(out_wb, sheet_name)
        if out_ws is None:
            out_ws = out_wb.create_sheet(title=sheet_name)
            copy_column_widths(src_ws, out_ws)

            # Je copie les en-tetes une seule fois, puis j'ajoute les lignes modifiees.
            dest_row = 1
            for src_header_row in range(1, header_row + 1):
                copy_row(src_ws, out_ws, src_header_row, dest_row)
                dest_row += 1

        for modified in sorted(modified_rows, key=lambda item: item.source_row):
            source_row_id = src_ws.cell(modified.source_row, 1).value
            existing_row = find_existing_override_row(out_ws, source_row_id)
            dest_row = existing_row if existing_row is not None else out_ws.max_row + 1

            copy_row(src_ws, out_ws, modified.source_row, dest_row)

            for col, new_value in modified.new_values_by_col.items():
                out_ws.cell(dest_row, col).value = new_value

            report.note(
                f"{source_label} / {sheet_name} / {modified.readable_context}: "
                + "; ".join(modified.changes)
            )

            total_rows_changed += 1

    if total_rows_changed == 0:
        return False

    output_xlsx.parent.mkdir(parents=True, exist_ok=True)
    out_wb.save(output_xlsx)
    report.file(output_xlsx)
    return True


# =============================================================================
# MANIFEST / README / CACHE
# =============================================================================

def make_supported_versions(game_version: str) -> list[str]:
    gv = game_version.strip()
    candidates = [
        gv,
        gv.lower(),
        gv.replace("Patch", "patch"),
        "2026.1",
    ]

    match = re.search(r"(\d{4}\.\d+)", gv)
    if match:
        candidates.append(match.group(1))

    return unique_preserve_order(candidates)


def write_manifest(mod_dir: Path, args: argparse.Namespace) -> None:
    manifest = {
        "name": MOD_DISPLAY_NAME,
        "version": MOD_VERSION,
        "description": (
            f"Immersion longue : air de base x{args.air_capacity_factor:g}, "
            f"conso air x1/{args.oxygen_consumption_factor:g}, "
            f"discipline x1/{args.discipline_factor:g}, "
            f"batterie x{args.battery_capacity_factor:g}, "
            f"consommation électrique x{args.energy_usage_factor:g}, "
            f"recharge batterie x{args.battery_capacity_factor:g}, "
            f"{args.fast_speed_top_gears} crans rapides x{args.fast_speed_factor:g}, "
            f"vitesse max joueur {args.player_submarine_max_speed:g} km/h. "
            "cette version garde la ventilation vanilla par défaut."
        ),
        "author": MOD_AUTHOR,
        "minGameVersion": "2026.1",
        "maxGameVersion": "",
        "supportedGameVersions": make_supported_versions(args.game_version),
        "assemblyName": MOD_ASSEMBLY_NAME,
        "permissions": ["Reflection"],
        "steamFileId": 0,
    }

    (mod_dir / "Manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def write_runtime_patch_source(mod_dir: Path, args: argparse.Namespace, report: PatchReport) -> None:
    source_dir = mod_dir / "Source"
    source_dir.mkdir(parents=True, exist_ok=True)
    source_path = source_dir / "LongSubmergedRuntimePatch.cs"

    source = r'''using System;
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
        private const float FastSpeedFactor = __FAST_SPEED_FACTOR__;
        private const int FastForwardGearCount = __FAST_FORWARD_GEAR_COUNT__;

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
'''

    source = source.replace("__FAST_SPEED_FACTOR__", format_csharp_float(args.fast_speed_factor))
    source = source.replace("__FAST_FORWARD_GEAR_COUNT__", str(args.fast_speed_top_gears))

    source_path.write_text(source, encoding="utf-8")
    report.file(source_path)
    report.note(
        "Patch runtime Harmony ajoute : recalcul de OxygenBreathModifier apres Awake, "
        "chargement de sauvegarde et changement d'equipage, plus vitesse rapide moteur."
    )


def write_readme(mod_dir: Path, args: argparse.Namespace, report: PatchReport | None = None) -> None:
    lines = [
        f"{MOD_DISPLAY_NAME} v{MOD_VERSION}",
        "",
        "Paramètres utilisés :",
        f"- Air / atmosphère de base : capacité x{args.air_capacity_factor:g}",
        f"- Oxygen Consumption Per Character : divisé par {args.oxygen_consumption_factor:g}",
        f"- Discipline/fatigue sous l'eau : divisé par {args.discipline_factor:g}",
        f"- Batterie / Accumulators : x{args.battery_capacity_factor:g}",
        f"- EnergyUsage consommateurs hors ventilation/compresseurs : x{args.energy_usage_factor:g}",
        f"- EnergyUsage recharge/production batterie : x{args.battery_capacity_factor:g}",
        f"- Deux derniers crans avant : x{args.fast_speed_factor:g}",
        f"- Vitesse max sous-marin joueur : {args.player_submarine_max_speed:g} km/h",
        "- DLC Type IX officiel : lignes joueur Type IXA/IXC/IXC40 incluses si le DLC est installe",
        f"- Ventilation vanilla : {'non' if args.patch_ventilation else 'oui'}",
        f"- Patch runtime : {MOD_ASSEMBLY_NAME}, air apres chargement et crans rapides moteur",
        "",
        "Installation :",
        "1. Fermer UBOAT.",
        "2. Générer avec --force --clear-cache.",
        "3. Activer le mod dans le launcher.",
        "4. Charger la sauvegarde ou démarrer une nouvelle carrière pour tester les changements d'air.",
        "",
        "Notes :",
        "- La jauge du jeu est une qualité d'air/atmosphère, pas un vrai compteur O2 détaillé.",
        "- La lumière bleue reste vanilla et doit toujours aider en immersion silencieuse.",
        "- La ventilation reste vanilla par défaut pour éviter les bugs vus dans les essais précédents.",
        "- Le patch runtime recalcule l'oxygène sur les sauvegardes existantes qui gardaient l'ancien -4/min.",
        "- Les vitesses lentes et mi-vitesse restent vanilla ; seuls les deux crans rapides avant sont boostés vers 40/45 km/h.",
        "- Le plafond de vitesse inclut les Type IX officiels du DLC Steam quand le DLC est installe.",
        "- Si un autre mod touche l'air, mets Long Submerged 10x+ après lui dans l'ordre de chargement.",
    ]

    if report is not None:
        lines.extend(
            [
                "",
                "Compteurs de génération :",
                f"- Lignes General Oxygen Consumption : {report.counters.get('general_oxygen_consumption_rows', 0)}",
                f"- Lignes capacité air Parameters : {report.counters.get('air_capacity_parameter_rows', 0)}",
                f"- Lignes capacité air cellules : {report.counters.get('air_capacity_cell_rows', 0)}",
                f"- Lignes batterie : {report.counters.get('battery_capacity_rows', 0)}",
                f"- Lignes EnergyUsage consommation : {report.counters.get('energy_usage_rows', 0)}",
                f"- Lignes EnergyUsage recharge : {report.counters.get('energy_recharge_rows', 0)}",
                f"- Lignes vitesse sous-marin joueur : {report.counters.get('player_submarine_speed_rows', 0)}",
            ]
        )

    (mod_dir / "README_LongSubmerged10x.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_report(mod_dir: Path, report: PatchReport) -> None:
    lines: list[str] = []

    lines.append("=== Rapport Long Submerged 10x+ ===")
    lines.append("")
    lines.append("Compteurs :")
    for key in sorted(report.counters):
        lines.append(f"- {key}: {report.counters[key]}")

    lines.append("")
    lines.append("Fichiers générés :")
    for path in report.changed_files:
        lines.append(f"- {path}")

    lines.append("")
    lines.append("Changements :")
    for item in report.info:
        lines.append(f"- {item}")

    if report.warnings:
        lines.append("")
        lines.append("Avertissements :")
        for warning in report.warnings:
            lines.append(f"- {warning}")

    (mod_dir / "LongSubmerged10x_generation_report.txt").write_text(
        "\n".join(lines) + "\n",
        encoding="utf-8",
    )


def clear_uboat_cache(local_uboat_dir: Path, report: PatchReport) -> None:
    for folder_name in ("Cache", "Data Sheets", "Temp"):
        folder = local_uboat_dir / folder_name

        if not folder.exists():
            continue

        for child in folder.iterdir():
            try:
                if child.is_dir():
                    shutil.rmtree(child)
                else:
                    child.unlink()
            except OSError as exc:
                report.warn(f"Impossible de supprimer {child}: {exc}")

        report.note(f"Cache vidé : {folder}")


# =============================================================================
# ARGUMENTS
# =============================================================================

def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Génère le mod UBOAT Long Submerged 10x+.",
    )

    parser.add_argument(
        "--uboat",
        type=Path,
        required=True,
        help=r"Chemin d'installation UBOAT, ex: C:\Program Files (x86)\Steam\steamapps\common\UBOAT",
    )

    parser.add_argument(
        "--out",
        type=Path,
        default=default_local_mods_root() / MOD_FOLDER_NAME,
        help=(
            "Dossier de sortie du mod. Par défaut : "
            "AppData\\LocalLow\\Deep Water Studio\\UBOAT\\Mods\\LongSubmerged10x"
        ),
    )

    parser.add_argument(
        "--force",
        action="store_true",
        help="Écrase le dossier de mod existant.",
    )

    parser.add_argument(
        "--clear-cache",
        action="store_true",
        help="Vide Cache, Data Sheets et Temp dans AppData\\LocalLow\\Deep Water Studio\\UBOAT.",
    )

    parser.add_argument(
        "--game-version",
        default=DEFAULT_GAME_VERSION,
        help='Version affichée par le launcher, ex: "2026.1 Patch 20".',
    )

    parser.add_argument(
        "--air-capacity-factor",
        type=float,
        default=DEFAULT_AIR_CAPACITY_FACTOR,
        help="Multiplicateur de la réserve d'air/atmosphère. Défaut : 125, pour viser 7 à 8 jours.",
    )

    parser.add_argument(
        "--oxygen-consumption-factor",
        type=float,
        default=DEFAULT_OXYGEN_CONSUMPTION_FACTOR,
        help="Divise Oxygen Consumption Per Character par ce facteur. Défaut : 125.",
    )

    parser.add_argument(
        "--discipline-factor",
        type=float,
        default=DEFAULT_DISCIPLINE_FACTOR,
        help="Divise discipline/fatigue sous l'eau par ce facteur. Défaut : 15.",
    )

    parser.add_argument(
        "--battery-capacity-factor",
        type=float,
        default=DEFAULT_BATTERY_CAPACITY_FACTOR,
        help="Multiplicateur capacité batterie / Accumulators. Défaut : 10.",
    )

    parser.add_argument(
        "--energy-usage-factor",
        type=float,
        default=DEFAULT_EQUIPMENT_ENERGY_USAGE_FACTOR,
        help="Multiplicateur EnergyUsage positif hors ventilation/compresseurs. Défaut : 0.1.",
    )

    parser.add_argument(
        "--fast-speed-factor",
        type=float,
        default=DEFAULT_FAST_SPEED_FACTOR,
        help="Multiplicateur des derniers crans rapides de marche avant. Défaut : 2.",
    )

    parser.add_argument(
        "--fast-speed-top-gears",
        type=int,
        default=DEFAULT_FAST_SPEED_TOP_GEARS,
        help="Nombre de crans rapides avant à booster. Défaut : 2.",
    )

    parser.add_argument(
        "--player-submarine-max-speed",
        type=float,
        default=DEFAULT_PLAYER_SUBMARINE_MAX_SPEED,
        help="Vitesse max km/h des types de sous-marins joueur. Défaut : 45.",
    )

    parser.add_argument(
        "--patch-ventilation",
        action="store_true",
        default=DEFAULT_PATCH_VENTILATION,
        help="Optionnel : patche aussi OxygenGain de la ventilation. Désactivé par défaut.",
    )

    parser.add_argument(
        "--ventilation-gain-factor",
        type=float,
        default=DEFAULT_VENTILATION_GAIN_FACTOR,
        help="Si --patch-ventilation est activé, multiplie OxygenGain par ce facteur. Défaut : 1.",
    )

    parser.add_argument(
        "--patch-potassium",
        action="store_true",
        default=DEFAULT_PATCH_POTASSIUM,
        help="Optionnel : allonge les durées des absorbeurs potassium.",
    )

    parser.add_argument(
        "--potassium-duration-factor",
        type=float,
        default=DEFAULT_POTASSIUM_DURATION_FACTOR,
        help="Si --patch-potassium est activé, multiplie les durées par ce facteur. Défaut : 1.",
    )

    parser.add_argument(
        "--patch-energy-base-scale",
        action="store_true",
        default=DEFAULT_PATCH_ENERGY_BASE_SCALE,
        help="Patche aussi General.xlsx / Energy Base Scale. Désactivé par défaut.",
    )

    args = parser.parse_args(argv)

    positive_names = (
        "air_capacity_factor",
        "oxygen_consumption_factor",
        "discipline_factor",
        "battery_capacity_factor",
        "energy_usage_factor",
        "fast_speed_factor",
        "player_submarine_max_speed",
        "ventilation_gain_factor",
        "potassium_duration_factor",
    )

    for name in positive_names:
        value = getattr(args, name)
        if value <= 0:
            raise ValueError(f"--{name.replace('_', '-')} doit être > 0.")

    if args.fast_speed_top_gears <= 0:
        raise ValueError("--fast-speed-top-gears doit être > 0.")

    return args


# =============================================================================
# MAIN
# =============================================================================

def main(argv: list[str]) -> int:
    try:
        args = parse_args(argv)
    except Exception as exc:
        print(f"Arguments invalides : {exc}", file=sys.stderr)
        return 2

    report = PatchReport()
    out_mod_dir: Path | None = None

    try:
        uboat_root = args.uboat.expanduser().resolve()
        data_sheets_dir = resolve_data_sheets_dir(uboat_root)

        out_mod_dir = args.out.expanduser().resolve()
        out_data_sheets = out_mod_dir / "Data Sheets"

        ensure_clean_directory(out_mod_dir, force=args.force)
        out_data_sheets.mkdir(parents=True, exist_ok=True)

        write_manifest(out_mod_dir, args)
        write_runtime_patch_source(out_mod_dir, args, report)

        # 1) General.xlsx et Realistic Travel/General.xlsx.
        general_patches = build_general_patches(args)
        general_changed = 0

        for vanilla_general in find_datasheet_files(data_sheets_dir, "General.xlsx"):
            relative = vanilla_general.relative_to(data_sheets_dir)
            output_general = out_data_sheets / relative

            if create_general_override(
                vanilla_general,
                output_general,
                general_patches,
                report,
                data_sheets_dir,
            ):
                general_changed += 1

        if general_changed == 0:
            report.warn(
                "Aucun General.xlsx patché. "
                "La ligne Oxygen Consumption Per Character et la discipline ne changeront pas."
            )

        # 2) XLSX de gameplay : batterie, EnergyUsage, capacité d'air exposée dans Parameters/cellules.
        # Je ne scanne pas les gros fichiers Locales/Story/Graphics : ils ne portent pas ces systèmes
        # et ralentissent inutilement la génération.
        generic_changed = 0
        generic_targets = (
            "Entities.xlsx",
            "U-boat.xlsx",
            "Sandbox.xlsx",
            "CharacterClasses.xlsx",
        )
        type_ix_dlc_data_sheets = find_type_ix_dlc_data_sheets_dir(uboat_root)

        if type_ix_dlc_data_sheets is not None:
            report.note(f"DLC Type IX detecte : {type_ix_dlc_data_sheets}")

        for target_name in generic_targets:
            for vanilla_xlsx in find_datasheet_files(data_sheets_dir, target_name):
                relative = vanilla_xlsx.relative_to(data_sheets_dir)
                output_xlsx = out_data_sheets / relative

                if create_generic_xlsx_override(
                    vanilla_xlsx,
                    output_xlsx,
                    report,
                    data_sheets_dir,
                    args,
                ):
                    generic_changed += 1

            if type_ix_dlc_data_sheets is None:
                continue

            for dlc_xlsx in find_datasheet_files(type_ix_dlc_data_sheets, target_name):
                relative = dlc_xlsx.relative_to(type_ix_dlc_data_sheets)
                output_xlsx = out_data_sheets / relative

                if create_generic_xlsx_override(
                    dlc_xlsx,
                    output_xlsx,
                    report,
                    type_ix_dlc_data_sheets,
                    args,
                ):
                    generic_changed += 1

        if generic_changed == 0:
            report.warn(
                "Aucun XLSX hors General.xlsx n'a été patché. "
                "La batterie ou la capacité air exposée dans Entities/U-boat ne changera peut-être pas."
            )

        # Alertes ciblées.
        air_rows = (
            report.counters.get("air_capacity_parameter_rows", 0)
            + report.counters.get("air_capacity_cell_rows", 0)
        )

        if air_rows == 0:
            report.warn(
                "Aucune ligne de capacité d'air/atmosphère n'a été trouvée. "
                "Le mod a quand même réduit Oxygen Consumption Per Character, mais si l'UI reste à 13h, "
                "il faudra récupérer dans le rapport la ligne contenant Air/Oxygen/Atmosphere du fichier local."
            )

        if report.counters.get("battery_capacity_rows", 0) == 0:
            report.warn(
                "Aucune ligne Accumulators/Battery patchée. "
                "Si la batterie ne reste pas longue durée, vérifie le nom local de la ligne accumulateurs."
            )

        if args.clear_cache:
            clear_uboat_cache(default_local_uboat_dir(), report)

        write_readme(out_mod_dir, args, report)
        write_report(out_mod_dir, report)

    except Exception as exc:
        print("\nERREUR : génération impossible.", file=sys.stderr)
        print(str(exc), file=sys.stderr)
        return 1

    assert out_mod_dir is not None

    print("\n=== Long Submerged 10x+ généré avec succès ===")
    print(f"Dossier du mod : {out_mod_dir}")
    print(f"Version déclarée : {args.game_version}")

    print("\nProfil appliqué :")
    print(f"  - Air / atmosphère de base : x{args.air_capacity_factor:g}")
    print(f"  - Oxygen Consumption Per Character : /{args.oxygen_consumption_factor:g}")
    print(f"  - Discipline/fatigue : /{args.discipline_factor:g}")
    print(f"  - Batterie Accumulators : x{args.battery_capacity_factor:g}")
    print(f"  - EnergyUsage consommateurs hors ventilation/compresseurs : x{args.energy_usage_factor:g}")
    print(f"  - EnergyUsage recharge/production batterie : x{args.battery_capacity_factor:g}")
    print(f"  - Deux derniers crans avant : x{args.fast_speed_factor:g}")
    print(f"  - Vitesse max sous-marin joueur : {args.player_submarine_max_speed:g} km/h")
    print(f"  - Ventilation vanilla : {'NON, patchée' if args.patch_ventilation else 'OUI, laissée normale'}")

    print("\nCompteurs importants :")
    print(f"  - General Oxygen Consumption : {report.counters.get('general_oxygen_consumption_rows', 0)}")
    print(f"  - Capacité air dans Parameters : {report.counters.get('air_capacity_parameter_rows', 0)}")
    print(f"  - Capacité air dans cellules : {report.counters.get('air_capacity_cell_rows', 0)}")
    print(f"  - Batterie : {report.counters.get('battery_capacity_rows', 0)}")
    print(f"  - EnergyUsage consommation : {report.counters.get('energy_usage_rows', 0)}")
    print(f"  - EnergyUsage recharge : {report.counters.get('energy_recharge_rows', 0)}")
    print(f"  - Vitesse sous-marin joueur : {report.counters.get('player_submarine_speed_rows', 0)}")

    print("\nFichiers générés :")
    print(f"  - {out_mod_dir / 'Manifest.json'}")
    print(f"  - {out_mod_dir / 'README_LongSubmerged10x.txt'}")
    print(f"  - {out_mod_dir / 'LongSubmerged10x_generation_report.txt'}")
    for file_path in report.changed_files:
        print(f"  - {file_path}")

    if report.warnings:
        print("\nAvertissements :")
        for warning in report.warnings:
            print(f"  - {warning}")

    print("\nÉtapes obligatoires :")
    print("  1. Ferme complètement UBOAT.")
    print("  2. Lance le script avec --force --clear-cache.")
    print("  3. Active Long Submerged 10x+ dans le launcher.")
    print("  4. Place-le après les autres mods qui touchent General.xlsx, Entities.xlsx ou U-boat.xlsx.")
    print("  5. Charge ta sauvegarde existante ou lance une nouvelle carrière.")
    print("  6. Plonge à 100% air : le tooltip doit passer d'environ 13h à environ 8 jours.")
    print("  7. Teste pleine vitesse / machines à fond : on vise environ 40/45 km/h.")
    print("  8. Si la ventilation était cassée par une ancienne version, celle-ci doit la remettre vanilla car elle ne copie plus la ligne Ventilation.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
