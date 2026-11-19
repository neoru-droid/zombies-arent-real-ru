#!/bin/sh
"exec" "python3" "$0" "$@"
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib
from collections import Counter
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Protocol, TextIO

TARGET_APP_ID = 5185890
BOOSTY_URL = "https://boosty.to/cd_tixo"
GAME_FOLDER_NAME = "Zombies-Arent-Real"
GAME_EXE_NAME = "zombies-arent-real.exe"
SCHEMA_FILE_NAME = "UserGameStatsSchema_5185890.bin"
SETUP_EXE_NAME = "ZombiesArentReal-RU-Setup.exe"
BACKUP_DIR_NAME = "ru_backup_orig"
MANIFEST_FILE_NAME = "backup_manifest.json"

MANAGED_FONTS: tuple[str, ...] = ("fonts/Carlito.ttf", "fonts/Carlito-OFL.txt")
OPTIONAL_FONTS: tuple[str, ...] = ("fonts/Tactical_HUD.ttf", "fonts/Tactical_HUD-OFL.txt")
ALL_FONT_NAMES: tuple[str, ...] = (
    "Carlito.ttf",
    "Carlito-OFL.txt",
    "Tactical_HUD.ttf",
    "Tactical_HUD-OFL.txt",
)

RU_TOP_HEADER: tuple[str, ...] = (
    "# Zombies Aren't Real - Russian translation (lang/ru.lang) of lang/en.lang.",
    "#",
    "# Same keys and {placeholders} as lang/en.lang, in the same order; only the text between them",
    "# is translated. The notes on each section are lang/en.lang's, kept in English for whoever",
    "# edits this file. Every line is translated: test_lang_files_match fails on one left out. Written in UTF-8: the game",
    "# loads this file in its Cyrillic codepage (src/glyphs.odin) and draws it in a font file that",
    "# has Cyrillic letters (the bundled Carlito when the chosen font has none, src/fontfiles.odin).",
)

PLACEHOLDER_PATTERN = re.compile(r"\{[^{}]+\}")
VDF_PATH_PATTERN = re.compile(r'"path"\s+"([^"]+)"')
ACF_INSTALLDIR_PATTERN = re.compile(r'"installdir"\s+"([^"]+)"')
STATS_FILE_PATTERN = re.compile(r"^UserGameStats_(\d+)_5185890\.bin$", re.IGNORECASE)

KV_MAP = 0x00
KV_STR = 0x01
KV_INT32 = 0x02
KV_WSTRING = 0x05
KV_END = 0x08
KV_FIXED_WIDTHS: dict[int, int] = {0x02: 4, 0x03: 4, 0x04: 4, 0x06: 4, 0x07: 8, 0x0A: 8}


@dataclass(frozen=True)
class PayloadBundle:
    version: str
    app_id: int
    lang: dict[str, str]
    fonts: dict[str, bytes]


@dataclass
class BackupFileItem:
    target_kind: str
    rel_path: str
    backup_rel_path: str
    orig_sha256: str
    patched_sha256: str
    existed_originally: bool = True


@dataclass
class BackupManifest:
    manifest_version: int = 1
    installer_version: str = "1.1.0"
    app_id: int = TARGET_APP_ID
    created_utc: str = ""
    updated_utc: str = ""
    settings_existed: bool = False
    original_language_line: str | None = None
    steam_schema_source_path: str | None = None
    files: list[BackupFileItem] = field(default_factory=list)


class GuiBackend(Protocol):
    def choose_action(
        self, title: str, status_line: str, items: list[tuple[str, str]]
    ) -> str | None: ...

    def pick_directory(self, title: str, initial_dir: Path | None) -> Path | None: ...

    def show_info(self, title: str, message: str) -> None: ...

    def show_error(self, title: str, message: str) -> None: ...

    def show_report(self, title: str, body: str) -> None: ...


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _normalize_rel_slashes(rel: str) -> str:
    return rel.replace("\\", "/")


def _bundle_from_dict(root: dict[str, Any]) -> PayloadBundle | None:
    if root.get("app_id") != TARGET_APP_ID:
        return None
    if not {"lang", "fonts"}.issubset(root):
        return None
    fonts = {
        str(name): base64.b64decode(b64_str)
        for name, b64_str in dict(root["fonts"]).items()
    }
    lang_map = {str(k): str(v) for k, v in dict(root["lang"]).items()}
    return PayloadBundle(
        version=str(root.get("version", "1.1.0")),
        app_id=TARGET_APP_ID,
        lang=lang_map,
        fonts=fonts,
    )


def _try_decompress_payload_at(raw: bytes, offset: int) -> PayloadBundle | None:
    try:
        dec = zlib.decompressobj(16 + zlib.MAX_WBITS)
        out = dec.decompress(raw[offset:]) + dec.flush()
        parsed = json.loads(out.decode("utf-8")) if dec.eof else None
    except Exception:
        return None
    return _bundle_from_dict(parsed) if isinstance(parsed, dict) else None


def extract_payload_from_bytes(raw: bytes) -> PayloadBundle:
    pos = 0
    while True:
        idx = raw.find(b"\x1f\x8b\x08", pos)
        if idx < 0:
            break
        found = _try_decompress_payload_at(raw, idx)
        if found is not None:
            return found
        pos = idx + 1
    raise ValueError("Не удалось найти встроенный ресурс русификатора (zar_ru_payload.gz).")


