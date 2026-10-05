// Audiobook Library admin page, loaded by jellyfin-web as the config page's controller through data-controller.
// It lists every audiobook file, previews chapters from Audible or from silences, and applies or restores them.
// Jellyfin's own elements (emby-input, emby-select, emby-checkbox) only upgrade when they arrive as HTML,
// so those parts are HTML strings with every value escaped, and everything else is built with textContent.
// Book sections copy emby-collapse's look by hand, 12.1 only loads that element inside the library filter dialog.

const PLUGIN_ID = '4cecc660-432f-4714-8958-b5da8537e55d';
const POLL_MS = 2000;
const REGIONS = [
    ['us', 'United States (us)'],
    ['uk', 'United Kingdom (uk)'],
    ['ca', 'Canada (ca)'],
    ['au', 'Australia (au)'],
    ['de', 'Germany (de)'],
    ['fr', 'France (fr)'],
    ['it', 'Italy (it)'],
    ['es', 'Spain (es)'],
    ['in', 'India (in)'],
    ['jp', 'Japan (jp)']
];

// Helpers

function el(tag, props, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(props || {})) {
        if (value == null || value === false) {
            continue;
        }

        if (key === 'class') {
            node.className = value;
        } else if (key === 'text') {
            node.textContent = value;
        } else if (key.startsWith('on')) {
            node.addEventListener(key.substring(2), value);
        } else if (value === true) {
            node.setAttribute(key, '');
        } else {
            node.setAttribute(key, value);
        }
    }

    // Rows come back as lists of lists, and append turns anything that isn't a node into text
    for (const child of children.flat(Infinity)) {
        if (child != null && child !== false) {
            node.append(child);
        }
    }

    return node;
}

// Titles and file names come from people's files, so anything going into an HTML string goes through here
function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function show(node, visible) {
    node?.classList.toggle('hide', !visible);
}

function setButton(button, text, disabled) {
    button.querySelector('span').textContent = text;
    button.disabled = !!disabled;
}

// emby-input moves its label out of the way on change, setting the value alone leaves the label over it
function setInput(input, value) {
    input.value = value ?? '';
    input.dispatchEvent(new Event('change', { bubbles: true, cancelable: false }));
}

