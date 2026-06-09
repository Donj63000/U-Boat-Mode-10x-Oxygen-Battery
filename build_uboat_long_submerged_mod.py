#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
UBOAT - Long Submerged 10x+
Générateur de mod Data Sheets.

But de cette version :
- Garder la batterie longue durée qui fonctionne déjà.
- Corriger l'air / "qualité de l'air" pour viser environ 90 jours de base.
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
MOD_VERSION = "1.4.1"
MOD_AUTHOR = "VotreNomOuVotreEquipe"
MOD_ASSEMBLY_NAME = "LongSubmerged10xPatch_1_4_1"

DEFAULT_GAME_VERSION = "2026.1 Patch 20"

# Ton retour en jeu donne environ 6 à 7 jours avec le réglage 125.
# 1800 / 125 = x14.4, donc je vise environ 90 jours en conditions réelles.
DEFAULT_AIR_CAPACITY_FACTOR = 1800.0

# On garde aussi la baisse de consommation par personnage, mais ce n'est plus le levier principal :
# certaines versions / configs semblent garder un minimum visible dans l'UI, par exemple "Équipage -4/min".
DEFAULT_OXYGEN_CONSUMPTION_FACTOR = 1800.0

# Discipline/fatigue proportionnelles à l'immersion longue.
DEFAULT_DISCIPLINE_FACTOR = 15.0

# Batterie : le XLSX garde un fallback x0.1 restaurable, mais le runtime Mega Batterie coupe le drain.
DEFAULT_BATTERY_CAPACITY_FACTOR = 10.0
DEFAULT_EQUIPMENT_ENERGY_USAGE_FACTOR = 0.10

# Vitesse : seuls les deux derniers crans avant sont boostes.
DEFAULT_FAST_SPEED_FACTOR = 3.5
DEFAULT_FAST_SPEED_FUEL_FACTOR = 8.0
DEFAULT_FAST_SPEED_TOP_GEARS = 2
DEFAULT_PLAYER_SUBMARINE_MAX_SPEED = 45.0

