(function () {
    const STORAGE_KEY = 'mednews.analysis.settings.v1';
    const cfg = window.__analysisPageConfig;
    if (!cfg) return;

    let serverDefaults = null;
    let currentSettings = null;

    const form = document.getElementById('newsAnalyzeForm');
    const host = document.getElementById('analysisProgressHost');
    const msg = document.getElementById('analysisProgressMessage');
    const hScore = document.getElementById('analysisHeuristicScore');
    const lScore = document.getElementById('analysisLlmScore');
    const cScore = document.getElementById('analysisCombinedScore');
    const modifiedBadge = document.getElementById('settingsModifiedBadge');
    const cancelBtn = document.getElementById('analysisCancelBtn');
    const settingsOpenBtn = document.getElementById('analysisSettingsOpenBtn');
    const settingsPanel = document.getElementById('analysisSettingsOffcanvas');
    const settingsForm = document.getElementById('analysisSettingsForm');
    const progressTitle = document.getElementById('analysisProgressTitle');
    const progressSpinner = host?.querySelector('.spinner-border');
    const lockableButtons = Array.from(document.querySelectorAll('.js-analysis-lockable'));
    const lockableNav = Array.from(document.querySelectorAll('.js-analysis-lockable-nav, .navbar a, a.nav-link'));
    let analysisRunning = false;
    let activeJobId = null;
    let pollTimer = null;

    function $(id) { return document.getElementById(id); }

    function statusUrl(jobId) {
        return cfg.statusUrlTemplate.replace(cfg.statusJobPlaceholder, jobId);
    }

    function cancelUrl(jobId) {
        return cfg.cancelUrlTemplate.replace(cfg.cancelJobPlaceholder, jobId);
    }

    function detailsUrl(id) {
        return cfg.detailsUrlTemplate.replace(cfg.detailsIdPlaceholder, String(id));
    }

    function pctFromWeight(w) {
        return Math.round(w * 100);
    }

    function weightFromPct(p) {
        return Math.round(p) / 100;
    }

    function settingsFromDefaults(d) {
        return {
            ollamaEnabled: d.ollamaEnabled,
            maxCorpusSnippets: d.maxCorpusSnippets,
            maxCorpusCharsPerSnippet: d.maxCorpusCharsPerSnippet,
            maxResponseTokens: d.maxResponseTokens,
            temperature: d.temperature,
            topP: d.topP,
            enableThinking: d.enableThinking,
            maxArticlesPerAnalysis: d.maxArticlesPerAnalysis,
            minRelevanceScore: d.minRelevanceScore,
            minzdravMaxFeedScan: d.minzdravMaxFeedScan,
            heuristicBlendWeight: d.heuristicBlendWeight,
            llmBlendWeight: d.llmBlendWeight,
            pythonTimeoutSeconds: d.pythonTimeoutSeconds,
            pythonEnableNatasha: d.pythonEnableNatasha,
            pythonEnableStanza: d.pythonEnableStanza
        };
    }

    function populateSettingsForm(s) {
        $('setMaxArticles').value = s.maxArticlesPerAnalysis;
        $('setMinRelevance').value = s.minRelevanceScore;
        $('setMaxFeedScan').value = s.minzdravMaxFeedScan;
        $('setOllamaEnabled').checked = s.ollamaEnabled;
        $('setMaxCorpusSnippets').value = s.maxCorpusSnippets;
        $('setMaxCorpusChars').value = s.maxCorpusCharsPerSnippet;
        $('setMaxResponseTokens').value = s.maxResponseTokens;
        $('setTemperature').value = s.temperature;
        $('setTopP').value = s.topP;
        $('setEnableThinking').checked = s.enableThinking;
        const featurePct = pctFromWeight(s.heuristicBlendWeight);
        $('setFeatureBlend').value = featurePct;
        $('setFeatureBlendValue').textContent = featurePct;
        $('setNeuralBlendValue').textContent = 100 - featurePct;
        $('setPythonTimeout').value = s.pythonTimeoutSeconds;
        $('setPythonNatasha').checked = s.pythonEnableNatasha;
        $('setPythonStanza').checked = s.pythonEnableStanza;
    }

    function readSettingsForm() {
        const featurePct = parseInt($('setFeatureBlend').value, 10);
        return {
            ollamaEnabled: $('setOllamaEnabled').checked,
            maxCorpusSnippets: parseInt($('setMaxCorpusSnippets').value, 10),
            maxCorpusCharsPerSnippet: parseInt($('setMaxCorpusChars').value, 10),
            maxResponseTokens: parseInt($('setMaxResponseTokens').value, 10),
            temperature: parseFloat($('setTemperature').value),
            topP: parseFloat($('setTopP').value),
            enableThinking: $('setEnableThinking').checked,
            maxArticlesPerAnalysis: parseInt($('setMaxArticles').value, 10),
            minRelevanceScore: parseInt($('setMinRelevance').value, 10),
            minzdravMaxFeedScan: parseInt($('setMaxFeedScan').value, 10),
            heuristicBlendWeight: weightFromPct(featurePct),
            llmBlendWeight: weightFromPct(100 - featurePct),
            pythonTimeoutSeconds: parseInt($('setPythonTimeout').value, 10),
            pythonEnableNatasha: $('setPythonNatasha').checked,
            pythonEnableStanza: $('setPythonStanza').checked
        };
    }

    function isEqual(a, b) {
        if (!a || !b) return false;
        const keys = Object.keys(a);
        for (const k of keys) {
            const va = a[k];
            const vb = b[k];
            if (typeof va === 'number' && typeof vb === 'number') {
                if (Math.abs(va - vb) > 0.001) return false;
            } else if (va !== vb) {
                return false;
            }
        }
        return true;
    }

    function updateModifiedBadge() {
        if (!modifiedBadge || !serverDefaults) return;
        const def = settingsFromDefaults(serverDefaults);
        const changed = currentSettings && !isEqual(currentSettings, def);
        modifiedBadge.classList.toggle('d-none', !changed);
    }

    function buildRunSettingsPayload() {
        if (!serverDefaults || !currentSettings) return null;
        const def = settingsFromDefaults(serverDefaults);
        const cur = currentSettings;
        const out = {};
        const map = [
            ['ollamaEnabled', 'ollamaEnabled'],
            ['maxCorpusSnippets', 'maxCorpusSnippets'],
            ['maxCorpusCharsPerSnippet', 'maxCorpusCharsPerSnippet'],
            ['maxResponseTokens', 'maxResponseTokens'],
            ['temperature', 'temperature'],
            ['topP', 'topP'],
            ['enableThinking', 'enableThinking'],
            ['maxArticlesPerAnalysis', 'maxArticlesPerAnalysis'],
            ['minRelevanceScore', 'minRelevanceScore'],
            ['minzdravMaxFeedScan', 'minzdravMaxFeedScan'],
            ['heuristicBlendWeight', 'heuristicBlendWeight'],
            ['llmBlendWeight', 'llmBlendWeight'],
            ['pythonTimeoutSeconds', 'pythonTimeoutSeconds'],
            ['pythonEnableNatasha', 'pythonEnableNatasha'],
            ['pythonEnableStanza', 'pythonEnableStanza']
        ];
        let any = false;
        for (const [from, to] of map) {
            const cv = cur[from];
            const dv = def[from];
            if (typeof cv === 'number' && typeof dv === 'number') {
                if (Math.abs(cv - dv) > 0.001) {
                    out[to] = cv;
                    any = true;
                }
            } else if (cv !== dv) {
                out[to] = cv;
                any = true;
            }
        }
        return any ? out : null;
    }

    function saveToStorage() {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(currentSettings));
        } catch { /* ignore */ }
        updateModifiedBadge();
    }

    function loadFromStorage() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (raw) {
                return JSON.parse(raw);
            }
        } catch { /* ignore */ }
        return null;
    }

    async function loadDefaults() {
        const resp = await fetch(cfg.defaultsUrl, { headers: { Accept: 'application/json' } });
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        serverDefaults = await resp.json();
        const stored = loadFromStorage();
        currentSettings = stored ?? settingsFromDefaults(serverDefaults);
        populateSettingsForm(currentSettings);
        updateModifiedBadge();
    }

    function setUiLocked(isLocked) {
        lockableButtons.forEach((btn) => {
            btn.disabled = isLocked;
            btn.setAttribute('aria-disabled', isLocked ? 'true' : 'false');
        });

        if (settingsOpenBtn) {
            if (isLocked) {
                settingsOpenBtn.setAttribute('data-analysis-was-disabled', settingsOpenBtn.disabled ? '1' : '0');
                settingsOpenBtn.disabled = true;
                settingsOpenBtn.removeAttribute('data-bs-toggle');
                settingsOpenBtn.removeAttribute('data-bs-target');
                if (settingsPanel && typeof bootstrap !== 'undefined') {
                    const inst = bootstrap.Offcanvas.getInstance(settingsPanel);
                    inst?.hide();
                }
            } else {
                const wasDisabled = settingsOpenBtn.getAttribute('data-analysis-was-disabled') === '1';
                settingsOpenBtn.disabled = wasDisabled;
                settingsOpenBtn.removeAttribute('data-analysis-was-disabled');
                settingsOpenBtn.setAttribute('data-bs-toggle', 'offcanvas');
                settingsOpenBtn.setAttribute('data-bs-target', '#analysisSettingsOffcanvas');
            }
        }

        if (settingsForm) {
            settingsForm.querySelectorAll('input, textarea, select, button').forEach((control) => {
                if (isLocked) {
                    control.setAttribute('data-analysis-was-disabled', control.disabled ? '1' : '0');
                    control.disabled = true;
                } else {
                    const wasDisabled = control.getAttribute('data-analysis-was-disabled') === '1';
                    control.disabled = wasDisabled;
                    control.removeAttribute('data-analysis-was-disabled');
                }
            });
        }

        if (form) {
            form.querySelectorAll('input, textarea, select, button').forEach((control) => {
                if (isLocked) {
                    control.setAttribute('data-analysis-was-disabled', control.disabled ? '1' : '0');
                    control.disabled = true;
                } else {
                    const wasDisabled = control.getAttribute('data-analysis-was-disabled') === '1';
                    control.disabled = wasDisabled;
                    control.removeAttribute('data-analysis-was-disabled');
                }
            });
        }
        lockableNav.forEach((link) => {
            link.style.pointerEvents = isLocked ? 'none' : '';
            link.style.opacity = isLocked ? '0.55' : '';
            link.setAttribute('aria-disabled', isLocked ? 'true' : 'false');
            if (isLocked) link.setAttribute('tabindex', '-1');
            else link.removeAttribute('tabindex');
        });
    }

    function updateStepper(state) {
        const phase = state?.phase ?? '';
        const featuresDone = !!state?.featuresCompleted || phase === 'Combining' || phase === 'Completed';
        const neuralDone = !!state?.neuralCompleted || phase === 'Combining' || phase === 'Completed';
        const sourcesDone = phase !== 'LoadingSources' && phase !== 'Started' && phase !== 'Pending' && phase !== '';
        let activeStep = 0;
        if (sourcesDone) activeStep = featuresDone ? (neuralDone ? 3 : 2) : 1;
        if (phase === 'Combining') activeStep = 3;
        if (phase === 'Completed') activeStep = 4;
        document.querySelectorAll('#analysisStepper .analysis-step').forEach((el) => {
            const idx = parseInt(el.getAttribute('data-step') || '0', 10);
            const done = idx === 0 ? sourcesDone : idx === 1 ? featuresDone : idx === 2 ? neuralDone : phase === 'Combining' || phase === 'Completed';
            el.classList.toggle('is-active', idx === activeStep && phase !== 'Completed');
            el.classList.toggle('is-done', done && idx !== activeStep);
        });
    }

    function updateProgressUi(state) {
        const phase = state?.phase ?? '';
        updateStepper(state);
        const hasHeuristic = typeof state?.heuristicScore === 'number';
        const hasLlm = typeof state?.llmScore === 'number';
        const hasCombined = typeof state?.combinedScore === 'number';
        if (hScore) hScore.textContent = hasHeuristic ? state.heuristicScore + '/100' : 'Выполняется…';
        if (lScore) lScore.textContent = hasLlm ? state.llmScore + '/100' : hasHeuristic ? 'Нейросетевой этап…' : 'Ожидание…';
        if (cScore) cScore.textContent = hasCombined ? state.combinedScore + '/100' : hasHeuristic ? 'Формируется итог…' : 'Ожидание…';
        if (!msg) return;
        if (phase === 'HeuristicReady') {
            msg.textContent = state?.message || 'Признаковый анализ завершён. Выполняется нейросетевой анализ…';
        } else if (phase === 'LlmReady') {
            msg.textContent = state?.message || 'Нейросетевой анализ завершён. Формируется итоговый вывод…';
        } else if (state?.message) {
            msg.textContent = state.message;
        }
    }

    function initSettingsPanel() {
        $('setFeatureBlend')?.addEventListener('input', function () {
            const v = parseInt(this.value, 10);
            $('setFeatureBlendValue').textContent = v;
            $('setNeuralBlendValue').textContent = 100 - v;
        });

        $('analysisSettingsApply')?.addEventListener('click', function () {
            currentSettings = readSettingsForm();
            saveToStorage();
            const offcanvas = bootstrap.Offcanvas.getInstance($('analysisSettingsOffcanvas'));
            offcanvas?.hide();
        });

        $('analysisSettingsReset')?.addEventListener('click', async function () {
            if (!serverDefaults) await loadDefaults();
            currentSettings = settingsFromDefaults(serverDefaults);
            populateSettingsForm(currentSettings);
            localStorage.removeItem(STORAGE_KEY);
            updateModifiedBadge();
        });

        loadDefaults().catch(function () {
            console.warn('Не удалось загрузить настройки по умолчанию');
        });
    }

    function stopPolling() {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
        activeJobId = null;
        cancelBtn?.classList.add('d-none');
    }

    function resetProgressPanel() {
        host?.classList.add('d-none');
        if (progressTitle) progressTitle.textContent = 'Анализ выполняется…';
        progressSpinner?.classList.remove('d-none');
        document.querySelectorAll('#analysisStepper .analysis-step').forEach((el) => {
            el.classList.remove('is-active', 'is-done');
        });
        document.getElementById('analysisResultSection')?.classList.remove('d-none');
    }

    function finishAnalysisRun() {
        analysisRunning = false;
        stopPolling();
        setUiLocked(false);
        resetProgressPanel();
    }

    async function requestCancel() {
        if (!activeJobId || !cfg.cancelUrlTemplate) return;
        cancelBtn.disabled = true;
        try {
            await fetch(cancelUrl(activeJobId), { method: 'POST', headers: { Accept: 'application/json' } });
            if (msg) msg.textContent = 'Прерывание анализа…';
        } catch {
            if (msg) msg.textContent = 'Не удалось отправить запрос на прерывание.';
            cancelBtn.disabled = false;
        }
    }

    cancelBtn?.addEventListener('click', requestCancel);

    function hidePreviousResults() {
        document.getElementById('analysisResultSection')?.classList.add('d-none');
        host?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    function initAnalysisForm() {
        if (!form || !host || !cfg.startUrl) return;

        form.addEventListener('submit', async function (e) {
            const submitter = e.submitter;
            e.preventDefault();
            if (analysisRunning) {
                msg?.classList.remove('text-danger');
                if (msg) msg.textContent = 'Проверка уже выполняется.';
                return;
            }
            if (!form.checkValidity()) {
                form.reportValidity();
                return;
            }
            analysisRunning = true;
            hidePreviousResults();
            setUiLocked(true);
            host.classList.remove('d-none');
            updateStepper({ phase: 'LoadingSources' });
            if (msg) {
                msg.textContent = 'Отправка запроса…';
                msg.classList.remove('text-danger');
            }
            if (hScore) hScore.textContent = 'Ожидание…';
            if (lScore) lScore.textContent = 'Ожидание…';
            if (cScore) cScore.textContent = 'Ожидание…';

            const payload = {
                headline: document.querySelector('[name="Input.Headline"]')?.value ?? '',
                newsText: document.querySelector('[name="Input.NewsText"]')?.value ?? '',
                forceNew: submitter && submitter.getAttribute('name') === 'forceNew' && submitter.value === 'true',
                sourceUrl: (() => {
                    const v = document.querySelector('[name="Input.SourceUrl"]')?.value?.trim() ?? '';
                    return v === '' ? null : v;
                })(),
                runSettings: buildRunSettingsPayload()
            };

            const startResp = await fetch(cfg.startUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!startResp.ok) {
                let detail = 'HTTP ' + startResp.status;
                try {
                    const errBody = await startResp.json();
                    if (errBody.message) detail = errBody.message;
                    else if (errBody.errors) {
                        detail = Object.values(errBody.errors).flat().join(' ');
                    }
                } catch { /* ignore */ }
                msg?.classList.add('text-danger');
                if (msg) msg.textContent = 'Не удалось запустить анализ: ' + detail;
                finishAnalysisRun();
                return;
            }

            const startJson = await startResp.json();
            const jobId = startJson.jobId;
            if (!jobId) {
                msg?.classList.add('text-danger');
                if (msg) msg.textContent = 'Сервер не вернул идентификатор задания.';
                finishAnalysisRun();
                return;
            }
            if (msg) msg.textContent = 'Анализ запущен, ожидайте этапы…';
            activeJobId = jobId;
            cancelBtn?.classList.remove('d-none');

            pollTimer = setInterval(async function () {
                const st = await fetch(statusUrl(jobId), { headers: { Accept: 'application/json' } });
                if (!st.ok) {
                    finishAnalysisRun();
                    msg?.classList.add('text-danger');
                    if (msg) msg.textContent = 'Статус анализа недоступен (HTTP ' + st.status + ').';
                    return;
                }
                const s = await st.json();
                updateProgressUi(s);
                if (s.phase === 'Completed') {
                    stopPolling();
                    analysisRunning = false;
                    if (s.recordId) {
                        window.location.href = detailsUrl(s.recordId);
                    } else {
                        finishAnalysisRun();
                    }
                    return;
                }
                if (s.phase === 'Cancelled') {
                    finishAnalysisRun();
                    msg?.classList.remove('text-danger');
                    if (msg) msg.textContent = s.message || 'Анализ прерван.';
                    return;
                }
                if (s.phase === 'Failed') {
                    finishAnalysisRun();
                    msg?.classList.add('text-danger');
                    if (msg) msg.textContent = 'Ошибка: ' + (s.error || 'неизвестно');
                }
            }, 450);
        });
    }

    initSettingsPanel();
    initAnalysisForm();
})();
