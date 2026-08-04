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
using System.Security.Cryptography.X509Certificates;

namespace Vanaheimr.V2G.Simulation.Tests.TestData
{
    /// <summary>
    /// A throwaway self-signed TLS server certificate generated at test-time — nothing checked into git,
    /// nothing to ever expire/rotate. Test-only; production use would supply a real certificate.
    /// </summary>
    public static class TestCertificate
    {
        public static X509Certificate2 CreateSelfSigned(string subjectCn = "localhost")
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var req = new CertificateRequest($"CN={subjectCn}", ecdsa, HashAlgorithmName.SHA256);
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            req.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") /* serverAuth */ }, critical: false));

            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

            // Re-import so the returned certificate carries an exportable private key (Windows/Schannel
            // needs this for SslStream server auth; CreateSelfSigned's result alone isn't always enough).
            return X509CertificateLoader.LoadPkcs12(
                cert.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
        }
    }
}
