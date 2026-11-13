namespace Kameo.Core
{
    using System;
    using System.IO;

    /// <summary>Refreshes a profile's Mux endpoints from a shared, read-only source at launch time.</summary>
    public static class MuxEndpointLink
    {
        /// <summary>Validates a shared file outside managed profile data and returns its absolute path.</summary>
        public static string Validate(string storeRoot, string source)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            string path = Path.GetFullPath(source);
            string profiles = Path.Combine(Path.GetFullPath(storeRoot), "profiles") + Path.DirectorySeparatorChar;
            if (path.StartsWith(profiles, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Choose a shared endpoints file outside Kameo's managed profiles directory.");
            CredentialFiles.Validate(HarnessKind.Mux, PrivateFiles.Read(path));
            return path;
        }

        /// <summary>Refreshes only the local snapshot. The caller must hold the profile lease.</summary>
        public static void Refresh(ProfileStore store, Profile profile)
        {
            if (profile.Harness != HarnessKind.Mux || profile.Kind != CredentialKind.Native || profile.MuxEndpointsFile.Length == 0)
                throw new InvalidOperationException("This profile does not link a Mux endpoints file.");
            string source = Validate(store.Root, profile.MuxEndpointsFile);
            string content = PrivateFiles.Read(source);
            CredentialFiles.Validate(HarnessKind.Mux, content);
            content = MuxCredentialImport.Resolve(content, Environment.GetEnvironmentVariable);
            PrivateFiles.Write(Path.Combine(store.ProfileDirectory(profile.Id), "endpoints.json"), content);
        }
    }
}
