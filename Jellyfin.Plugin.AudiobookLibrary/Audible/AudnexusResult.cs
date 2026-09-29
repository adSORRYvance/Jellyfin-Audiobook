using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.AudiobookLibrary.Audible;

/// <summary>
/// Either the data we asked for or the reason we don't have it.
/// Expected failures come back as values so the admin page can show them without catching exceptions.
/// </summary>
/// <typeparam name="T">The kind of data asked for.</typeparam>
public sealed class AudnexusResult<T>
    where T : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudnexusResult{T}"/> class for a lookup that worked.
    /// </summary>
    /// <param name="value">The data.</param>
    /// <param name="fetchedAt">When the data came from Audnexus.</param>
    /// <param name="isOld">True when Audnexus failed and this is an older saved copy.</param>
    public AudnexusResult(T value, DateTimeOffset fetchedAt, bool isOld)
    {
        Value = value;
        FetchedAt = fetchedAt;
        IsOld = isOld;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudnexusResult{T}"/> class for a lookup that failed.
    /// </summary>
    /// <param name="error">Why it failed.</param>
    /// <param name="message">A message a person can read.</param>
    /// <param name="retryAt">When it's worth asking again, for rate limits.</param>
    public AudnexusResult(AudnexusError error, string message, DateTimeOffset? retryAt = null)
    {
        Error = error;
        Message = message;
        RetryAt = retryAt;
    }

    /// <summary>
    /// Gets the data, or null when the lookup failed.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets why the lookup failed, or <see cref="AudnexusError.None"/>.
    /// </summary>
    public AudnexusError Error { get; }

    /// <summary>
    /// Gets a message a person can read when the lookup failed.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets when it's worth asking again after a rate limit.
    /// </summary>
    public DateTimeOffset? RetryAt { get; }

    /// <summary>
    /// Gets when the data came from Audnexus.
    /// </summary>
    public DateTimeOffset? FetchedAt { get; }

    /// <summary>
    /// Gets a value indicating whether Audnexus failed and this is an older saved copy.
    /// </summary>
    public bool IsOld { get; }

    /// <summary>
    /// Gets a value indicating whether the lookup worked.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => Value is not null;
}
