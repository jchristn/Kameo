namespace Test.Desktop
{
    using System;

    /// <summary>A settable clock for usage-cache tests.</summary>
    internal sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
