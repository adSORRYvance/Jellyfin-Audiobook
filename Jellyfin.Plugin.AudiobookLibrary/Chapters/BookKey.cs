using System;

namespace Jellyfin.Plugin.AudiobookLibrary.Chapters;

/// <summary>
/// What we save per-book settings under, the same for every file of the book.
/// </summary>
/// <param name="Id">The book folder's item id, or the file's own id when it has no folder.</param>
/// <param name="Title">The book's title.</param>
public sealed record BookKey(Guid Id, string Title);
