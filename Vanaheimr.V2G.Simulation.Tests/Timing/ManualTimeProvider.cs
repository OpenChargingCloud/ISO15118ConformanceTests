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

namespace Vanaheimr.V2G.Simulation.Tests.Timing
{
    /// <summary>
    /// A <see cref="TimeProvider"/> whose clock only moves when the test calls <see cref="Advance"/> —
    /// for asserting sequence-timeout behaviour without a real wall-clock wait. Deliberately does not
    /// override <c>CreateTimer</c>: nothing in this project schedules a fired callback, every timeout
    /// check is a pull-based "has too much time passed since I last saw you" comparison on the next
    /// incoming message, so the base class's (non-functional, in a fake) timer plumbing is never exercised.
    /// </summary>
    public sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset? start = null) => _utcNow = start ?? DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by) => _utcNow += by;
    }
}
