#!/usr/bin/env python3
# -*- coding: utf-8 -*-

r"""
UBOAT - LongSubmerged10x
Générateur de mod Data Sheets pour augmenter fortement l'autonomie en immersion.

Objectif gameplay :
- Batterie environ 10x plus durable.
- Consommation d'air / oxygène environ 10x plus lente.
- Discipline / fatigue liées à l'immersion environ 10x moins punitives.
- Ventilation / régénération d'air renforcée si les lignes correspondantes existent dans Entities.xlsx.

Pourquoi un générateur ?
- Les lignes changent selon les versions UBOAT.
- Je lis donc les fichiers vanilla locaux et je génère des overrides minimaux.

Prérequis :
    py -m pip install openpyxl

Exemple :
    py build_uboat_long_submerged_mod.py ^
      --uboat "C:\Program Files (x86)\Steam\steamapps\common\UBOAT" ^
      --force

Le mod sera généré par défaut ici :
    %USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\LongSubmerged10x
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
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from openpyxl import Workbook, load_workbook
from openpyxl.cell import Cell
from openpyxl.worksheet.worksheet import Worksheet


# =============================================================================
# CONFIGURATION DU MOD
# =============================================================================

MOD_FOLDER_NAME = "LongSubmerged10x"
MOD_DISPLAY_NAME = "Long Submerged 10x"
MOD_VERSION = "1.0.0"
MOD_AUTHOR = "VotreNomOuVotreEquipe"
MIN_GAME_VERSION = "2026.1"

# Je garde un facteur unique pour que le comportement reste lisible et ajustable.
AUTONOMY_MULTIPLIER = 10.0

# Batterie :
# - Energy Base Scale x10 : réserve effective plus grande.
# - Recharge Rate reste vanilla par défaut pour éviter une recharge instantanée.
ENERGY_BASE_SCALE_MULTIPLIER = AUTONOMY_MULTIPLIER
ENERGY_RECHARGE_RATE_MULTIPLIER = 1.0

# Air :
# - Consommation par personnage divisée par 10.
# - Ventilation / filtre renforcés si trouvés dans Entities.xlsx.
OXYGEN_CONSUMPTION_MULTIPLIER = 1.0 / AUTONOMY_MULTIPLIER
VENTILATION_OXYGEN_GAIN_MULTIPLIER = AUTONOMY_MULTIPLIER
VENTILATION_REGENERATION_LIMIT_MULTIPLIER = AUTONOMY_MULTIPLIER
VENTILATION_ENERGY_USAGE_MULTIPLIER = 1.0 / AUTONOMY_MULTIPLIER

# Discipline / moral :
# Je réduis les pertes passives liées à l'immersion et à la fatigue longue durée.
UNDERWATER_DISCIPLINE_LOSS_MULTIPLIER = 1.0 / AUTONOMY_MULTIPLIER
FATIGUE_PER_DAY_MULTIPLIER = 1.0 / AUTONOMY_MULTIPLIER
FATIGUE_MAX_PENALTY_MULTIPLIER = 1.0 / AUTONOMY_MULTIPLIER

# Je ne réduis pas la perte de discipline quand le sous-marin est détecté.
DETECTED_DISCIPLINE_LOSS_MULTIPLIER: float | None = None

# Je ne touche pas aux paramètres batterie d'Entities.xlsx par défaut pour éviter
# un cumul Energy Base Scale x10 puis équipement x10.
PATCH_BATTERY_EQUIPMENT_PARAMETERS = False
BATTERY_EQUIPMENT_MULTIPLIER = AUTONOMY_MULTIPLIER


# =============================================================================
# TYPES ET OUTILS
# =============================================================================


@dataclass(frozen=True)
class GeneralPatch:
    category: str
    row_id: str
    multiplier: float
    reason: str


@dataclass
class PatchReport:
    changed_files: list[str]
    warnings: list[str]
    info: list[str]

    def add_file(self, path: Path) -> None:
        self.changed_files.append(str(path))

    def warn(self, message: str) -> None:
        self.warnings.append(message)

    def note(self, message: str) -> None:
        self.info.append(message)


def norm_text(value: Any) -> str:
    """Je normalise une valeur Excel pour les comparaisons robustes."""
    if value is None:
        return ""
    return str(value).strip().lower()


def norm_category(value: Any) -> str:
    """Je normalise une catégorie de type '/Resources'."""
    text = norm_text(value)
    if not text:
        return ""
    if not text.startswith("/"):
        text = "/" + text
    return text


def safe_float(value: Any) -> float | None:
    """
    Je convertis un contenu Excel en float quand c'est possible :
    - nombres natifs Excel
    - chaînes avec virgule décimale
    - notation scientifique
    - pourcentages simples
    """
    if value is None:
        return None

    if isinstance(value, bool):
        return None

    if isinstance(value, int | float):
        if math.isfinite(float(value)):
            return float(value)
        return None

    text = str(value).strip()
    if not text:
        return None

    if text.endswith("%"):
        text = text[:-1].strip()

    text = text.replace(" ", "")
    text = text.replace(",", ".")

    try:
        number = float(text)
    except ValueError:
        return None

    if not math.isfinite(number):
        return None
    return number


def scale_excel_value(original_value: Any, multiplier: float) -> Any:
    """Je multiplie une valeur Excel numérique en écrivant un vrai nombre Excel."""
    parsed = safe_float(original_value)
    if parsed is None:
        raise ValueError(f"Valeur non numérique impossible à multiplier : {original_value!r}")

    return parsed * multiplier


def copy_cell(src: Cell, dst: Cell) -> None:
    """Je copie la valeur et le style minimal d'une cellule."""
    dst.value = src.value

    if src.has_style:
        dst._style = copy.copy(src._style)

    if src.number_format:
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
    """Je copie une ligne entière d'une worksheet vers une autre."""
    for col in range(1, src_ws.max_column + 1):
        copy_cell(src_ws.cell(src_row, col), dst_ws.cell(dst_row, col))

    if src_ws.row_dimensions[src_row].height:
        dst_ws.row_dimensions[dst_row].height = src_ws.row_dimensions[src_row].height


