namespace Test.Shared
{
    using System;
    using System.IO;
    using Kameo.Core;

    internal class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "kameo-test-" + Guid.NewGuid().ToString("N"));
        internal ProfileStore Store { get; }
        private bool _Disposed;

        internal Fixture() => Store = new ProfileStore(Root);
        internal Profile Add(HarnessKind harness = HarnessKind.Codex, string name = "Work", CredentialKind kind = CredentialKind.ApiKey) => Store.Create(new Profile
        { Harness = harness, Name = name, Kind = kind, ApiKey = kind == CredentialKind.ApiKey ? "synthetic-test-key" : "" });

        /// <summary>Deletes the disposable Kameo home. Safe to call more than once.</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }

        /// <summary>Deletes the disposable Kameo home when called from <see cref="Dispose()"/>.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>; false from a finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed && disposing && Directory.Exists(Root)) Directory.Delete(Root, true);
            _Disposed = true;
        }

        internal static void Check(bool value, string message)
        { if (!value) throw new TestFailureException(message); }

        internal static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new TestFailureException("Expected exception: " + typeof(T).Name);
        }

        internal static async System.Threading.Tasks.Task ThrowsAsync<T>(Func<System.Threading.Tasks.Task> action) where T : Exception
        {
            try { await action().ConfigureAwait(false); }
            catch (T) { return; }
            throw new TestFailureException("Expected exception: " + typeof(T).Name);
        }

        internal static string Probe()
        {
            string? configured = Environment.GetEnvironmentVariable("KAMEO_TEST_PROBE");
            if (!string.IsNullOrEmpty(configured)) return Path.GetFullPath(configured);
            return FindBuiltExecutable("Test.Harness", "Test.Harness");
        }

        internal static string Cli()
        {
            string? configured = Environment.GetEnvironmentVariable("KAMEO_TEST_CLI");
            return string.IsNullOrEmpty(configured) ? FindBuiltExecutable("Kameo.Cli", "kameo") : Path.GetFullPath(configured);
        }

        private static string FindBuiltExecutable(string project, string name)
        {
            string executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                foreach (string configuration in new[] { "Release", "Debug" })
                {
                    string path = Path.Combine(directory.FullName, "src", project, "bin", configuration, "net10.0", executable);
                    if (File.Exists(path)) return path;
                }
                directory = directory.Parent;
            }
            throw new FileNotFoundException("Build " + project + " or configure its test executable path.");
        }
    }
}