# Mega torpilles : active par defaut, parce que le mod doit livrer le comportement demande.
# Je touche uniquement aux degats et aux effets d'explosion, pas a la vitesse ni a la portee.
DEFAULT_MEGA_TORPEDOES = True
DEFAULT_TORPEDO_DAMAGE_FACTOR = 10.0
DEFAULT_TORPEDO_CREW_DAMAGE_FACTOR = 10.0
DEFAULT_TORPEDO_EXPLOSION_RADIUS_FACTOR = 10.0
DEFAULT_TORPEDO_EXPLOSION_INTENSITY_FACTOR = 10.0
DEFAULT_PERFECT_TORPEDO_RELIABILITY = True
DEFAULT_TORPEDO_DUD_CHANCE = 0.0
DEFAULT_TORPEDO_MAGNETIC_FAILURE_CHANCE = 0.0
DEFAULT_TORPEDO_PREMATURE_MAGNETIC_CHANCE = 0.0

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
    - valeur négative : l'équipement produit/recharge, donc je laisse la recharge vanilla.
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
            return match.group(0)

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

    # Conso/recharge électrique : je réduis les consommateurs et je garde les rechargeurs en vanilla.
    # Multiplier les EnergyUsage négatifs rendait les diesels trop rapides à recharger.
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

    # Torpilles : je les applique maintenant en runtime pour pouvoir les couper proprement dans le menu.
    # Les XLSX gardent les valeurs vanilla, le toggle Mega Torpilles ajoute/retire les multiplicateurs en jeu.

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
            f"consommation electrique runtime 0 par defaut, fallback x{args.energy_usage_factor:g}, "
            "recharge diesel vanilla, "
            f"{args.fast_speed_top_gears} crans rapides vitesse x{args.fast_speed_factor:g}, "
            f"carburant rapide x{args.fast_speed_fuel_factor:g}, "
            f"vitesse max joueur {args.player_submarine_max_speed:g} km/h, menu runtime F10. "
            "sliders runtime 1-100 pour batterie, oxygene, SuperVitesse et torpilles. "
            + (
                f"mega torpilles : degats x{args.torpedo_damage_factor:g}, "
                f"rayon explosion x{args.torpedo_explosion_radius_factor:g}, "
                f"intensite explosion x{args.torpedo_explosion_intensity_factor:g}, "
                "guidage cible verrouillee. "
                if args.mega_torpedoes
                else "mega torpilles desactivees. "
            ) +
            (
                "fiabilite torpilles parfaite : DudChance et defaillances magnetiques a 0. "
                if args.perfect_torpedo_reliability
                else "fiabilite torpilles vanilla. "
            ) +
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
using System.Text;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.UI.Notifications;
using UBOAT.Game.UI.Resources;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongSubmerged10x
{
    public sealed class LongSubmergedRuntimePatchMod : IUserMod
    {
        private const string RuntimeVersion = "__MOD_VERSION__";

        public string Name
        {
            get { return "Long Submerged 10x+ AirFix"; }
        }

        public void OnLoaded()
        {
            try
            {
                // La je charge les reglages et le menu avant Harmony.
                // Comme ca le garde-fou batterie tourne meme si un patch Harmony ne passe pas.
                LongSubmergedRuntimeSettings.Load();
                LongSubmergedMenuController.Ensure();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            try
            {
                new Harmony("donj.longsubmerged10x.airfix").PatchAll();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LongSubmerged10x] Harmony PatchAll a echoue, mais le runtime direct batterie continue: " + ex.GetType().Name + ": " + ex.Message);
                Debug.LogException(ex);
            }

            try
            {
                LongSubmergedRuntimeApplier.ApplyAll("mod loaded");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            Debug.Log("[LongSubmerged10x] Runtime patch loaded v" + RuntimeVersion + ". F10 ouvre le menu Long Submerged.");
        }
    }

    internal static class LongSubmergedRuntimePatcher
    {
        private static readonly Type[] PatchTypes = new Type[]
        {
            typeof(PlayerShipAwakePatch),
            typeof(PlayerShipOnAfterDeserializePatch),
            typeof(PlayerShipUpdatePatch),
            typeof(ResourceUpdateAmountBatteryPatch),
            typeof(PlayerShipValidateTargetVelocityPatch),
            typeof(PlayerShipSavesManagerOnLoadedPatch),
            typeof(PlayerShipCrewAddedPatch),
            typeof(PlayerShipCrewRemovedPatch),
            typeof(PlayerShipEngineAwakePatch),
            typeof(PlayerShipEngineOnAfterDeserializePatch),
            typeof(PlayerShipEngineSavesManagerOnLoadedPatch),
            typeof(AccumulatorsUpgradeStartPatch),
            typeof(DivingPlanesStationAwakePatch),
            typeof(DivingPlanesStationUpdateModifiersPatch),
            typeof(AirCompressorOnEnablePatch),
            typeof(AirCompressorEnergyUsageChangedPatch),
            typeof(GyrocompassApplyModifiersPatch),
            typeof(TrimPumpOnEnablePatch),
            typeof(VentilationOnEnablePatch),
            typeof(StoredTorpedoStartPatch),
            typeof(StoredTorpedoApplyWarmUpModifierPatch),
            typeof(TorpedoAwakePatch),
            typeof(TorpedoFixedUpdatePatch),
            typeof(TorpedoDetonatePatch),
            typeof(ResourceGuiGetTooltipContentsPatch),
            typeof(ResourceGuiUpdateDisplayedValuePatch),
            typeof(DepletingResourceNotificationDoUpdatePatch)
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
                    // Une seule methode renomme dans UBOAT ne doit plus neutraliser toute la batterie infinie.
                    Debug.LogWarning("[LongSubmerged10x] Harmony patch skipped: " + patchType.Name + " -> " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }

    internal static class LongSubmergedRuntimeSettings
    {
        private const string PrefPrefix = "LongSubmerged10x.";
        private const int RuntimeSettingsVersion = 7;
        public const float MinRuntimeFactor = 1f;
        public const float MaxRuntimeFactor = 100f;
        private const bool DefaultMegaBattery = true;
        private const bool DefaultMegaOxygen = true;
        private const bool DefaultSuperSpeed = true;
        private const bool DefaultMegaTorpedoes = __DEFAULT_MEGA_TORPEDOES__;
        private const float DefaultBatteryFactor = 100f;
        private const float DefaultOxygenFactor = 100f;
        private const float DefaultSpeedFactor = __FAST_SPEED_FACTOR__;
        private const float DefaultTorpedoFactor = __TORPEDO_DAMAGE_FACTOR__;

        public static bool MegaBattery = DefaultMegaBattery;
        public static bool MegaOxygen = DefaultMegaOxygen;
        public static bool SuperSpeed = DefaultSuperSpeed;
        public static bool MegaTorpedoes = DefaultMegaTorpedoes;
        public static float BatteryFactor = DefaultBatteryFactor;
        public static float OxygenFactor = DefaultOxygenFactor;
        public static float SpeedFactor = DefaultSpeedFactor;
        public static float TorpedoFactor = DefaultTorpedoFactor;

        public static void Load()
        {
            if (PlayerPrefs.GetInt(PrefPrefix + "RuntimeSettingsVersion", 0) < RuntimeSettingsVersion)
            {
                ResetToDefaults();
                Save();
                Debug.Log("[LongSubmerged10x] Runtime settings migrated to defaults v" + RuntimeSettingsVersion + ".");
                return;
            }

            MegaBattery = ReadBool("MegaBattery", DefaultMegaBattery);
            MegaOxygen = ReadBool("MegaOxygen", DefaultMegaOxygen);
            SuperSpeed = ReadBool("SuperSpeed", DefaultSuperSpeed);
            MegaTorpedoes = ReadBool("MegaTorpedoes", DefaultMegaTorpedoes);
            BatteryFactor = ReadFactor("BatteryFactor", DefaultBatteryFactor);
            OxygenFactor = ReadFactor("OxygenFactor", DefaultOxygenFactor);
            SpeedFactor = ReadFactor("SpeedFactor", DefaultSpeedFactor);
            TorpedoFactor = ReadFactor("TorpedoFactor", DefaultTorpedoFactor);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(PrefPrefix + "MegaBattery", MegaBattery ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaOxygen", MegaOxygen ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "SuperSpeed", SuperSpeed ? 1 : 0);
            PlayerPrefs.SetInt(PrefPrefix + "MegaTorpedoes", MegaTorpedoes ? 1 : 0);
            PlayerPrefs.SetFloat(PrefPrefix + "BatteryFactor", ClampFactor(BatteryFactor));
            PlayerPrefs.SetFloat(PrefPrefix + "OxygenFactor", ClampFactor(OxygenFactor));
            PlayerPrefs.SetFloat(PrefPrefix + "SpeedFactor", ClampFactor(SpeedFactor));
            PlayerPrefs.SetFloat(PrefPrefix + "TorpedoFactor", ClampFactor(TorpedoFactor));
            PlayerPrefs.SetInt(PrefPrefix + "RuntimeSettingsVersion", RuntimeSettingsVersion);
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            MegaBattery = DefaultMegaBattery;
            MegaOxygen = DefaultMegaOxygen;
            SuperSpeed = DefaultSuperSpeed;
            MegaTorpedoes = DefaultMegaTorpedoes;
            BatteryFactor = DefaultBatteryFactor;
            OxygenFactor = DefaultOxygenFactor;
            SpeedFactor = DefaultSpeedFactor;
            TorpedoFactor = DefaultTorpedoFactor;
        }

        public static float ClampFactor(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return MinRuntimeFactor;

            return Mathf.Clamp(value, MinRuntimeFactor, MaxRuntimeFactor);
        }

        private static bool ReadBool(string key, bool fallback)
        {
            return PlayerPrefs.GetInt(PrefPrefix + key, fallback ? 1 : 0) != 0;
        }

        private static float ReadFactor(string key, float fallback)
        {
            return ClampFactor(PlayerPrefs.GetFloat(PrefPrefix + key, fallback));
        }
    }

    internal sealed class LongSubmergedMenuController : MonoBehaviour
    {
        private const KeyCode MenuKey = KeyCode.F10;
        private const int CanvasSortingOrder = 32000;
        private const float BatteryMaintenanceIntervalSeconds = 0.20f;
        private static LongSubmergedMenuController instance;
        private static Font cachedFont;

        private GameObject panelObject;
        private Toggle megaBatteryToggle;
        private Toggle megaOxygenToggle;
        private Toggle superSpeedToggle;
        private Toggle megaTorpedoesToggle;
        private Slider batteryFactorSlider;
        private Slider oxygenFactorSlider;
        private Slider speedFactorSlider;
        private Slider torpedoFactorSlider;
        private Text batteryFactorValueText;
        private Text oxygenFactorValueText;
        private Text speedFactorValueText;
        private Text torpedoFactorValueText;
        private bool visible;
        private bool suppressToggleEvents;
        private float nextBatteryMaintenanceTime;
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
        }

        private void RunBatteryMaintenanceTick()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return;

            float now = Time.unscaledTime;
            if (now < nextBatteryMaintenanceTime)
                return;

            nextBatteryMaintenanceTime = now + BatteryMaintenanceIntervalSeconds;
            LongSubmergedRuntimeApplier.MaintainBatteryRuntime("runtime heartbeat");
        }

        private void EnsureUi()
        {
            if (panelObject != null)
                return;

            try
            {
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
            panelObject = CreateUiObject("LongSubmerged10x Panel", transform);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.04f, 0.05f, 0.06f, 0.96f);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(28f, -82f);
            panelRect.sizeDelta = new Vector2(470f, 512f);

            CreateText(panelObject.transform, "Title", "Long Submerged 10x+", 20, FontStyle.Bold, new Vector2(18f, -16f), new Vector2(410f, 30f));
            CreateText(panelObject.transform, "Hint", "F10 ferme. Les reglages sont sauvegardes et appliques en partie.", 13, FontStyle.Normal, new Vector2(18f, -48f), new Vector2(430f, 24f));

            megaBatteryToggle = CreateToggle(panelObject.transform, "Mega Batterie", new Vector2(20f, -82f));
            batteryFactorSlider = CreateFactorSlider(panelObject.transform, "Batterie", new Vector2(20f, -118f), out batteryFactorValueText);

            megaOxygenToggle = CreateToggle(panelObject.transform, "Mega Oxygene", new Vector2(20f, -158f));
            oxygenFactorSlider = CreateFactorSlider(panelObject.transform, "Oxygene", new Vector2(20f, -194f), out oxygenFactorValueText);

            superSpeedToggle = CreateToggle(panelObject.transform, "SuperVitesse", new Vector2(20f, -234f));
            speedFactorSlider = CreateFactorSlider(panelObject.transform, "Vitesses rapides", new Vector2(20f, -270f), out speedFactorValueText);

            megaTorpedoesToggle = CreateToggle(panelObject.transform, "Mega Torpilles", new Vector2(20f, -310f));
            torpedoFactorSlider = CreateFactorSlider(panelObject.transform, "Torpilles", new Vector2(20f, -346f), out torpedoFactorValueText);

            Button defaultsButton = CreateButton(panelObject.transform, "Par defaut", new Vector2(20f, -430f), new Vector2(140f, 38f));
            defaultsButton.onClick.AddListener(OnDefaultsClicked);

            Button refreshButton = CreateButton(panelObject.transform, "Reappliquer maintenant", new Vector2(176f, -430f), new Vector2(220f, 38f));
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

            LongSubmergedRuntimeSettings.MegaBattery = megaBatteryToggle != null && megaBatteryToggle.isOn;
            LongSubmergedRuntimeSettings.MegaOxygen = megaOxygenToggle != null && megaOxygenToggle.isOn;
            LongSubmergedRuntimeSettings.SuperSpeed = superSpeedToggle != null && superSpeedToggle.isOn;
            LongSubmergedRuntimeSettings.MegaTorpedoes = megaTorpedoesToggle != null && megaTorpedoesToggle.isOn;
            LongSubmergedRuntimeSettings.BatteryFactor = ReadSliderFactor(batteryFactorSlider);
            LongSubmergedRuntimeSettings.OxygenFactor = ReadSliderFactor(oxygenFactorSlider);
            LongSubmergedRuntimeSettings.SpeedFactor = ReadSliderFactor(speedFactorSlider);
            LongSubmergedRuntimeSettings.TorpedoFactor = ReadSliderFactor(torpedoFactorSlider);
            LongSubmergedRuntimeSettings.Save();
            LongSubmergedRuntimeApplier.ApplyAll("unity ui toggle");
        }

        private void OnFactorSliderChanged(float ignored)
        {
            if (suppressToggleEvents)
                return;

            OnToggleChanged(false);
            RefreshFactorLabels();
        }

        private void OnDefaultsClicked()
        {
            LongSubmergedRuntimeSettings.ResetToDefaults();
            LongSubmergedRuntimeSettings.Save();
            RefreshControlState();
            LongSubmergedRuntimeApplier.ApplyAll("unity ui defaults");
        }

        private void OnRefreshClicked()
        {
            LongSubmergedRuntimeApplier.ApplyAll("unity ui refresh");
        }

        private void RefreshControlState()
        {
            suppressToggleEvents = true;

            if (megaBatteryToggle != null)
                megaBatteryToggle.isOn = LongSubmergedRuntimeSettings.MegaBattery;

            if (megaOxygenToggle != null)
                megaOxygenToggle.isOn = LongSubmergedRuntimeSettings.MegaOxygen;

            if (superSpeedToggle != null)
                superSpeedToggle.isOn = LongSubmergedRuntimeSettings.SuperSpeed;

            if (megaTorpedoesToggle != null)
                megaTorpedoesToggle.isOn = LongSubmergedRuntimeSettings.MegaTorpedoes;

            SetSliderValue(batteryFactorSlider, LongSubmergedRuntimeSettings.BatteryFactor);
            SetSliderValue(oxygenFactorSlider, LongSubmergedRuntimeSettings.OxygenFactor);
            SetSliderValue(speedFactorSlider, LongSubmergedRuntimeSettings.SpeedFactor);
            SetSliderValue(torpedoFactorSlider, LongSubmergedRuntimeSettings.TorpedoFactor);
            RefreshFactorLabels();

            suppressToggleEvents = false;
        }

        private void RefreshFactorLabels()
        {
            SetFactorLabel(batteryFactorValueText, batteryFactorSlider, "x", batteryFactorSlider != null && batteryFactorSlider.value >= LongSubmergedRuntimeSettings.MaxRuntimeFactor ? "inf" : null);
            SetFactorLabel(oxygenFactorValueText, oxygenFactorSlider, "x", oxygenFactorSlider != null && oxygenFactorSlider.value >= LongSubmergedRuntimeSettings.MaxRuntimeFactor ? "90j" : null);
            SetFactorLabel(speedFactorValueText, speedFactorSlider, "x", null);
            SetFactorLabel(torpedoFactorValueText, torpedoFactorSlider, "x", null);
        }

        private static void SetSliderValue(Slider slider, float value)
        {
            if (slider == null)
                return;

            slider.value = LongSubmergedRuntimeSettings.ClampFactor(value);
        }

        private static float ReadSliderFactor(Slider slider)
        {
            return slider == null ? LongSubmergedRuntimeSettings.MinRuntimeFactor : LongSubmergedRuntimeSettings.ClampFactor(slider.value);
        }

        private static void SetFactorLabel(Text text, Slider slider, string prefix, string suffixOverride)
        {
            if (text == null || slider == null)
                return;

            float value = LongSubmergedRuntimeSettings.ClampFactor(slider.value);
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

        private Slider CreateFactorSlider(Transform parent, string label, Vector2 anchoredPosition, out Text valueText)
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
            slider.maxValue = LongSubmergedRuntimeSettings.MaxRuntimeFactor;
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

    internal static class LongSubmergedRuntimeApplier
    {
        private const float OxygenVanillaRestoreFactor = __OXYGEN_CONSUMPTION_FACTOR__;
        private const float BatteryCapacityDataFactor = __BATTERY_CAPACITY_FACTOR__;
        private const float EnergyUsageDataFactor = __ENERGY_USAGE_FACTOR__;
        private const float BatteryCapacityVanillaRestoreScale = 1f / __BATTERY_CAPACITY_FACTOR__;
        private const float EnergyUsageVanillaRestoreScale = 1f / __ENERGY_USAGE_FACTOR__;
        private const float TorpedoDamageScale = __TORPEDO_DAMAGE_FACTOR__;
        private const float TorpedoCrewDamageScale = __TORPEDO_CREW_DAMAGE_FACTOR__;
        private const float TorpedoExplosionRadiusScale = __TORPEDO_EXPLOSION_RADIUS_FACTOR__;
        private const float TorpedoExplosionIntensityScale = __TORPEDO_EXPLOSION_INTENSITY_FACTOR__;
        private const bool PerfectTorpedoReliability = __PERFECT_TORPEDO_RELIABILITY__;
        private const float TorpedoGuidanceLeadSeconds = 4f;
        private const float TorpedoGuidanceMinimumDetonationDistance = 20f;
        private const float TorpedoGuidanceMaximumDetonationDistance = 80f;
        private const float TorpedoGuidanceDetonationRadiusRatio = 0.75f;
        private const string RuntimeScaleModifierName = "LongSubmerged10x Runtime Toggle";
        private const string RuntimeBatteryGainModifierName = "LongSubmerged10x Battery Gain Runtime";
        private const string RuntimeNuclearBatteryCapacityModifierName = "LongSubmerged10x Nuclear Battery Capacity Runtime";
        private const float NuclearBatteryCapacityFloor = 1000000000f;

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

        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> BatteryGainDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<Parameter, ParameterDeltaPatchData> NuclearBatteryCapacityDeltaData =
            new ConditionalWeakTable<Parameter, ParameterDeltaPatchData>();

        private static readonly ConditionalWeakTable<Torpedo, TorpedoGuidancePatchData> TorpedoGuidanceData =
            new ConditionalWeakTable<Torpedo, TorpedoGuidancePatchData>();

        private static readonly HashSet<int> InfiniteBatteryLoggedShipIds = new HashSet<int>();

        private static readonly HashSet<int> BatteryGainRuntimeLoggedResourceIds = new HashSet<int>();

        private static readonly HashSet<int> NuclearBatteryCapacityLoggedResourceIds = new HashSet<int>();

        private static readonly HashSet<int> BatteryTooltipRuntimeLoggedResourceIds = new HashSet<int>();

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
                LongSubmergedMenuController.Ensure();
                ApplyPlayerShip(UnityEngine.Object.FindObjectOfType<PlayerShip>(), reason);
                ApplyBatteryConsumers(reason);

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
                foreach (AccumulatorsUpgrade item in UnityEngine.Object.FindObjectsOfType<AccumulatorsUpgrade>())
                    ApplyBatteryObject(item, reason + ".AccumulatorsUpgrade");

                foreach (PlayerShipEngine item in UnityEngine.Object.FindObjectsOfType<PlayerShipEngine>())
                    ApplyBatteryObject(item, reason + ".PlayerShipEngine");

                foreach (DivingPlanesStation item in UnityEngine.Object.FindObjectsOfType<DivingPlanesStation>())
                    ApplyBatteryObject(item, reason + ".DivingPlanesStation");

                foreach (AirCompressor item in UnityEngine.Object.FindObjectsOfType<AirCompressor>())
                    ApplyBatteryObject(item, reason + ".AirCompressor");

                foreach (Gyrocompass item in UnityEngine.Object.FindObjectsOfType<Gyrocompass>())
                    ApplyBatteryObject(item, reason + ".Gyrocompass");

                foreach (TrimPump item in UnityEngine.Object.FindObjectsOfType<TrimPump>())
                    ApplyBatteryObject(item, reason + ".TrimPump");

                foreach (Ventilation item in UnityEngine.Object.FindObjectsOfType<Ventilation>())
                    ApplyBatteryObject(item, reason + ".Ventilation");

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

        public static bool TryUpdateBatteryResourceAmount(Resource resource, string reason)
        {
            try
            {
                if (!IsPlayerShipEnergyResource(resource))
                    return false;

                ApplyBatteryRuntimeToResource(resource, reason);

                // En mode infini on bloque l'UpdateAmount vanilla : la ressource reste pleine.
                return IsInfiniteBatteryRuntimeActive();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private static void ApplyBatteryRuntimeToResource(Resource energy, string reason)
        {
            if (energy == null)
                return;

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
            if (resource == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            resource.PrintInfo(builder, 1, 1f, "per min", string.Empty, false);
            builder.Append("<line-height=50%>\n<line-height=100%>");
            builder.AppendLine("Mega Batterie : batterie infinie active.");
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
                // La je garde la batterie au maximum avec le setter Amount pour forcer aussi le refresh UI.
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

            if (factor >= LongSubmergedRuntimeSettings.MaxRuntimeFactor - 0.0001f)
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

            return LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.BatteryFactor);
        }

        private static void ApplyBatteryGainParameter(Parameter parameter, float factor)
        {
            if (parameter == null)
                return;

            float baseValue = parameter.GetValueExcludingModifier(RuntimeBatteryGainModifierName);
            float desiredValue = baseValue;

            // Le slider Batterie regle deja la duree via la capacite effective.
            // Ici on ne touche au gain global que pour le cran 100/infini, afin d'eviter un double xN.
            if (factor >= LongSubmergedRuntimeSettings.MaxRuntimeFactor - 0.0001f && baseValue < 0f)
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

            if (owner != null && object.ReferenceEquals(owner.Energy, resource))
                return true;

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

        public static void RestoreVanillaOxygenIfNeeded(PlayerShip ship)
        {
            if (ship == null || OxygenBreathModifierField == null)
                return;

            Modifier oxygenModifier = OxygenBreathModifierField.GetValue(ship) as Modifier;
            if (oxygenModifier == null)
                return;

            // La je compense le XLSX pour que le slider 1 soit vanilla et 100 garde mon profil environ 90 jours.
            oxygenModifier.Value *= OxygenVanillaRestoreFactor / GetEffectiveOxygenDataFactor();
        }

        public static void ApplyBatteryObject(object target, string reason)
        {
            if (target == null)
                return;

            Equipment equipment = target as Equipment;
            if (equipment != null)
                ApplyBatteryEquipment(equipment, reason);

            ApplyBatteryCapacityParameter(GetParameterField(target, "energyCapacityGain"));
            Parameter energyUsage = GetParameterField(target, "energyUsage");
            ApplyEnergyUsageParameter(energyUsage);
            ApplyDirectEnergyGainModifier(target, energyUsage, reason);
        }

        public static void ApplyBatteryEquipment(Equipment equipment, string reason)
        {
            if (equipment == null || equipment.Parameters == null)
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
                float torpedoFactor = GetEffectiveTorpedoFactor();
                float damageScale = torpedoFactor;
                float crewDamageScale = torpedoFactor;
                float radiusScale = torpedoFactor;
                float intensityScale = torpedoFactor;
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

            if (IsMegaTorpedoRuntimeActive())
                ApplyLockedTargetGuidance(torpedo, reason);
            else
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

            // La je transforme le tir verrouille en visee cartésienne dynamique pour que l'angle soit toujours bon.
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
                ? LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.TorpedoFactor)
                : 1f;
        }

        private static float GetEffectiveOxygenDataFactor()
        {
            if (!LongSubmergedRuntimeSettings.MegaOxygen)
                return 1f;

            float factor = LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.OxygenFactor);
            if (factor <= LongSubmergedRuntimeSettings.MinRuntimeFactor)
                return 1f;

            float normalized = (factor - LongSubmergedRuntimeSettings.MinRuntimeFactor)
                / (LongSubmergedRuntimeSettings.MaxRuntimeFactor - LongSubmergedRuntimeSettings.MinRuntimeFactor);
            return 1f + normalized * (OxygenVanillaRestoreFactor - 1f);
        }

        private static float GetEffectiveBatteryCapacityScale()
        {
            if (!LongSubmergedRuntimeSettings.MegaBattery)
                return BatteryCapacityVanillaRestoreScale;

            float factor = LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.BatteryFactor);
            return factor / BatteryCapacityDataFactor;
        }

        private static float GetEffectiveBatteryEnergyUsageScale()
        {
            // Pour les valeurs finies, la duree est pilotee par la capacite :
            // 1 = vanilla, 4 = x4, 99 = x99. On restaure donc le fallback XLSX x0.1 vers vanilla.
            // Au cran 100, on coupe explicitement les consommateurs electriques pour que l'UI et le jeu voient l'infini.
            if (IsInfiniteBatteryRuntimeActive())
                return 0f;

            return EnergyUsageVanillaRestoreScale;
        }

        private static bool IsInfiniteBatteryRuntimeActive()
        {
            return LongSubmergedRuntimeSettings.MegaBattery
                && LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.BatteryFactor) >= LongSubmergedRuntimeSettings.MaxRuntimeFactor;
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

            // Ne pas tester parameter.Value ici : en mode infini notre scale vaut 0.
            // Quand le joueur redescend le slider, il faut pouvoir restaurer le drain vanilla.
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
            if (target == null || energyUsage == null)
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
            if (target == null)
                return;

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
            if (target == null || energyUsage == null)
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
                LongSubmergedRuntimeApplier.RestoreVanillaOxygenIfNeeded(ship);
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
        private const float FastSpeedFuelFactor = __FAST_SPEED_FUEL_FACTOR__;
        private const float PlayerSubmarineMaxSpeed = __PLAYER_SUBMARINE_MAX_SPEED__;
        private const int FastForwardGearCount = __FAST_FORWARD_GEAR_COUNT__;
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

                float speedFactor = GetEffectiveFastSpeedFactor();
                float fuelFactor = GetEffectiveFastFuelFactor(speedFactor);

                ApplyTopGearBasePower(forwardPresets, data.ForwardBasePower, speedFactor);
                ApplyTopGearFuelConsumption(forwardPresets, data.ForwardFuelConsumption, fuelFactor);
                ApplyTopGearFloatArray(expectedVelocityPerGear, data.ExpectedVelocityPerGear, speedFactor);
                ApplyTopGearFloatArray(expectedVelocityPerGearUnderwater, data.ExpectedVelocityPerGearUnderwater, speedFactor);

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
                Modifier modifier = null;

                if (originalVelocity < PlayerSubmarineMaxSpeed)
                    modifier = ship.Blueprint.Velocity.AddDeltaModifier(RuntimeVelocityModifierName, false);

                data = new ShipRuntimePatchData(originalVelocity, modifier);
                ShipRuntimeData.Add(ship, data);
            }

            if (data.VelocityModifier == null)
                return;

            float effectiveSpeedFactor = GetEffectiveFastSpeedFactor();
            float desiredMaxSpeed = effectiveSpeedFactor <= 1.0001f
                ? data.OriginalVelocity
                : Math.Max(data.OriginalVelocity, PlayerSubmarineMaxSpeed * (effectiveSpeedFactor / FastSpeedFactor));
            float desiredDelta = desiredMaxSpeed - data.OriginalVelocity;
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
                    + desiredMaxSpeed
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

            return LongSubmergedRuntimeSettings.ClampFactor(LongSubmergedRuntimeSettings.SpeedFactor);
        }

        private static float GetEffectiveFastFuelFactor(float speedFactor)
        {
            if (!LongSubmergedRuntimeSettings.SuperSpeed || speedFactor <= 1.0001f)
                return 1f;

            float referenceSpeedFactor = Math.Max(1.0001f, FastSpeedFactor);
            float normalized = (speedFactor - 1f) / (referenceSpeedFactor - 1f);
            return Math.Max(1f, 1f + normalized * (FastSpeedFuelFactor - 1f));
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
            LongSubmergedRuntimeApplier.ApplyBatteryResource(__instance, "PlayerShip.Update");
        }
    }

    [HarmonyPatch(typeof(Resource), "UpdateAmount")]
    internal static class ResourceUpdateAmountBatteryPatch
    {
        private static bool Prefix(Resource __instance)
        {
            return !LongSubmergedRuntimeApplier.TryUpdateBatteryResourceAmount(__instance, "Resource.UpdateAmount");
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
        }
    }

    [HarmonyPatch(typeof(DivingPlanesStation), "UpdateModifiers")]
    internal static class DivingPlanesStationUpdateModifiersPatch
    {
        private static void Postfix(DivingPlanesStation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "DivingPlanesStation.UpdateModifiers");
        }
    }

    [HarmonyPatch(typeof(AirCompressor), "OnEnable")]
    internal static class AirCompressorOnEnablePatch
    {
        private static void Postfix(AirCompressor __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "AirCompressor.OnEnable");
        }
    }

    [HarmonyPatch(typeof(AirCompressor), "EnergyUsage_Changed")]
    internal static class AirCompressorEnergyUsageChangedPatch
    {
        private static void Postfix(AirCompressor __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "AirCompressor.EnergyUsage_Changed");
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

    [HarmonyPatch(typeof(Ventilation), "OnEnable")]
    internal static class VentilationOnEnablePatch
    {
        private static void Postfix(Ventilation __instance)
        {
            LongSubmergedRuntimeApplier.ApplyBatteryObject(__instance, "Ventilation.OnEnable");
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
}
'''

    source = source.replace("__FAST_SPEED_FACTOR__", format_csharp_float(args.fast_speed_factor))
    source = source.replace("__FAST_SPEED_FUEL_FACTOR__", format_csharp_float(args.fast_speed_fuel_factor))
    source = source.replace("__PLAYER_SUBMARINE_MAX_SPEED__", format_csharp_float(args.player_submarine_max_speed))
    source = source.replace("__FAST_FORWARD_GEAR_COUNT__", str(args.fast_speed_top_gears))
    source = source.replace("__OXYGEN_CONSUMPTION_FACTOR__", format_csharp_float(args.oxygen_consumption_factor))
    source = source.replace("__BATTERY_CAPACITY_FACTOR__", format_csharp_float(args.battery_capacity_factor))
    source = source.replace("__ENERGY_USAGE_FACTOR__", format_csharp_float(args.energy_usage_factor))
    source = source.replace("__TORPEDO_DAMAGE_FACTOR__", format_csharp_float(args.torpedo_damage_factor))
    source = source.replace("__TORPEDO_CREW_DAMAGE_FACTOR__", format_csharp_float(args.torpedo_crew_damage_factor))
    source = source.replace("__TORPEDO_EXPLOSION_RADIUS_FACTOR__", format_csharp_float(args.torpedo_explosion_radius_factor))
    source = source.replace("__TORPEDO_EXPLOSION_INTENSITY_FACTOR__", format_csharp_float(args.torpedo_explosion_intensity_factor))
    source = source.replace("__PERFECT_TORPEDO_RELIABILITY__", "true" if args.perfect_torpedo_reliability else "false")
    source = source.replace("__DEFAULT_MEGA_TORPEDOES__", "true" if args.mega_torpedoes else "false")
    source = source.replace("__MOD_VERSION__", MOD_VERSION)

    source_path.write_text(source, encoding="utf-8")
    report.file(source_path)
    report.note(
        "Patch runtime Harmony ajoute : recalcul de OxygenBreathModifier apres Awake, "
        "chargement de sauvegarde et changement d'equipage, plus vitesse rapide, "
        "plafond runtime, propulseurs, carburant rapide, menu F10 et toggles mega."
    )
    report.note("Mega Batterie : runtime F10 reglable 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut.")
    report.note("Mega Oxygene : profil par defaut calibre pour environ 90 jours.")
    report.note("Menu F10 : sliders runtime 1-100 et bouton Par defaut.")
    report.note("SuperVitesse : runtime F10 reglable 1-100 sur les deux crans rapides avant.")
    report.note("Mega torpilles : runtime F10 reglable 1-100, defaut x10, guidage cible verrouillee, aucune ligne torpille XLSX ecrasee.")


def write_readme(mod_dir: Path, args: argparse.Namespace, report: PatchReport | None = None) -> None:
    lines = [
        f"{MOD_DISPLAY_NAME} v{MOD_VERSION}",
        "",
        "Paramètres utilisés :",
        f"- Air / atmosphère de base : capacité x{args.air_capacity_factor:g}",
        f"- Oxygen Consumption Per Character : divisé par {args.oxygen_consumption_factor:g}",
        f"- Discipline/fatigue sous l'eau : divisé par {args.discipline_factor:g}",
        f"- Batterie / Accumulators : x{args.battery_capacity_factor:g}",
        f"- EnergyUsage consommateurs hors ventilation/compresseurs : x{args.energy_usage_factor:g} dans les datasheets",
        "- Mega Batterie runtime : slider 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut",
        "- EnergyUsage recharge/production batterie : vanilla",
        f"- Deux derniers crans avant : vitesse/propulsion x{args.fast_speed_factor:g}",
        f"- Deux derniers crans avant : carburant x{args.fast_speed_fuel_factor:g}",
        f"- Vitesse max sous-marin joueur : {args.player_submarine_max_speed:g} km/h",
        "- Sliders F10 : Batterie, Oxygene, SuperVitesse et Torpilles de 1 a 100",
        "- Slider Batterie : 1 = vanilla, 4 = duree x4, 99 = duree x99, 100 = batterie infinie",
        "- Slider Oxygene : 1 = vanilla, 100 = profil environ 90 jours",
        f"- Slider SuperVitesse : 1 = vanilla, {args.fast_speed_factor:g} = defaut actuel, 100 = extreme",
        f"- Slider Torpilles : 1 = vanilla, {args.torpedo_damage_factor:g} = defaut actuel, 100 = extreme",
        "- Bouton Par defaut : restaure les reglages du profil actuel",
        f"- Mega torpilles : {'oui' if args.mega_torpedoes else 'non'}",
        f"- Mega torpilles degats : x{args.torpedo_damage_factor:g}",
        f"- Mega torpilles rayon explosion : x{args.torpedo_explosion_radius_factor:g}",
        f"- Mega torpilles intensite explosion : x{args.torpedo_explosion_intensity_factor:g}",
        "- Mega torpilles guidage : cible verrouillee corrigee pendant le vol",
        f"- Fiabilite parfaite torpilles : {'oui' if args.perfect_torpedo_reliability else 'non'}",
        f"- DudChance torpilles : {args.torpedo_dud_chance:g}",
        f"- Defaillance magnetique torpilles : {args.torpedo_magnetic_failure_chance:g}",
        f"- Explosion magnetique prematuree torpilles : {args.torpedo_premature_magnetic_chance:g}",
        "- Menu en jeu : F10 pour activer/desactiver Mega Batterie, Mega Oxygene, SuperVitesse et Mega Torpilles",
        "- DLC Type IX officiel : lignes joueur Type IXA/IXC/IXC40 incluses si le DLC est installe",
        f"- Ventilation vanilla : {'non' if args.patch_ventilation else 'oui'}",
        f"- Patch runtime : {MOD_ASSEMBLY_NAME}, air apres chargement, plafond vitesse, propulseurs, carburant rapide, torpilles et menu",
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
        "- Le profil air vise environ 90 jours d'immersion avec Mega Oxygene actif.",
        "- Mega Batterie est reglable en runtime ; 1 revient vanilla, 4 donne x4, 99 donne x99, 100 coupe le drain electrique positif.",
        "- Les sliders F10 sont persistants et s'appliquent directement en partie avec Reappliquer maintenant ou au changement de valeur.",
        "- Les vitesses lentes et mi-vitesse restent vanilla ; seuls les deux crans rapides avant sont boostés vers 40/45 km/h.",
        "- Les crans rapides consomment plus de carburant pour garder une autonomie logique.",
        "- Les torpilles gardent leur vitesse/portee vanilla ; les degats, explosions, rates et le guidage verrouille sont geres en runtime.",
        "- Le guidage mega met les tirs verrouilles en cible cartésienne dynamique et force l'impact a courte distance.",
        "- La fiabilite parfaite met DudChance, MagneticExplosionFail, MagneticExplosionOnArm et MagneticExplosionAfterArm a 0 quand Mega Torpilles est actif.",
        "- Couper Mega Torpilles remet les torpilles sur les valeurs vanilla, car les XLSX torpilles ne sont pas ecrases.",
        "- La recharge diesel reste vanilla pour éviter une recharge batterie instantanée.",
        "- Le plafond de vitesse inclut les Type IX officiels du DLC Steam quand le DLC est installe.",
        "- Compatible sauvegarde existante après fermeture complète puis relance du jeu.",
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
                "- Mega Batterie : runtime F10 reglable 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut",
                "- Menu F10 : sliders runtime 1-100 et bouton Par defaut",
                "- SuperVitesse : runtime F10 reglable 1-100 sur les deux crans rapides avant",
                f"- Lignes vitesse sous-marin joueur : {report.counters.get('player_submarine_speed_rows', 0)}",
                "- Mega torpilles : runtime F10 reglable 1-100, defaut x10, guidage cible verrouillee, aucune ligne torpille XLSX ecrasee",
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
        help="Multiplicateur de la réserve d'air/atmosphère. Défaut : 1800, pour viser environ 90 jours.",
    )

    parser.add_argument(
        "--oxygen-consumption-factor",
        type=float,
        default=DEFAULT_OXYGEN_CONSUMPTION_FACTOR,
        help="Divise Oxygen Consumption Per Character par ce facteur. Défaut : 1800.",
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
        help="Multiplicateur EnergyUsage positif datasheet hors ventilation/compresseurs. Défaut : 0.1 ; Mega Batterie runtime force 0.",
    )

    parser.add_argument(
        "--fast-speed-factor",
        type=float,
        default=DEFAULT_FAST_SPEED_FACTOR,
        help="Multiplicateur vitesse/propulsion des derniers crans rapides de marche avant. Défaut : 3.5.",
    )

    parser.add_argument(
        "--fast-speed-fuel-factor",
        type=float,
        default=DEFAULT_FAST_SPEED_FUEL_FACTOR,
        help="Multiplicateur carburant des derniers crans rapides de marche avant. Défaut : 8.",
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

    mega_torpedoes_group = parser.add_mutually_exclusive_group()
    mega_torpedoes_group.add_argument(
        "--mega-torpedoes",
        dest="mega_torpedoes",
        action="store_true",
        default=DEFAULT_MEGA_TORPEDOES,
        help="Active les mega torpilles : degats et explosions renforces. Active par defaut.",
    )
    mega_torpedoes_group.add_argument(
        "--no-mega-torpedoes",
        dest="mega_torpedoes",
        action="store_false",
        help="Desactive le patch mega torpilles.",
    )

    parser.add_argument(
        "--torpedo-damage-factor",
        type=float,
        default=DEFAULT_TORPEDO_DAMAGE_FACTOR,
        help="Multiplicateur Damage des torpilles. Défaut : 10.",
    )

    parser.add_argument(
        "--torpedo-crew-damage-factor",
        type=float,
        default=DEFAULT_TORPEDO_CREW_DAMAGE_FACTOR,
        help="Multiplicateur CrewDamage des torpilles. Défaut : 10.",
    )

    parser.add_argument(
        "--torpedo-explosion-radius-factor",
        type=float,
        default=DEFAULT_TORPEDO_EXPLOSION_RADIUS_FACTOR,
        help="Multiplicateur DamageRadius et DamageEffectsRadius des torpilles. Défaut : 10.",
    )

    parser.add_argument(
        "--torpedo-explosion-intensity-factor",
        type=float,
        default=DEFAULT_TORPEDO_EXPLOSION_INTENSITY_FACTOR,
        help="Multiplicateur DamageEffectsIntensity des torpilles. Défaut : 10.",
    )

    torpedo_reliability_group = parser.add_mutually_exclusive_group()
    torpedo_reliability_group.add_argument(
        "--perfect-torpedo-reliability",
        dest="perfect_torpedo_reliability",
        action="store_true",
        default=DEFAULT_PERFECT_TORPEDO_RELIABILITY,
        help="Supprime les rates et defaillances des torpilles. Active par defaut.",
    )
    torpedo_reliability_group.add_argument(
        "--no-perfect-torpedo-reliability",
        dest="perfect_torpedo_reliability",
        action="store_false",
        help="Garde la fiabilite vanilla des torpilles.",
    )

    parser.add_argument(
        "--torpedo-dud-chance",
        type=float,
        default=DEFAULT_TORPEDO_DUD_CHANCE,
        help="Valeur DudChance appliquee aux torpilles si la fiabilite parfaite est active. Défaut : 0.",
    )

    parser.add_argument(
        "--torpedo-magnetic-failure-chance",
        type=float,
        default=DEFAULT_TORPEDO_MAGNETIC_FAILURE_CHANCE,
        help="Valeur MagneticExplosionFail appliquee aux torpilles si la fiabilite parfaite est active. Défaut : 0.",
    )

    parser.add_argument(
        "--torpedo-premature-magnetic-chance",
        type=float,
        default=DEFAULT_TORPEDO_PREMATURE_MAGNETIC_CHANCE,
        help=(
            "Valeur MagneticExplosionOnArm/AfterArm appliquee aux torpilles "
            "si la fiabilite parfaite est active. Défaut : 0."
        ),
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
        "fast_speed_fuel_factor",
        "player_submarine_max_speed",
        "torpedo_damage_factor",
        "torpedo_crew_damage_factor",
        "torpedo_explosion_radius_factor",
        "torpedo_explosion_intensity_factor",
        "ventilation_gain_factor",
        "potassium_duration_factor",
    )

    for name in positive_names:
        value = getattr(args, name)
        if value <= 0:
            raise ValueError(f"--{name.replace('_', '-')} doit être > 0.")

    probability_names = (
        "torpedo_dud_chance",
        "torpedo_magnetic_failure_chance",
        "torpedo_premature_magnetic_chance",
    )

    for name in probability_names:
        value = getattr(args, name)
        if value < 0 or value > 1:
            raise ValueError(f"--{name.replace('_', '-')} doit être entre 0 et 1.")

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
    print("  - Objectif air : environ 90 jours avec Mega Oxygene actif")
    print(f"  - Discipline/fatigue : /{args.discipline_factor:g}")
    print(f"  - Batterie Accumulators : x{args.battery_capacity_factor:g}")
    print(f"  - EnergyUsage consommateurs hors ventilation/compresseurs : x{args.energy_usage_factor:g} dans les datasheets")
    print("  - Mega Batterie runtime : slider 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut")
    print("  - EnergyUsage recharge/production batterie : vanilla")
    print(f"  - Deux derniers crans avant : vitesse/propulsion x{args.fast_speed_factor:g}")
    print(f"  - Deux derniers crans avant : carburant x{args.fast_speed_fuel_factor:g}")
    print(f"  - Vitesse max sous-marin joueur : {args.player_submarine_max_speed:g} km/h")
    print("  - Menu F10 : reglages Batterie, Oxygene, SuperVitesse et Torpilles de 1 a 100")
    print(f"  - Mega torpilles : {'OUI' if args.mega_torpedoes else 'NON'}")
    print(f"  - Torpilles degats : x{args.torpedo_damage_factor:g}")
    print(f"  - Torpilles rayon explosion : x{args.torpedo_explosion_radius_factor:g}")
    print(f"  - Torpilles intensite explosion : x{args.torpedo_explosion_intensity_factor:g}")
    print("  - Torpilles guidage : cible verrouillee corrigee pendant le vol")
    print(f"  - Fiabilite parfaite torpilles : {'OUI' if args.perfect_torpedo_reliability else 'NON'}")
    print(f"  - DudChance / defaillances torpilles : {args.torpedo_dud_chance:g}")
    print(f"  - Ventilation vanilla : {'NON, patchée' if args.patch_ventilation else 'OUI, laissée normale'}")

    print("\nCompteurs importants :")
    print(f"  - General Oxygen Consumption : {report.counters.get('general_oxygen_consumption_rows', 0)}")
    print(f"  - Capacité air dans Parameters : {report.counters.get('air_capacity_parameter_rows', 0)}")
    print(f"  - Capacité air dans cellules : {report.counters.get('air_capacity_cell_rows', 0)}")
    print(f"  - Batterie : {report.counters.get('battery_capacity_rows', 0)}")
    print(f"  - EnergyUsage consommation : {report.counters.get('energy_usage_rows', 0)}")
    print(f"  - EnergyUsage recharge : {report.counters.get('energy_recharge_rows', 0)}")
    print("  - Mega Batterie : runtime F10 reglable 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut")
    print(f"  - Vitesse sous-marin joueur : {report.counters.get('player_submarine_speed_rows', 0)}")
    print("  - SuperVitesse : runtime F10 reglable 1-100")
    print("  - Mega torpilles : runtime F10 reglable 1-100, defaut x10 et guidage cible verrouillee")
    print("  - Fiabilite torpilles : runtime F10")

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
    print("  6. Plonge à 100% air : le tooltip doit viser environ 90 jours avec Mega Oxygene actif.")
    print("  7. Teste pleine vitesse / machines à fond : on vise environ 40/45 km/h.")
    print("  8. Si la ventilation était cassée par une ancienne version, celle-ci doit la remettre vanilla car elle ne copie plus la ligne Ventilation.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