def copy_column_widths(src_ws: Worksheet, dst_ws: Worksheet) -> None:
    """Je copie les largeurs de colonnes pour garder les fichiers lisibles."""
    for key, dim in src_ws.column_dimensions.items():
        dst_ws.column_dimensions[key].width = dim.width


def get_sheet_case_insensitive(workbook: Workbook, wanted_name: str) -> Worksheet:
    for name in workbook.sheetnames:
        if name.strip().lower() == wanted_name.strip().lower():
            return workbook[name]
    raise KeyError(f"Onglet introuvable : {wanted_name!r}. Onglets disponibles : {workbook.sheetnames}")


def path_contains(parent: Path, child: Path) -> bool:
    parent_resolved = parent.resolve()
    child_resolved = child.resolve()
    return parent_resolved == child_resolved or parent_resolved in child_resolved.parents


def ensure_clean_directory(path: Path, force: bool) -> None:
    """
    Je crée un dossier vide.
    Si force=True, je supprime l'ancien dossier sauf s'il contient ce script.
    """
    if path.exists():
        if not force:
            raise FileExistsError(
                f"Le dossier existe déjà : {path}\n"
                f"Relance avec --force pour l'écraser."
            )

        script_path = Path(__file__).resolve()
        if path_contains(path, script_path):
            raise ValueError(
                f"Refus de supprimer {path} car ce dossier contient le générateur.\n"
                "Choisis un sous-dossier de sortie ou le dossier Mods local UBOAT."
            )

        shutil.rmtree(path)

    path.mkdir(parents=True, exist_ok=True)


def default_local_mods_root() -> Path:
    user_profile = os.environ.get("USERPROFILE")
    if not user_profile:
        return Path.cwd() / "UBOAT_Mods"
    return Path(user_profile) / "AppData" / "LocalLow" / "Deep Water Studio" / "UBOAT" / "Mods"


