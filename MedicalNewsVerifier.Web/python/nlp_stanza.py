"""
Stanza (русский): токенизация + NER — те же эвристики, что и Natasha, без дублирования кода логики.
Включается MEDNEWS_ENABLE_STANZA=1 при MEDNEWS_ENABLE_NATASHA выключен (см. appsettings Python:EnableStanza).

Первый запуск: загрузка моделей `ru` (см. README).
"""
from __future__ import annotations

import re
from typing import Any

_nlp: Any | None | bool = None

_ANON_AUTHORITY = re.compile(
    r"(?iu)(?:по\s+словам|по\s+мнению|по\s+их\s+словам|как\s+утверждают|как\s+сообщают|как\s+пишут)\s+"
    r"(?:некоторых|независимых|ряда|многих|отдельных)?\s*"
    r"(?:экспертов|исследователей|специалистов|врачей|источников|авторов|критиков)\b"
)

_MAX_ANON_FRAGMENTS = 14
_ANON_SEVERITY = 6


def _env_truthy(name: str) -> bool:
    import os

    v = os.environ.get(name, "").strip().lower()
    return v in ("1", "true", "yes", "on")


def _get_nlp():
    global _nlp
    if _nlp is False:
        return None
    if _nlp is not None:
        return _nlp
    try:
        import stanza
    except ImportError:
        _nlp = False
        return None

    try:
        nlp = stanza.Pipeline(
            lang="ru",
            processors="tokenize,ner",
            download_method=None,
            verbose=False,
        )
    except Exception:
        try:
            stanza.download("ru")
        except Exception:
            _nlp = False
            return None
        try:
            nlp = stanza.Pipeline(
                lang="ru",
                processors="tokenize,ner",
                download_method=None,
                verbose=False,
            )
        except Exception:
            _nlp = False
            return None

    _nlp = nlp
    return _nlp


def collect_stanza_fragments(text: str) -> list[dict[str, Any]]:
    if not text or not text.strip():
        return []
    if not _env_truthy("MEDNEWS_ENABLE_STANZA"):
        return []
    if _env_truthy("MEDNEWS_ENABLE_NATASHA"):
        return []

    nlp = _get_nlp()
    if not nlp:
        return []

    try:
        from razdel import sentenize
    except ImportError:
        return []

    doc = nlp(text)
    # Stanza entities: start_char, end_char, type
    ents = list(doc.ents) if hasattr(doc, "ents") else []

    def has_per_in(st: int, en: int) -> bool:
        for e in ents:
            t = getattr(e, "type", "") or ""
            if t != "PER":
                continue
            es = getattr(e, "start_char", None)
            ee = getattr(e, "end_char", None)
            if es is None or ee is None:
                continue
            if es < en and ee > st:
                return True
        return False

    out: list[dict[str, Any]] = []
    used = 0
    for sent in sentenize(text):
        if used >= _MAX_ANON_FRAGMENTS:
            break
        st, en = sent.start, sent.stop
        chunk = text[st:en]
        if has_per_in(st, en):
            continue
        for m in _ANON_AUTHORITY.finditer(chunk):
            if used >= _MAX_ANON_FRAGMENTS:
                break
            a = st + m.start()
            b = st + m.end()
            frag = text[a:b].strip()
            if len(frag) < 6:
                continue
            out.append(
                {
                    "fragment": frag,
                    "reason": "Анонимные «эксперты»/источники без именованной персоны (Stanza NER).",
                    "severity": _ANON_SEVERITY,
                    "start": a,
                    "end": b,
                    "kind": "python",
                }
            )
            used += 1

    return out
