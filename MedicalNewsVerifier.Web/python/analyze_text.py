"""
Дополнительный лингвистический слой: загрузка словарей из каталога MEDNEWS_LEXICON_ROOT
(по умолчанию Resources/Lexicons рядом с проектом веб-приложения).

Словари:
- emotional_ru.txt / emotional_en.txt / emotional_ru_rusentilex.txt — окраска (RU+EN + доп. список;
  rusentilex-файл строится скриптом python/tools/build_lexicons.py, см. docs/lexicon_sources.md);
- evaluative_ru.txt / evaluative_en.txt — оценочность;
- manipulative_ru.txt / manipulative_en.txt — манипулятивные фразы (в т.ч. многословные).

Строки, начинающиеся с #, игнорируются. Эвристики по фиксированным regex остаются с kind=python.
"""
import json
import os
import re
import sys
from pathlib import Path

_SCRIPT_DIR = Path(__file__).resolve().parent
if str(_SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(_SCRIPT_DIR))

try:
    import pymorphy2
except ImportError:
    pymorphy2 = None

try:
    from razdel import tokenize as _razdel_tokenize
except ImportError:
    _razdel_tokenize = None

_token_word_regex = re.compile(r"(?u)(?:[A-Za-z][A-Za-z'-]*|[А-Яа-яЁё][А-Яа-яЁё-]*)")
_morph = None
if pymorphy2 is not None:
    try:
        _morph = pymorphy2.MorphAnalyzer()
    except Exception:
        _morph = None


def _lexicon_root() -> Path:
    env = os.environ.get("MEDNEWS_LEXICON_ROOT", "").strip()
    if env:
        return Path(env)
    return Path(__file__).resolve().parent.parent / "Resources" / "Lexicons"


def _read_lexicon_lines(path: Path) -> list[str]:
    if not path.is_file():
        return []
    text = path.read_text(encoding="utf-8")
    lines = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        lines.append(line)
    return lines


def _load_word_set(names: tuple[str, ...]) -> set[str]:
    root = _lexicon_root()
    words: set[str] = set()
    for name in names:
        for line in _read_lexicon_lines(root / name):
            words.add(line.lower())
    return words


def _load_manipulative_phrases(names: tuple[str, ...]) -> list[str]:
    root = _lexicon_root()
    phrases: list[str] = []
    for name in names:
        for line in _read_lexicon_lines(root / name):
            phrases.append(line.lower())
    phrases.sort(key=len, reverse=True)
    return phrases


def _normalize_token(token: str) -> str:
    token = token.strip().lower()
    cleaned = _token_word_regex.match(token)
    if not cleaned:
        return ""
    word = cleaned.group(0)
    if _morph is not None:
        parsed = _morph.parse(word)
        if parsed:
            return parsed[0].normal_form
    return word


def _iterate_tokens(text: str):
    if _razdel_tokenize is not None:
        for token in _razdel_tokenize(text):
            yield token.start, token.stop, token.text
        return

    for m in _token_word_regex.finditer(text):
        yield m.start(), m.end(), m.group(0)


def _add_span(spans: list[dict], start: int, end: int, fragment: str, reason: str, severity: int, kind: str):
    if end <= start or start < 0:
        return
    spans.append(
        {
            "fragment": fragment,
            "reason": reason,
            "severity": severity,
            "start": start,
            "end": end,
            "kind": kind,
        }
    )


def _dedupe_spans(spans: list[dict]) -> list[dict]:
    priority = {"manipulative": 0, "emotional": 1, "evaluative": 2, "python": 3}

    def pr(item: dict) -> int:
        return priority.get(item.get("kind", "python"), 9)

    by_key: dict[tuple[int, int], dict] = {}
    rest: list[dict] = []
    for s in spans:
        st = s.get("start", -1)
        en = s.get("end", -1)
        if st is None or en is None or st < 0 or en <= st:
            rest.append(s)
            continue
        key = (st, en)
        if key not in by_key or pr(s) < pr(by_key[key]):
            by_key[key] = s
    merged = list(by_key.values()) + rest
    merged.sort(key=lambda x: (x.get("start", 0), x.get("end", 0)))
    return merged[:120]