def _load_payload_from_data_dir(data_dir: Path) -> PayloadBundle | None:
    lang_json = data_dir / "ru_translations.json"
    fonts_dir = data_dir / "fonts"
    if not (lang_json.is_file() and fonts_dir.is_dir()):
        return None
    fonts_b64 = {
        fname: base64.b64encode((fonts_dir / fname).read_bytes()).decode("ascii")
        for fname in ALL_FONT_NAMES
        if (fonts_dir / fname).is_file()
    }
    if len(fonts_b64) != len(ALL_FONT_NAMES):
        return None
    return _bundle_from_dict(
        {
            "version": "1.2.0",
            "app_id": TARGET_APP_ID,
            "lang": json.loads(lang_json.read_text(encoding="utf-8")),
            "fonts": fonts_b64,
        }
    )


def _payload_candidate_files(script_dir: Path) -> tuple[Path, ...]:
    return (
        script_dir / SETUP_EXE_NAME,
        script_dir / "ru_payload.gz",
        script_dir.parent / "payload" / "ru_payload.gz",
        script_dir.parent.parent / "payload" / "ru_payload.gz",
    )


def load_payload(explicit_payload: Path | None, script_path: Path) -> PayloadBundle:
    if explicit_payload is not None:
        if not explicit_payload.is_file():
            raise ValueError(f"Не найден файл ресурсов русификатора: {explicit_payload}")
        return extract_payload_from_bytes(explicit_payload.read_bytes())

    base_dir = script_path if script_path.is_dir() else script_path.resolve().parent
    for cand in _payload_candidate_files(base_dir):
        if cand.is_file():
            return extract_payload_from_bytes(cand.read_bytes())

    from_data = _load_payload_from_data_dir(base_dir.parent / "data")
    if from_data is not None:
        return from_data
    raise ValueError(
        f"Не найден {SETUP_EXE_NAME} рядом со скриптом. Распакуйте все файлы архива в одну папку."
    )


def is_valid_game_dir(path: Path | None) -> bool:
    if path is None:
        return False
    return (path / GAME_EXE_NAME).is_file() and (path / "lang" / "en.lang").is_file()


def _clean_path_arg(raw_arg: Path | str | None) -> Path | None:
    if raw_arg is None:
        return None
    cleaned = str(raw_arg).strip().strip('"')
    return Path(cleaned).expanduser() if cleaned else None


def normalize_game_dir(picked: Path | str | None) -> Path | None:
    cand = _clean_path_arg(picked)
    if cand is None:
        return None
    if cand.is_file():
        cand = cand.parent
    if is_valid_game_dir(cand):
        return cand.resolve()
    if is_valid_game_dir(cand.parent):
        return cand.parent.resolve()
    return cand


def _home_steam_candidates(home: Path) -> tuple[Path, ...]:
    return (
        home / ".local" / "share" / "Steam",
        home / ".steam" / "steam",
        home / ".steam" / "root",
        home / ".var" / "app" / "com.valvesoftware.Steam" / ".local" / "share" / "Steam",
    )


def _is_steam_root(cand: Path) -> bool:
    if not cand.is_dir():
        return False
    return (
        (cand / "appcache" / "stats").is_dir()
        or (cand / "steamapps" / "libraryfolders.vdf").is_file()
        or (cand / "steam.sh").is_file()
    )


def _infer_steam_root_from_game(game_dir: Path | str | None) -> Path | None:
    norm = normalize_game_dir(game_dir)
    if norm is None:
        return None
    if norm.parent.name.lower() == "common" and norm.parent.parent.name.lower() == "steamapps":
        return norm.parent.parent.parent
    return None


def detect_steam_dir(
    explicit_steam_dir: Path | str | None,
    hint_game_dir: Path | str | None = None,
    *,
    home_dir: Path | None = None,
    media_root: Path = Path("/run/media"),
) -> Path | None:
    explicit = _clean_path_arg(explicit_steam_dir)
    if explicit is not None:
        return explicit.resolve() if explicit.is_dir() else None

    for cand in _home_steam_candidates(home_dir or Path.home()):
        if _is_steam_root(cand):
            return cand.resolve()

    inferred = _infer_steam_root_from_game(hint_game_dir)
    return inferred.resolve() if (inferred is not None and _is_steam_root(inferred)) else None


def resolve_stats_dir(steam_dir: Path | None) -> Path | None:
    if steam_dir is None:
        return None
    nested = steam_dir / "appcache" / "stats"
    if nested.is_dir():
        return nested
    if (steam_dir / SCHEMA_FILE_NAME).is_file() or steam_dir.name == "stats":
        return steam_dir
    return nested


def _vdf_library_roots(steam_root: Path) -> list[Path]:
    vdf_file = steam_root / "steamapps" / "libraryfolders.vdf"
    if not vdf_file.is_file():
        return []
    text = vdf_file.read_text(encoding="utf-8", errors="ignore")
    return [Path(m.replace("\\\\", "\\")) for m in VDF_PATH_PATTERN.findall(text)]


def _media_library_roots(media_root: Path, user_name: str) -> list[Path]:
    roots: list[Path] = [media_root / "mmcblk0p1", media_root / "mmcblk0p1" / "SteamLibrary"]
    for owner in dict.fromkeys(("deck", user_name)):
        owner_dir = (media_root / owner) if owner else None
        if owner_dir is not None and owner_dir.is_dir():
            for child in sorted(owner_dir.iterdir()):
                roots.extend((child, child / "SteamLibrary"))
    return roots


def _game_candidates_in_library(lib_root: Path) -> list[Path]:
    steamapps = lib_root / "steamapps"
    acf = steamapps / f"appmanifest_{TARGET_APP_ID}.acf"
    out: list[Path] = []
    if acf.is_file():
        match = ACF_INSTALLDIR_PATTERN.search(acf.read_text(encoding="utf-8", errors="ignore"))
        if match:
            out.append(steamapps / "common" / match.group(1))
    out.append(steamapps / "common" / GAME_FOLDER_NAME)
    return out


