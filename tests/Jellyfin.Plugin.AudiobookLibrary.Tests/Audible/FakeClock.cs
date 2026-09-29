namespace Jellyfin.Plugin.AudiobookLibrary.Tests.Audible;

// A clock the tests move by hand, so a 30-day cache can be tested without waiting 30 days
internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
