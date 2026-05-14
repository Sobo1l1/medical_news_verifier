(function () {
  const root = document.querySelector('[data-analysis-markup]');
  if (!root) {
    return;
  }

  const hints = root.querySelectorAll('[data-feature-hints]');
  const marks = root.querySelectorAll('mark.text-marker[data-kind]');

  hints.forEach((hint) => {
    const raw = hint.getAttribute('data-feature-hints');
    const kinds = raw
      ? raw
          .split(',')
          .map((s) => s.trim())
          .filter(Boolean)
      : [];

    if (kinds.length === 0) {
      return;
    }

    hint.addEventListener('mouseenter', () => {
      hint.classList.add('is-row-hover');
      marks.forEach((m) => {
        const mk = m.getAttribute('data-kind');
        if (mk && kinds.includes(mk)) {
          m.classList.add('is-kind-hover');
        }
      });
    });

    hint.addEventListener('mouseleave', () => {
      hint.classList.remove('is-row-hover');
      marks.forEach((m) => m.classList.remove('is-kind-hover'));
    });
  });
})();