def resolve_data_sheets_dir(uboat_root: Path) -> Path:
    candidate = uboat_root / "UBOAT_Data" / "Data Sheets"
    if not candidate.exists():
        raise FileNotFoundError(
            f"Dossier Data Sheets introuvable : {candidate}\n"
            f"Vérifie le chemin --uboat. Il doit pointer vers le dossier d'installation UBOAT."
        )
    return candidate


# =============================================================================
# PATCH GENERAL.XLSX
# =============================================================================


def build_general_patches() -> list[GeneralPatch]:
    patches: list[GeneralPatch] = [
        GeneralPatch(
            category="/Resources",
            row_id="Oxygen Consumption Per Character",
            multiplier=OXYGEN_CONSUMPTION_MULTIPLIER,
            reason="Réduit la consommation d'air par membre d'équipage pour tenir environ 10x plus longtemps.",
        ),
        GeneralPatch(
            category="/Resources",
            row_id="Energy Base Scale",
            multiplier=ENERGY_BASE_SCALE_MULTIPLIER,
            reason="Augmente la réserve d'énergie batterie.",
        ),
        GeneralPatch(
            category="/Discipline",
            row_id="Underwater Discipline Loss",
            multiplier=UNDERWATER_DISCIPLINE_LOSS_MULTIPLIER,
            reason="Réduit la perte de discipline pendant l'immersion.",
        ),
        GeneralPatch(
            category="/Discipline",
            row_id="Fatigue - Per Day",
            multiplier=FATIGUE_PER_DAY_MULTIPLIER,
            reason="Réduit l'accumulation de fatigue longue durée.",
        ),
        GeneralPatch(
            category="/Discipline",
            row_id="Fatigue - Max Penalty",
            multiplier=FATIGUE_MAX_PENALTY_MULTIPLIER,
            reason="Réduit la pénalité maximale de fatigue sur la discipline.",
        ),
    ]

    if ENERGY_RECHARGE_RATE_MULTIPLIER != 1.0:
        patches.append(
            GeneralPatch(
                category="/Resources",
                row_id="Energy Recharge Rate",
                multiplier=ENERGY_RECHARGE_RATE_MULTIPLIER,
                reason="Ajuste la recharge batterie.",
            )
        )

    if DETECTED_DISCIPLINE_LOSS_MULTIPLIER is not None:
        patches.append(
            GeneralPatch(
                category="/Discipline",
                row_id="Detected Discipline Loss",
                multiplier=DETECTED_DISCIPLINE_LOSS_MULTIPLIER,
                reason="Réduit la perte de discipline quand le sous-marin est détecté.",
            )
        )

    return patches


def index_general_settings(settings_ws: Worksheet) -> tuple[dict[str, int], dict[tuple[str, str], int]]:
    """
    Je retourne :
    - category_rows : catégorie normalisée -> numéro de ligne
    - data_rows : (catégorie normalisée, row_id normalisé) -> numéro de ligne
    """
    category_rows: dict[str, int] = {}
    data_rows: dict[tuple[str, str], int] = {}

    current_category = ""

    for row in range(1, settings_ws.max_row + 1):
        first_value = settings_ws.cell(row, 1).value
        first_text = str(first_value).strip() if first_value is not None else ""

        if not first_text:
            continue

        if first_text.startswith("/"):
            current_category = norm_category(first_text)
            category_rows[current_category] = row
            continue

        if current_category:
            data_rows[(current_category, norm_text(first_text))] = row

    return category_rows, data_rows


def find_value_columns_for_category(settings_ws: Worksheet, category_row: int) -> list[int]:
    """
    Je détecte toutes les colonnes de valeur d'une catégorie.
    /Resources expose souvent 'Value' seulement, tandis que /Discipline expose
    'Normal', 'Hard' et 'Very Hard'. Je patche donc toutes les colonnes non vides.
    """
    value_columns: list[int] = []

    for col in range(2, settings_ws.max_column + 1):
        header_value = settings_ws.cell(category_row, col).value
        if header_value is None or str(header_value).strip() == "":
            continue
        value_columns.append(col)

    return value_columns or [2]


