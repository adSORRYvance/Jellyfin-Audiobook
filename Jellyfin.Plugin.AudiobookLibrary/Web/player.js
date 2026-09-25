// Audiobook Library player for jellyfin-web, loaded as a window plugin through config.json.
// playbackManager hands us anything whose Type is AudioBook, and music and video keep the stock players.
(function () {
    'use strict';

    const TICKS_PER_SECOND = 10000000;

    window.AudiobookLibraryPlayer = async function () {
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
                this._overlay = null;
                this._audio = null;
                this._log = null;
                this._currentSrc = null;
                this._currentTime = null;
                this._hiddenAt = null;
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
                this._ensureOverlay();
                this._overlay.querySelector('.abl-title').textContent = options.item?.Name || 'Audiobook';
                this._logLine('play requested');

                const audio = this._audio;
                this._currentSrc = options.url;
                this._currentTime = null;
                audio.src = options.url;

                const startTicks = options.playerStartPositionTicks || 0;
                if (startTicks > 0) {
                    audio.addEventListener('loadedmetadata', () => {
                        audio.currentTime = startTicks / TICKS_PER_SECOND;
                        this._logLine(`resumed at ${formatTime(audio.currentTime)}`);
                    }, { once: true });
                }

                // The WebView can refuse autoplay once the tap is a few awaits behind us, the audio controls still work then
                return audio.play().catch((err) => {
                    this._logLine(`autoplay refused: ${err?.name || err}`);
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

                if (destroyPlayer) {
                    this.destroy();
                }

                return Promise.resolve();
            }

            destroy() {
                document.removeEventListener('visibilitychange', this._onVisibility);
                this._overlay?.remove();
                this._overlay = null;
                this._audio = null;
                this._log = null;
            }

            currentSrc() {
                return this._currentSrc;
            }

            currentTime(val) {
                if (!this._audio) {
                    return 0;
                }

                if (val != null) {
                    this._audio.currentTime = val / 1000;
                    return;
                }

                // Prefer the last timeupdate, the element itself can read 0 once the WebView or an ended event resets it
                if (this._currentTime) {
                    return this._currentTime * 1000;
                }

                return this._audio.currentTime * 1000;
            }

            duration() {
                const d = this._audio?.duration;
                return Number.isFinite(d) ? d * 1000 : null;
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
                    this._audio.playbackRate = value;
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

            _ensureOverlay() {
                if (this._overlay) {
                    return;
                }

                const overlay = document.createElement('div');
                overlay.className = 'abl-overlay';
                overlay.style.cssText = 'position:fixed;inset:0;z-index:10000;background:#101010;color:#eee;'
                    + 'display:flex;flex-direction:column;gap:12px;padding:16px;font-family:inherit;overflow:auto;';
                overlay.innerHTML = '<div style="font-size:0.9em;opacity:0.7">Audiobook Library says hello</div>'
                    + '<h2 class="abl-title" style="margin:0"></h2>'
                    + '<audio class="abl-audio" controls preload="metadata" style="width:100%"></audio>'
                    + '<button class="abl-close" type="button" style="align-self:flex-start;padding:8px 16px">Close</button>'
                    + '<pre class="abl-log" style="margin:0;font-size:0.8em;white-space:pre-wrap;opacity:0.8"></pre>';
                document.body.appendChild(overlay);

                this._overlay = overlay;
                this._audio = overlay.querySelector('.abl-audio');
                this._log = overlay.querySelector('.abl-log');

                overlay.querySelector('.abl-close').addEventListener('click', () => {
                    this._playbackManager.stop(this);
                });

                this._bindAudioEvents(this._audio);

                // Tells us whether the WebView keeps playing with the screen off or the app in the background
                this._onVisibility = () => {
                    const t = this._audio ? this._audio.currentTime : 0;
                    if (document.hidden) {
                        this._hiddenAt = t;
                        this._logLine(`page hidden at ${formatTime(t)}`);
                    } else {
                        const moved = this._hiddenAt == null ? 0 : t - this._hiddenAt;
                        this._logLine(`page visible at ${formatTime(t)}, moved ${moved.toFixed(1)}s while hidden`);
                    }
                };
                document.addEventListener('visibilitychange', this._onVisibility);
            }

            _bindAudioEvents(audio) {
                audio.addEventListener('playing', () => {
                    this._logLine('playing');
                    this._events.trigger(this, 'playing');
                    this._events.trigger(this, 'unpause');
                });
                audio.addEventListener('pause', () => {
                    this._logLine(`paused at ${formatTime(audio.currentTime)}`);
                    this._events.trigger(this, 'pause');
                });
                audio.addEventListener('timeupdate', () => {
                    this._currentTime = audio.currentTime;
                    this._events.trigger(this, 'timeupdate');
                });
                audio.addEventListener('seeked', () => {
                    this._logLine(`seeked to ${formatTime(audio.currentTime)}`);
                });
                audio.addEventListener('ratechange', () => {
                    this._logLine(`speed ${audio.playbackRate}x`);
                });
                audio.addEventListener('ended', () => {
                    this._logLine('ended');
                    this.stop(false);
                });
                audio.addEventListener('error', () => {
                    this._logLine(`error: code ${audio.error?.code}`);
                });
            }

            _logLine(text) {
                if (this._log) {
                    this._log.textContent += `${new Date().toLocaleTimeString()}  ${text}\n`;
                }
            }
        };
    };

    function formatTime(seconds) {
        const s = Math.floor(seconds || 0);
        const h = Math.floor(s / 3600);
        const m = Math.floor((s % 3600) / 60);
        const sec = s % 60;
        return `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
    }
})();
