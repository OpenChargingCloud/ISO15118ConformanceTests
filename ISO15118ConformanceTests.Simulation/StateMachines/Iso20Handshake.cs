/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of WWCP ISO/IEC 15118 <https://github.com/OpenChargingCloud/WWCP_ISO15118>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using cloud.charging.open.protocols.ISO15118_20.CommonMessages.Generated;
using cloud.charging.open.protocols.ISO15118.Session;
using cloud.charging.open.protocols.ISO15118.StateMachines.Iso20;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

namespace ISO15118ConformanceTests.Simulation.StateMachines
{

    /// <summary>
    /// The opening exchange of an ISO 15118-20 session, for the fixtures that drive
    /// <see cref="Secc20Base.Handle"/> directly rather than through <see cref="Iso20SessionDriver"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One line, and it exists because leaving it out was invisible for the whole life of this suite.
    /// <see cref="SessionContext.SessionId"/> starts as <b>eight zero bytes</b>, so a fixture that built
    /// every request from its own context sent the all-zero SessionID in every message after
    /// <c>SessionSetupReq</c> — the value ISO reserves for *"I have no session"*, and the one EVerest's
    /// `-2` station is filed for serving (<c>docs/reports/everest-evsev2g-session-id-zero.md</c>). Our own
    /// `-20` station served it just as happily, because until 2026-08-11 it had no <c>[V2G20-460]</c>
    /// check to fail. Adding that check turned 32 of these tests red at once, which is how the modelling
    /// error surfaced — the same way, and for the same reason, as the four `-2` harnesses that had been
    /// building headers with <c>new byte[8]</c>.
    /// </para>
    /// <para>
    /// The EV keeping its <em>own</em> context, rather than borrowing the station's, was always right: a
    /// fixture sharing the station's context could not notice a wrong echo. What was missing is the line
    /// that makes a separate context correct — the one <c>Evcc20Base</c> has always had.
    /// </para>
    /// </remarks>
    internal static class Iso20Handshake
    {

        /// <summary>
        /// Send <c>SessionSetupReq</c> to <paramref name="secc"/> and take up the SessionID it issues, so
        /// every later request built from <paramref name="ev"/> carries it — what a real car does.
        /// </summary>
        /// <returns>The station's answer, for the fixtures that assert on it.</returns>
        public static SessionSetupRes OpenSession(this SessionContext ev,
                                                  Secc20Base           secc,
                                                  string               evccId = "EVCC01")
        {

            var setup = (SessionSetupRes) secc.Handle(MessageSet.Iso20CommonMessages,
                                                      new SessionSetupReq(ev.ToCommonHeader(), evccId)).Response;

            // Only after the request has gone out: SessionSetupReq is the one message [V2G20-460] excepts,
            // and it is where the opening id (or a resume id) legitimately differs from the station's.
            ev.SessionId = setup.Header.SessionID;

            return setup;

        }

    }

}
