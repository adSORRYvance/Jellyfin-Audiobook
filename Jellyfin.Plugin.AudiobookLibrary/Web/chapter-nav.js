// Chapter maths for the Audiobook Library player, kept free of the DOM so node --test can check it.
// The browser gets it as window.AudiobookLibraryChapterNav and the tests load it with require.
(function (root) {
    'use strict';

    // A seek can land a hair before the chapter start we asked for, this keeps us from counting that as the previous chapter
    const BOUNDARY_TOLERANCE = 0.5;

    // Previous goes to the start of the current chapter unless we're this close to it already
    const RESTART_WINDOW = 5;

    const MIN_SPEED = 0.5;
    const MAX_SPEED = 3;

    function normalizeId(id) {
        return String(id || '').replace(/-/g, '').toLowerCase();
    }

    // Picks the chapters of one file out of the whole-book response, in seconds from the start of that file
    function chaptersForTrack(book, itemId) {
        const tracks = book?.Tracks || [];
        const index = tracks.findIndex((t) => normalizeId(t.ItemId) === normalizeId(itemId));
        if (index < 0) {
            return [];
        }

        const track = tracks[index];
        return (book.Chapters || [])
            .filter((c) => c.TrackIndex === index)
            .map((c) => ({
                title: c.Title,
                start: c.TrackOffsetSec,
                end: Math.min(c.EndSec - track.StartSec, track.DurationSec)
            }));
    }

    function currentIndex(chapters, time) {
        let found = -1;
        for (let i = 0; i < chapters.length; i++) {
            if (chapters[i].start <= time + BOUNDARY_TOLERANCE) {
                found = i;
            } else {
                break;
            }
        }

        return found;
    }

    function previousTarget(chapters, time) {
        const index = currentIndex(chapters, time);
        if (index < 0) {
            return 0;
        }

        if (time - chapters[index].start > RESTART_WINDOW) {
            return chapters[index].start;
        }

        return index === 0 ? 0 : chapters[index - 1].start;
    }

    // Null means there is no next chapter, so the button should be disabled
    function nextTarget(chapters, time) {
        const next = chapters[currentIndex(chapters, time) + 1];
        return next ? next.start : null;
    }

    function clamp(time, duration) {
        const max = Number.isFinite(duration) && duration > 0 ? duration : Infinity;
        return Math.min(Math.max(time, 0), max);
    }

    // Rounded to one decimal, otherwise 1.1 plus 0.1 becomes 1.2000000000000002
    function clampSpeed(rate) {
        const rounded = Math.round(rate * 10) / 10;
        return Math.min(Math.max(rounded, MIN_SPEED), MAX_SPEED);
    }

    function formatTime(seconds) {
        const s = Math.floor(Math.max(seconds || 0, 0));
        const h = Math.floor(s / 3600);
        const m = Math.floor((s % 3600) / 60);
        const sec = String(s % 60).padStart(2, '0');
        return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${sec}` : `${m}:${sec}`;
    }

    const api = {
        RESTART_WINDOW,
        MIN_SPEED,
        MAX_SPEED,
        chaptersForTrack,
        currentIndex,
        previousTarget,
        nextTarget,
        clamp,
        clampSpeed,
        formatTime
    };

    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    } else {
        root.AudiobookLibraryChapterNav = api;
    }
})(typeof window !== 'undefined' ? window : globalThis);