def collect_lexicon_fragments(text: str) -> list[dict]:
    emotional = _load_word_set(("emotional_ru.txt", "emotional_en.txt", "emotional_ru_rusentilex.txt"))
    evaluative = _load_word_set(("evaluative_ru.txt", "evaluative_en.txt"))
    manipulative = _load_manipulative_phrases(("manipulative_ru.txt", "manipulative_en.txt"))

    spans: list[dict] = []

    for phrase in manipulative:
        if len(phrase) < 3:
            continue
        for m in re.finditer(re.escape(phrase), text, flags=re.IGNORECASE):
            _add_span(
                spans,
                m.start(),
                m.end(),
                text[m.start() : m.end()],
                "Совпадение со словарём манипулятивных конструкций (RU/EN, внешние списки).",
                5,
                "manipulative",
            )

    for start, end, tok in _iterate_tokens(text):
        key = _normalize_token(tok)
        if not key:
            continue

        if key in emotional:
            _add_span(
                spans,
                start,
                end,
                tok,
                "Совпадение со словарём эмоциональной лексики (RU/EN, внешние списки).",
                0,
                "emotional",
            )
        elif key in evaluative:
            _add_span(
                spans,
                start,
                end,
                tok,
                "Совпадение со словарём оценочной лексики (RU/EN, внешние списки).",
                0,
                "evaluative",
            )

    return spans


