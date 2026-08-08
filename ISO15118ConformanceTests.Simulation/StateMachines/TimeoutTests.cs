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

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118_2.Generated;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso2;
using ISO15118ConformanceTests.Simulation.Timing;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{
    /// <summary>
    /// Proves the SECC sequence timeout using <see cref="ManualTimeProvider"/> — no real waiting, the
    /// clock is advanced programmatically between two <c>Handle</c> calls.
    /// </summary>
    [TestFixture]
    public class TimeoutTests
    {
        private static V2G_Message Wrap(byte[] sid, BodyBaseType body) =>
            new(new MessageHeaderType(sid, Notification: null, Signature: null), new BodyType(body));

        [Test]
        public void SequenceTimeout_ThrowsWhenExceeded()
        {
            var clock = new ManualTimeProvider();
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), clock);
            var sid = new byte[8];

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            clock.Advance(TimeSpan.FromSeconds(61));

            Assert.That(() => secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null))),
                Throws.InstanceOf<SessionAborted>().With.Message.Contain("sequence timeout"));
        }

        [Test]
        public void SequenceTimeout_DoesNotThrowWhenWithinBudget()
        {
            var clock = new ManualTimeProvider();
            var secc = new Secc2(PowerMode.Ac, TimeSpan.FromSeconds(60), clock);
            var sid = new byte[8];

            secc.Handle(Wrap(sid, new SessionSetupReqType(new byte[] { 1 })));
            clock.Advance(TimeSpan.FromSeconds(30));

            Assert.That(() => secc.Handle(Wrap(sid, new ServiceDiscoveryReqType(null, null))), Throws.Nothing);
        }
    }
}
