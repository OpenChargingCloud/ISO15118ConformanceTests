/*
 * Copyright (c) 2021-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Diagnostics;

using NUnit.Framework;

namespace ISO15118ConformanceTests.Simulation.Timing
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
