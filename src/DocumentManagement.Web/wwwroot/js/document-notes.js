// Notes on the document page (AJAX). Positions are % of the board so pins stay put on resize.
(function () {
    'use strict';

    const board = document.getElementById('note-board');
    if (!board) {
        return;
    }

    const notesUrl = board.dataset.notesUrl;
    const layer = document.getElementById('note-layer');
    const toggle = document.getElementById('note-mode-toggle');
    const hint = document.getElementById('note-mode-hint');
    const list = document.getElementById('note-list');
    const listStatus = document.getElementById('note-list-status');
    const count = document.getElementById('note-count');
    const editor = document.getElementById('note-editor');
    const editorText = document.getElementById('note-text');
    const editorError = document.getElementById('note-error');
    const saveButton = document.getElementById('note-save');
    const antiforgeryToken = document.querySelector('input[name="__RequestVerificationToken"]').value;

    let notes = [];
    let pendingPosition = null;
    let addMode = false;



    async function sendJson(method, body) {
        let response;
        try {
            response = await fetch(notesUrl, {
                method: method,
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgeryToken
                },
                body: body === undefined ? undefined : JSON.stringify(body)
            });
        } catch (networkError) {
            throw new Error('Could not reach the server. Check your connection and try again.');
        }

        const isJson = (response.headers.get('Content-Type') || '').includes('json');
        const payload = isJson ? await response.json().catch(() => null) : null;

        if (!response.ok) {
            throw new Error(describeProblem(response.status, payload));
        }
        return payload;
    }

    function describeProblem(status, problem) {
        if (problem && problem.errors) {
            const messages = Object.values(problem.errors).flat();
            if (messages.length) {
                return messages.join(' ');
            }
        }
        if (problem && problem.detail) {
            return problem.detail;
        }
        const errorId = problem && problem.errorId ? ' (error id ' + problem.errorId + ')' : '';
        return 'The request failed with status ' + status + errorId + '. Please try again.';
    }

    // textContent only: note text is user input.

    function render() {
        layer.querySelectorAll('.note-pin').forEach(pin => pin.remove());
        list.replaceChildren();

        notes.forEach((note, index) => {
            const number = String(index + 1);

            const pin = document.createElement('button');
            pin.type = 'button';
            pin.className = 'note-pin';
            pin.style.top = note.positionTop + '%';
            pin.style.left = note.positionLeft + '%';
            pin.textContent = number;
            pin.title = note.noteText;
            pin.setAttribute('aria-label', 'Note ' + number + ': ' + note.noteText);
            pin.addEventListener('click', event => {
                event.stopPropagation();
                highlight(note.noteID);
            });
            layer.appendChild(pin);

            const item = document.createElement('li');
            item.className = 'note-list-item';
            item.dataset.noteId = note.noteID;

            const marker = document.createElement('span');
            marker.className = 'note-list-number';
            marker.textContent = number;

            const text = document.createElement('div');
            text.className = 'note-list-text';
            text.textContent = note.noteText;

            const date = document.createElement('div');
            date.className = 'small text-muted';
            date.textContent = new Date(note.dateAdded).toLocaleString();

            text.appendChild(date);
            item.append(marker, text);
            item.addEventListener('click', () => highlight(note.noteID));
            list.appendChild(item);
        });

        count.textContent = String(notes.length);
        listStatus.hidden = notes.length > 0;
        listStatus.textContent = 'No notes yet.';
    }

    function highlight(noteID) {
        const index = notes.findIndex(note => note.noteID === noteID);
        layer.querySelectorAll('.note-pin').forEach((pin, i) => pin.classList.toggle('active', i === index));
        list.querySelectorAll('.note-list-item').forEach(item => item.classList.toggle('active', Number(item.dataset.noteId) === noteID));
    }



    function setAddMode(on) {
        addMode = on;
        layer.classList.toggle('adding', on);
        toggle.setAttribute('aria-pressed', String(on));
        toggle.textContent = on ? 'Done adding' : 'Add note';
        toggle.classList.toggle('btn-primary', !on);
        toggle.classList.toggle('btn-outline-primary', on);
        hint.textContent = on
            ? 'Click on the document where the note belongs.'
            : 'Turn on "Add note", then click where the note belongs.';
        if (!on) {
            closeEditor();
        }
    }

    function openEditor(clientX, clientY, position) {
        pendingPosition = position;
        editorText.value = '';
        editorError.hidden = true;
        editor.hidden = false;

        // Keep the popover on screen.
        const editorWidth = editor.offsetWidth;
        const left = Math.min(clientX + window.scrollX, window.scrollX + document.documentElement.clientWidth - editorWidth - 8);
        editor.style.left = Math.max(8, left) + 'px';
        editor.style.top = (clientY + window.scrollY + 12) + 'px';
        editorText.focus();
    }

    function closeEditor() {
        editor.hidden = true;
        pendingPosition = null;
    }

    layer.addEventListener('click', event => {
        if (!addMode) {
            return;
        }
        const rect = layer.getBoundingClientRect();
        const top = ((event.clientY - rect.top) / rect.height) * 100;
        const left = ((event.clientX - rect.left) / rect.width) * 100;
        openEditor(event.clientX, event.clientY, {
            positionTop: Math.round(top * 10000) / 10000,
            positionLeft: Math.round(left * 10000) / 10000
        });
    });

    editor.addEventListener('submit', async event => {
        event.preventDefault();
        if (!pendingPosition) {
            return;
        }
        if (editorText.value.trim() === '') {
            editorError.textContent = 'Note text is required.';
            editorError.hidden = false;
            editorText.focus();
            return;
        }
        saveButton.disabled = true;
        editorError.hidden = true;
        try {
            const note = await sendJson('POST', {
                noteText: editorText.value,
                positionTop: pendingPosition.positionTop,
                positionLeft: pendingPosition.positionLeft
            });
            notes.push(note);
            render();
            highlight(note.noteID);
            closeEditor();
        } catch (error) {
            editorError.textContent = error.message;
            editorError.hidden = false;
        } finally {
            saveButton.disabled = false;
        }
    });

    document.getElementById('note-cancel').addEventListener('click', closeEditor);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            closeEditor();
        }
    });
    toggle.addEventListener('click', () => setAddMode(!addMode));



    (async function load() {
        try {
            notes = await sendJson('GET') || [];
            render();
        } catch (error) {
            listStatus.textContent = 'Notes could not be loaded: ' + error.message;
            listStatus.classList.add('text-danger');
        }
    })();
})();
