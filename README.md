# MVP: первичный анализ достоверности мед-новостей

Стек: `C#`, `ASP.NET Core MVC (.NET 9)`, `PostgreSQL`, `Python`.

## Что реализовано

- форма ввода новости (заголовок, текст, ссылка);
- расчет предварительной оценки достоверности (0-100);
- лингвистический анализ подозрительных фрагментов;
- сопоставление с публикациями из официальных источников (web-fetch + fallback на БД);
- визуализация результата и рисковых фрагментов;
- сохранение истории проверок в PostgreSQL;
- удаление проверок из истории;
- повторное использование результата при дубликате (`headline + text`).

## Быстрый запуск

1. Убедиться, что запущен PostgreSQL и доступна БД:
   - `Host=localhost;Port=5432;Database=mednews_mvp;Username=postgres;Password=postgres`
2. Убедиться, что установлен Python и команда `python` доступна в PATH.
3. При необходимости установить зависимости для Python-анализа:
   - базовый слой: `python -m pip install -r MedicalNewsVerifier.Web/python/requirements.txt`
   - опционально Natasha (NER, эвристики в `nlp_natasha.py`):
     `python -m pip install -r MedicalNewsVerifier.Web/python/requirements.txt -r MedicalNewsVerifier.Web/python/requirements-nlp.txt`
   - альтернатива — Stanza (только если Natasha отключён):
     `python -m pip install -r MedicalNewsVerifier.Web/python/requirements.txt -r MedicalNewsVerifier.Web/python/requirements-stanza.txt`
     Первый запуск скачает модели `ru` (нужен интернет);
     один раз выполните: `python -c "import stanza; stanza.download('ru')"`
4. Опционально пересобрать дополнительный эмоциональный словарь:
   `python MedicalNewsVerifier.Web/python/tools/build_lexicons.py --use-seed`
   (создаёт/обновляет `Resources/Lexicons/emotional_ru_rusentilex.txt`;
   источники — `MedicalNewsVerifier.Web/docs/lexicon_sources.md`).
5. Выполнить:
   - `dotnet restore`
   - `dotnet run --project MedicalNewsVerifier.Web/MedicalNewsVerifier.Web.csproj`
6. Открыть URL, который покажет приложение в консоли.

При старте приложение автоматически создаёт таблицы (через `EnsureCreated`) и добавляет тестовые официальные публикации.

## Конфигурация анализаторов

В `MedicalNewsVerifier.Web/appsettings.json` доступны настройки:
- `Python:TimeoutSeconds` — таймаут Python анализа (при включённой Natasha/Stanza первый прогрев моделей может быть долгим;
  для разработки в `appsettings.Development.json` задано 45 с);
- `OfficialSources:TimeoutSeconds` — таймаут запросов к официальным сайтам;
- `OfficialSources:Urls` — список официальных источников для онлайн-сопоставления;
- `AnalysisLexicons:*` — имена файлов словарей признаков;
- `AnalysisScoring:WeightsFile` — JSON с весами признаков.

Словари и веса находятся здесь:
- `MedicalNewsVerifier.Web/Resources/Lexicons/emotional_ru.txt`
- `MedicalNewsVerifier.Web/Resources/Lexicons/emotional_ru_rusentilex.txt` (опционально)
- `MedicalNewsVerifier.Web/Resources/Lexicons/manipulative_ru.txt`
- `MedicalNewsVerifier.Web/Resources/Lexicons/evaluative_ru.txt`
- `MedicalNewsVerifier.Web/Resources/Lexicons/source_cues_ru.txt`
- `MedicalNewsVerifier.Web/Resources/Scoring/feature_weights.json`

Текущие признаки анализа:
- эмоционально окрашенная лексика;
- манипулятивные выражения;
- оценочная лексика;
- слова в верхнем регистре;
- количество `!` и `?`;
- наличие чисел, дат и ссылок;
- наличие указания на источник.

## Диагностика Python-анализатора

1. В логах приложения ищите предупреждения `PythonLinguisticClient`:
   скрипт не найден, таймаут, ненулевой код выхода, ошибка JSON.
2. Вручную из каталога `MedicalNewsVerifier.Web` запустите:
   `Get-Content .\sample.txt -Raw | python .\python\analyze_text.py`
   и проверьте, что stdout выдаёт корректный JSON-массив.
3. Убедитесь, что `Python:TimeoutSeconds` достаточен при первом запуске Natasha/Stanza.

Если web-источники недоступны, приложение автоматически использует локальные записи из таблицы `OfficialPublications`.
