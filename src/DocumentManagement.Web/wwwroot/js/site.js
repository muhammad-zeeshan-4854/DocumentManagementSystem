// data-confirm: ask before submitting (delete).
document.addEventListener('submit', function (event) {
    const form = event.target;
    if (form instanceof HTMLFormElement && form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
        event.preventDefault();
    }
});

// data-submit-on-change: submit when the select changes (company picker).
document.addEventListener('change', function (event) {
    const field = event.target;
    if (field instanceof HTMLSelectElement && field.hasAttribute('data-submit-on-change') && field.form) {
        field.form.requestSubmit();
    }
});
