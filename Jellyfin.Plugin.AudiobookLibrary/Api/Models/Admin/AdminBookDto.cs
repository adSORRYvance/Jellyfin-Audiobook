using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AudiobookLibrary.Api.Models.Admin;

/// <summary>
/// A book as the admin page lists it, every audiobook file in one folder.
/// </summary>
/// <param name="FolderId">The book folder's item, or the file's own id when it has no folder.</param>
/// <param name="Title">The album tag, or the folder name.</param>
/// <param name="Author">The first album artist or artist.</param>
/// <param name="Files">The book's files in name order.</param>
public sealed record AdminBookDto(Guid FolderId, string Title, string? Author, IReadOnlyList<AdminFileDto> Files);
