/*
 * Copyright (c) 2021-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
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

using System.Diagnostics;
using System.Runtime.InteropServices;

using NUnit.Framework;

namespace ISO15118ConformanceTests.Simulation.Interop
{

    /// <summary>
    /// Runs <c>tools/cbv2g-defect-probe/</c>, which builds against EVerest's libcbv2g and exercises the
    /// three defects drafted in <c>docs/reports/libcbv2g-grammar-deviations.md</c>.
    ///
    /// <para>
    /// <see cref="ExplicitAttribute">[Explicit]</see>, like every interop fixture, and for the usual
    /// reason: it needs a C toolchain and — on a cold machine — the network, and the offline run must
    /// stay green without either.
    /// </para>
    ///
    /// <para>
    /// <b>This test asserts that somebody else's bugs are still there, which makes it the one fixture
    /// here that is meant to stop passing.</b> Against the pinned commit it is a regression guard on our
    /// own report: if it fails, either the report's claims were wrong or the probe drifted, and both are
    /// worth knowing before the report is posted. Against <c>master</c> —
    /// </para>
    /// <code>
    ///   CBV2G_PROBE_REF=main dotnet test --filter TestCategory=Interop
    /// </code>
    /// <para>
    /// — a failure is the good news: it means one of the three has been fixed upstream, and the filing
    /// can be closed. That is the question worth asking periodically, and the reason this is wired up at
    /// all rather than left as a script somebody remembers to run. (<c>main</c> is libcbv2g's default
    /// branch, and as of 2026-08-08 it is still the pinned commit.)
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [Explicit("Builds libcbv2g from source (see tools/cbv2g-defect-probe/README.md); never part of the offline run.")]
    public class CbV2GDefectProbeTests
    {

        [Test]
        public void TheReportedLibCbV2GDefectsStillReproduce()
        {

            var script = Path.Combine(TestContext.CurrentContext.TestDirectory,
                                      "..", "..", "..", "..",
                                      "tools", "cbv2g-defect-probe", "build.sh");
            script = Path.GetFullPath(script);

            if (!File.Exists(script))
                Assert.Ignore($"probe script not found at {script}");

            var (exe, prefix) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                    ? ("wsl.exe", "-- bash ")     // the probe is POSIX; WSL is how this box runs it
                                    : ("bash", "");

            var path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ToWslPath(script) : script;

            var start = new ProcessStartInfo(exe, prefix + "\"" + path + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };

            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Exception e)
            {
                Assert.Ignore($"cannot run the probe ({exe}): {e.Message}");
                return;
            }

            Assert.That(process, Is.Not.Null);

            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(milliseconds: 10 * 60 * 1000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("the probe did not finish within ten minutes");
            }

            TestContext.Out.WriteLine(stdout);
            if (stderr.Length > 0)
                TestContext.Out.WriteLine(stderr);

            // A missing compiler or no network on a cold machine is not a finding about libcbv2g.
            if (stderr.Contains("command not found") || stderr.Contains("could not resolve host") ||
                stdout.Length == 0)
                Assert.Ignore("the probe could not build — needs a C compiler, and git/network on a cold machine");

            var reference = Environment.GetEnvironmentVariable("CBV2G_PROBE_REF");

            Assert.That(process.ExitCode, Is.Zero,
                reference is null or ""
                    ? "the probe contradicted docs/reports/libcbv2g-grammar-deviations.md at the commit it " +
                      "cites. Either a claim in the report is wrong or the probe has drifted — read the " +
                      "output above before posting the report."
                    : $"against '{reference}' the probe no longer reproduces every defect. That is most " +
                       "likely good news: check which one was fixed upstream and close that part of the " +
                       "filing. Read the output above.");

        }


        /// <summary>`D:\repo\x` to `/mnt/d/repo/x`, so the Windows-side test can hand a path to WSL.</summary>
        private static string ToWslPath(string windowsPath)
        {
            var full = Path.GetFullPath(windowsPath);
            if (full.Length > 1 && full[1] == ':')
                return "/mnt/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
            return full.Replace('\\', '/');
        }

    }

}