def _collect_library_roots(
    steam_dir: Path | None, home: Path, media_root: Path, user: str
) -> list[Path]:
    roots: list[Path] = []
    for cand in (steam_dir, *_home_steam_candidates(home)):
        if cand is not None and cand.is_dir():
            roots.append(cand)
            roots.extend(_vdf_library_roots(cand))
    roots.extend(_media_library_roots(media_root, user))
    return roots


def detect_game_dir(
    explicit_game_dir: Path | str | None,
    steam_dir: Path | None,
    script_dir: Path,
    *,
    home_dir: Path | None = None,
    media_root: Path = Path("/run/media"),
    user_name: str | None = None,
) -> Path | None:
    if _clean_path_arg(explicit_game_dir) is not None:
        norm = normalize_game_dir(explicit_game_dir)
        return norm if is_valid_game_dir(norm) else None

    user = user_name if user_name is not None else os.environ.get("USER", "")
    libs = _collect_library_roots(steam_dir, home_dir or Path.home(), media_root, user)
    candidates = [cand for lib in libs for cand in _game_candidates_in_library(lib)]
    candidates.extend((script_dir, script_dir.parent))
    for cand in candidates:
        if is_valid_game_dir(cand):
            return cand.resolve()
    return None


def _pid_runs_game(pid_dir: Path) -> bool:
    if not pid_dir.name.isdigit():
        return False
    try:
        return b"zombies-arent-real" in (pid_dir / "cmdline").read_bytes().lower()
    except OSError:
        return False


def assert_game_closed(proc_root: Path = Path("/proc")) -> None:
    if not proc_root.is_dir():
        return
    for entry in proc_root.iterdir():
        if _pid_runs_game(entry):
            raise RuntimeError(
                f"Игра Zombies Aren't Real сейчас запущена (PID {entry.name}). "
                "Закройте игру перед установкой или возвратом оригинала."
            )


def _is_lang_comment_or_raw(line: str) -> bool:
    stripped = line.strip()
    return (not stripped) or (stripped[0] in ("#", ";")) or ("=" not in line)


def parse_lang_kv_map(text: str) -> dict[str, str]:
    mapping: dict[str, str] = {}
    for raw_line in text.splitlines():
        if _is_lang_comment_or_raw(raw_line):
            continue
        key, _, val = raw_line.partition("=")
        k = key.strip()
        if k:
            mapping[k] = val.strip()
    return mapping


def _valid_lang_candidate(en_val: str, cand: str | None) -> bool:
    if cand is None or (bool(en_val) and not cand):
        return False
    return Counter(PLACEHOLDER_PATTERN.findall(en_val)) == Counter(
        PLACEHOLDER_PATTERN.findall(cand)
    )


def _pick_translated_value(
    key: str,
    en_val: str,
    payload_lang: dict[str, str],
    fallback_ru: dict[str, str],
) -> tuple[str, bool]:
    cand = payload_lang.get(key)
    if _valid_lang_candidate(en_val, cand):
        return str(cand), True
    fb = fallback_ru.get(key)
    if _valid_lang_candidate(en_val, fb):
        return str(fb), False
    return en_val, False


def _replace_lang_header(raw_lines: list[str]) -> list[str]:
    hdr_end = 0
    while hdr_end < len(raw_lines) and raw_lines[hdr_end].lstrip().startswith(("#", ";")):
        hdr_end += 1
    return [*RU_TOP_HEADER, *raw_lines[hdr_end:]] if hdr_end > 0 else raw_lines


def patch_lang_to_utf8_crlf(
    en_text: str,
    payload_lang: dict[str, str],
    fallback_ru: dict[str, str],
) -> tuple[bytes, int, int, int]:
    lines = _replace_lang_header(en_text.splitlines())
    out_lines: list[str] = []
    total = translated = 0
    for line in lines:
        if _is_lang_comment_or_raw(line):
            out_lines.append(line)
            continue
        key_part, _, val_part = line.partition("=")
        key, en_val = key_part.strip(), val_part.strip()
        chosen, is_primary = _pick_translated_value(key, en_val, payload_lang, fallback_ru)
        total += 1
        translated += int(is_primary)
        out_lines.append(f"{key} = {chosen}")

    suffix = "\r\n" if en_text.endswith(("\n", "\r")) else ""
    fallbacks = total - translated
    return ("\r\n".join(out_lines) + suffix).encode("utf-8"), total, translated, fallbacks


def _split_bom_and_eol(raw_line: bytes, is_first: bool) -> tuple[bytes, bytes, bytes]:
    has_bom = is_first and raw_line.startswith(b"\xef\xbb\xbf")
    bom = b"\xef\xbb\xbf" if has_bom else b""
    body = raw_line[3:] if has_bom else raw_line
    if body.endswith(b"\r\n"):
        return bom, body[:-2], b"\r\n"
    if body.endswith(b"\n"):
        return bom, body[:-1], b"\n"
    return bom, body, b""


def _patch_single_ini_line(
    raw_line: bytes, replacement: bytes, is_first: bool
) -> tuple[bytes, str | None]:
    bom, content, eol = _split_bom_and_eol(raw_line, is_first)
    stripped = content.strip(b" \t")
    if not stripped or stripped[:1] in (b"#", b";", b"[") or b"=" not in stripped:
        return raw_line, None
    key_part, _, _ = content.partition(b"=")
    if key_part.strip(b" \t") != b"language":
        return raw_line, None
    return bom + replacement + eol, content.decode("utf-8", errors="replace")


def update_settings_language_bytes(
    raw_settings: bytes, target_line: str
) -> tuple[bytes, str | None]:
    rep_bytes = target_line.encode("utf-8")
    out = bytearray()
    orig_line: str | None = None
    for idx, line in enumerate(raw_settings.splitlines(keepends=True)):
        if orig_line is None:
            patched, detected = _patch_single_ini_line(line, rep_bytes, idx == 0)
            if detected is not None:
                orig_line = detected
                out.extend(patched)
                continue
        out.extend(line)
    return bytes(out), orig_line


