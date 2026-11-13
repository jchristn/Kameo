namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Kameo.Core;
    using Touchstone.Core;

    internal static class LifecycleCases
    {
        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (string platform in new[] { "windows", "macos", "linux", "wsl", "wsl2" })
            {
                cases.Add(KameoSuites.Case("Lifecycle", "StartupRoundTrip" + platform, fixture =>
                {
                    string exe = Path.Combine(fixture.Root, "Kameo & app"); File.WriteAllText(exe, "test");
                    string bash = Path.Combine(fixture.Root, ".bashrc"); File.WriteAllText(bash, "# existing shell configuration\n");
                    StartupRegistration.Install(platform, exe, fixture.Root, fixture.Root);
                    StartupRegistration.Install(platform, exe, fixture.Root, fixture.Root);
                    string path = StartupRegistration.RegistrationPath(platform, fixture.Root);
                    Fixture.Check(File.Exists(path), "Startup entry missing.");
                    string content = File.ReadAllText(path);
                    if (platform == "macos") XDocument.Parse(content);
                    Fixture.Check(content.Contains("KAMEO_HOME", StringComparison.Ordinal), "Startup must preserve the store location.");
                    StartupRegistration.Remove(platform, fixture.Root);
                    StartupRegistration.Remove(platform, fixture.Root);
                    Fixture.Check(!File.Exists(path), "Startup entry was not removed.");
                    Fixture.Check(File.ReadAllText(bash).Contains("# existing shell configuration", StringComparison.Ordinal), "Existing shell settings were damaged.");
                    Fixture.Check(!File.ReadAllText(bash).Contains("BEGIN KAMEO", StringComparison.Ordinal), "Managed block was not removed.");
                }));
            }
            cases.Add(KameoSuites.Case("Lifecycle", "StartupMissingBuildRejected", fixture => Fixture.Throws<FileNotFoundException>(() => StartupRegistration.Install("macos", Path.Combine(fixture.Root, "missing"), fixture.Root, fixture.Root))));
            cases.Add(KameoSuites.Case("Lifecycle", "UnsupportedStartupPlatform", fixture => Fixture.Throws<ArgumentException>(() => StartupRegistration.RegistrationPath("unsupported", fixture.Root))));
            cases.Add(KameoSuites.Case("Lifecycle", "DesktopSingleInstance", fixture =>
            {
                Fixture.Check(!DesktopControl.IsRunning(fixture.Store), "Desktop unexpectedly running.");
                using (FileStream? lease = DesktopControl.TryAcquire(fixture.Store))
                {
                    Fixture.Check(lease != null, "Could not acquire initial lease.");
                    Fixture.Check(DesktopControl.IsRunning(fixture.Store), "Second instance was not detected.");
                    Fixture.Check(DesktopControl.TryAcquire(fixture.Store) == null, "Two desktop instances acquired the lease.");
                }
                Fixture.Check(!DesktopControl.IsRunning(fixture.Store), "Lease not released.");
            }));
            cases.Add(KameoSuites.Case("Lifecycle", "StopWhenNotRunning", async (fixture, cancellationToken) => await DesktopControl.StopAsync(fixture.Store, cancellationToken).ConfigureAwait(false)));
            cases.Add(KameoSuites.Case("Lifecycle", "GracefulStopHandshake", async (fixture, cancellationToken) =>
            {
                FileStream? lease = DesktopControl.TryAcquire(fixture.Store);
                try
                {
                    Task stop = DesktopControl.StopAsync(fixture.Store, cancellationToken);
                    Fixture.Check(File.Exists(Path.Combine(fixture.Root, "stop.request")), "Stop request was not written.");
                    lease?.Dispose(); lease = null;
                    await stop.ConfigureAwait(false);
                }
                finally { lease?.Dispose(); }
            }));
            return new TestSuiteDescriptor("Lifecycle", "Startup scripts and desktop coordination", cases);
        }
    }
}
