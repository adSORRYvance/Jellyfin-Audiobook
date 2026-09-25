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

test('chaptersForTrack keeps only the file being played, in file seconds', () => {
    const book = {
        Tracks: [
            { ItemId: '3c1290a36e5dbda4552220eeb40aa0fb', StartSec: 0, DurationSec: 100 },
            { ItemId: '88bdc683e146e2f0583e2a81652f7fb4', StartSec: 100, DurationSec: 50 }
        ],
        Chapters: [
            { Title: 'Chapter 1', StartSec: 0, EndSec: 40, TrackIndex: 0, TrackOffsetSec: 0 },
            { Title: 'Chapter 2', StartSec: 40, EndSec: 100, TrackIndex: 0, TrackOffsetSec: 40 },
            { Title: 'Chapter 1', StartSec: 100, EndSec: 120, TrackIndex: 1, TrackOffsetSec: 0 },
            { Title: 'Chapter 2', StartSec: 120, EndSec: 150, TrackIndex: 1, TrackOffsetSec: 20 }
        ]
    };

    assert.deepEqual(nav.chaptersForTrack(book, '88BDC683-E146-E2F0-583E-2A81652F7FB4'), [
        { title: 'Chapter 1', start: 0, end: 20 },
        { title: 'Chapter 2', start: 20, end: 50 }
    ]);
});

test('chaptersForTrack with an unknown item or bad response is empty', () => {
    assert.deepEqual(nav.chaptersForTrack({ Tracks: [], Chapters: [] }, 'abc'), []);
    assert.deepEqual(nav.chaptersForTrack(null, 'abc'), []);
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