def load_backup_manifest(manifest_path: Path) -> BackupManifest:
    root = json.loads(manifest_path.read_text(encoding="utf-8"))
    files = [
        BackupFileItem(
            target_kind=str(f.get("target_kind", "game")),
            rel_path=_normalize_rel_slashes(str(f.get("rel_path", ""))),
            backup_rel_path=_normalize_rel_slashes(str(f.get("backup_rel_path", ""))),
            orig_sha256=str(f.get("orig_sha256", "")),
            patched_sha256=str(f.get("patched_sha256", "")),
            existed_originally=bool(f.get("existed_originally", True)),
        )
        for f in root.get("files", [])
    ]
    return BackupManifest(
        manifest_version=int(root.get("manifest_version", 1)),
        installer_version=str(root.get("installer_version", "1.1.0")),
        app_id=int(root.get("app_id", TARGET_APP_ID)),
        created_utc=str(root.get("created_utc", "")),
        updated_utc=str(root.get("updated_utc", "")),
        settings_existed=bool(root.get("settings_existed", False)),
        original_language_line=root.get("original_language_line"),
        steam_schema_source_path=root.get("steam_schema_source_path"),
        files=files,
    )


def save_backup_manifest(manifest_path: Path, manifest: BackupManifest) -> None:
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "manifest_version": manifest.manifest_version,
        "installer_version": manifest.installer_version,
        "app_id": manifest.app_id,
        "created_utc": manifest.created_utc,
        "updated_utc": manifest.updated_utc,
        "settings_existed": manifest.settings_existed,
        "original_language_line": manifest.original_language_line,
        "steam_schema_source_path": manifest.steam_schema_source_path,
        "files": [
            {
                "target_kind": item.target_kind,
                "rel_path": item.rel_path,
                "backup_rel_path": item.backup_rel_path,
                "orig_sha256": item.orig_sha256,
                "patched_sha256": item.patched_sha256,
                "existed_originally": item.existed_originally,
            }
            for item in manifest.files
        ],
    }
    text = json.dumps(payload, ensure_ascii=False, indent=2) + "\n"
    write_bytes_atomic(manifest_path, text.encode("utf-8"))


def write_bytes_atomic(target: Path, data: bytes) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    tmp_path = target.parent / f".{target.name}.zar_tmp"
    try:
        tmp_path.write_bytes(data)
        os.replace(tmp_path, target)
    finally:
        tmp_path.unlink(missing_ok=True)


def has_valid_backup(game_dir: Path | None) -> bool:
    if not is_valid_game_dir(game_dir) or game_dir is None:
        return False
    mp = game_dir / BACKUP_DIR_NAME / MANIFEST_FILE_NAME
    if not mp.is_file():
        return False
    try:
        return len(load_backup_manifest(mp).files) > 0
    except Exception:
        return False


def is_russifier_installed(game_dir: Path | None) -> bool:
    if not has_valid_backup(game_dir) or game_dir is None:
        return False
    manifest = load_backup_manifest(game_dir / BACKUP_DIR_NAME / MANIFEST_FILE_NAME)
    for rec in manifest.files:
        if rec.rel_path.lower() == "lang/ru.lang":
            live_ru = game_dir / "lang" / "ru.lang"
            return live_ru.is_file() and sha256_hex(live_ru.read_bytes()) == rec.patched_sha256.lower()
    return True


def find_non_ascii_files(game_dir: Path, subdir: str, suffix: str) -> list[str]:
    folder = game_dir / subdir
    if not folder.is_dir():
        return []
    return [
        p.name
        for p in sorted(folder.iterdir())
        if p.is_file() and p.name.endswith(suffix) and max(p.read_bytes(), default=0) >= 0x80
    ]


def _find_prev_record(
    manifest: BackupManifest | None, kind: str, rel_path: str
) -> BackupFileItem | None:
    if manifest is None:
        return None
    norm = _normalize_rel_slashes(rel_path).lower()
    for item in manifest.files:
        if item.target_kind.lower() == kind.lower() and item.rel_path.lower() == norm:
            return item
    return None


def _can_reuse_backup(prev: BackupFileItem | None, backup_file: Path, live_sha: str) -> bool:
    if prev is None or not prev.existed_originally or not backup_file.is_file():
        return False
    backup_sha = sha256_hex(backup_file.read_bytes())
    if backup_sha != prev.orig_sha256.lower():
        return False
    return live_sha in (prev.patched_sha256.lower(), prev.orig_sha256.lower())


def _backup_live_file(
    live_path: Path,
    backup_path: Path,
    prev: BackupFileItem | None,
    kind: str,
    rel_path: str,
    backup_rel: str,
) -> BackupFileItem:
    if not live_path.is_file():
        return BackupFileItem(kind, rel_path, backup_rel, "", "", existed_originally=False)
    live_bytes = live_path.read_bytes()
    if not _can_reuse_backup(prev, backup_path, sha256_hex(live_bytes)):
        write_bytes_atomic(backup_path, live_bytes)
    orig_sha = sha256_hex(backup_path.read_bytes())
    return BackupFileItem(kind, rel_path, backup_rel, orig_sha, "", existed_originally=True)


