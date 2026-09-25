// Audiobook Library player for jellyfin-web, loaded as a window plugin through config.json.
// playbackManager hands us anything whose Type is AudioBook, and music and video keep the stock players.
// While a book plays our bar stands in for Jellyfin's now-playing bar, and on phones tapping it opens a full page.
// playbackManager also reports progress to the server for us, so resume and Continue Listening need nothing extra here.
(function () {
    'use strict';

    const TICKS_PER_SECOND = 10000000;
    const SKIP_SECONDS = 10;
    const SPEED_STEP = 0.1;
    const SPEED_PRESETS = [0.5, 1, 1.2, 1.5, 2];

    // jellyfin-web tags <html> with this in the app and on phones, narrow windows get the same layout
    const COMPACT_QUERY = '(max-width: 700px)';

    // Everything in these templates is fixed markup, titles and names go in later through textContent
    const icon = (name) => `<span class="material-icons ${name}" aria-hidden="true"></span>`;

    const PROGRESS = `
        <div class="abl-progress">
            <div class="abl-ticks"></div>
            <input class="abl-seek" type="range" min="0" max="0" step="0.1" value="0" aria-label="Position">
        </div>`;

    const CONTROLS = `
        <div class="abl-controls">
            <button type="button" class="abl-icon abl-prev" data-action="prev" title="Previous chapter">${icon('skip_previous')}</button>
            <button type="button" class="abl-icon" data-action="back" title="Back 10 seconds">${icon('replay_10')}</button>
            <button type="button" class="abl-icon abl-play" data-action="play" title="Play">${icon('play_arrow')}</button>
            <button type="button" class="abl-icon" data-action="forward" title="Forward 10 seconds">${icon('forward_10')}</button>
            <button type="button" class="abl-icon abl-next" data-action="next" title="Next chapter">${icon('skip_next')}</button>
        </div>`;

    const BAR_TEMPLATE = `
        ${PROGRESS}
        <div class="abl-bar-times">
            <span><span class="abl-elapsed"></span><span class="abl-percent"></span></span>
            <span class="abl-chapter-title"></span>
            <span class="abl-remaining"></span>
        </div>
        <div class="abl-bar-main">
            <div class="abl-bar-info">
                <img class="abl-bar-cover abl-cover abl-hidden" alt="">
                <div class="abl-bar-text">
                    <div class="abl-book-title"></div>
                    <div class="abl-book-author"></div>
                    <div class="abl-book-length"></div>
                </div>
            </div>
            ${CONTROLS}
            <div class="abl-bar-right">
                <button type="button" class="abl-text-btn" data-action="speed" title="Playback speed"><span class="abl-speed-label"></span></button>
                <button type="button" class="abl-icon abl-mute" data-action="mute" title="Mute">${icon('volume_up')}</button>
                <input class="abl-volume" type="range" min="0" max="100" step="1" value="100" aria-label="Volume">
                <button type="button" class="abl-icon abl-chapters-open" data-action="chapters" title="Chapters">${icon('format_list_bulleted')}</button>
                <button type="button" class="abl-icon" data-action="stop" title="Stop">${icon('close')}</button>
            </div>
        </div>`;

    const PAGE_TEMPLATE = `
        <div class="abl-page-top">
            <button type="button" class="abl-icon" data-action="close-page" title="Close">${icon('expand_more')}</button>
            <button type="button" class="abl-icon" data-action="stop" title="Stop">${icon('close')}</button>
        </div>
        <div class="abl-page-body">
            <img class="abl-page-cover abl-cover abl-hidden" alt="">
            <div class="abl-page-titles">
                <div class="abl-page-heading"></div>
                <div class="abl-book-author"></div>
            </div>
            <div class="abl-page-progress">
                <div class="abl-page-times"><span class="abl-elapsed"></span><span class="abl-remaining"></span></div>
                ${PROGRESS}
            </div>
            ${CONTROLS}
            <div class="abl-page-extras">
                <button type="button" class="abl-text-btn" data-action="speed" title="Playback speed"><span class="abl-speed-label"></span></button>
                <button type="button" class="abl-icon abl-chapters-open" data-action="chapters" title="Chapters">${icon('format_list_bulleted')}</button>
            </div>
        </div>`;

    const SPEED_TEMPLATE = `
        <div class="abl-sheet-head"><span>Speed</span><button type="button" class="abl-icon" data-action="close-sheet" title="Close">${icon('close')}</button></div>
        <div class="abl-speed-row">
            <button type="button" class="abl-icon abl-speed-down" data-action="slower" title="Slower">${icon('remove')}</button>
            <span class="abl-speed-value"></span>
            <button type="button" class="abl-icon abl-speed-up" data-action="faster" title="Faster">${icon('add')}</button>
        </div>
        <div class="abl-presets"></div>`;

    const CHAPTER_TEMPLATE = `
        <div class="abl-sheet-head"><span>Chapters</span><button type="button" class="abl-icon" data-action="close-sheet" title="Close">${icon('close')}</button></div>
        <ol class="abl-chapter-list"></ol>`;

    window.AudiobookLibraryPlayer = async function () {
        const nav = window.AudiobookLibraryChapterNav;

        // jellyfin-web catches this and keeps its own players, which beats registering one that breaks on the first tap
        if (!nav) {
            throw new Error('Audiobook Library: chapter-nav.js did not load');
        }

        return class AudiobookLibraryPlayer {
            constructor(deps) {
                this.name = 'Audiobook Library';
                this.type = 'mediaplayer';
                this.id = 'audiobooklibraryplayer';

                // getPlayer takes the lowest priority that can play the item, the stock audio player is 1
                this.priority = -10;
                this.isLocalPlayer = true;

                this._events = deps.events;
                this._appHost = deps.appHost;
                this._playbackManager = deps.playbackManager;
                this._roots = [];
                this._audio = null;
                this._item = null;
                this._chapters = [];
                this._chapterIndex = -2;
                this._currentSrc = null;
                this._currentTime = null;
                this._dragging = false;
                this._loadToken = 0;
                this._active = false;
                this._pageOpen = false;
                this._mediaControlAllowed = true;
            }

            canPlayMediaType(mediaType) {
                return (mediaType || '').toLowerCase() === 'audio';
            }

            canPlayItem(item) {
                return item?.Type === 'AudioBook';
            }

            getDeviceProfile(item) {
                if (this._appHost?.getDeviceProfile) {
                    return this._appHost.getDeviceProfile(item);
                }

                // playbackManager won't call play without a profile, so fall back to direct play for any audio
                return Promise.resolve({
                    MaxStreamingBitrate: 120000000,
                    DirectPlayProfiles: [{ Type: 'Audio' }],
                    TranscodingProfiles: [],
                    ContainerProfiles: [],
                    CodecProfiles: [],
                    SubtitleProfiles: []
                });
            }

            play(options) {
                this._ensureView();

                const item = options.item || {};
                this._item = item;
                this._chapters = [];
                this._chapterIndex = -2;
                this._active = true;
                this._closeSheets();
                this._renderItem(item);
                this._renderChapters();
                this._renderCompact();
                this._renderVisibility();

                const audio = this._audio;
                this._currentSrc = options.url;
                this._currentTime = null;
                audio.src = options.url;

                const startTicks = options.playerStartPositionTicks || 0;
                if (startTicks > 0) {
                    audio.addEventListener('loadedmetadata', () => {
                        audio.currentTime = startTicks / TICKS_PER_SECOND;
                    }, { once: true });
                }

                this._loadChapters(item);

                // The WebView can refuse autoplay once the tap is a few awaits behind us, the play button still works then
                return audio.play().catch((err) => {
                    console.warn('Audiobook Library: autoplay refused', err);
                });
            }

            stop(destroyPlayer) {
                const src = this._currentSrc;
                this._audio?.pause();

                // playbackManager reads currentTime() while handling this to report the stop position
                // Firing it after the reset made the server save 0 and drop the book from Continue Listening
                this._events.trigger(this, 'stopped', [{ src }]);

                if (this._audio) {
                    this._audio.removeAttribute('src');
                    this._audio.load();
                }

                this._currentSrc = null;
                this._currentTime = null;
                this._loadToken++;
                this._active = false;
                this._pageOpen = false;
                this._renderVisibility();

                if (destroyPlayer) {
                    this.destroy();
                }

                return Promise.resolve();
            }

            destroy() {
                document.removeEventListener('viewbeforeshow', this._onViewBeforeShow);
                this._compactQuery?.removeEventListener('change', this._onCompactChange);
                document.body.classList.remove('abl-active');
                this._roots.forEach((root) => root.remove());
                this._scrim?.remove();
                this._audio?.remove();
                this._roots = [];
                this._bar = null;
                this._page = null;
                this._audio = null;
            }

            currentSrc() {
                return this._currentSrc;
            }

            currentTime(val) {
                if (!this._audio) {
                    return 0;
                }

                if (val != null) {
                    this._seekTo(val / 1000);
                    return;
                }

                // Prefer the last timeupdate, the element itself can read 0 once the WebView or an ended event resets it
                if (this._currentTime) {
                    return this._currentTime * 1000;
                }

                return this._audio.currentTime * 1000;
            }

            duration() {
                const d = this._durationSec();
                return d > 0 ? d * 1000 : null;
            }

            seekable() {
                return true;
            }

            getBufferedRanges() {
                return [];
            }

            pause() {
                this._audio?.pause();
            }

            resume() {
                this.unpause();
            }

            unpause() {
                this._audio?.play();
            }

            paused() {
                return this._audio ? this._audio.paused : false;
            }

            setPlaybackRate(value) {
                if (this._audio) {
                    const rate = nav.clampSpeed(Number(value) || 1);

                    // Loading a new src resets playbackRate to defaultPlaybackRate, so set both
                    this._audio.defaultPlaybackRate = rate;
                    this._audio.playbackRate = rate;
                }
            }

            getPlaybackRate() {
                return this._audio ? this._audio.playbackRate : 1;
            }

            setVolume(val) {
                if (this._audio) {
                    this._audio.volume = Math.min(Math.max(val, 0), 100) / 100;
                }
            }

            getVolume() {
                return this._audio ? Math.round(this._audio.volume * 100) : 100;
            }

            volumeUp() {
                this.setVolume(Math.min(this.getVolume() + 2, 100));
            }

            volumeDown() {
                this.setVolume(Math.max(this.getVolume() - 2, 0));
            }

            setMute(mute) {
                if (this._audio) {
                    this._audio.muted = mute;
                }
            }

            isMuted() {
                return this._audio ? this._audio.muted : false;
            }

            supports(feature) {
                return feature === 'PlaybackRate';
            }

            // View setup

            _ensureView() {
                if (this._bar) {
                    return;
                }

                const audio = document.createElement('audio');
                audio.preload = 'metadata';
                document.body.appendChild(audio);
                this._audio = audio;

                // Inside the footer we get the theme background, the safe-area padding and Jellyfin's stacking order for free
                this._bar = createElement('div', 'abl-bar abl-hidden', BAR_TEMPLATE);
                const footer = document.querySelector('.appfooter');
                if (footer) {
                    footer.insertAdjacentElement('afterbegin', this._bar);
                } else {
                    this._bar.classList.add('abl-floating');
                    document.body.appendChild(this._bar);
                }

                this._page = createElement('div', 'abl-page abl-hidden', PAGE_TEMPLATE);
                this._speedSheet = createElement('div', 'abl-sheet abl-hidden', SPEED_TEMPLATE);
                this._chapterSheet = createElement('div', 'abl-sheet abl-hidden', CHAPTER_TEMPLATE);
                this._scrim = createElement('div', 'abl-scrim abl-hidden', '');
                document.body.append(this._page, this._scrim, this._speedSheet, this._chapterSheet);

                this._roots = [this._bar, this._page, this._speedSheet, this._chapterSheet];
                this._roots.forEach((root) => root.addEventListener('click', (e) => this._onClick(root, e)));
                this._scrim.addEventListener('click', () => this._closeSheets());

                this._bindSliders();
                this._bindAudioEvents(audio);
                this._buildPresets();
                this._renderSpeed();
                this._renderVolume();

                // Same rule Jellyfin's bar follows, pages like the video player ask for no media controls
                this._onViewBeforeShow = (e) => {
                    this._mediaControlAllowed = !!e.detail?.options?.enableMediaControl;
                    this._renderVisibility();
                };
                document.addEventListener('viewbeforeshow', this._onViewBeforeShow);

                this._compactQuery = window.matchMedia(COMPACT_QUERY);
                this._onCompactChange = () => this._renderCompact();
                this._compactQuery.addEventListener('change', this._onCompactChange);
            }

            _onClick(root, e) {
                const button = e.target.closest('[data-action]');
                if (button && root.contains(button)) {
                    if (!button.disabled) {
                        this._onAction(button.dataset.action);
                    }

                    return;
                }

                // Tapping the compact bar anywhere that isn't a control opens the full page, like Audiobookshelf's app
                if (root === this._bar && this._isCompact() && !e.target.closest('button, input')) {
                    this._pageOpen = true;
                    this._renderVisibility();
                }
            }

            _onAction(action) {
                const position = this._position();
                switch (action) {
                    case 'play':
                        if (this._audio.paused) {
                            this.unpause();
                        } else {
                            this.pause();
                        }

                        break;
                    case 'back':
                        this._seekTo(position - SKIP_SECONDS);
                        break;
                    case 'forward':
                        this._seekTo(position + SKIP_SECONDS);
                        break;
                    case 'prev':
                        this._seekTo(nav.previousTarget(this._chapters, position));
                        break;
                    case 'next': {
                        const target = nav.nextTarget(this._chapters, position);
                        if (target != null) {
                            this._seekTo(target);
                        }

                        break;
                    }
                    case 'speed':
                        this._openSheet(this._speedSheet);
                        break;
                    case 'chapters':
                        this._openSheet(this._chapterSheet);
                        this._chapterSheet.querySelector('.abl-current')?.scrollIntoView({ block: 'center' });
                        break;
                    case 'slower':
                        this.setPlaybackRate(this.getPlaybackRate() - SPEED_STEP);
                        break;
                    case 'faster':
                        this.setPlaybackRate(this.getPlaybackRate() + SPEED_STEP);
                        break;
                    case 'mute':
                        this.setMute(!this.isMuted());
                        break;
                    case 'stop':
                        this._playbackManager.stop(this);
                        break;
                    case 'close-page':
                        this._pageOpen = false;
                        this._renderVisibility();
                        break;
                    case 'close-sheet':
                        this._closeSheets();
                        break;
                    default:
                        break;
                }
            }

            _bindSliders() {
                // While a thumb is held we only preview the time, the seek happens once on release
                this._each('.abl-seek', (seek) => {
                    seek.addEventListener('input', () => {
                        this._dragging = true;
                        this._renderPosition(Number(seek.value));
                    });
                    seek.addEventListener('change', () => {
                        this._dragging = false;
                        this._seekTo(Number(seek.value));
                    });
                });

                this._each('.abl-volume', (volume) => {
                    volume.addEventListener('input', () => this.setVolume(Number(volume.value)));
                });
            }

            _bindAudioEvents(audio) {
                audio.addEventListener('playing', () => {
                    this._events.trigger(this, 'playing');
                    this._events.trigger(this, 'unpause');
                });
                audio.addEventListener('play', () => this._renderPlayState());
                audio.addEventListener('pause', () => {
                    this._renderPlayState();
                    this._events.trigger(this, 'pause');
                });
                audio.addEventListener('timeupdate', () => {
                    this._currentTime = audio.currentTime;
                    this._events.trigger(this, 'timeupdate');
                    if (!this._dragging) {
                        this._renderPosition(audio.currentTime);
                    }
                });
                audio.addEventListener('durationchange', () => {
                    this._renderTicks();
                    this._renderPosition(this._position());
                });
                audio.addEventListener('ratechange', () => this._renderSpeed());
                audio.addEventListener('volumechange', () => {
                    this._renderVolume();
                    this._events.trigger(this, 'volumechange');
                });
                audio.addEventListener('ended', () => this.stop(false));
                audio.addEventListener('error', () => {
                    console.warn('Audiobook Library: audio error', audio.error?.code);
                });
            }

            _buildPresets() {
                const presets = this._speedSheet.querySelector('.abl-presets');
                for (const rate of SPEED_PRESETS) {
                    const btn = createElement('button', 'abl-text-btn abl-preset', '');
                    btn.type = 'button';
                    btn.dataset.rate = String(rate);
                    btn.textContent = `${rate}x`;
                    btn.addEventListener('click', () => this.setPlaybackRate(rate));
                    presets.appendChild(btn);
                }
            }

            // Chapters

            _loadChapters(item) {
                const token = ++this._loadToken;
                const api = window.ApiClient;
                if (!api || !item.Id) {
                    return;
                }

                api.getJSON(api.getUrl(`AudiobookLibrary/Books/${item.Id}/Chapters`))
                    .then((book) => {
                        // A later play or stop bumps the token, so an old answer can't paint over a newer book
                        if (token !== this._loadToken) {
                            return;
                        }

                        this._chapters = nav.chaptersForTrack(book, item.Id);
                        this._chapterIndex = -2;
                        this._renderChapters();
                    })
                    .catch((err) => {
                        // Without chapters the player still works, it just looks like a book that has none
                        console.warn('Audiobook Library: could not load chapters', err);
                    });
            }

            // Rendering

            _renderItem(item) {
                // Split MP3 books name each file after its chapter, the album is the book
                const title = item.Album || item.Name || 'Audiobook';
                const author = item.AlbumArtist || item.Artists?.[0] || '';
                const length = (item.RunTimeTicks || 0) / TICKS_PER_SECOND;

                this._each('.abl-book-title', (el) => {
                    el.textContent = title;
                });
                this._each('.abl-book-author', (el) => {
                    el.textContent = author;
                });
                this._each('.abl-book-length', (el) => {
                    el.textContent = length > 0 ? nav.formatTime(length) : '';
                });

                const url = coverUrl(item);
                this._each('.abl-cover', (img) => {
                    img.classList.toggle('abl-hidden', !url);
                    if (url) {
                        img.src = url;
                    } else {
                        img.removeAttribute('src');
                    }
                });
            }

            _renderChapters() {
                const hasChapters = this._chapters.length > 0;
                this._each('.abl-prev, .abl-next, .abl-chapters-open', (el) => {
                    el.classList.toggle('abl-hidden', !hasChapters);
                });

                const list = this._chapterSheet.querySelector('.abl-chapter-list');
                list.replaceChildren();
                this._chapters.forEach((chapter, index) => {
                    const btn = createElement('button', 'abl-chapter-item', '');
                    btn.type = 'button';
                    btn.dataset.index = String(index);

                    const title = document.createElement('span');
                    title.textContent = chapter.title;
                    const start = createElement('span', 'abl-chapter-start', '');
                    start.textContent = nav.formatTime(chapter.start);
                    btn.append(title, start);

                    btn.addEventListener('click', () => {
                        this._seekTo(chapter.start);
                        this._closeSheets();
                    });

                    const li = document.createElement('li');
                    li.appendChild(btn);
                    list.appendChild(li);
                });

                this._renderTicks();
                this._renderPosition(this._position());
            }

            _renderTicks() {
                const duration = this._durationSec();
                this._each('.abl-ticks', (ticks) => {
                    ticks.replaceChildren();
                    if (!(duration > 0)) {
                        return;
                    }

                    for (const chapter of this._chapters) {
                        if (chapter.start <= 0) {
                            continue;
                        }

                        const tick = createElement('div', 'abl-tick', '');
                        tick.style.left = `${Math.min(chapter.start / duration, 1) * 100}%`;
                        ticks.appendChild(tick);
                    }
                });
            }

            _renderPosition(time) {
                if (!this._bar) {
                    return;
                }

                const duration = this._durationSec();
                this._each('.abl-seek', (seek) => {
                    seek.max = String(duration > 0 ? duration : 0);
                    if (!this._dragging) {
                        seek.value = String(time);
                    }
                });

                const percent = duration > 0 ? Math.floor((time / duration) * 100) : 0;
                this._each('.abl-elapsed', (el) => {
                    el.textContent = nav.formatTime(time);
                });
                this._each('.abl-percent', (el) => {
                    el.textContent = ` / ${percent}%`;
                });
                this._each('.abl-remaining', (el) => {
                    el.textContent = `-${nav.formatTime(duration - time)}`;
                });

                const index = nav.currentIndex(this._chapters, time);
                const chapter = this._chapters[index];
                this._each('.abl-chapter-title', (el) => {
                    el.textContent = chapter ? chapter.title : '';
                });

                // The full page leads with the chapter like Audiobookshelf, books without chapters show their title there
                this._page.querySelector('.abl-page-heading').textContent = chapter
                    ? chapter.title
                    : (this._item?.Album || this._item?.Name || '');

                const noNext = nav.nextTarget(this._chapters, time) == null;
                this._each('.abl-next', (btn) => {
                    btn.disabled = noNext;
                });

                if (index !== this._chapterIndex) {
                    this._chapterIndex = index;
                    this._each('.abl-chapter-item', (btn) => {
                        btn.classList.toggle('abl-current', Number(btn.dataset.index) === index);
                    });
                }
            }

            _renderPlayState() {
                const paused = this._audio.paused;
                this._each('.abl-play', (btn) => {
                    btn.title = paused ? 'Play' : 'Pause';
                    btn.querySelector('.material-icons').className = `material-icons ${paused ? 'play_arrow' : 'pause'}`;
                });
            }

            _renderSpeed() {
                const rate = this.getPlaybackRate();
                this._each('.abl-speed-label, .abl-speed-value', (el) => {
                    el.textContent = `${rate.toFixed(1)}x`;
                });
                this._each('.abl-speed-down', (btn) => {
                    btn.disabled = rate <= nav.MIN_SPEED;
                });
                this._each('.abl-speed-up', (btn) => {
                    btn.disabled = rate >= nav.MAX_SPEED;
                });
                this._each('.abl-preset', (btn) => {
                    btn.classList.toggle('abl-current', Number(btn.dataset.rate) === rate);
                });
            }

            _renderVolume() {
                const muted = this.isMuted();
                this._each('.abl-mute', (btn) => {
                    btn.title = muted ? 'Unmute' : 'Mute';
                    btn.querySelector('.material-icons').className = `material-icons ${muted ? 'volume_off' : 'volume_up'}`;
                });
                this._each('.abl-volume', (slider) => {
                    slider.value = String(this.getVolume());
                });
            }

            _renderCompact() {
                const compact = this._isCompact();
                this._bar.classList.toggle('abl-compact', compact);

                // The full page only exists in the compact layout, widening the window drops back to the bar
                if (!compact && this._pageOpen) {
                    this._pageOpen = false;
                    this._renderVisibility();
                }
            }

            _renderVisibility() {
                if (!this._bar) {
                    return;
                }

                const showBar = this._active && this._mediaControlAllowed;
                this._bar.classList.toggle('abl-hidden', !showBar);
                document.body.classList.toggle('abl-active', showBar);

                this._page.classList.toggle('abl-hidden', !(this._active && this._pageOpen));

                if (!this._active) {
                    this._closeSheets();
                }
            }

            // Helpers

            _each(selector, fn) {
                for (const root of this._roots) {
                    root.querySelectorAll(selector).forEach(fn);
                }
            }

            _isCompact() {
                return document.documentElement.classList.contains('layout-mobile')
                    || window.matchMedia(COMPACT_QUERY).matches;
            }

            _position() {
                return this._audio ? this._audio.currentTime : 0;
            }

            _durationSec() {
                const d = this._audio?.duration;
                if (Number.isFinite(d) && d > 0) {
                    return d;
                }

                // Before metadata loads the element has no duration, the item's runtime is close enough for the bar
                return (this._item?.RunTimeTicks || 0) / TICKS_PER_SECOND;
            }

            _seekTo(seconds) {
                if (!this._audio) {
                    return;
                }

                const target = nav.clamp(seconds, this._durationSec());
                this._audio.currentTime = target;

                // currentTime() prefers this copy, so update it now rather than waiting for the next timeupdate
                this._currentTime = target;
                this._renderPosition(target);
            }

            _openSheet(sheet) {
                this._closeSheets();

                // Over the full page or on a phone the popup spans the bottom, on a wide screen it sits above the bar's right side
                const fullWidth = this._pageOpen || this._isCompact();
                sheet.classList.toggle('abl-full-width', fullWidth);
                sheet.style.bottom = fullWidth ? '0' : `${this._bar.getBoundingClientRect().height + 8}px`;

                sheet.classList.remove('abl-hidden');
                this._scrim.classList.remove('abl-hidden');
            }

            _closeSheets() {
                this._speedSheet?.classList.add('abl-hidden');
                this._chapterSheet?.classList.add('abl-hidden');
                this._scrim?.classList.add('abl-hidden');
            }
        };
    };

    function createElement(tag, className, html) {
        const el = document.createElement(tag);
        el.className = className;
        if (html) {
            el.innerHTML = html;
        }

        return el;
    }

    // AudioBook files often carry their art on the album rather than the item itself
    function coverUrl(item) {
        const api = window.ApiClient;
        if (!api) {
            return null;
        }

        if (item.ImageTags?.Primary) {
            return api.getScaledImageUrl(item.Id, { type: 'Primary', maxHeight: 800, tag: item.ImageTags.Primary });
        }

        if (item.AlbumId && item.AlbumPrimaryImageTag) {
            return api.getScaledImageUrl(item.AlbumId, { type: 'Primary', maxHeight: 800, tag: item.AlbumPrimaryImageTag });
        }

        return null;
    }
})();
