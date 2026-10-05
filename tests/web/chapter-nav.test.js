const test = require('node:test');
const assert = require('node:assert/strict');
const nav = require('../../Jellyfin.Plugin.AudiobookLibrary/Web/chapter-nav.js');

const chapters = [
    { title: 'Opening', start: 0, end: 37 },
    { title: 'Acknowledgments', start: 37, end: 604 },
    { title: 'Prologue', start: 604, end: 1862 }
];

test('previous restarts the current chapter when more than 5 s in', () => {
    assert.equal(nav.previousTarget(chapters, 604 + 5.1), 604);
});

test('previous goes to the previous chapter at exactly 5 s in', () => {
    assert.equal(nav.previousTarget(chapters, 604 + 5), 37);
});

test('previous goes to the previous chapter when less than 5 s in', () => {
    assert.equal(nav.previousTarget(chapters, 604 + 4.9), 37);
});

test('previous in chapter 1 goes to 0:00', () => {
    assert.equal(nav.previousTarget(chapters, 3), 0);
    assert.equal(nav.previousTarget(chapters, 30), 0);
});

test('previous before the first chapter starts goes to 0:00', () => {
    assert.equal(nav.previousTarget([{ title: 'Late', start: 20, end: 60 }], 10), 0);
});

test('previous with no chapters goes to 0:00', () => {
    assert.equal(nav.previousTarget([], 500), 0);
});

test('next goes to the start of the next chapter', () => {
    assert.equal(nav.nextTarget(chapters, 10), 37);
    assert.equal(nav.nextTarget(chapters, 37), 604);
});

test('next on the last chapter has no target', () => {
    assert.equal(nav.nextTarget(chapters, 700), null);
    assert.equal(nav.nextTarget([], 0), null);
});

test('a seek that lands just short of a chapter start still counts as that chapter', () => {
    assert.equal(nav.currentIndex(chapters, 603.8), 2);
    assert.equal(nav.nextTarget(chapters, 603.8), null);
});

test('current chapter at an exact boundary is the new chapter', () => {
    assert.equal(nav.currentIndex(chapters, 37), 1);
    assert.equal(nav.currentIndex(chapters, 36), 0);
});

test('current chapter before the first chapter is -1', () => {
    assert.equal(nav.currentIndex([{ title: 'Late', start: 20, end: 60 }], 5), -1);
});

// Four files laid end to end, track 3 is index 2 and ends at 350 s
const tracks = [
    { ItemId: '3c1290a36e5dbda4552220eeb40aa0fb', StartSec: 0, DurationSec: 100 },
    { ItemId: '88bdc683e146e2f0583e2a81652f7fb4', StartSec: 100, DurationSec: 50 },
    { ItemId: 'a1b2c3d4e5f60718293a4b5c6d7e8f90', StartSec: 150, DurationSec: 200 },
    { ItemId: '0f9e8d7c6b5a49382716051a2b3c4d5e', StartSec: 350, DurationSec: 300 }
];

test('bookChapters puts every chapter in book seconds', () => {
    const book = {
        Tracks: tracks.slice(0, 2),
        Chapters: [
            { Title: 'Chapter 1', StartSec: 0, EndSec: 40, TrackIndex: 0, TrackOffsetSec: 0 },
            { Title: 'Chapter 2', StartSec: 40, EndSec: 100, TrackIndex: 0, TrackOffsetSec: 40 },
            { Title: 'Chapter 3', StartSec: 100, EndSec: 150, TrackIndex: 1, TrackOffsetSec: 0 }
        ]
    };

    assert.deepEqual(nav.bookChapters(book), [
        { title: 'Chapter 1', start: 0, end: 40 },
        { title: 'Chapter 2', start: 40, end: 100 },
        { title: 'Chapter 3', start: 100, end: 150 }
    ]);
    assert.deepEqual(nav.bookChapters(null), []);
});

test('sameId ignores dashes and case', () => {
    assert.equal(nav.sameId('88BDC683-E146-E2F0-583E-2A81652F7FB4', '88bdc683e146e2f0583e2a81652f7fb4'), true);
    assert.equal(nav.sameId('88bdc683e146e2f0583e2a81652f7fb4', '3c1290a36e5dbda4552220eeb40aa0fb'), false);
});

