"""
Natasha: сегментация + NER → эвристики для фрагментов (kind=python).
Включается переменной окружения MEDNEWS_ENABLE_NATASHA=1 (см. appsettings Python:EnableNatasha).
"""
from __future__ import annotations

import re
from typing import Any

_pipeline: tuple[Any, ...] | None | bool = None

_ANON_AUTHORITY = re.compile(
    r"(?iu)(?:по\s+словам|по\s+мнению|по\s+их\s+словам|как\s+утверждают|как\s+сообщают|как\s+пишут)\s+"
    r"(?:некоторых|независимых|ряда|многих|отдельных)?\s*"
    r"(?:экспертов|исследователей|специалистов|врачей|источников|авторов|критиков)\b"
)

_MAX_ANON_FRAGMENTS = 14
_ANON_SEVERITY = 6


def _load_pipeline():
    global _pipeline
    if _pipeline is False:
        return None
    if _pipeline is not None:
        return _pipeline
    try:
        from natasha import Doc, NewsEmbedding, NewsNERTagger, Segmenter
    except ImportError:
        _pipeline = False
        return None

    segmenter = Segmenter()
    emb = NewsEmbedding()
    ner = NewsNERTagger(emb)
    _pipeline = (segmenter, ner)
    return _pipeline


def _env_truthy(name: str) -> bool:
    import os

    v = os.environ.get(name, "").strip().lower()
    return v in ("1", "true", "yes", "on")


def collect_natasha_fragments(text: str) -> list[dict[str, Any]]:
    if not text or not text.strip():
        return []
    if not _env_truthy("MEDNEWS_ENABLE_NATASHA"):
        return []

    pipe = _load_pipeline()
    if not pipe:
        return []

    try:
        from natasha import Doc
        from razdel import sentenize
    except ImportError:
        return []

    segmenter, ner = pipe
    doc = Doc(text)
    doc.segment(segmenter)
    doc.tag_ner(ner)

    spans = list(doc.spans)
    out: list[dict[str, Any]] = []
    used = 0

    for sent in sentenize(text):
        if used >= _MAX_ANON_FRAGMENTS:
            break
        st, en = sent.start, sent.stop
        chunk = text[st:en]
        has_per = any(s.type == "PER" and s.start < en and s.stop > st for s in spans)
        if has_per:
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
                    "reason": "Анонимные «эксперты»/источники без именованной персоны (Natasha NER).",
                    "severity": _ANON_SEVERITY,
                    "start": a,
                    "end": b,
                    "kind": "python",
                }
            )
            used += 1

    return out
