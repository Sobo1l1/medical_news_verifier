# MVP: первичный анализ достоверности мед-новостей

Стек: `C#`, `ASP.NET Core MVC (.NET 9)`, `PostgreSQL`, `Python`, опционально **Ollama** (локальная LLM, совместимая с OpenAI Chat Completions).

## Что реализовано

- форма ввода новости (заголовок, текст, ссылка);
- **асинхронный анализ** с индикатором этапов: параллельно Python и сравнение с корпусом через Ollama, затем эвристическая оценка и итог;
- расчёт **трёх оценок**: эвристика (0–100), согласованность с корпусом по мнению LLM (0–100, если Ollama доступна), **итоговая** смесь (веса `AnalysisScoring:HeuristicBlendWeight` и `LlmBlendWeight`);
- лингвистический анализ подозрительных фрагментов;
- сопоставление с публикациями из официальных источников (web-fetch + fallback на БД);
- **справочник доверенных URL** (`/Sources`) и **ручной корпус текстов** (`/Corpus`) для офлайн-сопоставления и контекста LLM;
- визуализация результата и рисковых фрагментов;
- сохранение истории проверок в PostgreSQL;
- удаление проверок из истории;
- повторное использование результата при дубликате (`headline + text`).

## Локальная модель (Ollama)

1. Установите [Ollama](https://ollama.com/) и загрузите модель, например: `ollama pull qwen2.5:latest` (или вашу сборку Qwen под именем в реестре Ollama).
2. Убедитесь, что API доступен по умолчанию: `http://localhost:11434/v1/chat/completions`.
3. В `MedicalNewsVerifier.Web/appsettings.json` (или `appsettings.Development.json`) настройте секцию **`Ollama`**:
   - `Enabled` — по умолчанию в репозитории **`false`**, чтобы приложение не ждалало таймаут при отсутствии Ollama; установите **`true`**, когда модель запущена;
   - `BaseUrl` — базовый URL API, по умолчанию `http://localhost:11434/v1`;
   - `Model` — имя модели в Ollama (должно совпадать с выводом `ollama list`);
   - `TimeoutSeconds` — таймаут HTTP (первый прогрев модели может быть долгим).
4. Оценка LLM **вспомогательная** и не заменяет медицинскую экспертизу или официальные рекомендации.

При недоступности Ollama приложение логирует предупреждение и сохраняет результат с пояснением в поле резюме модели; итоговая оценка в этом случае совпадает с эвристикой.

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
5. Опционально запустить **Ollama** и подтянуть модель (см. раздел выше).
6. Выполнить:
   - `dotnet restore`
   - `dotnet run --project MedicalNewsVerifier.Web/MedicalNewsVerifier.Web.csproj`
7. Открыть URL, который покажет приложение в консоли.

При старте приложение автоматически создаёт таблицы (через `EnsureCreated`), применяет `DatabaseSchemaPatcher` (новые колонки и таблица справочника источников) и добавляет сиды: тестовые публикации в корпус и **справочник доверенных URL** (Минздрав, Росздравнадзор, ГРЛС и др.).

## Конфигурация анализаторов

В `MedicalNewsVerifier.Web/appsettings.json` доступны настройки:
- `Python:TimeoutSeconds` — таймаут Python анализа (при включённой Natasha/Stanza первый прогрев моделей может быть долгим;
  для разработки в `appsettings.Development.json` задано 45 с);
- `OfficialSources:TimeoutSeconds` — таймаут запросов к официальным сайтам;
- `OfficialSources:Urls` — список официальных источников для онлайн-сопоставления;
- `AnalysisLexicons:*` — имена файлов словарей признаков;
- `AnalysisScoring:WeightsFile` — JSON с весами признаков;
- `AnalysisScoring:HeuristicBlendWeight` / `LlmBlendWeight` — веса при смешивании эвристики и оценки Ollama (после нормализации суммы к 1);
- `Ollama:*` — см. раздел «Локальная модель (Ollama)».

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

## Разделы «Источники» и «Корпус»

В MVP **нет авторизации**: страницы `/Sources` и `/Corpus` не выносите в открытый интернет без защиты.

- **Источники** — каталог доверенных URL (библиография).
- **Корпус** — тексты, по которым считается релевантность и из которых в Ollama подставляются выдержки (в т.ч. если live-fetch сайтов не дал материала).

## Диагностика Python-анализатора

1. В логах приложения ищите предупреждения `PythonLinguisticClient`:
   скрипт не найден, таймаут, ненулевой код выхода, ошибка JSON.
2. Вручную из каталога `MedicalNewsVerifier.Web` запустите:
   `Get-Content .\sample.txt -Raw | python .\python\analyze_text.py`
   и проверьте, что stdout выдаёт корректный JSON-массив.
3. Убедитесь, что `Python:TimeoutSeconds` достаточен при первом запуске Natasha/Stanza.

## Статус фонового анализа

Состояние задачи анализа хранится в **памяти процесса** (`IMemoryCache`, срок жизни до нескольких часов). После перезапуска приложения идентификатор задания из браузера станет недействителен — запустите анализ снова.

Если web-источники недоступны, приложение автоматически использует локальные записи из таблицы `OfficialPublications`.
