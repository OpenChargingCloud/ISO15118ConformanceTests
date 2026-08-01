using System.Security.Authentication;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.StateMachines;
using Vanaheimr.V2G.Simulation.Transport;

namespace Vanaheimr.V2G.Simulation.Tests.Interop;

/// <summary>
/// How an interop fixture learns where the peer is: one vocabulary of environment variables, shared by
/// every counterparty, because the fixtures differ in who is on the other end and in nothing else.
/// </summary>
/// <remarks>
/// <list type="table">
///   <item><term><c>V2G_INTEROP_SECC</c></term><description><c>host:port</c> or <c>[ipv6%zone]:port</c> —
///         their station, for a run in which we are the car.</description></item>
///   <item><term><c>V2G_INTEROP_LISTEN</c></term><description>a port — our station, for a run in which
///         they are the car.</description></item>
///   <item><term><c>V2G_INTEROP_PROTOCOL</c></term><description><c>2</c> (default) or <c>20</c>.</description></item>
///   <item><term><c>V2G_INTEROP_MODE</c></term><description><c>ac</c> (default) or <c>dc</c>.</description></item>
///   <item><term><c>V2G_INTEROP_TLS</c></term><description><c>1</c> to run TLS, accepting any server
///         certificate. Development only.</description></item>
///   <item><term><c>V2G_INTEROP_RECORD</c></term><description>a directory for the artifacts — see
///         <see cref="InteropRecording"/>. Unset means a run that leaves nothing behind.</description></item>
/// </list>
/// </remarks>
internal static class InteropEnvironment
{

    /// <summary>
    /// Their station's endpoint, parsed and checked before anything opens a socket.
    /// </summary>
    /// <remarks>
    /// <see cref="V2GEndpoint"/> rather than a split at the last colon, because an ISO 15118 station is
    /// reached at a link-local address with a zone — <c>[fe80::ac52:27ff:fef3:d0d7%evcc-veth]:64109</c> is
    /// the form these simulators' own documentation uses — and a zone naming an interface this machine
    /// does not have is discarded by the platform without a word. The resulting connection failure looks
    /// exactly like "their station is not listening", which is the most expensive possible way to be told
    /// that the veth pair has not been created yet.
    /// </remarks>
    public static V2GEndpoint SeccEndpointOrIgnore(String hint)
    {

        var value = Environment.GetEnvironmentVariable("V2G_INTEROP_SECC");

        if (String.IsNullOrWhiteSpace(value))
            Assert.Ignore($"set V2G_INTEROP_SECC=host:port to run this — {hint}");

        return V2GEndpoint.Parse(value!, "V2G_INTEROP_SECC");

    }


    public static Int32 ListenPortOrIgnore(String hint)
    {

        var value = Environment.GetEnvironmentVariable("V2G_INTEROP_LISTEN");

        if (String.IsNullOrWhiteSpace(value))
            Assert.Ignore($"set V2G_INTEROP_LISTEN=port to run this — {hint}");

        return Int32.TryParse(value, out var port) && port is > 0 and <= 65535
                   ? port
                   : throw new ArgumentException($"V2G_INTEROP_LISTEN must be a TCP port, got '{value}'.");

    }


    public static (ProtocolVariant Protocol, PowerMode Mode) ProtocolAndMode()
        => (Environment.GetEnvironmentVariable("V2G_INTEROP_PROTOCOL") switch
            {
                "20" => ProtocolVariant.Iso15118_20,
                _    => ProtocolVariant.Iso15118_2,
            },
            Environment.GetEnvironmentVariable("V2G_INTEROP_MODE") == "dc"
                ? PowerMode.Dc
                : PowerMode.Ac);


    /// <summary>The names the trace corpus uses, so a recorded interop session is filed like any other.</summary>
    public static (String Protocol, String Mode) ProtocolAndModeNames()
    {
        var (protocol, mode) = ProtocolAndMode();
        return (protocol == ProtocolVariant.Iso15118_20 ? "iso15118-20" : "iso15118-2",
                mode     == PowerMode.Dc                ? "dc"          : "ac");
    }


    /// <summary>
    /// TLS for a probe against a third-party station whose version we do not control — hence permissive,
    /// and hence never a conformance path. Josev serves TLS 1.2 unilateral by default; the Rust simulators
    /// use GnuTLS with their own profile.
    /// </summary>
    public static TlsOptions? DevTlsOrNull()
        => Environment.GetEnvironmentVariable("V2G_INTEROP_TLS") == "1"
               ? new TlsOptions
                 {
                     ServerCertificateValidation = (_, _, _, _) => true,   // dev only
                     EnabledSslProtocols         = SslProtocols.Tls12 | SslProtocols.Tls13,
                 }
               : null;

}