def scale_general_row_values(
    src_ws: Worksheet,
    out_ws: Worksheet,
    src_row: int,
    dest_row: int,
    category_row: int,
    value_columns: list[int],
    patch: GeneralPatch,
    report: PatchReport,
) -> int:
    changes_count = 0
    changes: list[str] = []

    for value_col in value_columns:
        original_value = src_ws.cell(src_row, value_col).value
        if original_value is None:
            continue

        try:
            new_value = scale_excel_value(original_value, patch.multiplier)
        except ValueError as exc:
            report.warn(f"General.xlsx : {patch.row_id} colonne {value_col} non patchée : {exc}")
            continue

        out_ws.cell(dest_row, value_col).value = new_value
        header = src_ws.cell(category_row, value_col).value
        header_text = str(header).strip() if header is not None else f"Colonne {value_col}"
        changes.append(f"{header_text}: {original_value!r} -> {new_value!r}")
        changes_count += 1

    if changes:
        report.note(
            f"General.xlsx / Settings {patch.category} / {patch.row_id}: "
            + "; ".join(changes)
            + f" | {patch.reason}"
        )

    return changes_count


def create_general_override(vanilla_general: Path, out_general: Path, report: PatchReport) -> None:
    src_wb = load_workbook(vanilla_general, data_only=False)
    src_ws = get_sheet_case_insensitive(src_wb, "Settings")

    category_rows, data_rows = index_general_settings(src_ws)
    patches = build_general_patches()

    out_wb = Workbook()
    out_ws = out_wb.active
    out_ws.title = "Settings"
    copy_column_widths(src_ws, out_ws)

    patches_by_category: dict[str, list[GeneralPatch]] = {}
    for patch in patches:
        patches_by_category.setdefault(norm_category(patch.category), []).append(patch)

    dest_row = 1
    changes_count = 0

    for category_norm, category_row in sorted(category_rows.items(), key=lambda item: item[1]):
        category_patches = patches_by_category.get(category_norm, [])
        if not category_patches:
            continue

        copy_row(src_ws, out_ws, category_row, dest_row)
        dest_row += 1

        value_columns = find_value_columns_for_category(src_ws, category_row)

        for patch in category_patches:
            src_data_row = data_rows.get((category_norm, norm_text(patch.row_id)))
            if src_data_row is None:
                report.warn(
                    f"General.xlsx : ligne introuvable : {patch.category} / {patch.row_id}. "
                    f"Le jeu a peut-être renommé cette entrée."
                )
                continue

            copy_row(src_ws, out_ws, src_data_row, dest_row)
            changes_count += scale_general_row_values(
                src_ws=src_ws,
                out_ws=out_ws,
                src_row=src_data_row,
                dest_row=dest_row,
                category_row=category_row,
                value_columns=value_columns,
                patch=patch,
                report=report,
            )
            dest_row += 1

        dest_row += 1

    if changes_count == 0:
        raise RuntimeError("Aucun patch General.xlsx n'a été appliqué. Vérifie la version du jeu.")

    out_general.parent.mkdir(parents=True, exist_ok=True)
    out_wb.save(out_general)
    report.add_file(out_general)


# =============================================================================
# PATCH ENTITIES.XLSX
# =============================================================================


PARAM_ASSIGNMENT_PATTERN_TEMPLATE = (
    r"(?P<prefix>\b{key}\b\s*=\s*)"
    r"(?P<number>[-+]?\d+(?:[.,]\d+)?(?:[eE][-+]?\d+)?)"
    r"(?P<percent>%?)"
)


def format_parameter_number(value: float, percent: str = "") -> str:
    """Je garde un format compact et compatible avec les paramètres Entities."""
    text = f"{value:.10g}"
    return text + percent


