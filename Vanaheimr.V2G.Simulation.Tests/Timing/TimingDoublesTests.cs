using System.Diagnostics;

using NUnit.Framework;

namespace Vanaheimr.V2G.Simulation.Tests.Timing
{
    /// <summary>Sanity checks for the test-only timing doubles themselves, before anything else depends on them.</summary>
    [TestFixture]
    public class TimingDoublesTests
    {
        [Test]
        public void ManualTimeProvider_OnlyMovesOnAdvance()
        {
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var clock = new ManualTimeProvider(start);

            Assert.That(clock.GetUtcNow(), Is.EqualTo(start));
            clock.Advance(TimeSpan.FromSeconds(90));
            Assert.That(clock.GetUtcNow(), Is.EqualTo(start + TimeSpan.FromSeconds(90)));
        }

        [Test]
        public async Task ImmediateAsyncDelay_ReturnsWithoutWaiting()
        {
            var delay = new ImmediateAsyncDelay();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < 100; i++)
                await delay.Wait(TimeSpan.FromSeconds(1));

            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), "100 iterations of a 1s delay must not actually wait 100s.");
        }
    }
}