function formatTime(seconds) {
    const s = Math.floor(Math.max(seconds || 0, 0));
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    return `${h}:${String(m).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
}

function formatLength(seconds) {
    const s = Math.round(seconds || 0);
    return `${Math.floor(s / 3600)}h ${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}m`;
}

// ASP.NET sends validation errors as JSON with a title, our own refusals as plain text
function readError(text, status) {
    if (!text) {
        return `Request failed (HTTP ${status})`;
    }

    try {
        const json = JSON.parse(text);
        if (typeof json === 'string') {
            return json;
        }

        return json.title || json.message || `Request failed (HTTP ${status})`;
    } catch {
        return text;
    }
}

async function call(method, path, body) {
    const request = {
        type: method,
        url: ApiClient.getUrl(`AudiobookLibrary/Admin/${path}`)
    };
    if (body !== undefined) {
        request.data = JSON.stringify(body);
        request.contentType = 'application/json';
    }

    let response;
    try {
        response = await ApiClient.ajax(request);
    } catch (failed) {
        // ApiClient rejects with the fetch Response for HTTP errors, and with something else when the server can't be reached
        if (failed && typeof failed.text === 'function') {
            throw new Error(readError(await failed.text(), failed.status));
        }

        throw new Error('Could not reach the server');
    }

    // ApiClient already reads text responses itself, JSON ones come back as the Response
    if (typeof response === 'string') {
        return response ? JSON.parse(response) : null;
    }

    if (!response || response.status === 204) {
        return null;
    }

    const text = await response.text();
    return text ? JSON.parse(text) : null;
}

const STATUS_LABELS = {
    NoChapters: 'No chapters',
    Embedded: 'Embedded',
    Audible: 'Audible',
    Silence: 'Silence',
    NeedsReview: 'Needs review',
    NotSupported: 'MP3, later'
};

function pill(status) {
    return el('span', { class: `abl-pill abl-pill-${status}`, text: STATUS_LABELS[status] || status });
}

// A book's pill is its most urgent file's, so a half-done split book doesn't look finished
function bookStatus(book) {
    const order = ['NeedsReview', 'NoChapters', 'Silence', 'Audible', 'Embedded', 'NotSupported'];
    return order.find((s) => book.Files.some((f) => f.Status === s)) || 'NotSupported';
}

// The open file's panel, as HTML so Jellyfin upgrades its inputs, built once and then only updated
function panelHtml(file, config) {
    const region = file.Region || config?.AudibleRegion || 'us';
    const regions = REGIONS
        .map(([code, label]) => `<option value="${code}"${code === region ? ' selected' : ''}>${label}</option>`)
        .join('');

    const audible = `
        <h3 class="abl-file-name"></h3>
        <div class="abl-file-meta abl-muted"></div>
        <div class="abl-section">
            <h4>Audible</h4>
            <div class="abl-row">
                <div class="inputContainer">
                    <input is="emby-input" type="text" class="abl-asin" label="ASIN" maxlength="10" autocomplete="off" value="${escapeHtml(file.Asin || '')}">
                    <div class="fieldDescription">10 letters and digits, from the book's Audible page address. Save it empty to clear it.</div>
                </div>
                <div class="selectContainer">
                    <select is="emby-select" class="abl-region" label="Store">${regions}</select>
                </div>
            </div>
            <div class="abl-actions">
                <button is="emby-button" type="button" class="raised emby-button abl-fetch"><span>${file.CanWrite ? 'Save and fetch chapters' : 'Save ASIN'}</span></button>
            </div>
            <div class="abl-error abl-error-audible hide"></div>
        </div>`;

    if (!file.CanWrite) {
        return audible + `
        <p class="abl-section abl-muted">Chapters can only be written into M4B and M4A files for now. MP3 books get chapter tools in a later release.</p>`;
    }

    return audible + `
        <div class="abl-section">
            <h4>Silences</h4>
            <div class="abl-row">
                <div class="inputContainer">
                    <input is="emby-input" type="number" class="abl-noise" label="Quieter than (dB)" min="-80" max="-10" step="1" value="${escapeHtml(config?.SilenceNoiseDb ?? -30)}">
                    <div class="fieldDescription">A new value needs a new scan.</div>
                </div>
                <div class="inputContainer">
                    <input is="emby-input" type="number" class="abl-min" label="At least (seconds)" min="1" max="30" step="0.5" value="${escapeHtml(config?.SilenceMinSeconds ?? 3)}">
                    <div class="fieldDescription">Changes the preview straight away, without scanning again.</div>
                </div>
            </div>
            <div class="abl-actions">
                <button is="emby-button" type="button" class="raised emby-button abl-scan"><span>Detect silences</span></button>
                <button is="emby-button" type="button" class="raised emby-button abl-stop hide"><span>Stop</span></button>
                <button is="emby-button" type="button" class="raised emby-button abl-show-silence hide"><span>Show these chapters</span></button>
                <span class="abl-silence-status abl-muted"></span>
            </div>
            <div class="abl-error abl-error-silence hide"></div>
        </div>
        <div class="abl-section abl-preview"></div>
        <div class="abl-section">
            <div class="abl-mismatch abl-warning hide">
                <div class="abl-mismatch-text"></div>
                <label class="checkboxContainer">
                    <input is="emby-checkbox" type="checkbox" class="abl-use-anyway">
                    <span>Use these chapters anyway</span>
                </label>
            </div>
            <div class="abl-actions">
                <button is="emby-button" type="button" class="raised button-submit emby-button abl-apply" disabled><span>Apply</span></button>
                <button is="emby-button" type="button" class="raised emby-button abl-restore hide"><span>Restore original</span></button>
                <span class="abl-apply-status abl-muted"></span>
            </div>
            <div class="abl-error abl-error-apply hide"></div>
            <div class="abl-backup-note abl-muted hide">The original is kept next to the file as .bak until you delete it.</div>
        </div>`;
}

export default function (view) {
    const state = {
        books: [],
        filter: 'all',
        search: '',
        config: null,
        open: null,
        timer: null
    };

    const listStatus = view.querySelector('.abl-list-status');
    const booksRoot = view.querySelector('.abl-books');
    const expanded = new Set();
    const sections = new Map();

    // Settings

    async function loadSettings() {
        state.config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        view.querySelector('.abl-setting-region').value = state.config.AudibleRegion || 'us';
        setInput(view.querySelector('.abl-setting-noise'), state.config.SilenceNoiseDb);
        setInput(view.querySelector('.abl-setting-min'), state.config.SilenceMinSeconds);
    }

    view.querySelector('.abl-settings').addEventListener('submit', async (e) => {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.AudibleRegion = view.querySelector('.abl-setting-region').value;
        config.SilenceNoiseDb = Number(view.querySelector('.abl-setting-noise').value);
        config.SilenceMinSeconds = Number(view.querySelector('.abl-setting-min').value);
        const result = await ApiClient.updatePluginConfiguration(PLUGIN_ID, config);
        state.config = config;
        Dashboard.processPluginConfigurationUpdateResult(result);
    });

    // Book list

    async function loadBooks() {
        listStatus.textContent = 'Loading...';
        try {
            state.books = await call('GET', 'Books');
            renderBooks();
        } catch (err) {
            listStatus.textContent = err.message;
        }
    }

    function matches(book) {
        const search = state.search.trim().toLowerCase();
        if (search && !`${book.Title} ${book.Author || ''}`.toLowerCase().includes(search)) {
            return false;
        }

        return state.filter === 'all' || book.Files.some((f) => f.Status === state.filter);
    }

    // Only for loading, searching and filtering, a file change redraws just its own book
    function renderBooks() {
        const shown = state.books.filter(matches);
        listStatus.textContent = `${shown.length} of ${state.books.length} books`;

        sections.clear();
        booksRoot.replaceChildren(...shown.map(renderSection));
    }

    // The same parts as emby-collapse, a full-width heading button with an expand_more arrow that turns when open
    // The arrow sits in front of the title rather than at the end like emby-collapse's
    function renderSection(book) {
        const isOpen = expanded.has(book.FolderId) || book.Files.some((f) => f.ItemId === state.open?.file.ItemId);
        const status = el('span');
        const files = el('span', { class: 'abl-muted' });
        const body = el('div', { class: 'abl-book-body' });
        const content = el('div', { class: 'abl-book-content', inert: !isOpen }, body);
        const head = el('button', { type: 'button', class: 'abl-book-head', 'aria-expanded': String(isOpen) },
            el('span', { class: 'material-icons expand_more abl-expand', 'aria-hidden': 'true' }),
            el('span', { class: 'abl-book-title' },
                el('h3', { text: book.Title }),
                book.Author ? el('span', { class: 'abl-muted', text: book.Author }) : null),
            status,
            files);
        const section = el('div', { class: isOpen ? 'abl-book abl-open' : 'abl-book' }, head, content);

        head.addEventListener('click', () => {
            const opening = !section.classList.contains('abl-open');
            section.classList.toggle('abl-open', opening);
            head.setAttribute('aria-expanded', String(opening));

            // A closed book's buttons and inputs are still there, inert keeps them out of the tab order
            content.inert = !opening;
            if (opening) {
                expanded.add(book.FolderId);
            } else {
                expanded.delete(book.FolderId);
            }
        });

        sections.set(book.FolderId, { body, status, files });
        renderBookBody(book);
        return section;
    }

    function renderBookBody(book) {
        const section = sections.get(book.FolderId);
        if (!section) {
            return;
        }

        section.status.replaceChildren(pill(bookStatus(book)));
        section.files.textContent = book.Files.length === 1 ? '1 file' : `${book.Files.length} files`;

        // replaceChildren takes nodes one by one, a list passed whole would be printed as text
        section.body.replaceChildren(...book.Files.flatMap((file) => renderFile(book, file)));
    }

    function renderFile(book, file) {
        const isOpen = state.open?.file.ItemId === file.ItemId;
        const row = el('div', { class: 'abl-file' },
            el('span', { class: 'abl-name', text: file.FileName }),
            el('span', { class: 'abl-muted', text: `${formatLength(file.DurationSec)}  ${file.ChapterCount} ch` }),
            file.Asin ? el('span', { class: 'abl-muted', text: file.Asin }) : null,
            pill(file.Status),
            el('button', { type: 'button', class: 'raised emby-button', onclick: () => (isOpen ? closeFile() : openFile(book, file)) },
                el('span', { text: isOpen ? 'Close' : 'Open' })));

        return isOpen ? [row, state.open.panel] : [row];
    }

    // One open file

    function closeFile() {
        const book = state.open?.book;
        stopPolling();
        state.open = null;
        if (book) {
            renderBookBody(book);
        }
    }

    function openFile(book, file) {
        const previous = state.open?.book;
        stopPolling();
        state.open = {
            book,
            file,
            panel: buildPanel(file),
            preview: null,
            silence: null,
            apply: null,
            useAnyway: false,
            errors: {}
        };

        if (previous && previous !== book) {
            renderBookBody(previous);
        }

        renderBookBody(book);
        update();

        // An earlier scan or a write still running shows up straight away
        refreshSilence();
        refreshApply();
    }

    function buildPanel(file) {
        const panel = el('div', { class: 'abl-detail' });
        panel.innerHTML = panelHtml(file, state.config);

        const on = (selector, event, handler) => panel.querySelector(selector)?.addEventListener(event, handler);
        on('.abl-fetch', 'click', fetchAudible);
        on('.abl-scan', 'click', startSilence);
        on('.abl-stop', 'click', stopSilence);
        on('.abl-show-silence', 'click', showSilencePreview);
        on('.abl-noise', 'change', refreshSilence);
        on('.abl-min', 'change', refreshSilence);
        on('.abl-apply', 'click', apply);
        on('.abl-restore', 'click', restore);
        on('.abl-use-anyway', 'change', (e) => {
            state.open.useAnyway = e.target.checked;
            update();
        });

        return panel;
    }

    function setError(where, message) {
        state.open.errors[where] = message;
        update();
    }

    function noiseValue() {
        return Number(state.open.panel.querySelector('.abl-noise')?.value) || state.config?.SilenceNoiseDb || -30;
    }

    function minValue() {
        return Number(state.open.panel.querySelector('.abl-min')?.value) || state.config?.SilenceMinSeconds || 3;
    }

    function replaceFile(file) {
        const open = state.open;
        const index = open.book.Files.findIndex((f) => f.ItemId === file.ItemId);
        open.book.Files[index] = file;
        open.file = file;
        renderBookBody(open.book);
        update();
    }

    async function refreshFile() {
        const open = state.open;
        if (open) {
            const file = await call('GET', `Files/${open.file.ItemId}`);
            if (state.open === open) {
                replaceFile(file);
            }
        }
    }

    // Audible

    async function fetchAudible() {
        const open = state.open;
        open.errors.audible = null;
        try {
            replaceFile(await call('PUT', `Files/${open.file.ItemId}/Asin`, {
                Asin: open.panel.querySelector('.abl-asin').value,
                Region: open.panel.querySelector('.abl-region').value
            }));

            if (!open.file.Asin) {
                setError('audible', 'ASIN cleared');
                return;
            }

            if (!open.file.CanWrite) {
                return;
            }

            const match = await call('GET', `Files/${open.file.ItemId}/Audible`);
            open.useAnyway = false;
            open.preview = { source: 'Audible', chapters: match.Chapters, removed: new Set(), match };
            update();
        } catch (err) {
            setError('audible', err.message);
        }
    }

    // Silences

    async function startSilence() {
        const open = state.open;
        open.errors.silence = null;
        try {
            open.silence = await call('POST', `Files/${open.file.ItemId}/Silences?noise=${noiseValue()}`);
            update();
            startPolling();
        } catch (err) {
            setError('silence', err.message);
        }
    }

    async function stopSilence() {
        try {
            await call('DELETE', `Files/${state.open.file.ItemId}/Silences?noise=${noiseValue()}`);
        } catch (err) {
            setError('silence', err.message);
        }

        await refreshSilence();
    }

    async function refreshSilence() {
        const open = state.open;
        if (!open || !open.file.CanWrite) {
            return;
        }

        try {
            const silence = await call('GET', `Files/${open.file.ItemId}/Silences?noise=${noiseValue()}&minSeconds=${minValue()}`);
            if (state.open !== open) {
                return;
            }

            const finishedNow = silence.State === 'Done' && open.silence?.State !== 'Done';
            open.silence = silence;
            open.errors.silence = null;

            // A scan that just finished, or a new minimum, becomes the preview unless Audible's is showing
            if (silence.State === 'Done' && (finishedNow || open.preview?.source === 'Silence') && open.preview?.source !== 'Audible') {
                open.preview = { source: 'Silence', chapters: silence.Chapters, removed: new Set() };
            }

            update();
            if (silence.State === 'Queued' || silence.State === 'Running') {
                startPolling();
            }
        } catch (err) {
            setError('silence', err.message);
        }
    }

    function showSilencePreview() {
        const open = state.open;
        if (open.silence?.State === 'Done') {
            open.preview = { source: 'Silence', chapters: open.silence.Chapters, removed: new Set() };
            update();
        }
    }

    // Apply and restore

    function chaptersToWrite() {
        const preview = state.open.preview;
        return preview.chapters.filter((_, i) => !preview.removed.has(i));
    }

    async function apply() {
        const open = state.open;
        open.errors.apply = null;
        try {
            open.apply = await call('POST', `Files/${open.file.ItemId}/Apply`, {
                Chapters: chaptersToWrite(),
                Source: open.preview.source
            });
            update();
            startPolling();
        } catch (err) {
            setError('apply', err.message);
        }
    }

    async function refreshApply() {
        const open = state.open;
        if (!open) {
            return;
        }

        try {
            const before = open.apply?.State;
            const status = await call('GET', `Files/${open.file.ItemId}/Apply`);
            if (state.open !== open) {
                return;
            }

            open.apply = status;
            update();
            if (status.State === 'Queued' || status.State === 'Running') {
                startPolling();
            } else if (status.State === 'Done' && before && before !== 'Done') {
                // Jellyfin re-reads the file in the background, the chapter count catches up a few seconds later
                await refreshFile();
                setTimeout(() => refreshFile().catch(() => {}), 8000);
            }
        } catch (err) {
            setError('apply', err.message);
        }
    }

    async function restore() {
        const open = state.open;
        open.errors.apply = null;
        try {
            await call('POST', `Files/${open.file.ItemId}/Restore`);
            open.apply = null;
            await refreshFile();
            setTimeout(() => refreshFile().catch(() => {}), 8000);
        } catch (err) {
            setError('apply', err.message);
        }
    }

    // Polling

    function startPolling() {
        if (state.timer) {
            return;
        }

        state.timer = setInterval(async () => {
            const open = state.open;
            const silenceBusy = open?.silence?.State === 'Queued' || open?.silence?.State === 'Running';
            const applyBusy = open?.apply?.State === 'Queued' || open?.apply?.State === 'Running';
            if (!silenceBusy && !applyBusy) {
                stopPolling();
                return;
            }

            if (silenceBusy) {
                await refreshSilence();
            }

            if (applyBusy) {
                await refreshApply();
            }
        }, POLL_MS);
    }

    function stopPolling() {
        clearInterval(state.timer);
        state.timer = null;
    }

    // Panel updates, text and visibility only, so nothing being typed in gets rebuilt

    function showError(where) {
        const node = state.open.panel.querySelector(`.abl-error-${where}`);
        if (node) {
            node.textContent = state.open.errors[where] || '';
            show(node, !!state.open.errors[where]);
        }
    }

    function update() {
        const open = state.open;
        if (!open) {
            return;
        }

        const { panel, file } = open;
        panel.querySelector('.abl-file-name').textContent = file.FileName;
        panel.querySelector('.abl-file-meta').replaceChildren(`${formatTime(file.DurationSec)}  Now: ${file.ChapterCount} chapters  `, pill(file.Status));
        showError('audible');
        if (!file.CanWrite) {
            return;
        }

        updateSilence();
        updatePreview();
        updateApply();
    }

    function updateSilence() {
        const { panel, silence } = state.open;
        const busy = silence?.State === 'Queued' || silence?.State === 'Running';
        const texts = {
            None: 'Not scanned at this loudness yet',
            Queued: 'Waiting for another scan to finish',
            Running: `Scanning ${Math.round(silence?.Percent || 0)}%`,
            Done: `${silence?.Chapters?.length ?? 0} chapters at these settings`,
            Cancelled: 'Stopped',
            Failed: silence?.Error
        };

        const scan = panel.querySelector('.abl-scan');
        setButton(scan, silence?.State === 'Done' ? 'Scan again' : 'Detect silences');
        show(scan, !busy);
        show(panel.querySelector('.abl-stop'), busy);
        show(panel.querySelector('.abl-show-silence'), silence?.State === 'Done' && state.open.preview?.source !== 'Silence');
        panel.querySelector('.abl-silence-status').textContent = silence ? texts[silence.State] || '' : '';
        showError('silence');
    }

    function updatePreview() {
        const open = state.open;
        const preview = open.preview;
        const root = open.panel.querySelector('.abl-preview');
        if (!preview) {
            root.replaceChildren(el('p', { class: 'abl-muted', text: 'Fetch Audible chapters or detect silences to preview chapters here.' }));
            return;
        }

        const match = preview.match;
        const kept = preview.chapters.length - preview.removed.size;
        const source = preview.source === 'Audible'
            ? `Audible${match.Title ? `: ${match.Title}` : ''} (${match.Asin}, ${match.Region})`
            : 'Silences';

        const items = preview.chapters.map((chapter, i) => (preview.removed.has(i) ? null : el('li', {},
            el('span', { class: 'abl-time', text: formatTime(chapter.StartSec) }),
            el('span', { class: 'abl-title', text: chapter.Title }),
            i === 0
                ? el('span', { class: 'abl-muted', text: 'first, stays' })
                : el('button', {
                    type: 'button',
                    class: 'paper-icon-button-light',
                    title: 'Remove this mark',
                    'aria-label': 'Remove this mark',
                    onclick: () => {
                        preview.removed.add(i);
                        update();
                    }
                }, el('span', { class: 'material-icons close', 'aria-hidden': 'true' })))));

        // replaceChildren prints a null as the word null, so the lines that don't apply are dropped first
        root.replaceChildren(...[
            el('h4', { text: `Preview from ${source}, ${kept} chapters` }),
            match?.IsOld ? el('div', { class: 'abl-muted', text: 'Audnexus is unreachable, this is an older saved copy.' }) : null,
            match?.DroppedPastEnd ? el('div', { class: 'abl-muted', text: `${match.DroppedPastEnd} Audible chapters start after this file ends and are left out.` }) : null,
            el('ol', { class: 'abl-chapters' }, items)
        ].filter(Boolean));
    }

    function updateApply() {
        const open = state.open;
        const { panel, preview, apply: status } = open;
        const match = preview?.match;
        const mismatch = !!match && !match.Matches;
        const running = status?.State === 'Queued' || status?.State === 'Running';
        const stages = {
            Preparing: 'Checking the folder and reading the file',
            Writing: 'Writing a copy with the new chapters',
            Checking: 'Checking the copy against the original',
            Replacing: 'Putting the new file in place',
            Refreshing: 'Asking Jellyfin to re-read the file'
        };

        show(panel.querySelector('.abl-mismatch'), mismatch);
        if (mismatch) {
            const apart = Math.abs(match.DifferenceSec).toFixed(1);
            panel.querySelector('.abl-mismatch-text').textContent = `Audible's book is ${formatTime(match.AudibleSec)} and this file is ${formatTime(match.FileSec)}, ${apart} s apart. It may be another edition or the wrong ASIN, and every chapter could be off by that much.`;
            panel.querySelector('.abl-use-anyway').checked = open.useAnyway;
        }

        const count = preview ? preview.chapters.length - preview.removed.size : 0;
        const blocked = !preview || running || (mismatch && !open.useAnyway);
        setButton(panel.querySelector('.abl-apply'), preview ? `Apply ${count} chapters` : 'Apply', blocked);

        const hasBackup = status?.HasBackup ?? open.file.HasBackup;
        show(panel.querySelector('.abl-restore'), hasBackup && !running);
        show(panel.querySelector('.abl-backup-note'), hasBackup);

        let line = '';
        if (status?.State === 'Queued') {
            line = 'Waiting for another write to finish';
        } else if (status?.State === 'Running') {
            line = `${stages[status.Stage] || status.Stage}...`;
        } else if (status?.State === 'Done') {
            line = 'Done. Jellyfin is re-reading the file, the chapter count updates in a few seconds.';
        }

        panel.querySelector('.abl-apply-status').textContent = line;
        if (status?.State === 'Failed') {
            open.errors.apply = status.Error;
        }

        showError('apply');
    }

    // Page events

    view.querySelector('.abl-search').addEventListener('input', (e) => {
        state.search = e.target.value;
        renderBooks();
    });

    view.querySelectorAll('.abl-filters button').forEach((button) => {
        button.addEventListener('click', () => {
            state.filter = button.dataset.filter;
            view.querySelectorAll('.abl-filters button').forEach((b) => b.classList.toggle('abl-on', b === button));
            renderBooks();
        });
    });

    view.addEventListener('viewshow', async () => {
        Dashboard.showLoadingMsg();
        try {
            await loadSettings();
            await loadBooks();
        } finally {
            Dashboard.hideLoadingMsg();
        }
    });

    view.addEventListener('viewhide', stopPolling);
}