def scale_parameter_string(parameters: str, multipliers: dict[str, float]) -> tuple[str, list[str]]:
    """
    Je modifie une chaîne de paramètres du style :
        EnergyUsage = 0.0002, Noise = 0.4, OxygenGain = 0.003
    """
    changed: list[str] = []
    result = parameters

    for key, multiplier in multipliers.items():
        pattern = re.compile(
            PARAM_ASSIGNMENT_PATTERN_TEMPLATE.format(key=re.escape(key)),
            flags=re.IGNORECASE,
        )

        def replace(match: re.Match[str]) -> str:
            original_text = match.group("number")
            percent = match.group("percent") or ""
            original = safe_float(original_text + percent)

            if original is None:
                return match.group(0)

            new_value = original * multiplier
            changed.append(f"{key}: {original_text}{percent} -> {format_parameter_number(new_value, percent)}")
            return match.group("prefix") + format_parameter_number(new_value, percent)

        result = pattern.sub(replace, result)

    return result, changed


def find_header_row_and_parameters_col(ws: Worksheet) -> tuple[int | None, int | None]:
    """Je trouve la ligne d'en-tête et la colonne Parameters dans une feuille Entities."""
    for row in range(1, min(ws.max_row, 20) + 1):
        for col in range(1, ws.max_column + 1):
            if norm_text(ws.cell(row, col).value) == "parameters":
                return row, col

    if ws.max_column >= 16:
        return 1, 16

    return None, None


def row_text(row_values: Iterable[Any]) -> str:
    return " | ".join(str(v) for v in row_values if v is not None)


def should_patch_ventilation_row(row_values: list[Any], parameters: str) -> bool:
    """
    Je patche les lignes qui portent explicitement des paramètres d'air, ce qui
    couvre la ventilation et les absorbeurs sans dépendre d'un numéro de ligne.
    """
    params_norm = parameters.lower()
    if "oxygengain" in params_norm or "regenerationlimit" in params_norm:
        return True

    text = row_text(row_values[:6]).lower()
    if "ventilation" in text:
        return True

    return False


def should_patch_battery_row(row_values: list[Any], parameters: str) -> bool:
    if not PATCH_BATTERY_EQUIPMENT_PARAMETERS:
        return False

    text = (row_text(row_values[:8]) + " | " + parameters).lower()
    battery_words = ("accumulator", "accumulators", "battery", "batteries")
    parameter_words = ("capacity", "energycapacity", "batterycapacity", "capacityscale", "capacitymultiplier")

    return any(word in text for word in battery_words) and any(word in text for word in parameter_words)


