namespace Kameo.Core
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>Atomic private files and link checks. Unix permissions are owner-only.</summary>
    public static class PrivateFiles
    {
        /// <summary>Rejects symbolic links and reparse points in an existing path or its ancestors.</summary>
        /// <exception cref="IOException">A path traverses a link.</exception>
        public static void CheckPath(string path)
        {
            string? current = Path.GetFullPath(path);
            while (current != null)
            {
                // macOS /tmp and /var are OS-managed symlinks. Callers should supply resolved temp roots.
                if (File.Exists(current) || Directory.Exists(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("A credential path contains a symbolic link or reparse point.");
                }
                else if (new FileInfo(current).LinkTarget != null)
                    throw new IOException("A credential path contains a broken symbolic link.");
                current = Path.GetDirectoryName(current);
            }
        }

        /// <summary>Creates an owner-only directory and checks for links.</summary>
        public static void DirectoryCreate(string path)
        {
            CheckPath(path);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
            else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        /// <summary>Writes UTF-8 text using an owner-only sibling temporary file and atomic replacement.</summary>
        public static void Write(string path, string content)
        {
            ArgumentNullException.ThrowIfNull(content);
            DirectoryCreate(Path.GetDirectoryName(Path.GetFullPath(path))!);
            CheckPath(path);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new FileStream(temporary, options))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(content);
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        /// <summary>Reads a bounded credential file without including its contents in errors.</summary>
        public static string Read(string path)
        {
            CheckPath(path);
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("Credential files must be smaller than 1 MiB.");
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
