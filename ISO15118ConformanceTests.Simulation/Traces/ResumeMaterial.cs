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

using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace ISO15118ConformanceTests.Simulation.Traces;

/// <summary>
/// The two leaf certificates a recorded ISO 15118-<b>20</b> resume is bound to — the one input the
/// corpus could not otherwise supply, because the recording speaks plain TCP.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a -20 session binding is.</b> A paused session may only be resumed by the peer that opened
/// it, and `-20` makes the check a <i>shall</i> while leaving the method open. The standard's own
/// worked example — which this stack implements — is
/// <c>SHA-512(SessionID ‖ SHA-512(peer leaf certificate))</c>, the certificate taken from the verified
/// TLS handshake. See <c>SessionBinding20</c> for why omitting the check is a hole rather than a
/// missed hardening: a second EV that names another's SessionID inherits that EV's authorization.
/// </para>
/// <para>
/// <b>Why these bytes exist at all.</b> The trace corpus records over a loopback socket with no TLS,
/// so there is no handshake to take a leaf from and <c>PeerLeafOf</c> returns null on both sides. Both
/// state machines therefore expose the leaf as a settable property "for callers that drive the machine
/// over something else", and this is that caller. Without them a recorded resume could only ever be a
/// <i>refused</i> one — which is worth recording too, and is, but is not the case the feature is about.
/// </para>
/// <para>
/// <b>Deliberately not real certificates.</b> They are labelled ASCII, and that is the honest choice
/// rather than a lazy one: a real X.509 leaf here would suggest the recording exercised TLS, which it
/// did not. The binding hashes opaque bytes, so what a port must reproduce is the hash — not a
/// certificate parse.
/// </para>
/// <para>
/// <b>Two leaves, because the binding is not symmetric.</b> The station binds the session to the
/// <i>vehicle's</i> leaf and the car binds it to the <i>station's</i>, so the same session has two
/// different binding values and each side checks the one it is owed. A port that computed only one
/// would pass whichever half it happened to implement.
/// </para>
/// </remarks>
internal static class ResumeMaterial
{

    private const string FileName = "Session.resume-material.json";

    private static string Path_ =>
        System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "Vectors", FileName);

    private static JsonElement Material =>
        JsonDocument.Parse(File.ReadAllText(Path_)).RootElement;

    /// <summary>What the station sees of the car — the leaf the SECC binds the session to.</summary>
    public static byte[] VehicleLeaf => Read("vehicleLeafCertificate");

    /// <summary>What the car sees of the station — the leaf the EVCC binds the session to.</summary>
    public static byte[] SeccLeaf => Read("seccLeafCertificate");

    private static byte[] Read(string name) =>
        Convert.FromHexString(Material.GetProperty(name).GetString()!);


    private static string SourcePath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "ISO15118ConformanceTests.Simulation.csproj")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not find the test project directory");
        var vectors = System.IO.Path.Combine(dir!.FullName, "..", "libs", "EVSimulatorApp", "vectors");
        Directory.CreateDirectory(vectors);
        return System.IO.Path.Combine(vectors, FileName);
    }

    /// <summary>
    /// Writes the file. Unlike its siblings there is nothing random to preserve — the bytes are a
    /// constant — so this is idempotent by construction rather than by care. It is still
    /// <see cref="ExplicitAttribute"/> where it is called from: changing these bytes changes every
    /// binding computed from them, and therefore invalidates both recorded resumes.
    /// </summary>
    public static string Regenerate()
    {

        var json = JsonSerializer.Serialize(new
        {
            note = "Stand-in TLS leaf certificates for the recorded ISO 15118-20 pause/resume sessions. "
                 + "A -20 session binding is SHA-512(SessionID || SHA-512(peer leaf)), and the leaf comes "
                 + "from the TLS handshake — which a loopback recording does not have, so both state "
                 + "machines take it as a property instead. These are labelled ASCII rather than real "
                 + "certificates on purpose: a real X.509 leaf would imply the recording exercised TLS. "
                 + "TWO of them, because the binding is not symmetric — the station binds to the "
                 + "vehicle's leaf and the car to the station's, so one session has two binding values "
                 + "and each side checks the one it is owed. Read by the C#, Kotlin and Swift trace "
                 + "suites alike. Changing these invalidates Session.iso20-dc-eim-resume.trace.json.",
            vehicleLeafCertificate = Hex("vehicle-leaf-certificate-for-the-recorded-resume"),
            seccLeafCertificate    = Hex("station-leaf-certificate-for-the-recorded-resume"),
        }, new JsonSerializerOptions { WriteIndented = true });

        var path = SourcePath();
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;

    }

    private static string Hex(string ascii) =>
        Convert.ToHexString(Encoding.ASCII.GetBytes(ascii)).ToLowerInvariant();

}
