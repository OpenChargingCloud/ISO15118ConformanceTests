using System.Net;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.NetworkInterfaces;
using cloud.charging.open.protocols.ISO15118.SDP.Messages;

using Vanaheimr.V2G.Simulation.Cli;

namespace Vanaheimr.V2G.Simulation.Tests.Discovery
{
    /// <summary>
    /// Regression coverage for the CLI's SECC SDP-server option mapping (<see cref="Program.BuildSeccSdpOptions"/>).
    /// The key invariant: a <b>plaintext</b> SECC must NOT reject no-TLS SDP_Requests, otherwise <c>--sdp</c>
    /// discovery silently fails for a plaintext EVCC (this was the real cause behind the "SDP multicast" shim —
    /// the WWCP <c>SECC_SDPServerOptions.RejectNoTlsRequests</c> default is <c>true</c>).
    /// </summary>
    [TestFixture]
    public class SeccSdpOptionsTests
    {
        private static readonly V2GNetworkInterface Iface =
            new(2, "eth0", IPAddress.Parse("fe80::215:5dff:fe46:863f"), new byte[] { 0, 0x15, 0x5d, 0x46, 0x86, 0x3f });

        [Test]
        public void PlaintextSecc_OffersNoTls_AndAnswersNoTlsRequests()
        {
            var opt = Program.BuildSeccSdpOptions(Iface, 55000, noTls: true);

            Assert.Multiple(() =>
            {
                Assert.That(opt.OfferedSecurity, Is.EqualTo(SDP_Security.NoTLS));
                Assert.That(opt.RejectNoTlsRequests, Is.False,
                    "a plaintext SECC must answer plaintext SDP_Requests, or --sdp discovery silently fails");
                Assert.That(opt.SeccPort, Is.EqualTo(55000));
            });
        }

        [Test]
        public void TlsSecc_OffersTls_AndRejectsNoTlsDowngradeRequests()
        {
            var opt = Program.BuildSeccSdpOptions(Iface, 55000, noTls: false);

            Assert.Multiple(() =>
            {
                Assert.That(opt.OfferedSecurity, Is.EqualTo(SDP_Security.TLS));
                Assert.That(opt.RejectNoTlsRequests, Is.True, "a TLS SECC should not honour no-TLS downgrade");
            });
        }
    }
}
