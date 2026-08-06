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

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// The reverse fixtures' SDP arm, checked without a link to advertise on.
/// </summary>
/// <remarks>
/// <para>
/// Only the two decisions taken <i>before</i> any socket exists are testable here, and they are the two
/// worth guarding. That the arm is <b>off</b> unless asked for is what keeps the offline suite offline:
/// <see cref="InteropSdp.AdvertiseOrNullAsync"/> is now on the path of four fixtures, and one that joined a
/// multicast group on import would take the whole run's "must pass without a network" with it. That an
/// <b>unknown interface is refused</b> is what keeps a live run readable, because the alternative failure
/// is silence — see <see cref="InteropSdp"/>.
/// </para>
/// <para>
/// The advertisement itself is not checked here and cannot honestly be: an SDP server that answers is a
/// property of a link, a peer and a multicast group, and the run notes under
/// <c>docs/interop-runs/</c> are where that is evidenced. The option mapping it advertises <i>with</i> has
/// its own offline guard in <c>Discovery/SeccSdpOptionsTests.cs</c>.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]   // the arm is configured by an environment variable, which is process-wide
public class InteropSdpTests
{

    private const String Variable = "V2G_INTEROP_SDP";

    private String? saved;

    [SetUp]
    public void RememberTheEnvironment()
        => saved = Environment.GetEnvironmentVariable(Variable);

    [TearDown]
    public void RestoreTheEnvironment()
        => Environment.SetEnvironmentVariable(Variable, saved);


    [Test]
    public async Task Unset_AdvertisesNothing()
    {

        Environment.SetEnvironmentVariable(Variable, null);

        Assert.That(InteropSdp.InterfaceName, Is.Null);
        Assert.That(await InteropSdp.AdvertiseOrNullAsync(55000, tls: false),
                    Is.Null,
                    "an unasked-for SDP arm must not open anything — this test is also the assertion that " +
                    "the offline suite stays offline");

    }


    /// <summary>Empty is unset, not "advertise on the interface named ''". A shell that exports the
    /// variable from an unset one — <c>V2G_INTEROP_SDP="$IFACE"</c> — produces exactly this.</summary>
    [Test]
    public async Task Empty_IsTheSameAsUnset()
    {

        Environment.SetEnvironmentVariable(Variable, "");

        Assert.That(InteropSdp.InterfaceName, Is.Null);
        Assert.That(await InteropSdp.AdvertiseOrNullAsync(55000, tls: false), Is.Null);

    }


    /// <summary>
    /// A name this machine does not have is refused, loudly, naming both what was asked for and what is
    /// on offer.
    /// </summary>
    /// <remarks>
    /// The whole point of the message. An SDP server started on the wrong link does not fail — it answers
    /// nobody, the fixture waits out its timeout, and the run reports that the peer never connected. The
    /// mistake behind that is almost always a name that is spelt differently on this host, which is
    /// recoverable in seconds if the alternatives are printed and expensive if they are not.
    /// </remarks>
    [Test]
    public void UnknownInterface_IsRefusedAndSaysWhatThisMachineHas()
    {

        Environment.SetEnvironmentVariable(Variable, "no-such-interface-42");

        var thrown = Assert.ThrowsAsync<ArgumentException>(
                         async () => await InteropSdp.AdvertiseOrNullAsync(55000, tls: false));

        Assert.That(thrown!.Message, Does.Contain("no-such-interface-42"));
        Assert.That(thrown.Message,  Does.Contain("V2G_INTEROP_SDP"),
                    "the message has to name the variable that caused it, not just the interface");

    }

}
