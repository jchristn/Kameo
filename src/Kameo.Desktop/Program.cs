namespace Kameo.Desktop
{
    using System;
    using System.IO;
    using Avalonia;
    using Kameo.Core;

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                ProfileStore store = new ProfileStore();
                using FileStream? instance = DesktopControl.TryAcquire(store);
                if (instance == null)
                {
                    PrivateFiles.Write(Path.Combine(store.Root, "show.request"), "show");
                    return 0;
                }
                if (File.Exists(Path.Combine(store.Root, "stop.request"))) File.Delete(Path.Combine(store.Root, "stop.request"));
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                return 0;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                Console.Error.WriteLine("Kameo could not open its local data directory. Check permissions and disk space.");
                return 1;
            }
        }

        /// <summary>Builds the native Avalonia application.</summary>
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