def _patch_game_lang_and_fonts(
    payload: PayloadBundle,
    game_dir: Path,
    backup_root: Path,
    prev_manifest: BackupManifest | None,
    log: Callable[[str], None],
) -> list[BackupFileItem]:
    records: list[BackupFileItem] = []
    lang_rec = _backup_live_file(
        game_dir / "lang" / "ru.lang",
        backup_root / "lang" / "ru.lang",
        _find_prev_record(prev_manifest, "game", "lang/ru.lang"),
        "game",
        "lang/ru.lang",
        "lang/ru.lang",
    )
    backup_ru = backup_root / lang_rec.backup_rel_path
    fb_map = parse_lang_kv_map(backup_ru.read_text("utf-8")) if backup_ru.is_file() else {}
    en_text = (game_dir / "lang" / "en.lang").read_text(encoding="utf-8")
    patched_ru, total, tr_cnt, fb_cnt = patch_lang_to_utf8_crlf(en_text, payload.lang, fb_map)
    write_bytes_atomic(game_dir / "lang" / "ru.lang", patched_ru)
    lang_rec.patched_sha256 = sha256_hex(patched_ru)
    records.append(lang_rec)
    suffix_note = f" (резерв совместимости: {fb_cnt})." if fb_cnt else "."
    log(f"Обновлён lang/ru.lang: переведено {tr_cnt} из {total} ключей{suffix_note}")

    for rel_font in MANAGED_FONTS:
        f_rec = _backup_live_file(
            game_dir / rel_font,
            backup_root / rel_font,
            _find_prev_record(prev_manifest, "game", rel_font),
            "game",
            rel_font,
            rel_font,
        )
        font_bytes = payload.fonts[Path(rel_font).name]
        write_bytes_atomic(game_dir / rel_font, font_bytes)
        f_rec.patched_sha256 = sha256_hex(font_bytes)
        records.append(f_rec)

    for opt_font in OPTIONAL_FONTS:
        write_bytes_atomic(game_dir / opt_font, payload.fonts[Path(opt_font).name])
    log("Установлены кириллические шрифты: пиксельный 8x8 и гладкий Tactical HUD.")
    return records


def _patch_settings_step(
    game_dir: Path,
    backup_root: Path,
    prev_manifest: BackupManifest | None,
    next_manifest: BackupManifest,
    log: Callable[[str], None],
) -> None:
    rel = "steam_cloud/settings.ini"
    live_path = game_dir / rel
    if not live_path.is_file():
        return
    backup_path = backup_root / rel
    live_bytes = live_path.read_bytes()
    if prev_manifest is None or not backup_path.is_file():
        write_bytes_atomic(backup_path, live_bytes)
    updated, detected = update_settings_language_bytes(live_bytes, "language = ru")
    write_bytes_atomic(live_path, updated)

    next_manifest.settings_existed = True
    prev_orig = prev_manifest.original_language_line if prev_manifest else None
    next_manifest.original_language_line = prev_orig if prev_orig is not None else detected
    next_manifest.files.append(
        BackupFileItem(
            "settings_reference",
            rel,
            rel,
            sha256_hex(backup_path.read_bytes()),
            sha256_hex(updated),
            True,
        )
    )
    log("В настройках steam_cloud/settings.ini включён русский язык (остальные параметры сохранены).")


def install_russifier(
    payload: PayloadBundle,
    game_dir: Path,
    steam_dir: Path | None,
    log: Callable[[str], None],
    *,
    proc_root: Path = Path("/proc"),
) -> None:
    if not is_valid_game_dir(game_dir):
        raise FileNotFoundError(f"Папка игры не содержит {GAME_EXE_NAME} и lang/en.lang: {game_dir}")
    assert_game_closed(proc_root)

    backup_root = game_dir / BACKUP_DIR_NAME
    manifest_path = backup_root / MANIFEST_FILE_NAME
    prev = load_backup_manifest(manifest_path) if has_valid_backup(game_dir) else None
    now_iso = datetime.now(timezone.utc).isoformat()
    next_manifest = BackupManifest(
        installer_version=payload.version,
        app_id=payload.app_id,
        created_utc=(prev.created_utc if prev and prev.created_utc else now_iso),
        updated_utc=now_iso,
    )

    log(f"Проверка папки игры: {game_dir}")
    next_manifest.files.extend(
        _patch_game_lang_and_fonts(payload, game_dir, backup_root, prev, log)
    )
    _patch_settings_step(game_dir, backup_root, prev, next_manifest, log)
    if prev:
        next_manifest.files.extend(r for r in prev.files if r.target_kind == "steam_schema")
        next_manifest.steam_schema_source_path = prev.steam_schema_source_path
    save_backup_manifest(manifest_path, next_manifest)
    log("Готово: Русификатор установлен.")


def _restore_game_record(rec: BackupFileItem, game_dir: Path, backup_root: Path) -> None:
    target = game_dir / rec.rel_path
    if not rec.existed_originally:
        target.unlink(missing_ok=True)
        return
    orig_bytes = (backup_root / rec.backup_rel_path).read_bytes()
    if rec.orig_sha256 and sha256_hex(orig_bytes) != rec.orig_sha256.lower():
        raise ValueError(f"Несовпадение контрольной суммы SHA-256 резервной копии {rec.rel_path}")
    write_bytes_atomic(target, orig_bytes)


def _resolve_schema_restore_target(
    steam_dir: Path | None, manifest: BackupManifest
) -> Path | None:
    stats_dir = resolve_stats_dir(steam_dir)
    if stats_dir is not None:
        return stats_dir / SCHEMA_FILE_NAME
    return Path(manifest.steam_schema_source_path) if manifest.steam_schema_source_path else None


def _restore_schema_record(
    rec: BackupFileItem,
    backup_root: Path,
    steam_dir: Path | None,
    manifest: BackupManifest,
    log: Callable[[str], None],
) -> None:
    schema_target = _resolve_schema_restore_target(steam_dir, manifest)
    backup_schema = backup_root / _normalize_rel_slashes(rec.backup_rel_path)
    if schema_target is None or not backup_schema.is_file():
        return
    try:
        write_bytes_atomic(schema_target, backup_schema.read_bytes())
        log("Восстановлена оригинальная схема достижений Steam.")
    except PermissionError:
        raise PermissionError(f"Исходная копия для {schema_target} сохранена в {backup_root}.")