test('trackIndex ignores dashes and case in item ids', () => {
    assert.equal(nav.trackIndex(tracks, '88BDC683-E146-E2F0-583E-2A81652F7FB4'), 1);
    assert.equal(nav.trackIndex(tracks, 'abc'), -1);
    assert.equal(nav.trackIndex(null, 'abc'), -1);
});

test('forward 10 s from 6 s before the end of track 3 lands 4 s into track 4', () => {
    const start = nav.bookTime(tracks, 2, 194);
    assert.deepEqual(nav.locate(tracks, start + 10), { index: 3, offset: 4 });
});

test('back 10 s from 4 s into track 4 lands 6 s before the end of track 3', () => {
    const start = nav.bookTime(tracks, 3, 4);
    assert.deepEqual(nav.locate(tracks, start - 10), { index: 2, offset: 194 });
});

test('a position on a boundary belongs to the file that starts there', () => {
    assert.deepEqual(nav.locate(tracks, 100), { index: 1, offset: 0 });
    assert.deepEqual(nav.locate(tracks, 99.9).index, 0);
});

test('locate keeps positions inside the book', () => {
    assert.deepEqual(nav.locate(tracks, -5), { index: 0, offset: 0 });
    assert.deepEqual(nav.locate(tracks, 0), { index: 0, offset: 0 });
    assert.deepEqual(nav.locate(tracks, 9999), { index: 3, offset: 300 });
});

test('locate skips a zero-length file', () => {
    const withEmpty = [
        { ItemId: 'a', StartSec: 0, DurationSec: 100 },
        { ItemId: 'b', StartSec: 100, DurationSec: 0 },
        { ItemId: 'c', StartSec: 100, DurationSec: 50 }
    ];
    assert.deepEqual(nav.locate(withEmpty, 100), { index: 2, offset: 0 });
});

test('locate with one file is just that file', () => {
    assert.deepEqual(nav.locate([{ ItemId: 'a', StartSec: 0, DurationSec: 3600 }], 1234), { index: 0, offset: 1234 });
    assert.equal(nav.locate([], 10), null);
});

test('bookTime and locate agree', () => {
    for (const sec of [0, 50, 100, 149, 150, 349.5, 350, 600]) {
        const at = nav.locate(tracks, sec);
        assert.equal(nav.bookTime(tracks, at.index, at.offset), sec);
    }
});

test('resume opens at the saved position', () => {
    assert.equal(nav.resumeTarget(4321, 80000), 4321);
});

test('resume of a finished book starts over', () => {
    assert.equal(nav.resumeTarget(80000 - 5, 80000), 0);
    assert.equal(nav.resumeTarget(80000, 80000), 0);
    assert.equal(nav.resumeTarget(80000 - 5.1, 80000), 80000 - 5.1);
});

test('resume without a saved position has no target', () => {
    assert.equal(nav.resumeTarget(null, 80000), null);
    assert.equal(nav.resumeTarget(0, 80000), null);
    assert.equal(nav.resumeTarget(NaN, 80000), null);
});

test('resume before the length is known keeps the saved position', () => {
    assert.equal(nav.resumeTarget(4321, 0), 4321);
});

test('clamp keeps skips inside the file', () => {
    assert.equal(nav.clamp(-4, 100), 0);
    assert.equal(nav.clamp(104, 100), 100);
    assert.equal(nav.clamp(50, 100), 50);
    assert.equal(nav.clamp(50, NaN), 50);
});

test('speed steps round to one decimal', () => {
    assert.equal(nav.clampSpeed(1.1 + 0.1), 1.2);
    assert.equal(nav.clampSpeed(0.7 + 0.1), 0.8);
});

test('speed stays between 0.5x and 3x', () => {
    assert.equal(nav.clampSpeed(0.4), 0.5);
    assert.equal(nav.clampSpeed(3.1), 3);
});

test('formatTime drops hours under an hour', () => {
    assert.equal(nav.formatTime(0), '0:00');
    assert.equal(nav.formatTime(65.9), '1:05');
    assert.equal(nav.formatTime(3600), '1:00:00');
    assert.equal(nav.formatTime(67619), '18:46:59');
    assert.equal(nav.formatTime(-3), '0:00');
});
