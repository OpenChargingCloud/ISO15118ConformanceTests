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
    /// The per-message deadline the loopback tests give an EVCC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 15118's own figure is 2 s, and that is what <c>MessageTimeoutOptions.Default</c> carries
    /// for the production path. These tests used it too, and it made them flaky: the EVCC runs on
    /// <see cref="TimeProvider.System"/>, so the deadline is wall-clock even though
    /// <see cref="ImmediateAsyncDelay"/> removes every artificial wait. Under load a loopback round
    /// trip simply takes longer than the protocol allows, and the session aborts.
    /// </para>
    /// <para>
    /// Measured, not guessed: <c>FullStackLoopbackTests.FixedDiscovery_DrivesIso20DcLoopbackSession</c>
    /// — the heaviest of them, SLAC over UDP plus a TLS handshake plus SAP plus a full DC session —
    /// overran in six of six runs once the machine was busy, at 2140, 2511, 2754, 2765, 2769 and
    /// 3564 ms. The smallest of those misses the deadline by 7 %, which is the shape of a budget set
    /// too close rather than of a hang.
    /// </para>
    /// <para>
    /// 10 s is roughly three times the worst measurement. What these tests assert is protocol
    /// behaviour — that the exchange completes and the state machines agree — never latency, so a
    /// generous deadline costs them nothing. A test that genuinely wants to prove timeout handling
    /// drives a <c>ManualTimeProvider</c> instead and never waits at all; see <c>TimeoutTests</c>.
    /// </para>
    /// </remarks>
    public static class LoopbackTimeouts
    {
        public static readonly TimeSpan PerMessage = TimeSpan.FromSeconds(10);
    }
}
