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

using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.NetworkInterfaces;
using cloud.charging.open.protocols.ISO15118.SDP.Server;

using cloud.charging.open.protocols.ISO15118.SECC;
using cloud.charging.open.protocols.ISO15118.Discovery;

namespace ISO15118ConformanceTests.Simulation.Interop;

/// <summary>
/// How the <i>peer</i> learns where <b>we</b> are: an SDP server beside a reverse fixture's TCP listener,
/// so a counterparty's EV that discovers stations by multicast can find one that is also recording.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reverse direction has a discovery asymmetry the forward one does not.</b> A forward run is told
/// where to go — <c>V2G_INTEROP_SECC=host:port</c> — because every counterparty's station listens on a
/// socket somebody can name. Their <i>cars</i> mostly do not accept an endpoint at all: EVerest's
/// <c>PyEvJosev</c> takes a <c>device</c> and finds a station by SDP on it, and so do Josev's and
/// eVDriveFlow's EVCCs. Against those, <c>V2G_INTEROP_LISTEN</c> alone describes a station nobody can find.
/// </para>
/// <para>
/// <b>That gap cost a run its evidence.</b> The 2026-08-06 MCS reverse session — their EV picking service
/// 8 out of our <c>Secc20Mcs</c> catalogue, and verifying a -20 contract signature on the way, the first
/// time either had happened inbound — had to be driven by the CLI, because the CLI was the only side of
/// this repository that could advertise. It came back as two console logs:
/// <c>docs/interop-runs/2026-08-06-everest-mcs-reverse/</c> closes by saying it left no frame log and no
/// trace, and that making the fixture SDP-capable was the obvious next piece of harness work. The
/// recording lives in <see cref="InteropRecording"/>, the advertising lived in the station program, and no
/// run could have both. This is that piece: the fixture now advertises, so a discovered session is a
/// recorded one.
/// </para>
/// <para>
/// <b>Opt-in, by interface name, never guessed.</b> <c>V2G_INTEROP_SDP=eth0</c>. SDP is per-interface —
/// the server joins <c>ff02::1</c> on exactly one link — and an interface picked for the operator would
/// fail by <i>silence</i>: their EV multicasts onto a link nothing is listening to, our listener waits out
/// its timeout, and the run reports that no car ever connected. That is indistinguishable from a peer that
/// never started, which makes it the most expensive possible way to be wrong. Unset, nothing changes and
/// the fixture waits on its socket as before — right for a peer that is pointed at an endpoint (tux-evse's
/// injector) or reached through a relay.
/// </para>
/// </remarks>
internal static class InteropSdp
{

    /// <summary>The interface to advertise on, or <c>null</c> when nobody asked for SDP.</summary>
    public static String? InterfaceName
        => Environment.GetEnvironmentVariable("V2G_INTEROP_SDP") is { Length: > 0 } name ? name : null;


    /// <summary>
    /// Starts advertising a station on <paramref name="tcpPort"/>, or returns <c>null</c> when
    /// <c>V2G_INTEROP_SDP</c> is unset.
    /// </summary>
    /// <param name="tcpPort">The port the listener <b>actually bound</b> — read back from the socket, not
    /// from the environment. An SDP response is a promise about where to connect, and the one way to be
    /// sure it is kept is to never let the advertised port and the accepting port come from two different
    /// places.</param>
    /// <param name="tls">Whether that listener authenticates as a TLS server. It decides both what the
    /// response offers <i>and</i> whether a plaintext EV's request is answered at all, so it is passed
    /// explicitly at every call site rather than defaulted: a station advertising TLS while listening in
    /// plaintext is discovered and then fails its handshake, which reads as a peer defect.</param>
    /// <returns>The running advertiser — dispose it with the fixture — or <c>null</c>.</returns>
    public static async Task<SeccSdpAdvertiser?> AdvertiseOrNullAsync(Int32 tcpPort, Boolean tls,
                                                                      CancellationToken ct = default)
    {

        if (InterfaceName is not { } name)
            return null;

        var iface = new SystemV2GNetworkInterfaceProvider().FindByName(name)
                        ?? throw new ArgumentException(
                               $"V2G_INTEROP_SDP='{name}': no V2G-capable interface of that name. An " +
                               $"interface qualifies by having an IPv6 link-local address; this machine " +
                               $"offers {Candidates()}.");

        // The CLI's mapping, not a copy of it. The subtlety worth not re-deriving is
        // SECC_SDPServerOptions.RejectNoTlsRequests, whose TLS-deployment default (true) makes a plaintext
        // station drop a plaintext EV's SDP_Request without a word — discovery that looks broken for a
        // reason nothing prints. Program.BuildSdpOptions gets that right and SeccSdpOptionsTests holds
        // it there, so the fixture and `secc --sdp` advertise identically by construction and a run through
        // one is comparable with a run through the other.
        var server = new SECC_SDPServer(Program.BuildSdpOptions(iface, tcpPort, noTls: !tls));

        // Whether their EV probed at all is the first question of every reverse run that times out, and
        // until now it could only be answered with tcpdump. Progress rather than Out: these arrive on the
        // server's receive loop, off the test's thread, and are worth seeing while the fixture is still
        // waiting rather than in the report afterwards.
        server.RequestReceived += e => Say(
            $"SDP: request from {e.RemoteEndpoint} — {e.Request.Version}, {e.Request.Security}, " +
            $"{e.Request.TransportProtocol}" +
            (e.Accepted ? "" : $" — DROPPED: {e.RejectReason ?? "no reason given"}"));

        server.ResponseSent     += e => Say(
            $"SDP: answered {e.RemoteEndpoint} with [{e.Response.SeccIPAddress}]:{e.Response.SeccPort} " +
            $"({e.Response.Security}, {e.Response.TransportProtocol}), {e.BytesSent} byte(s).");

        server.MalformedRequestReceived += e => Say($"SDP: malformed request from {e.RemoteEndpoint}.");

        var advertiser = new SeccSdpAdvertiser(server);
        await advertiser.StartAsync(ct);

        // iface.LinkLocalIPAddress already carries the scope id on Linux; re-derive it so the line shows
        // the scope exactly once — the same reason Program.StartSdpAsync does.
        var scoped = new IPAddress(iface.LinkLocalIPAddress.GetAddressBytes(), iface.Index);

        TestContext.Out.WriteLine($"SDP: advertising [{scoped}]:{tcpPort} " +
                                  $"({(tls ? "TLS" : "NoTLS")}) on {iface.Name} — their EV should discover " +
                                  $"this station rather than be pointed at it.");

        return advertiser;

    }


    /// <summary>What this machine does offer, for the error above. A name that is simply spelt differently
    /// on this host — <c>eth0</c> in WSL, <c>veth-evcc</c> on a namespace rig — is the likely mistake, and
    /// the fix is one line away once the list is in front of you.</summary>
    private static String Candidates()
    {
        var found = new SystemV2GNetworkInterfaceProvider().Discover();
        return found.Count == 0
                   ? "none at all"
                   : String.Join(", ", found.Select(i => $"'{i.Name}'"));
    }


    /// <summary>Diagnostics must not be able to end a live session. An interop run is not repeatable on
    /// demand, and a handler that throws on the server's receive loop would cost the run to save a log
    /// line.</summary>
    private static void Say(String line)
    {
        try
        {
            TestContext.Progress.WriteLine(line);
        }
        catch
        { }
    }

}
