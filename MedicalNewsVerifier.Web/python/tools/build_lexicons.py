#!/usr/bin/env python3
"""
Офлайн-сборка дополнительного словаря эмоционально окрашенной / субъективной лексики (RU).

Источники (см. docs/lexicon_sources.md в корне веб-проекта):
  - RuSentiLex и аналогичные табличные экспорты (ручная загрузка, затем --input).
  - Встроенный seed: python/tools/seeds/subjective_ru_seed.txt (учебная подборка для репозитория).

Примеры:
  python tools/build_lexicons.py --use-seed
  python tools/build_lexicons.py --input ~/Downloads/rusentilex.tsv --max-words 8000
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


def _repo_lexicons_dir() -> Path:
    # .../MedicalNewsVerifier.Web/python/tools/build_lexicons.py -> Web root
    return Path(__file__).resolve().parent.parent.parent / "Resources" / "Lexicons"


def _default_seed() -> Path:
    return Path(__file__).resolve().parent / "seeds" / "subjective_ru_seed.txt"


_STOP = frozenset(
    """
    и в во не что он на я с со как а то все она так его но да ты к мы вы
    за по из у ка ко ли эту от до без при над об под для про меж
    это эти этот эта этиот этих этом этим
    """.split()
)


def _clean_token(w: str) -> str | None:
    w = w.strip().lower().replace("ё", "е")
    if not w or w.startswith("#"):
        return None
    w = re.sub(r"^[^\w\u0400-\u04ff]+|[^\w\u0400-\u04ff]+$", "", w, flags=re.UNICODE)
    if not w or len(w) < 2 or len(w) > 48:
        return None
    if w in _STOP:
        return None
    if not re.search(r"[\u0400-\u04ff]", w):
        return None
    return w


def _parse_line_generic(line: str) -> str | None:
    line = line.strip()
    if not line or line.startswith("#"):
        return None
    # TSV / CSV: берём первую «ячейку» как лемму/словоформу
    for sep in ("\t", ";", ","):
        if sep in line:
            parts = [p.strip() for p in line.split(sep)]
            if parts:
                return _clean_token(parts[0])
            return None
    return _clean_token(line)


def load_words_from_path(path: Path) -> set[str]:
    out: set[str] = set()
    for raw in path.read_text(encoding="utf-8").splitlines():
        tok = _parse_line_generic(raw)
        if tok:
            out.add(tok)
    return out


def build_from_seed(seed: Path) -> set[str]:
    return load_words_from_path(seed)


def build_from_rusentilex_like(path: Path) -> set[str]:
    """Табличные дампы: первая колонка — слово; строки с # пропускаются."""
    return load_words_from_path(path)


def main() -> int:
    ap = argparse.ArgumentParser(description="Сборка emotional_ru_rusentilex.txt")
    ap.add_argument("--output", type=Path, default=None, help="Путь к выходному .txt")
    ap.add_argument("--input", type=Path, default=None, help="Входной TSV/CSV/построчный список")
    ap.add_argument("--use-seed", action="store_true", help="Использовать встроенный seed-файл")
    ap.add_argument("--max-words", type=int, default=12000, help="Максимум строк в выходе")
    args = ap.parse_args()

    out_path = args.output or (_repo_lexicons_dir() / "emotional_ru_rusentilex.txt")

    words: set[str] = set()
    if args.input:
        words |= build_from_rusentilex_like(args.input)
    if args.use_seed:
        seed = _default_seed()
        if not seed.is_file():
            print(f"Seed not found: {seed}", file=sys.stderr)
            return 2
        words |= build_from_seed(seed)

    if not words:
        print("Нет данных: укажите --input и/или --use-seed.", file=sys.stderr)
        return 1

    ordered = sorted(words)[: max(1, args.max_words)]
    out_path.parent.mkdir(parents=True, exist_ok=True)
    header = (
        "# Автогенерация: python/tools/build_lexicons.py\n"
        "# См. docs/lexicon_sources.md — лицензии исходных корпусов.\n"
        "# Одна строка = одна нормализованная лексема (нижний регистр).\n"
    )
    out_path.write_text(header + "\n".join(ordered) + "\n", encoding="utf-8")
    print(f"Wrote {len(ordered)} words to {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
