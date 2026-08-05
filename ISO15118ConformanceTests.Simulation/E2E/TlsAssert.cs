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

using System.Net.Security;
using System.Security.Authentication;

using NUnit.Framework;

using Vanaheimr.V2G.Simulation.Transport;

namespace ISO15118ConformanceTests.Simulation.E2E
{
    /// <summary>
    /// Asserts that a TLS session really runs the protocol version its ISO 15118 profile mandates
    /// (docs/pki-model.md: -2 ↔ TLS 1.2, -20 ↔ TLS 1.3). Without this, a permissive protocol set or a
    /// platform that silently caps the version lets a test go green while exercising the wrong profile —
    /// measured on macOS, where requesting <c>Tls12|Tls13</c> completes the handshake on <b>1.2</b>.
    /// </summary>
    internal static class TlsAssert
    {
        /// <param name="stream">The stream returned by <c>TcpV2GClient</c>/<c>TcpV2GListener</c>.</param>
        /// <param name="expected">The version the protocol's TLS profile requires.</param>
        /// <param name="tls">The options the session was opened with, when the backend was chosen rather
        /// than left to the platform — see <see cref="OnTheExpectedBackend"/>.</param>
        internal static void NegotiatedVersion(Stream stream, SslProtocols expected, TlsOptions? tls = null)
        {
            if (stream is SslStream ssl)
            {
                Assert.That(ssl.SslProtocol, Is.EqualTo(expected),
                            $"TLS profile violation: expected {expected}, negotiated {ssl.SslProtocol} " +
                            $"(cipher suite {ssl.NegotiatedCipherSuite}).");
                return;
            }

            // Not an SslStream, so this is the BouncyCastle backend. That stack offers exactly
            // ProtocolVersion.TLSv13 on both ends (BcV2GTls.Tls13Only), so a completed handshake is TLS 1.3
            // by construction; there is no negotiated-version property to read. Assert the two things that
            // are checkable: that TLS 1.3 was what we wanted, and that this backend is the one this session
            // was supposed to land on.
            Assert.That(expected, Is.EqualTo(SslProtocols.Tls13),
                        $"Only the TLS 1.3 profile may run on the BouncyCastle backend, but {expected} was expected.");
            OnTheExpectedBackend(tls);
        }

        /// <summary>
        /// Guards the substitution itself: a session that ended up on the managed stack must have been sent
        /// there. With <paramref name="tls"/> that is <see cref="TlsPlatform.ResolveBackend"/>'s verdict —
        /// which honours an explicit <see cref="TlsOptions.Backend"/>, so a -20 session that asked for the
        /// managed backend is legitimate on Windows too. Without it, the old expectation stands: only a
        /// platform whose <c>SslStream</c> has no TLS 1.3 may silently divert.
        /// </summary>
        private static void OnTheExpectedBackend(TlsOptions? tls)
            => Assert.That(tls is null
                               ? !TlsPlatform.SslStreamSupportsTls13
                               : TlsPlatform.ResolveBackend(tls) == TlsBackend.BouncyCastle,
                           Is.True,
                           "The session ran on the BouncyCastle backend, but nothing asked it to and this " +
                           "platform's SslStream can do TLS 1.3 — the .NET backend should have been used.");

        /// <summary>
        /// Asserts the session negotiated one of its profile's cipher suites. Where the platform cannot pin
        /// suites per connection (Windows/Schannel) there is nothing to enforce — the unpinned suite is
        /// reported to the test log as a deviation instead of failing a run we cannot control.
        /// </summary>
        internal static void NegotiatedCipherSuite(Stream stream, IReadOnlyList<TlsCipherSuite> allowed)
        {
            if (stream is not SslStream ssl)
                return; // BouncyCastle backend: BcV2GTls.CipherSuites pins the -20 pair by construction.

            if (!TlsPlatform.SupportsCipherSuitePinning)
            {
                // Nothing was pinned here, so the suite is whatever the platform preferred. Report which of the
                // two that turned out to be — claiming a deviation outright would be wrong whenever Schannel's
                // own preference happens to land inside the profile.
                TestContext.Out.WriteLine(
                    allowed.Contains(ssl.NegotiatedCipherSuite)
                        ? $"TLS suite {ssl.NegotiatedCipherSuite} is profile-conformant, but was not enforced: " +
                          "CipherSuitesPolicy is unsupported on Windows/Schannel, so this is the platform's own " +
                          "preference happening to agree — see docs/pki-model.md."
                        : $"TLS profile deviation (not enforceable on this platform): negotiated " +
                          $"{ssl.NegotiatedCipherSuite}, profile allows {string.Join(", ", allowed)}. " +
                          "CipherSuitesPolicy is unsupported on Windows/Schannel — see docs/pki-model.md.");
                return;
            }

            Assert.That(allowed, Does.Contain(ssl.NegotiatedCipherSuite),
                        $"TLS profile violation: negotiated {ssl.NegotiatedCipherSuite}, " +
                        $"profile allows {string.Join(", ", allowed)}.");
        }

        /// <summary>
        /// Asserts the peer authenticated with a certificate (mutual TLS), across both backends.
        /// </summary>
        internal static void MutuallyAuthenticated(Stream stream, string because, TlsOptions? tls = null)
        {
            if (stream is SslStream ssl)
            {
                Assert.That(ssl.IsMutuallyAuthenticated, Is.True, because);
                return;
            }

            // BouncyCastle backend: there is no IsMutuallyAuthenticated to read, but the property holds by
            // construction. With RequireClientCertificate the server sends a CertificateRequest, and
            // BcV2GTls.ValidatePeer raises certificate_required for an empty client Certificate message and
            // bad_certificate when the validation callback rejects it — so an established session cannot have
            // skipped client authentication. Guard the substitution itself instead.
            OnTheExpectedBackend(tls);
        }
    }
}