def create_entities_override(vanilla_entities: Path, out_entities: Path, report: PatchReport) -> None:
    src_wb = load_workbook(vanilla_entities, data_only=False)

    out_wb = Workbook()
    default_sheet = out_wb.active
    out_wb.remove(default_sheet)

    total_changes = 0

    ventilation_multipliers = {
        "OxygenGain": VENTILATION_OXYGEN_GAIN_MULTIPLIER,
        "RegenerationLimit": VENTILATION_REGENERATION_LIMIT_MULTIPLIER,
        "EnergyUsage": VENTILATION_ENERGY_USAGE_MULTIPLIER,
    }

    battery_multipliers = {
        "EnergyCapacity": BATTERY_EQUIPMENT_MULTIPLIER,
        "BatteryCapacity": BATTERY_EQUIPMENT_MULTIPLIER,
        "Capacity": BATTERY_EQUIPMENT_MULTIPLIER,
        "CapacityScale": BATTERY_EQUIPMENT_MULTIPLIER,
        "CapacityMultiplier": BATTERY_EQUIPMENT_MULTIPLIER,
    }

    for sheet_name in src_wb.sheetnames:
        src_ws = src_wb[sheet_name]

        header_row, parameters_col = find_header_row_and_parameters_col(src_ws)
        if header_row is None or parameters_col is None:
            continue

        modified_rows: list[tuple[int, str, list[str]]] = []

        for row_idx in range(header_row + 1, src_ws.max_row + 1):
            row_values = [src_ws.cell(row_idx, col).value for col in range(1, src_ws.max_column + 1)]
            parameters_value = src_ws.cell(row_idx, parameters_col).value

            if parameters_value is None:
                continue

            parameters = str(parameters_value)
            combined_changes: list[str] = []
            new_parameters = parameters

            if should_patch_ventilation_row(row_values, parameters):
                new_parameters, changes = scale_parameter_string(new_parameters, ventilation_multipliers)
                combined_changes.extend(changes)

            if should_patch_battery_row(row_values, parameters):
                new_parameters, changes = scale_parameter_string(new_parameters, battery_multipliers)
                combined_changes.extend(changes)

            if combined_changes and new_parameters != parameters:
                modified_rows.append((row_idx, new_parameters, combined_changes))

        if not modified_rows:
            continue

        out_ws = out_wb.create_sheet(title=sheet_name)
        copy_column_widths(src_ws, out_ws)

        dest_row = 1

        for src_header_row in range(1, header_row + 1):
            copy_row(src_ws, out_ws, src_header_row, dest_row)
            dest_row += 1

        for src_row_idx, new_parameters, changes in modified_rows:
            copy_row(src_ws, out_ws, src_row_idx, dest_row)
            out_ws.cell(dest_row, parameters_col).value = new_parameters

            row_id = src_ws.cell(src_row_idx, 1).value
            report.note(
                f"Entities.xlsx / {sheet_name} / ligne id={row_id!r}: "
                + "; ".join(changes)
            )

            dest_row += 1
            total_changes += 1

    if total_changes == 0:
        report.warn(
            "Entities.xlsx : aucune ligne ventilation/batterie patchée. "
            "Ce n'est pas bloquant : General.xlsx modifie déjà consommation O2 et réserve d'énergie. "
            "Si tu veux patcher une ligne précise, ouvre Entities.xlsx et vérifie le nom exact de l'équipement."
        )
        return

    out_entities.parent.mkdir(parents=True, exist_ok=True)
    out_wb.save(out_entities)
    report.add_file(out_entities)


# =============================================================================
# MANIFEST + CACHE
# =============================================================================


def write_manifest(mod_dir: Path) -> None:
    """Je crée un manifest local minimal compatible avec un mod Data Sheets."""
    manifest = {
        "name": MOD_DISPLAY_NAME,
        "version": MOD_VERSION,
        "description": (
            "Augmente l'autonomie en immersion : batterie environ 10x, "
            "consommation d'air environ 10x plus lente, discipline/fatigue sous l'eau environ 10x moins punitive. "
            "Mod Data Sheets sans DLL."
        ),
        "author": MOD_AUTHOR,
        "minGameVersion": MIN_GAME_VERSION,
        "maxGameVersion": "",
        "assemblyName": "",
        "permissions": [],
        "steamFileId": 0,
    }

    manifest_path = mod_dir / "Manifest.json"
    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def write_readme(mod_dir: Path) -> None:
    readme = f"""# {MOD_DISPLAY_NAME}

Mod généré automatiquement.

Effets principaux :
- Oxygen Consumption Per Character x {OXYGEN_CONSUMPTION_MULTIPLIER}
- Energy Base Scale x {ENERGY_BASE_SCALE_MULTIPLIER}
- Underwater Discipline Loss x {UNDERWATER_DISCIPLINE_LOSS_MULTIPLIER}
- Fatigue - Per Day x {FATIGUE_PER_DAY_MULTIPLIER}
- Fatigue - Max Penalty x {FATIGUE_MAX_PENALTY_MULTIPLIER}

Notes :
- Active ce mod dans UBOAT Launcher > Mods.
- Mets-le après les autres mods qui touchent General.xlsx ou Entities.xlsx.
- Pour que les modifications de datasheets soient bien prises en compte, vide le cache UBOAT si le jeu garde d'anciennes valeurs.
- Pour les valeurs touchant Entities.xlsx, une nouvelle carrière peut être nécessaire selon le système modifié par le jeu.
"""
    (mod_dir / "README_LongSubmerged10x.txt").write_text(readme, encoding="utf-8")