def collect_regex_fragments(text: str) -> list[dict]:
    fragments: list[dict] = []
    # Границы «слова» для кириллицы и латиницы (стандартный \b для RU ненадёжен).
    _b = r"(?<![А-Яа-яЁёA-Za-z0-9])"
    _a = r"(?![А-Яа-яЁёA-Za-z0-9])"

    patterns = [
        (r"\b100%\b", "Абсолютное утверждение без оговорок", 10),
        (r"(?iu)\bмгновенно\b", "Нереалистичное обещание эффекта", 12),
        (r"(?iu)\bне\s+имеет\s+побочных\s+эффектов\b", "Потенциально недостоверное медицинское утверждение", 15),
        (r"(?iu)\bлечит\s+рак\b", "Критичное медицинское обещание без доказательной базы", 20),
        (r"(?iu)\bбез\s+консультации\s+врача\b", "Опасный совет без медицинского контроля", 16),
        (r"(?iu)\bофициальная\s+медицина\s+скрывает\b", "Конспирологический признак манипуляции", 14),
        (r"(?iu)" + _b + r"официальн\w*\s+структур\w*\s+скрывают" + _a, "Обобщение об «официальных структурах» без источников", 15),
        (r"(?iu)" + _b + r"(?:меняет\s+днк|изменяет\s+днк|изменять\s+днк)" + _a, "Распространённый миф о вакцинах и геноме", 18),
        (r"(?iu)" + _b + r"(?:встраива\w*\s+в\s+геном|в\s+геном\s+человека)" + _a, "Недостоверное утверждение о встраивании в ДНК", 18),
        (r"(?iu)" + _b + r"глобальн\w*\s+(?:цифрового\s+)?контрол\w*" + _a, "Конспирологический нарратив о контроле населения", 16),
        (r"(?iu)" + _b + r"массов\w*\s+эксперимент\w*\s+над\s+человечеств" + _a, "Конспирологическая формулировка о пандемии/вакцинации", 17),
        (r"(?iu)" + _b + r"по\s+неподтвержд[её]нным\s+данным" + _a, "Ссылка на анонимные «данные» без проверки", 12),
        (r"(?iu)" + _b + r"(?:скрывают\s+правду|истинн\w*\s+причин)" + _a, "Манипулятивное обвинение в сокрытии", 15),
        (r"(?iu)" + _b + r"вакцин\w*[^\n]{0,160}5\s*g" + _a, "Связь вакцинации и 5G (типичный дезинформационный мотив)", 17),
        (r"(?iu)" + _b + r"(?:микроскопическ\w*\s+структур|наночастиц)" + _a, "Недоказанные утверждения о «наночастицах» в вакцинах", 15),
        (r"(?iu)" + _b + r"тотальн\w*\s+цифров\w*\s+контрол\w*" + _a, "Конспирологический штамп о цифровом контроле", 15),
        (r"(?iu)" + _b + r"пандеми\w*\s+был\w*\s+создан\w*" + _a, "Утверждение об искусственном происхождении пандемии", 16),
        (r"(?iu)" + _b + r"смертность[^\n]{0,80}намеренно\s+завыш" + _a, "Обвинение медицинской статистики без доказательств", 14),
        (r"(?iu)" + _b + r"врачам\s+выгодно\s+ставить" + _a, "Циничное обвинение врачей без подтверждения", 14),
        (r"(?iu)" + _b + r"(?:специально\s+замалчивают|удаляют\s+информацию\s+из\s+интернет\w*)" + _a, "Утверждение о цензуре без источника", 13),
        (r"(?iu)" + _b + r"блокируют\s+распространение\s+(?:этой\s+)?информации" + _a, "Штамп о «скрытии» альтернативных методов", 13),
        (r"(?iu)" + _b + r"(?:передаётся|передается)\s+через\s+сны" + _a, "Недостоверное утверждение о пути заражения", 18),
        (r"(?iu)" + _b + r"телефонн\w*\s+разговор\w*" + _a, "Миф о передаче инфекции через звонки", 17),
        (r"(?iu)" + _b + r"фольгированн\w*\s+шапочк\w*" + _a, "Типичный псевдонаучный «совет» из дезинформации", 16),
        (r"(?iu)" + _b + r"ночной\s+кошмар" + _a, "Сенсационное название штамма без подтверждения", 14),
        (r"(?iu)" + _b + r"секретн\w*\s+отдел\w*" + _a, "Выдуманный «секретный» источник без верификации", 15),
        (r"(?iu)" + _b + r"почему\s+нам\s+никто\s+не\s+говорит\s+правду" + _a, "Манипулятивный призыв «скрывают правду»", 14),
        (r"(?iu)" + _b + r"ваше\s+молчание\s+может\s+стоить" + _a, "Эмоциональный шантаж читателя", 16),
        (r"(?iu)" + _b + r"пересылайте\s+(?:этот\s+текст\s+)?(?:всем|близким)" + _a, "Призыв к массовому пересыланию без проверки", 13),
        (r"(?iu)" + _b + r"настой\s+из\s+редьки" + _a, "Непроверенное «народное средство» вместо медицины", 15),
        (r"(?iu)" + _b + r"странными\s+видениями" + _a, "Сенсационное описание симптомов без источника", 12),
        (r"(?iu)" + _b + r"смертность\s+может\s+достичь\s+\d+\s*%" + _a, "Необоснованная цифра смертности для запугивания", 16),
        (r"(?iu)" + _b + r"мутировало\s+в\s+смертельн\w*\s+вирус" + _a, "Сенсационное утверждение о «смертельной мутации»", 17),
        (r"(?iu)" + _b + r"(?:перекись\s+водорода|противопаразитарн\w*\s+препарат\w*)" + _a, "Опасные/неподтверждённые «средства» от COVID-19", 16),
    ]
    for pattern, reason, severity in patterns:
        for match in re.finditer(pattern, text, flags=re.IGNORECASE):
            fragments.append(
                {
                    "fragment": match.group(0),
                    "reason": reason,
                    "severity": severity,
                    "start": match.start(),
                    "end": match.end(),
                    "kind": "python",
                }
            )
    return fragments


def collect_fragments(text: str) -> list[dict]:
    lex = collect_lexicon_fragments(text)
    rx = collect_regex_fragments(text)
    nlp_extra: list[dict] = []

    def _truthy(k: str) -> bool:
        return os.environ.get(k, "").strip().lower() in ("1", "true", "yes", "on")

    if _truthy("MEDNEWS_ENABLE_NATASHA"):
        try:
            from nlp_natasha import collect_natasha_fragments

            nlp_extra.extend(collect_natasha_fragments(text))
        except Exception:
            pass
    elif _truthy("MEDNEWS_ENABLE_STANZA"):
        try:
            from nlp_stanza import collect_stanza_fragments

            nlp_extra.extend(collect_stanza_fragments(text))
        except Exception:
            pass

    return _dedupe_spans(lex + rx + nlp_extra)


def main():
    text = sys.stdin.read()
    if not text:
        print("[]")
        return

    result = collect_fragments(text)
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
