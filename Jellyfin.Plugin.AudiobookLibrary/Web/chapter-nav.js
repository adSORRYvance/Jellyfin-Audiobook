// Chapter and position maths for the Audiobook Library player, kept free of the DOM so node --test can check it.
// The browser gets it as window.AudiobookLibraryChapterNav and the tests load it with require.
(function (root) {
    'use strict';

    // A seek can land a hair before the chapter start we asked for, this keeps us from counting that as the previous chapter
    const BOUNDARY_TOLERANCE = 0.5;

    // Previous goes to the start of the current chapter unless we're this close to it already
    const RESTART_WINDOW = 5;

    const MIN_SPEED = 0.5;
    const MAX_SPEED = 3;

    // A saved position this close to the end means the book was finished, so it opens at the start again
    const FINISHED_WINDOW = 5;

    function normalizeId(id) {
        return String(id || '').replace(/-/g, '').toLowerCase();
    }

    // The server sends ids without dashes and jellyfin-web sometimes has them with, so compare them bare
    function sameId(a, b) {
        return normalizeId(a) === normalizeId(b);
    }

    function trackIndex(tracks, itemId) {
        return (tracks || []).findIndex((t) => sameId(t.ItemId, itemId));
    }

    // The whole book's chapters in book seconds, which is what the bar and the buttons work in
    function bookChapters(book) {
        return (book?.Chapters || []).map((c) => ({ title: c.Title, start: c.StartSec, end: c.EndSec }));
    }

    // Finds the file that holds a book position and how far into that file it is
    // A position right on a boundary belongs to the file that starts there, so the end of a file never plays twice
    function locate(tracks, bookSec) {
        if (!tracks?.length) {
            return null;
        }

        const last = tracks.length - 1;
        const sec = clamp(bookSec, tracks[last].StartSec + tracks[last].DurationSec);

        // Searching from the end also skips a zero-length file that shares its start with the next one
        for (let i = last; i > 0; i--) {
            if (sec >= tracks[i].StartSec) {
                return { index: i, offset: Math.min(sec - tracks[i].StartSec, tracks[i].DurationSec) };
            }
        }

        return { index: 0, offset: Math.min(sec, tracks[0].DurationSec) };
    }

    function bookTime(tracks, index, offset) {
        const track = tracks?.[index];
        return track ? track.StartSec + offset : offset;
    }

    // Where a book opens from its saved position, null when nothing was saved
    function resumeTarget(savedSec, duration) {
        if (!Number.isFinite(savedSec) || savedSec <= 0) {
            return null;
        }

        // Without a length we can't tell a finished book, and starting over would lose the place
        if (!(duration > 0)) {
            return savedSec;
        }

        return savedSec >= duration - FINISHED_WINDOW ? 0 : savedSec;
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
        sameId,
        trackIndex,
        bookChapters,
        locate,
        bookTime,
        resumeTarget,
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