def clear_uboat_cache(local_uboat_dir: Path, report: PatchReport) -> None:
    """
    Je vide les dossiers cache connus sans supprimer les dossiers eux-mêmes.
    """
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
# MAIN
# =============================================================================


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Génère le mod UBOAT LongSubmerged10x en Data Sheets.",
    )

    parser.add_argument(
        "--uboat",
        type=Path,
        required=True,
        help="Chemin du dossier d'installation UBOAT, ex: C:\\Program Files (x86)\\Steam\\steamapps\\common\\UBOAT",
    )

    parser.add_argument(
        "--out",
        type=Path,
        default=default_local_mods_root() / MOD_FOLDER_NAME,
        help="Dossier de sortie du mod. Par défaut : dossier local UBOAT Mods.",
    )

    parser.add_argument(
        "--force",
        action="store_true",
        help="Écrase le dossier de sortie s'il existe déjà.",
    )

    parser.add_argument(
        "--clear-cache",
        action="store_true",
        help="Vide Cache, Data Sheets et Temp dans AppData\\LocalLow\\Deep Water Studio\\UBOAT après génération.",
    )

    return parser.parse_args(argv)


def generate_mod(uboat_root: Path, out_mod_dir: Path, force: bool, clear_cache: bool) -> PatchReport:
    uboat_root = uboat_root.expanduser().resolve()
    out_mod_dir = out_mod_dir.expanduser().resolve()
    out_data_sheets = out_mod_dir / "Data Sheets"

    report = PatchReport(changed_files=[], warnings=[], info=[])

    data_sheets_dir = resolve_data_sheets_dir(uboat_root)

    vanilla_general = data_sheets_dir / "General.xlsx"
    vanilla_entities = data_sheets_dir / "Entities.xlsx"

    if not vanilla_general.exists():
        raise FileNotFoundError(f"Fichier introuvable : {vanilla_general}")

    if not vanilla_entities.exists():
        raise FileNotFoundError(f"Fichier introuvable : {vanilla_entities}")

    ensure_clean_directory(out_mod_dir, force=force)
    out_data_sheets.mkdir(parents=True, exist_ok=True)

    write_manifest(out_mod_dir)
    write_readme(out_mod_dir)

    create_general_override(
        vanilla_general=vanilla_general,
        out_general=out_data_sheets / "General.xlsx",
        report=report,
    )

    create_entities_override(
        vanilla_entities=vanilla_entities,
        out_entities=out_data_sheets / "Entities.xlsx",
        report=report,
    )

    if clear_cache:
        local_uboat_dir = default_local_mods_root().parent
        clear_uboat_cache(local_uboat_dir, report)

    return report


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    out_mod_dir = args.out.expanduser().resolve()

    try:
        report = generate_mod(
            uboat_root=args.uboat,
            out_mod_dir=out_mod_dir,
            force=args.force,
            clear_cache=args.clear_cache,
        )

    except Exception as exc:
        print("\nERREUR : génération du mod impossible.", file=sys.stderr)
        print(str(exc), file=sys.stderr)
        return 1

    print("\n=== Mod généré avec succès ===")
    print(f"Dossier du mod : {out_mod_dir}")

    print("\nFichiers modifiés/générés :")
    for changed in report.changed_files:
        print(f"  - {changed}")

    if report.info:
        print("\nDétails des changements :")
        for item in report.info:
            print(f"  - {item}")

    if report.warnings:
        print("\nAvertissements :")
        for warning in report.warnings:
            print(f"  - {warning}")

    print("\nÉtapes suivantes :")
    print("  1. Lance UBOAT Launcher.")
    print("  2. Va dans Mods.")
    print(f"  3. Active '{MOD_DISPLAY_NAME}'.")
    print("  4. Mets-le après les autres mods qui modifient General.xlsx ou Entities.xlsx.")
    print("  5. Lance une nouvelle carrière si les changements d'air/ventilation ne s'appliquent pas sur une sauvegarde existante.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
