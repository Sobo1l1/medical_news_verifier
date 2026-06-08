(function () {
    document.addEventListener('keydown', function (e) {
        if (!(e.ctrlKey || e.metaKey) || e.key !== 'Enter') {
            return;
        }

        const form = document.getElementById('newsAnalyzeForm');
        if (!form) {
            return;
        }

        const active = document.activeElement;
        if (!active || !form.contains(active)) {
            return;
        }

        if (active.tagName !== 'TEXTAREA' && active.tagName !== 'INPUT') {
            return;
        }

        e.preventDefault();
        const submit = form.querySelector('#newsAnalyzeSubmit, #newsAnalyzeStartAgain, button[type="submit"]');
        if (submit && !submit.disabled) {
            submit.click();
        }
    });
})();