def uninstall_russifier(
    game_dir: Path,
    steam_dir: Path | None,
    log: Callable[[str], None],
    *,
    proc_root: Path = Path("/proc"),
) -> None:
    if not is_valid_game_dir(game_dir):
        raise FileNotFoundError(f"Папка игры не содержит {GAME_EXE_NAME} и lang/en.lang: {game_dir}")
    assert_game_closed(proc_root)

    backup_root = game_dir / BACKUP_DIR_NAME
    manifest_path = backup_root / MANIFEST_FILE_NAME
    if not manifest_path.is_file():
        raise RuntimeError("Резервная копия ru_backup_orig не найдена: оригинальные файлы уже используются.")

    manifest = load_backup_manifest(manifest_path)
    for rec in manifest.files:
        if rec.target_kind == "game":
            _restore_game_record(rec, game_dir, backup_root)

    settings_path = game_dir / "steam_cloud" / "settings.ini"
    if manifest.settings_existed and manifest.original_language_line and settings_path.is_file():
        restored, _ = update_settings_language_bytes(
            settings_path.read_bytes(), manifest.original_language_line
        )
        write_bytes_atomic(settings_path, restored)
        log("Восстановлен исходный параметр языка в steam_cloud/settings.ini.")

    for opt_font in OPTIONAL_FONTS:
        (game_dir / opt_font).unlink(missing_ok=True)

    for rec in manifest.files:
        if rec.target_kind == "steam_schema":
            _restore_schema_record(rec, backup_root, steam_dir, manifest, log)
    shutil.rmtree(backup_root)
    log("Готово: Оригинальные файлы игры полностью восстановлены.")


def _format_cleanliness_line(label: str, game_dir: Path, subdir: str, suffix: str) -> str:
    tainted = find_non_ascii_files(game_dir, subdir, suffix)
    return f"{label}: true" if not tainted else f"{label}: warning ({', '.join(tainted)})"


def format_status_report(
    payload: PayloadBundle, game_dir: Path | None, steam_dir: Path | None
) -> tuple[str, int]:
    valid = is_valid_game_dir(game_dir)
    lines = [
        f"GameDir: {game_dir if valid else 'NOT_FOUND'}",
        f"SteamDir: {steam_dir if steam_dir is not None else 'NOT_FOUND'}",
        f"Installed: {'true' if is_russifier_installed(game_dir) else 'false'}",
        f"BackupManifest: {'present' if has_valid_backup(game_dir) else 'absent'}",
    ]
    if valid and game_dir is not None:
        lines.append(_format_cleanliness_line("CampaignFilesClean", game_dir, "campaigns", ".cmp"))
        lines.append(_format_cleanliness_line("MutatorFilesClean", game_dir, "mutators", ".ini"))
    return "\n".join(lines) + "\n", (0 if valid else 2)


class KdialogBackend:
    def __init__(self, runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run) -> None:
        self._run = runner

    def choose_action(
        self, title: str, status_line: str, items: list[tuple[str, str]]
    ) -> str | None:
        flat = [val for pair in items for val in pair]
        res = self._run(
            ["kdialog", "--title", title, "--menu", status_line, *flat],
            capture_output=True,
            text=True,
            check=False,
        )
        return res.stdout.strip() if res.returncode == 0 else None

    def pick_directory(self, title: str, initial_dir: Path | None) -> Path | None:
        start = str(initial_dir or Path.home())
        res = self._run(
            ["kdialog", "--title", title, "--getexistingdirectory", start],
            capture_output=True,
            text=True,
            check=False,
        )
        picked = res.stdout.strip() if res.returncode == 0 else ""
        return Path(picked) if picked else None

    def show_info(self, title: str, message: str) -> None:
        self._run(["kdialog", "--title", title, "--msgbox", message], check=False)

    def show_error(self, title: str, message: str) -> None:
        self._run(["kdialog", "--title", title, "--error", message], check=False)

    def show_report(self, title: str, body: str) -> None:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".txt", delete=False) as tmp:
            tmp.write(body)
            tmp_name = tmp.name
        try:
            self._run(["kdialog", "--title", title, "--textbox", tmp_name, "720", "520"], check=False)
        finally:
            Path(tmp_name).unlink(missing_ok=True)


