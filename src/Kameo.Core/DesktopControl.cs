namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Per-store desktop lifetime coordination without PID files or broad process-name termination.</summary>
    public static class DesktopControl
    {
        /// <summary>Acquires the desktop instance lease, or returns null if another instance owns it.</summary>
        public static FileStream? TryAcquire(ProfileStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            string path = Path.Combine(store.Root, "desktop.lock");
            PrivateFiles.CheckPath(path);
            try
            {
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                return new FileStream(path, options);
            }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 11 or 13 or 32 or 33 or 35) { return null; }
        }

        /// <summary>Reports whether the desktop holds this store's instance lease.</summary>
        public static bool IsRunning(ProfileStore store)
        {
            using FileStream? lease = TryAcquire(store);
            return lease == null;
        }

        /// <summary>Requests a graceful exit and waits up to 15 seconds. Harness processes are independent.</summary>
        public static async Task StopAsync(ProfileStore store, CancellationToken cancellationToken)
        {
            if (!IsRunning(store)) return;
            PrivateFiles.Write(Path.Combine(store.Root, "stop.request"), "stop");
            for (int attempt = 0; attempt < 60; attempt++)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                if (!IsRunning(store)) return;
            }
            throw new TimeoutException("Kameo did not stop within 15 seconds. Close it from its tray menu before updating.");
        }
    }
}
