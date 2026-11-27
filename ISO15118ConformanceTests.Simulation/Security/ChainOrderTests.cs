/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ISO15118ConformanceTests
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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using cloud.charging.open.protocols.ISO15118.SharedCC;

namespace ISO15118ConformanceTests.Simulation.Security
{

    /// <summary>
    /// <c>SubCertificates</c> leave in chain order, whatever order the PKCS#12 bag returned them in.
    /// </summary>
    /// <remarks>
    /// EVerest 2026.10.0 checks each sub-CA's <c>pathLenConstraint</c> against its position (Table B.9 of
    /// -20: the leaf's issuer has 0). A P-521 contract chain exported with Sub-CA2 before Sub-CA1 reached
    /// the station as Sub-CA1, Sub-CA2, and was refused with <c>WARNING_CertificateValidationError</c>
    /// (<c>docs/interop-runs/2026-11-26-everest-2026.10.0-forward/</c>).
    /// </remarks>
    [TestFixture]
    public class ChainOrderTests
    {

        private static X509Certificate2 Issue(string subject, ECDsa key, X509Certificate2? issuer, ECDsa? issuerKey, bool ca)
        {
            var req = new CertificateRequest(subject, key, HashAlgorithmName.SHA512);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
            var from = DateTimeOffset.UtcNow.AddMinutes(-5);
            var to   = DateTimeOffset.UtcNow.AddHours(1);
            if (issuer is null)
                return req.CreateSelfSigned(from, to);
            var serial = RandomNumberGenerator.GetBytes(8);
            return req.Create(issuer.SubjectName, X509SignatureGenerator.CreateForECDsa(issuerKey!), from, to, serial);
        }

        [Test]
        public void TheLeafsIssuerComesFirst_AndStrangersLast()
        {
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var sub1Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var sub2Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            using var root  = Issue("CN=Root",     rootKey,  null, null,    ca: true);
            using var sub1  = Issue("CN=Sub-CA1",  sub1Key,  root, rootKey, ca: true);
            using var sub2  = Issue("CN=Sub-CA2",  sub2Key,  sub1, sub1Key, ca: true);
            using var leaf  = Issue("CN=Contract", leafKey,  sub2, sub2Key, ca: false);
            using var other = Issue("CN=Stranger", otherKey, null, null,    ca: true);

            var ordered = Credentials.OrderFromLeaf(leaf, [other, sub1, sub2]);

            Assert.That(ordered.Select(c => c.Subject), Is.EqualTo(new[] { "CN=Sub-CA2", "CN=Sub-CA1", "CN=Stranger" }));
        }

        /// <summary>A bag that also carries the root ends the walk there; it does not loop on it.</summary>
        [Test]
        public void ASelfSignedRootStopsTheWalk()
        {
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var root = Issue("CN=Root",     rootKey, null, null,    ca: true);
            using var leaf = Issue("CN=Contract", leafKey, root, rootKey, ca: false);

            Assert.That(Credentials.OrderFromLeaf(leaf, [root]).Select(c => c.Subject), Is.EqualTo(new[] { "CN=Root" }));
        }

    }

}
