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
using System.Net.NetworkInformation;
using System.Net.Sockets;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.Transport;

namespace ISO15118ConformanceTests.Simulation.Transport;

/// <summary>
/// Endpoint parsing, and the one thing it exists to stop: a link-local address losing its zone.
/// </summary>
/// <remarks>
/// Nothing here needs a network. The tests that involve an interface ask the machine which interfaces it
/// has rather than naming one, so they run the same on a developer's Mac, in a container, and on a CI
/// box with nothing but a loopback.
/// </remarks>
[TestFixture]
public class V2GEndpointTests
{

    /// <summary>An interface this machine actually has, with its IPv6 index — or nothing, on a machine
    /// with no IPv6 at all, in which case the tests that need one are skipped rather than made up.</summary>
    private static (String Name, Int64 Index)? AnInterface()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                return (nic.Name, nic.GetIPProperties().GetIPv6Properties().Index);
            }
            catch (NetworkInformationException)
            { }
        }
        return null;
    }

    /// <summary>A name no machine has. Long and specific, because the test would otherwise pass for the
    /// wrong reason on the day somebody names a veth pair "test".</summary>
    private const String NoSuchInterface = "no-such-interface-6f3a1c";


    // ── the ordinary forms ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void ANameAndAPortAreTakenAsWritten()
    {
        var endpoint = V2GEndpoint.Parse("station.local:15118", "--connect");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Host,        Is.EqualTo("station.local"));
            Assert.That(endpoint.Port,        Is.EqualTo(15118));
            Assert.That(endpoint.Zone,        Is.Null);
            Assert.That(endpoint.Address,     Is.Null, "a name is resolved by the socket layer, not here");
            Assert.That(endpoint.ConnectHost, Is.EqualTo("station.local"));
        });
    }

    [Test]
    public void AnIPv4LiteralParsesToAnAddress()
    {
        var endpoint = V2GEndpoint.Parse("192.0.2.7:15118", "--connect");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Parse("192.0.2.7")));
            Assert.That(endpoint.Port,    Is.EqualTo(15118));
            Assert.That(endpoint.ToString(), Is.EqualTo("192.0.2.7:15118"));
        });
    }

    [Test]
    public void ARoutableIPv6LiteralNeedsNoZone()
    {
        var endpoint = V2GEndpoint.Parse("[2001:db8::1]:15118", "--connect");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Address!.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
            Assert.That(endpoint.Address!.ScopeId,       Is.EqualTo(0));
            Assert.That(endpoint.ToString(),             Is.EqualTo("[2001:db8::1]:15118"));
        });
    }

    [Test]
    public void ANumericZoneIsKept()
    {
        var endpoint = V2GEndpoint.Parse("[fe80::1%14]:64109", "--connect");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Zone,             Is.EqualTo("14"));
            Assert.That(endpoint.Address!.ScopeId, Is.EqualTo(14));
            // The form handed on to the socket layer carries the zone as a number, which no parser can
            // lose — see TheFrameworkSilentlyDropsAZoneItCannotResolve for what happens to names.
            Assert.That(endpoint.ConnectHost,      Is.EqualTo("fe80::1%14"));
        });
    }

    [Test]
    public void ANamedZoneResolvesToThatInterfacesIndex()
    {
        var nic = AnInterface();
        if (nic is null)
        {
            Assert.Ignore("this machine has no IPv6-capable interface to name");
            return;
        }

        var (name, index) = nic.Value;
        var endpoint = V2GEndpoint.Parse($"[fe80::1%{name}]:64109", "--connect");

        Assert.Multiple(() =>
        {
            Assert.That(endpoint.Zone,             Is.EqualTo(name), "the zone is kept as written, for messages");
            Assert.That(endpoint.Address!.ScopeId, Is.EqualTo(index));
            Assert.That(endpoint.ConnectHost,      Is.EqualTo($"fe80::1%{index}"));
        });
    }


    // ── the refusals ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The measurement this class is built on, pinned as a test.
    /// </summary>
    /// <remarks>
    /// If a future runtime starts refusing an unresolvable zone instead of discarding it, this test fails
    /// — and that is the signal that <see cref="V2GEndpoint"/>'s guard has become redundant. Without it,
    /// the guard is a claim about a platform nobody re-measures.
    /// </remarks>
    [Test]
    public void TheFrameworkSilentlyDropsAZoneItCannotResolve()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IPAddress.TryParse($"fe80::1%{NoSuchInterface}", out var address), Is.True,
                        "the platform accepts an unresolvable zone rather than refusing it");
            Assert.That(address!.ScopeId, Is.EqualTo(0),
                        "and keeps nothing of it: the address that comes out is not the address that went in");

            Assert.That(IPEndPoint.TryParse($"[fe80::1%{NoSuchInterface}]:64109", out var endpoint), Is.True);
            Assert.That(endpoint!.Address.ScopeId, Is.EqualTo(0));
        });
    }

    [Test]
    public void AZoneThisMachineDoesNotHaveIsRefusedAndSaysWhatItDoesHave()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => V2GEndpoint.Parse($"[fe80::1%{NoSuchInterface}]:64109", "V2G_INTEROP_SECC"));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain("V2G_INTEROP_SECC"), "the message names where the value came from");
            Assert.That(thrown!.Message, Does.Contain(NoSuchInterface));

            if (AnInterface() is { } nic)
                Assert.That(thrown!.Message, Does.Contain(nic.Name),
                            "a refusal that does not say what would have worked costs a round of guessing");
        });
    }

    [Test]
    public void ALinkLocalAddressWithNoZoneAtAllIsRefused()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => V2GEndpoint.Parse("[fe80::ac52:27ff:fef3:d0d7]:64109", "--connect"));

        // The suggestion is the whole point: this is the address a station's SDP response carries, and
        // "which interface" is the one thing the address itself cannot say.
        Assert.That(thrown!.Message, Does.Contain("%<interface>"));
    }

    [Test]
    public void AnUnbracketedIPv6LiteralIsRefusedRatherThanGuessedAt()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => V2GEndpoint.Parse("fe80::1:9000", "--connect"));

        // 'fe80::1:9000' is a valid address in its own right. Splitting at the last colon would connect
        // to fe80::1 on port 9000 and never mention that it made a choice.
        Assert.That(thrown!.Message, Does.Contain("[fe80::1]:9000"));
    }

    [Test]
    public void AZoneOnAnIPv4AddressIsRefused()
    {
        var thrown = Assert.Throws<ArgumentException>(() => V2GEndpoint.Parse("192.0.2.7%eth0:9000", "--connect"));
        Assert.That(thrown!.Message, Does.Contain("IPv4"));
    }

    [Test]
    public void AZoneOnANameIsRefused()
    {
        var thrown = Assert.Throws<ArgumentException>(() => V2GEndpoint.Parse("station.local%eth0:9000", "--connect"));
        Assert.That(thrown!.Message, Does.Contain("not an address"));
    }

    [TestCase("",                  TestName = "empty")]
    [TestCase("   ",               TestName = "blank")]
    [TestCase("station.local",     TestName = "no port")]
    [TestCase("station.local:",    TestName = "trailing colon")]
    [TestCase("station.local:http",TestName = "named port")]
    [TestCase("station.local:0",   TestName = "port zero")]
    [TestCase("station.local:65536", TestName = "port too large")]
    [TestCase(":9000",             TestName = "no host")]
    [TestCase("[fe80::1]",         TestName = "bracketed, no port")]
    [TestCase("[fe80::1]:",        TestName = "bracketed, empty port")]
    [TestCase("[fe80::1:9000",     TestName = "unclosed bracket")]
    [TestCase("[fe80::1%]:9000",   TestName = "empty zone")]
    public void MalformedValuesAreRefused(String value)
        => Assert.Throws<ArgumentException>(() => V2GEndpoint.Parse(value, "--connect"));

}
