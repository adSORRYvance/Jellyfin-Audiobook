// Audiobook Library player for jellyfin-web, loaded as a window plugin through config.json.
// playbackManager hands us anything whose Type is AudioBook, and music and video keep the stock players.
// playbackManager also reports progress to the server for us, so resume and Continue Listening need nothing extra here.
(function () {
    'use strict';

    const TICKS_PER_SECOND = 10000000;
    const SKIP_SECONDS = 10;
    const SPEED_STEP = 0.1;
    const SPEED_PRESETS = [0.5, 1, 1.2, 1.5, 2];

    // Everything in here is fixed markup, titles and names go in later through textContent
    const TEMPLATE = `
        <div class="abl-top">
            <button type="button" class="abl-icon abl-minimize" title="Minimize"><span class="material-icons expand_more" aria-hidden="true"></span></button>
        </div>
        <div class="abl-body">
            <img class="abl-cover abl-hidden" alt="">
            <div class="abl-book"></div>
            <div class="abl-chapter"></div>
            <div class="abl-progress">
                <div class="abl-ticks"></div>
                <input class="abl-seek" type="range" min="0" max="0" step="0.1" value="0" aria-label="Position">
            </div>
            <div class="abl-times">
                <span class="abl-chapter-time"></span>
                <span class="abl-book-time"></span>
            </div>
            <div class="abl-controls">
                <button type="button" class="abl-icon abl-prev" title="Previous chapter"><span class="material-icons skip_previous" aria-hidden="true"></span></button>
                <button type="button" class="abl-icon abl-back" title="Back 10 seconds"><span class="material-icons replay_10" aria-hidden="true"></span></button>
                <button type="button" class="abl-icon abl-play" title="Play"><span class="material-icons play_arrow" aria-hidden="true"></span></button>
                <button type="button" class="abl-icon abl-forward" title="Forward 10 seconds"><span class="material-icons forward_10" aria-hidden="true"></span></button>
                <button type="button" class="abl-icon abl-next" title="Next chapter"><span class="material-icons skip_next" aria-hidden="true"></span></button>
            </div>
            <div class="abl-extras">
                <button type="button" class="abl-text-btn abl-speed-open"><span class="material-icons speed" aria-hidden="true"></span><span class="abl-speed-label">1x</span></button>
                <button type="button" class="abl-text-btn abl-chapters-open"><span class="material-icons format_list_bulleted" aria-hidden="true"></span><span>Chapters</span></button>
            </div>
        </div>
        <div class="abl-sheet abl-speed-sheet abl-hidden">
            <div class="abl-sheet-head"><span>Speed</span><button type="button" class="abl-icon abl-sheet-close" title="Close"><span class="material-icons close" aria-hidden="true"></span></button></div>
            <div class="abl-speed-row">
                <button type="button" class="abl-icon abl-speed-down" title="Slower"><span class="material-icons remove" aria-hidden="true"></span></button>
                <span class="abl-speed-value"></span>
                <button type="button" class="abl-icon abl-speed-up" title="Faster"><span class="material-icons add" aria-hidden="true"></span></button>
            </div>
            <div class="abl-presets"></div>
        </div>
        <div class="abl-sheet abl-chapter-sheet abl-hidden">
            <div class="abl-sheet-head"><span>Chapters</span><button type="button" class="abl-icon abl-sheet-close" title="Close"><span class="material-icons close" aria-hidden="true"></span></button></div>
            <ol class="abl-chapter-list"></ol>
        </div>`;

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
                this._view = null;
                this._audio = null;
                this._item = null;
                this._chapters = [];
                this._chapterIndex = -2;
                this._currentSrc = null;
                this._currentTime = null;
                this._dragging = false;
                this._loadToken = 0;
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
                this._renderItem(item);
                this._renderChapters();
                this._closeSheets();
                this._show();

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
                this._hide();

                if (destroyPlayer) {
                    this.destroy();
                }

                return Promise.resolve();
            }

            destroy() {
                document.removeEventListener('click', this._onDocumentClick, true);
                this._view?.remove();
                this._view = null;
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
                    this._audio.volume = val / 100;
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
                if (this._view) {
                    return;
                }

                const view = document.createElement('div');
                view.className = 'abl-player abl-hidden';
                view.innerHTML = TEMPLATE;

                const audio = document.createElement('audio');
                audio.preload = 'metadata';
                view.appendChild(audio);
                document.body.appendChild(view);

                this._view = view;
                this._audio = audio;
                this._el = (selector) => view.querySelector(selector);

                this._bindControls();
                this._bindAudioEvents(audio);
                this._buildPresets();
                this._renderSpeed();

                // The now-playing bar normally opens the stock queue page, while we're playing it reopens our view instead
                // If jellyfin-web renames the bar this just stops matching and the stock page still controls us
                this._onDocumentClick = (e) => {
                    if (!this._view?.classList.contains('abl-hidden') || !this._currentSrc) {
                        return;
                    }

                    const bar = e.target.closest?.('.nowPlayingBar');
                    if (!bar || e.target.closest('button, input')) {
                        return;
                    }

                    if (this._playbackManager.getCurrentPlayer?.() !== this) {
                        return;
                    }

                    e.preventDefault();
                    e.stopPropagation();
                    this._show();
                };
                document.addEventListener('click', this._onDocumentClick, true);
            }

            _bindControls() {
                const on = (selector, handler) => this._el(selector).addEventListener('click', handler);

                on('.abl-minimize', () => this._hide());
                on('.abl-play', () => (this._audio.paused ? this.unpause() : this.pause()));
                on('.abl-back', () => this._seekTo(this._position() - SKIP_SECONDS));
                on('.abl-forward', () => this._seekTo(this._position() + SKIP_SECONDS));
                on('.abl-prev', () => this._seekTo(nav.previousTarget(this._chapters, this._position())));
                on('.abl-next', () => {
                    const target = nav.nextTarget(this._chapters, this._position());
                    if (target != null) {
                        this._seekTo(target);
                    }
                });
                on('.abl-speed-open', () => this._openSheet('.abl-speed-sheet'));
                on('.abl-chapters-open', () => this._openSheet('.abl-chapter-sheet'));
                on('.abl-speed-down', () => this.setPlaybackRate(this.getPlaybackRate() - SPEED_STEP));
                on('.abl-speed-up', () => this.setPlaybackRate(this.getPlaybackRate() + SPEED_STEP));

                this._view.querySelectorAll('.abl-sheet-close').forEach((btn) => {
                    btn.addEventListener('click', () => this._closeSheets());
                });

                // While the thumb is held we only preview the time, the seek happens once on release
                const seek = this._el('.abl-seek');
                seek.addEventListener('input', () => {
                    this._dragging = true;
                    this._renderPosition(Number(seek.value));
                });
                seek.addEventListener('change', () => {
                    this._dragging = false;
                    this._seekTo(Number(seek.value));
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
                audio.addEventListener('ended', () => this.stop(false));
                audio.addEventListener('error', () => {
                    console.warn('Audiobook Library: audio error', audio.error?.code);
                });
            }

            _buildPresets() {
                const presets = this._el('.abl-presets');
                for (const rate of SPEED_PRESETS) {
                    const btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'abl-text-btn abl-preset';
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
                this._el('.abl-book').textContent = item.Name || 'Audiobook';

                const cover = this._el('.abl-cover');
                const url = coverUrl(item);
                cover.classList.toggle('abl-hidden', !url);
                if (url) {
                    cover.src = url;
                } else {
                    cover.removeAttribute('src');
                }
            }

            _renderChapters() {
                const hasChapters = this._chapters.length > 0;
                this._el('.abl-prev').classList.toggle('abl-hidden', !hasChapters);
                this._el('.abl-next').classList.toggle('abl-hidden', !hasChapters);
                this._el('.abl-chapters-open').classList.toggle('abl-hidden', !hasChapters);
                this._el('.abl-chapter-time').classList.toggle('abl-hidden', !hasChapters);

                const list = this._el('.abl-chapter-list');
                list.replaceChildren();
                this._chapters.forEach((chapter, index) => {
                    const btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'abl-chapter-item';
                    btn.dataset.index = String(index);

                    const title = document.createElement('span');
                    title.textContent = chapter.title;
                    const start = document.createElement('span');
                    start.className = 'abl-chapter-start';
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
                const ticks = this._el('.abl-ticks');
                ticks.replaceChildren();

                const duration = this._durationSec();
                if (!(duration > 0)) {
                    return;
                }

                for (const chapter of this._chapters) {
                    if (chapter.start <= 0) {
                        continue;
                    }

                    const tick = document.createElement('div');
                    tick.className = 'abl-tick';
                    tick.style.left = `${Math.min(chapter.start / duration, 1) * 100}%`;
                    ticks.appendChild(tick);
                }
            }

            _renderPosition(time) {
                if (!this._view) {
                    return;
                }

                const duration = this._durationSec();
                const seek = this._el('.abl-seek');
                seek.max = String(duration > 0 ? duration : 0);
                if (!this._dragging) {
                    seek.value = String(time);
                }

                this._el('.abl-book-time').textContent = `${nav.formatTime(time)} / ${nav.formatTime(duration)}`;

                const index = nav.currentIndex(this._chapters, time);
                const chapter = this._chapters[index];
                this._el('.abl-chapter').textContent = chapter ? chapter.title : '';
                this._el('.abl-chapter-time').textContent = chapter
                    ? `${nav.formatTime(time - chapter.start)} / ${nav.formatTime(chapter.end - chapter.start)}`
                    : '';

                this._el('.abl-next').disabled = nav.nextTarget(this._chapters, time) == null;

                if (index !== this._chapterIndex) {
                    this._chapterIndex = index;
                    this._view.querySelectorAll('.abl-chapter-item').forEach((btn) => {
                        btn.classList.toggle('abl-current', Number(btn.dataset.index) === index);
                    });
                }
            }

            _renderPlayState() {
                const paused = this._audio.paused;
                const btn = this._el('.abl-play');
                btn.title = paused ? 'Play' : 'Pause';
                btn.querySelector('.material-icons').className = `material-icons ${paused ? 'play_arrow' : 'pause'}`;
            }

            _renderSpeed() {
                const rate = this.getPlaybackRate();
                this._el('.abl-speed-label').textContent = `${rate}x`;
                this._el('.abl-speed-value').textContent = `${rate.toFixed(1)}x`;
                this._el('.abl-speed-down').disabled = rate <= nav.MIN_SPEED;
                this._el('.abl-speed-up').disabled = rate >= nav.MAX_SPEED;
                this._view.querySelectorAll('.abl-preset').forEach((btn) => {
                    btn.classList.toggle('abl-current', Number(btn.dataset.rate) === rate);
                });
            }

            // Helpers

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

            _openSheet(selector) {
                this._closeSheets();
                this._el(selector).classList.remove('abl-hidden');
            }

            _closeSheets() {
                this._view?.querySelectorAll('.abl-sheet').forEach((sheet) => sheet.classList.add('abl-hidden'));
            }

            _show() {
                this._view?.classList.remove('abl-hidden');
            }

            _hide() {
                this._closeSheets();
                this._view?.classList.add('abl-hidden');
            }
        };
    };

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
