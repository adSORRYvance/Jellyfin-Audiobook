// Audiobook Library admin page, loaded by jellyfin-web as the config page's controller through data-controller.
// It lists every audiobook file, previews chapters from Audible or from silences, and applies or restores them.
// Every title on this page comes from someone's files, so text always goes in through textContent, never as HTML.

const PLUGIN_ID = '4cecc660-432f-4714-8958-b5da8537e55d';
const REGIONS = ['us', 'uk', 'ca', 'au', 'de', 'fr', 'it', 'es', 'in', 'jp'];
const POLL_MS = 2000;
const MATCH_TOLERANCE_SEC = 2;

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

    for (const child of children.flat()) {
        if (child != null && child !== false) {
            node.append(child);
        }
    }

    return node;
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

    // Settings

    async function loadSettings() {
        state.config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        const region = view.querySelector('.abl-setting-region');
        region.replaceChildren(...REGIONS.map((r) => el('option', { value: r, text: r })));
        region.value = state.config.AudibleRegion || 'us';
        view.querySelector('.abl-setting-noise').value = state.config.SilenceNoiseDb;
        view.querySelector('.abl-setting-min').value = state.config.SilenceMinSeconds;
    }

    view.querySelector('.abl-settings').addEventListener('submit', async (e) => {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(PLUGIN_ID);
        config.AudibleRegion = view.querySelector('.abl-setting-region').value;
        config.SilenceNoiseDb = Number(view.querySelector('.abl-setting-noise').value) || -30;
        config.SilenceMinSeconds = Number(view.querySelector('.abl-setting-min').value) || 3;
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

    // A book's pill is its most urgent file's, so a half-done split book doesn't look finished
    function bookStatus(book) {
        const order = ['NeedsReview', 'NoChapters', 'Silence', 'Audible', 'Embedded', 'NotSupported'];
        return order.find((s) => book.Files.some((f) => f.Status === s)) || 'NotSupported';
    }

    function pill(status) {
        const labels = {
            NoChapters: 'No chapters',
            Embedded: 'Embedded',
            Audible: 'Audible',
            Silence: 'Silence',
            NeedsReview: 'Needs review',
            NotSupported: 'MP3, later'
        };
        return el('span', { class: `abl-pill abl-pill-${status}`, text: labels[status] || status });
    }

    function renderBooks() {
        const shown = state.books.filter(matches);
        listStatus.textContent = `${shown.length} of ${state.books.length} books`;
        booksRoot.replaceChildren(...shown.map(renderBook));
    }

    function renderBook(book) {
        const isOpen = expanded.has(book.FolderId) || book.Files.some((f) => f.ItemId === state.open?.file.ItemId);
        const files = book.Files.length === 1 ? '1 file' : `${book.Files.length} files`;
        const head = el('div', { class: 'abl-book-head', onclick: () => {
            if (expanded.has(book.FolderId)) {
                expanded.delete(book.FolderId);
            } else {
                expanded.add(book.FolderId);
            }

            renderBooks();
        } },
        el('span', { text: isOpen ? 'v' : '>' }),
        el('span', { class: 'abl-name' }, el('strong', { text: book.Title }), book.Author ? el('span', { class: 'abl-muted', text: ` - ${book.Author}` }) : null),
        el('span', { class: 'abl-muted', text: files }),
        pill(bookStatus(book)));

        const rows = isOpen ? book.Files.map((file) => renderFile(book, file)) : [];
        return el('div', { class: 'abl-book' }, head, rows);
    }

    function renderFile(book, file) {
        const isOpen = state.open?.file.ItemId === file.ItemId;
        const row = el('div', { class: 'abl-file' },
            el('span', { class: 'abl-name', text: file.FileName }),
            el('span', { class: 'abl-muted', text: `${formatLength(file.DurationSec)}  ${file.ChapterCount} ch` }),
            file.Asin ? el('span', { class: 'abl-muted', text: file.Asin }) : null,
            pill(file.Status),
            el('button', { type: 'button', class: 'raised emby-button', text: isOpen ? 'Close' : 'Open', onclick: () => {
                if (isOpen) {
                    closeFile();
                } else {
                    openFile(book, file);
                }
            } }));

        return isOpen ? [row, state.open.panel] : [row];
    }

    // One open file

    function closeFile() {
        stopPolling();
        state.open = null;
        renderBooks();
    }

    function openFile(book, file) {
        stopPolling();
        const open = {
            book,
            file,
            panel: el('div', { class: 'abl-detail' }),
            preview: null,
            silence: null,
            apply: null,
            useAnyway: false,
            errors: {}
        };
        state.open = open;
        renderPanel();
        renderBooks();

        // An earlier scan or a write still running shows up straight away
        refreshSilence();
        refreshApply();
    }

    function setError(where, message) {
        state.open.errors[where] = message;
        renderPanel();
    }

    function noiseValue() {
        return Number(state.open.panel.querySelector('.abl-noise')?.value) || state.config?.SilenceNoiseDb || -30;
    }

    function minValue() {
        return Number(state.open.panel.querySelector('.abl-min')?.value) || state.config?.SilenceMinSeconds || 3;
    }

    async function refreshFile() {
        const open = state.open;
        if (!open) {
            return;
        }

        const file = await call('GET', `Files/${open.file.ItemId}`);
        const index = open.book.Files.findIndex((f) => f.ItemId === file.ItemId);
        open.book.Files[index] = file;
        open.file = file;
        renderPanel();
        renderBooks();
    }

    // Audible

    async function fetchAudible() {
        const open = state.open;
        const asin = open.panel.querySelector('.abl-asin').value;
        const region = open.panel.querySelector('.abl-region').value;
        open.errors.audible = null;
        try {
            open.file = await call('PUT', `Files/${open.file.ItemId}/Asin`, { Asin: asin, Region: region });
            const index = open.book.Files.findIndex((f) => f.ItemId === open.file.ItemId);
            open.book.Files[index] = open.file;
            if (!open.file.Asin) {
                setError('audible', 'ASIN cleared');
                renderBooks();
                return;
            }

            const match = await call('GET', `Files/${open.file.ItemId}/Audible`);
            open.useAnyway = false;
            open.preview = { source: 'Audible', chapters: match.Chapters, removed: new Set(), match };
            renderPanel();
            renderBooks();
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
            renderPanel();
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

            // A scan that just finished, or one found on opening, becomes the preview unless Audible's is showing
            if (silence.State === 'Done' && (finishedNow || open.preview?.source === 'Silence') && open.preview?.source !== 'Audible') {
                open.preview = { source: 'Silence', chapters: silence.Chapters, removed: new Set() };
            }

            renderPanel();
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
            renderPanel();
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
            renderPanel();
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
            renderPanel();
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
            if (!open) {
                stopPolling();
                return;
            }

            const silenceBusy = open.silence?.State === 'Queued' || open.silence?.State === 'Running';
            const applyBusy = open.apply?.State === 'Queued' || open.apply?.State === 'Running';
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

    // Panel

    function errorLine(where) {
        const message = state.open.errors[where];
        return message ? el('div', { class: 'abl-error', text: message }) : null;
    }

    function renderPanel() {
        const open = state.open;
        if (!open) {
            return;
        }

        // Keep what was typed, the panel is rebuilt on every status change
        const typed = {
            asin: open.panel.querySelector('.abl-asin')?.value,
            region: open.panel.querySelector('.abl-region')?.value,
            noise: open.panel.querySelector('.abl-noise')?.value,
            min: open.panel.querySelector('.abl-min')?.value
        };
        const file = open.file;

        const header = el('div', {},
            el('h3', { text: file.FileName }),
            el('div', { class: 'abl-muted', text: `${formatTime(file.DurationSec)}  Now: ${file.ChapterCount} chapters` }, ' ', pill(file.Status)));

        const audible = el('div', { class: 'abl-section' },
            el('strong', { text: 'Audible' }),
            el('div', { class: 'abl-row' },
                el('label', {}, 'ASIN', el('input', { class: 'abl-asin', value: typed.asin ?? file.Asin ?? '', placeholder: 'B0XXXXXXXX' })),
                el('label', {}, 'Store', el('select', { class: 'abl-region' },
                    REGIONS.map((r) => el('option', { value: r, text: r, selected: r === (typed.region ?? file.Region ?? state.config?.AudibleRegion ?? 'us') })))),
                el('button', { type: 'button', class: 'raised emby-button', text: file.CanWrite ? 'Save and fetch chapters' : 'Save ASIN', onclick: fetchAudible })),
            errorLine('audible'));

        if (!file.CanWrite) {
            open.panel.replaceChildren(header, audible,
                el('p', { class: 'abl-muted', text: 'Chapters can only be written into M4B and M4A files for now. MP3 books get chapter tools in a later release.' }));
            return;
        }

        const silence = open.silence;
        const busy = silence?.State === 'Queued' || silence?.State === 'Running';
        let silenceText = '';
        if (silence?.State === 'Running') {
            silenceText = `Scanning ${Math.round(silence.Percent)}%`;
        } else if (silence?.State === 'Queued') {
            silenceText = 'Waiting for another scan to finish';
        } else if (silence?.State === 'Done') {
            silenceText = `${silence.Chapters.length} chapters at these settings`;
        } else if (silence?.State === 'Cancelled') {
            silenceText = 'Stopped';
        }

        const silences = el('div', { class: 'abl-section' },
            el('strong', { text: 'Silences' }),
            el('div', { class: 'abl-row' },
                el('label', {}, 'Quieter than (dB)', el('input', { class: 'abl-noise abl-narrow', type: 'number', min: '-80', max: '-10', step: '1', value: typed.noise ?? state.config?.SilenceNoiseDb ?? -30 })),
                el('label', {}, 'At least (s)', el('input', { class: 'abl-min abl-narrow', type: 'number', min: '1', max: '30', step: '0.5', value: typed.min ?? state.config?.SilenceMinSeconds ?? 3, onchange: refreshSilence })),
                busy
                    ? el('button', { type: 'button', class: 'raised emby-button', text: 'Stop', onclick: stopSilence })
                    : el('button', { type: 'button', class: 'raised emby-button', text: silence?.State === 'Done' ? 'Scan again' : 'Detect silences', onclick: startSilence }),
                silence?.State === 'Done' && open.preview?.source !== 'Silence'
                    ? el('button', { type: 'button', class: 'raised emby-button', text: 'Show these chapters', onclick: showSilencePreview })
                    : null,
                el('span', { class: 'abl-muted', text: silenceText })),
            silence?.State === 'Failed' ? el('div', { class: 'abl-error', text: silence.Error }) : null,
            errorLine('silence'));

        open.panel.replaceChildren(header, audible, silences, renderPreview(), renderApply());
    }

    function renderPreview() {
        const open = state.open;
        const preview = open.preview;
        if (!preview) {
            return el('div', { class: 'abl-section abl-muted', text: 'Fetch Audible chapters or detect silences to preview chapters here.' });
        }

        const kept = preview.chapters.length - preview.removed.size;
        const match = preview.match;
        const items = preview.chapters.map((chapter, i) => {
            if (preview.removed.has(i)) {
                return null;
            }

            return el('li', {},
                el('span', { class: 'abl-time', text: formatTime(chapter.StartSec) }),
                el('span', { class: 'abl-title', text: chapter.Title }),
                i === 0
                    ? el('span', { class: 'abl-muted', text: 'first, stays' })
                    : el('button', { type: 'button', class: 'abl-x', title: 'Remove this mark', text: 'x', onclick: () => {
                        preview.removed.add(i);
                        renderPanel();
                    } }));
        });

        let warning = null;
        if (match && !match.Matches) {
            const apart = Math.abs(match.DifferenceSec).toFixed(1);
            warning = el('div', { class: 'abl-warning' },
                el('div', { text: `Audible's book is ${formatTime(match.AudibleSec)} and this file is ${formatTime(match.FileSec)}, ${apart} s apart. It may be another edition or the wrong ASIN, and every chapter could be off by that much.` }),
                el('label', {}, el('input', { type: 'checkbox', checked: open.useAnyway, onchange: (e) => {
                    open.useAnyway = e.target.checked;
                    renderPanel();
                } }), ' Use anyway'));
        }

        const source = preview.source === 'Audible'
            ? `Audible${match.Title ? `: ${match.Title}` : ''} (${match.Asin}, ${match.Region})`
            : 'Silences';
        return el('div', { class: 'abl-section' },
            el('strong', { text: `Preview from ${source}, ${kept} chapters` }),
            match?.IsOld ? el('div', { class: 'abl-muted', text: 'Audnexus is unreachable, this is an older saved copy.' }) : null,
            match?.DroppedPastEnd ? el('div', { class: 'abl-muted', text: `${match.DroppedPastEnd} Audible chapters start after this file ends and are left out.` }) : null,
            warning,
            el('ol', { class: 'abl-chapters' }, items));
    }

    function renderApply() {
        const open = state.open;
        const status = open.apply;
        const running = status?.State === 'Queued' || status?.State === 'Running';
        const blocked = !open.preview || running || (open.preview.match && !open.preview.match.Matches && !open.useAnyway);
        const stages = {
            Preparing: 'Checking the folder and reading the file',
            Writing: 'Writing a copy with the new chapters',
            Checking: 'Checking the copy against the original',
            Replacing: 'Putting the new file in place',
            Refreshing: 'Asking Jellyfin to re-read the file'
        };

        let line = null;
        if (status?.State === 'Queued') {
            line = el('span', { class: 'abl-muted', text: 'Waiting for another write to finish' });
        } else if (status?.State === 'Running') {
            line = el('span', { class: 'abl-muted', text: `${stages[status.Stage] || status.Stage}...` });
        } else if (status?.State === 'Done') {
            line = el('span', { class: 'abl-muted', text: 'Done. Jellyfin is re-reading the file, the chapter count updates in a few seconds.' });
        }

        const hasBackup = status?.HasBackup ?? open.file.HasBackup;
        const count = open.preview ? open.preview.chapters.length - open.preview.removed.size : 0;
        return el('div', { class: 'abl-section' },
            el('div', { class: 'abl-row' },
                el('button', { type: 'button', class: 'raised button-submit emby-button', disabled: blocked, text: `Apply ${count} chapters`, onclick: apply }),
                hasBackup && !running ? el('button', { type: 'button', class: 'raised emby-button', text: 'Restore original', onclick: restore }) : null,
                line),
            status?.State === 'Failed' ? el('div', { class: 'abl-error', text: status.Error }) : null,
            errorLine('apply'),
            hasBackup ? el('div', { class: 'abl-muted', text: 'The original is kept next to the file as .bak until you delete it.' }) : null);
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
