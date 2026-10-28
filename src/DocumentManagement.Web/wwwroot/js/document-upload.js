// Quick checks on the upload form so obvious mistakes don't need a round trip. The SP still checks everything.
(function () {
    'use strict';

    const form = document.getElementById('upload-form');
    if (!form) {
        return;
    }

    const titleInput = document.getElementById('Title');
    const fileInput = document.getElementById('File');
    const titleError = document.getElementById('title-error');
    const fileError = document.getElementById('file-error');
    const allowedTypes = JSON.parse(form.dataset.allowedTypes);
    const maxUploadBytes = Number(form.dataset.maxUploadBytes);

    function formatSize(bytes) {
        return bytes < 1048576 ? Math.floor(bytes / 1024) + ' KB' : Math.floor(bytes / 1048576) + ' MB';
    }

    function showError(input, errorElement, message) {
        input.classList.toggle('is-invalid', !!message);
        errorElement.textContent = message;
    }

    function checkTitle() {
        const message = titleInput.value.trim() === '' ? 'Title is required.' : '';
        showError(titleInput, titleError, message);
        return message === '';
    }

    function checkFile() {
        const file = fileInput.files[0];
        let message = '';
        if (!file) {
            message = 'Please choose a file to upload.';
        } else {
            const dot = file.name.lastIndexOf('.');
            const extension = dot < 0 ? '' : file.name.substring(dot).toLowerCase();
            const type = allowedTypes.find(t => t.extension === extension);
            if (!type) {
                message = extension === '' ? 'The file has no extension, so its type cannot be checked.' : 'Files of type "' + extension + '" are not allowed.';
            } else if (file.size === 0) {
                message = 'The selected file is empty.';
            } else if (file.size > type.maxBytes) {
                message = 'The file is too large. The maximum size for ' + extension + ' files is ' + formatSize(type.maxBytes) + '.';
            } else if (file.size > maxUploadBytes) {
                message = 'The file is too large. The maximum upload size is ' + formatSize(maxUploadBytes) + '.';
            }
        }
        showError(fileInput, fileError, message);
        return message === '';
    }

    titleInput.addEventListener('input', checkTitle);
    fileInput.addEventListener('change', checkFile);

    form.addEventListener('submit', event => {
        const titleOk = checkTitle();
        const fileOk = checkFile();
        if (!titleOk || !fileOk) {
            event.preventDefault();
            event.stopImmediatePropagation();
            (titleOk ? fileInput : titleInput).focus();
        }
    });
})();
