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

using System.Security.Cryptography;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Metering;

namespace ISO15118ConformanceTests.Simulation.Metering;

/// <summary>
/// The simulated meter that signs its own readings into <c>SigMeterReading</c> /
/// <c>MeterSignature</c>.
/// </summary>
/// <remarks>
/// These pin a convention rather than a standard. ISO 15118 defines the field and not its content
/// (see <see cref="MeterSigningPayload"/>), so there is no reference implementation to check
/// against and no vector corpus can exist. What the tests can do — and what they are for — is make
/// every property the convention was designed to have explicit, so that changing the layout breaks
/// something loudly instead of silently invalidating every signature in the field.
/// </remarks>
[TestFixture]
public class SigningMeterTests
{
    private static readonly byte[] Session  = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] Session2 = [1, 2, 3, 4, 5, 6, 7, 9];

    private sealed class FixedClock(long unix) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(unix);
    }

    private static SigningMeter Meter(string id = "VAN*M1", ECDsa? key = null) =>
        new(id, key ?? ECDsa.Create(ECCurve.NamedCurves.nistP256), new FixedClock(1_700_000_000));

    /// <summary>
    /// The field holds 64 bytes and no more, which is exactly P-256 r‖s. A DER signature is 70-ish
    /// and would not fit — for once the usual DER trap fails loudly rather than quietly.
    /// </summary>
    [Test]
    public void ASignatureIsSixtyFourBytesAndFitsTheField()
    {
        var meter = Meter();
        var (wh, t) = meter.Read();

        Assert.That(meter.Sign(2, Session, wh, t), Has.Length.EqualTo(64));
    }

    [Test]
    public void AReadingVerifiesWithTheMeterPublicKey()
    {
        var meter = Meter();
        meter.Add(1234);
        var (wh, t) = meter.Read();
        var signature = meter.Sign(2, Session, wh, t);

        Assert.That(SigningMeter.Verify(meter.PublicKey, 2, Session, meter.MeterId, wh, t, signature),
                    Is.True);
    }

    /// <summary>
    /// A tampered reading must not verify — §6.3's "tampered MeterInfo (does the SECC notice?)",
    /// from the other side.
    /// </summary>
    [TestCase(1235UL, 1_700_000_000L, "VAN*M1", Description = "reading changed")]
    [TestCase(1234UL, 1_700_000_001L, "VAN*M1", Description = "timestamp changed")]
    [TestCase(1234UL, 1_700_000_000L, "VAN*M2", Description = "meter id changed")]
    public void TamperingWithAnyFieldBreaksIt(ulong reading, long timestamp, string meterId)
    {
        var meter = Meter();
        meter.Add(1234);
        var signature = meter.Sign(2, Session, 1234, 1_700_000_000);

        Assert.That(SigningMeter.Verify(meter.PublicKey, 2, Session, meterId, reading, timestamp, signature),
                    Is.False);
    }

    /// <summary>
    /// The session binding, and the reason it is in the payload at all: without it a signature is
    /// proof that the reading is genuine but not that it is <em>yours</em>, so one captured from
    /// another session could be replayed into this one.
    /// </summary>
    [Test]
    public void AReadingFromAnotherSessionDoesNotVerifyHere()
    {
        var meter = Meter();
        meter.Add(500);
        var (wh, t) = meter.Read();
        var signature = meter.Sign(2, Session, wh, t);

        Assert.That(SigningMeter.Verify(meter.PublicKey, 2, Session2, meter.MeterId, wh, t, signature),
                    Is.False);
    }

    /// <summary>The two protocols' payloads differ, so a -2 reading cannot be presented as a -20 one.</summary>
    [Test]
    public void AReadingDoesNotCrossBetweenProtocols()
    {
        var meter = Meter();
        var (wh, t) = meter.Read();
        var signature = meter.Sign(2, Session, wh, t);

        Assert.That(SigningMeter.Verify(meter.PublicKey, 20, Session, meter.MeterId, wh, t, signature),
                    Is.False);
    }

    /// <summary>
    /// Length-prefixing, stated as the collision it prevents. With plain concatenation, meter "A1"
    /// reading 23 and meter "A" reading 123 could produce the same octets — and one signature would
    /// then attest to two different facts.
    /// </summary>
    [Test]
    public void TwoDifferentReadingsCannotShareOctets()
    {
        var a = MeterSigningPayload.Build(2, Session, "A1", 23, 0);
        var b = MeterSigningPayload.Build(2, Session, "A", 123, 0);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    /// <summary>
    /// The domain separator, likewise: it stops a signature over the same numbers in some other
    /// context being presented as a meter reading.
    /// </summary>
    [Test]
    public void ThePayloadStartsWithItsDomainSeparator()
    {
        var payload = MeterSigningPayload.Build(2, Session, "M", 0, 0);

        Assert.That(payload[..12], Is.EqualTo("V2G-METER-1\0"u8.ToArray()));
    }

    /// <summary>
    /// An absent timestamp encodes as zero rather than being omitted, so the payload's length never
    /// depends on which optional fields are present — one fewer way for two readings to collide.
    /// </summary>
    [Test]
    public void AnAbsentTimestampDoesNotShortenThePayload()
    {
        Assert.That(MeterSigningPayload.Build(2, Session, "M", 7, null),
                    Is.EqualTo(MeterSigningPayload.Build(2, Session, "M", 7, 0)));
    }

    [Test]
    public void AnotherMetersKeyDoesNotVerify()
    {
        var meter = Meter();
        var (wh, t) = meter.Read();
        var signature = meter.Sign(2, Session, wh, t);

        Assert.That(SigningMeter.Verify(Meter().PublicKey, 2, Session, meter.MeterId, wh, t, signature),
                    Is.False);
    }

    /// <summary>
    /// A DER signature is refused on length before it reaches the crypto. It cannot occur through
    /// this API, but it is what a JCA- or OpenSSL-minded implementation on the other side would
    /// send, and "wrong shape" is a better answer than a verification failure.
    /// </summary>
    [Test]
    public void ADerShapedSignatureIsRefused()
    {
        var meter = Meter();
        var (wh, t) = meter.Read();

        Assert.That(SigningMeter.Verify(meter.PublicKey, 2, Session, meter.MeterId, wh, t,
                                        new byte[70]), Is.False);
    }

    /// <summary>
    /// A curve whose signatures do not fit the 64-byte field is refused at construction rather than
    /// producing readings nothing can carry.
    /// </summary>
    [Test]
    public void AKeyTooLargeForTheFieldIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            _ = new SigningMeter("M", ECDsa.Create(ECCurve.NamedCurves.nistP521),
                                 new FixedClock(0)));
    }

    [Test]
    public void TheMeterAccumulates()
    {
        var meter = Meter();
        meter.Add(100);
        meter.Add(50);

        Assert.That(meter.Read().Wh, Is.EqualTo(150));
    }
}