class ZenityBackend:
    def __init__(self, runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run) -> None:
        self._run = runner

    def choose_action(
        self, title: str, status_line: str, items: list[tuple[str, str]]
    ) -> str | None:
        flat = [val for pair in items for val in pair]
        res = self._run(
            [
                "zenity",
                "--list",
                f"--title={title}",
                f"--text={status_line}",
                "--width=640",
                "--height=380",
                "--hide-column=1",
                "--column=ID",
                "--column=Действие",
                *flat,
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        return res.stdout.strip() if res.returncode == 0 else None

    def pick_directory(self, title: str, initial_dir: Path | None) -> Path | None:
        start = f"{initial_dir or Path.home()}/"
        res = self._run(
            ["zenity", "--file-selection", "--directory", f"--title={title}", f"--filename={start}"],
            capture_output=True,
            text=True,
            check=False,
        )
        picked = res.stdout.strip() if res.returncode == 0 else ""
        return Path(picked) if picked else None

    def show_info(self, title: str, message: str) -> None:
        self._run(["zenity", "--info", f"--title={title}", "--width=520", f"--text={message}"], check=False)

    def show_error(self, title: str, message: str) -> None:
        self._run(["zenity", "--error", f"--title={title}", "--width=520", f"--text={message}"], check=False)

    def show_report(self, title: str, body: str) -> None:
        self._run(
            ["zenity", "--text-info", f"--title={title}", "--width=720", "--height=520"],
            input=body,
            text=True,
            check=False,
        )


def detect_gui_backend(
    env: dict[str, str] | None = None,
    which: Callable[[str], str | None] = shutil.which,
) -> GuiBackend | None:
    cur_env = env if env is not None else dict(os.environ)
    if not (cur_env.get("DISPLAY") or cur_env.get("WAYLAND_DISPLAY")):
        return None
    if which("kdialog"):
        return KdialogBackend()
    if which("zenity"):
        return ZenityBackend()
    return None


def _gui_pick_valid_game_dir(
    current_game: Path | None, gui: GuiBackend, title: str
) -> Path | None:
    picked = gui.pick_directory("Выберите папку Zombies-Arent-Real", current_game)
    norm = normalize_game_dir(picked)
    if is_valid_game_dir(norm):
        return norm
    if picked is not None:
        gui.show_error(title, f"В выбранной папке не найден {GAME_EXE_NAME}:\n{picked}")
    return None


def _gui_run_mutation(
    choice: str,
    payload: PayloadBundle,
    game_dir: Path,
    steam_dir: Path | None,
    gui: GuiBackend,
    title: str,
) -> None:
    logs: list[str] = []
    try:
        if choice == "install":
            install_russifier(payload, game_dir, steam_dir, logs.append)
        elif choice == "uninstall":
            uninstall_russifier(game_dir, steam_dir, logs.append)
        gui.show_info(title, "\n".join(logs))
    except Exception as exc:
        gui.show_error(title, str(exc))


def _execute_gui_action(
    choice: str,
    payload: PayloadBundle,
    game_dir: Path | None,
    steam_dir: Path | None,
    gui: GuiBackend,
    title: str,
) -> tuple[Path | None, Path | None]:
    if choice == "browse":
        picked = _gui_pick_valid_game_dir(game_dir, gui, title)
        return (picked, steam_dir or detect_steam_dir(None, picked)) if picked else (game_dir, steam_dir)

    resolved = game_dir if is_valid_game_dir(game_dir) else _gui_pick_valid_game_dir(game_dir, gui, title)
    if resolved is None:
        return game_dir, steam_dir
    eff_steam = steam_dir or detect_steam_dir(None, resolved)
    _gui_run_mutation(choice, payload, resolved, eff_steam, gui, title)
    return resolved, eff_steam


def _gui_state_label(game_dir: Path | None) -> str:
    if not is_valid_game_dir(game_dir):
        return "Папка игры не выбрана"
    return "Установлен" if is_russifier_installed(game_dir) else "Не установлен"


def run_gui_menu(
    payload: PayloadBundle,
    game_dir: Path | None,
    steam_dir: Path | None,
    gui: GuiBackend,
) -> int:
    title = "Zombies Aren't Real - Русификатор v1.2"
    cur_game, cur_steam = game_dir, steam_dir
    while True:
        status_text = (
            f"Состояние: {_gui_state_label(cur_game)}\n"
            f"Папка игры: {cur_game or 'не найдена'}\n"
            f"Boosty: {BOOSTY_URL}"
        )
        items = [
            ("install", "Установить русификатор"),
            ("uninstall", "Вернуть оригинал"),
            ("browse", "Выбрать папку с игрой..."),
            ("exit", "Выход"),
        ]
        choice = gui.choose_action(title, status_text, items)
        if not choice or choice == "exit":
            return 0
        cur_game, cur_steam = _execute_gui_action(
            choice, payload, cur_game, cur_steam, gui, title
        )


def _prompt_terminal_game_dir(stdin: TextIO, stdout: TextIO) -> Path | None:
    stdout.write("Введите путь к папке Zombies-Arent-Real: ")
    stdout.flush()
    norm = normalize_game_dir(stdin.readline())
    if is_valid_game_dir(norm):
        return norm
    stdout.write(f"ОШИБКА: В указанной папке не найден {GAME_EXE_NAME}.\n")
    return None


def _run_terminal_mutation(
    is_install: bool,
    payload: PayloadBundle,
    game_dir: Path,
    steam_dir: Path | None,
    stdout: TextIO,
) -> None:
    writer = lambda s: stdout.write(s + "\n")  # noqa: E731
    try:
        if is_install:
            install_russifier(payload, game_dir, steam_dir, writer)
        else:
            uninstall_russifier(game_dir, steam_dir, writer)
    except Exception as exc:
        stdout.write(f"ОШИБКА: {exc}\n")


def _handle_terminal_mutation_choice(
    choice: str,
    payload: PayloadBundle,
    game_dir: Path | None,
    steam_dir: Path | None,
    stdin: TextIO,
    stdout: TextIO,
) -> tuple[Path | None, Path | None]:
    resolved = game_dir if is_valid_game_dir(game_dir) else _prompt_terminal_game_dir(stdin, stdout)
    if resolved is None:
        return game_dir, steam_dir
    eff_steam = steam_dir or detect_steam_dir(None, resolved)
    _run_terminal_mutation(choice == "1", payload, resolved, eff_steam, stdout)
    return resolved, eff_steam


def _handle_terminal_choice(
    choice: str,
    payload: PayloadBundle,
    game_dir: Path | None,
    steam_dir: Path | None,
    stdin: TextIO,
    stdout: TextIO,
) -> tuple[Path | None, Path | None, bool]:
    if choice in ("0", "q", "exit"):
        return game_dir, steam_dir, True
    if choice == "3":
        picked = _prompt_terminal_game_dir(stdin, stdout)
        return (picked, steam_dir or detect_steam_dir(None, picked), False) if picked else (game_dir, steam_dir, False)
    if choice in ("1", "2"):
        g_dir, s_dir = _handle_terminal_mutation_choice(
            choice, payload, game_dir, steam_dir, stdin, stdout
        )
        return g_dir, s_dir, False
    return game_dir, steam_dir, False


def run_terminal_menu(
    payload: PayloadBundle,
    game_dir: Path | None,
    steam_dir: Path | None,
    *,
    stdin: TextIO = sys.stdin,
    stdout: TextIO = sys.stdout,
) -> int:
    cur_game, cur_steam = game_dir, steam_dir
    while True:
        valid = is_valid_game_dir(cur_game)
        state_lbl = ("Установлен" if is_russifier_installed(cur_game) else "Не установлен") if valid else "укажите папку игры"
        stdout.write(
            f"\n=== Zombies Aren't Real - Русификатор v1.2 (Steam Deck / Linux) ===\n"
            f"Папка игры: {cur_game if valid else 'не найдена'}\n"
            f"Состояние:  {state_lbl}\n"
            f"Boosty:     {BOOSTY_URL}\n"
            f"1) Установить русификатор\n"
            f"2) Вернуть оригинал\n"
            f"3) Указать папку с игрой\n"
            f"0) Выход\n"
            f"Выберите пункт [0-3]: "
        )
        stdout.flush()
        raw = stdin.readline()
        if not raw:
            return 0
        cur_game, cur_steam, done = _handle_terminal_choice(
            raw.strip(), payload, cur_game, cur_steam, stdin, stdout
        )
        if done:
            return 0


def _build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="install-steamdeck.sh",
        description="Русификатор текста и шрифтов для Zombies Aren't Real (Steam Deck / Linux)",
    )
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--install", action="store_true", help="Установить русификатор")
    group.add_argument(
        "--uninstall", "--restore", dest="uninstall", action="store_true", help="Вернуть оригинал"
    )
    group.add_argument("--status", action="store_true", help="Показать состояние установки и пути")
    parser.add_argument("--game-dir", type=Path, default=None, help="Путь к папке Zombies-Arent-Real")
    parser.add_argument("--steam-dir", type=Path, default=None, help="Путь к папке клиента Steam")
    parser.add_argument("--payload", type=Path, default=None, help="Путь к ресурсу или Setup.exe")
    return parser


def _require_resolved_game_dir(
    explicit_game_dir: Path | None, steam_dir: Path | None, script_dir: Path
) -> Path:
    if explicit_game_dir is not None:
        norm = normalize_game_dir(explicit_game_dir)
        if not is_valid_game_dir(norm) or norm is None:
            raise FileNotFoundError(
                f"В указанной папке не найдены {GAME_EXE_NAME} и lang/en.lang: {explicit_game_dir}"
            )
        return norm
    detected = detect_game_dir(None, steam_dir, script_dir)
    if detected is None:
        raise FileNotFoundError(
            "Не удалось автоматически найти папку Zombies-Arent-Real. Укажите --game-dir."
        )
    return detected


def _run_cli_mutation(
    is_install: bool,
    explicit_game: Path | None,
    payload: PayloadBundle,
    steam_dir: Path | None,
    script_dir: Path,
    stdout: TextIO,
) -> int:
    game_dir = _require_resolved_game_dir(explicit_game, steam_dir, script_dir)
    eff_steam = steam_dir or detect_steam_dir(None, game_dir)
    writer = lambda s: stdout.write(s + "\n")  # noqa: E731
    if is_install:
        install_russifier(payload, game_dir, eff_steam, writer)
    else:
        uninstall_russifier(game_dir, eff_steam, writer)
    return 0


def _run_interactive_mode(
    explicit_game: Path | None,
    payload: PayloadBundle,
    steam_dir: Path | None,
    script_dir: Path,
    stdin: TextIO,
    stdout: TextIO,
    gui_override: GuiBackend | None = None,
) -> int:
    game_dir = detect_game_dir(explicit_game, steam_dir, script_dir)
    eff_steam = steam_dir or detect_steam_dir(None, game_dir)
    gui = gui_override if gui_override is not None else detect_gui_backend()
    if gui is not None:
        return run_gui_menu(payload, game_dir, eff_steam, gui)
    return run_terminal_menu(payload, game_dir, eff_steam, stdin=stdin, stdout=stdout)


def _dispatch_cli_mode(
    args: argparse.Namespace,
    payload: PayloadBundle,
    steam_dir: Path | None,
    script_dir: Path,
    stdin: TextIO,
    stdout: TextIO,
) -> int:
    if args.status:
        game_dir = detect_game_dir(args.game_dir, steam_dir, script_dir)
        report, code = format_status_report(
            payload, game_dir, steam_dir or detect_steam_dir(None, game_dir)
        )
        stdout.write(report)
        return code
    if args.install or args.uninstall:
        return _run_cli_mutation(
            bool(args.install), args.game_dir, payload, steam_dir, script_dir, stdout
        )
    return _run_interactive_mode(args.game_dir, payload, steam_dir, script_dir, stdin, stdout)


def run_cli(
    argv: Sequence[str] | None = None,
    *,
    script_path: Path | None = None,
    stdin: TextIO = sys.stdin,
    stdout: TextIO = sys.stdout,
    stderr: TextIO = sys.stderr,
) -> int:
    parser = _build_arg_parser()
    args = parser.parse_args(list(argv) if argv is not None else None)
    eff_script = (script_path or Path(__file__)).resolve()
    script_dir = eff_script if eff_script.is_dir() else eff_script.parent

    try:
        payload = load_payload(args.payload, eff_script)
        steam_dir = detect_steam_dir(args.steam_dir, args.game_dir)
        return _dispatch_cli_mode(args, payload, steam_dir, script_dir, stdin, stdout)
    except Exception as exc:
        stderr.write(f"ОШИБКА: {exc}\n")
        return 1


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(run_cli())
