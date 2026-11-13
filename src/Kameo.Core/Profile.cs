namespace Kameo.Core
{
    using System;

    /// <summary>A stored identity. Mutable snapshots are not thread safe; stores return independent instances.</summary>
    public sealed class Profile
    {
        private string _Name = "New profile";
        private string _ApiKey = "";
        private string _Executable = "";

        /// <summary>Opaque stable identifier, generated on creation.</summary>
        public string Id { get; internal set; } = "";
        /// <summary>Display name, between 1 and 80 characters.</summary>
        /// <exception cref="ArgumentException">The name is blank, too long, or contains control characters.</exception>
        public string Name
        {
            get => _Name;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Enter a profile name.");
                if (value.Trim().Length > 80 || System.Linq.Enumerable.Any(value, char.IsControl))
                    throw new ArgumentException("Profile names must have 1 to 80 printable characters.", nameof(value));
                _Name = value.Trim();
            }
        }
        /// <summary>The harness this profile belongs to. Cannot change after creation.</summary>
        public HarnessKind Harness { get; set; }
        /// <summary>The authentication method. Cannot change after creation.</summary>
        public CredentialKind Kind { get; set; }
        /// <summary>Plaintext API key. Never include this value in logs or command arguments.</summary>
        public string ApiKey { get => _ApiKey; set => _ApiKey = value ?? throw new ArgumentNullException(nameof(value)); }
        /// <summary>Optional executable name or absolute path. Empty uses the harness command on PATH.</summary>
        public string Executable { get => _Executable; set => _Executable = value ?? throw new ArgumentNullException(nameof(value)); }
        /// <summary>Creation timestamp in UTC.</summary>
        public DateTimeOffset CreatedUtc { get; internal set; }
        /// <summary>Whether this is the selected profile for its harness.</summary>
        public bool IsActive { get; internal set; }
        /// <summary>Whether a saved native authentication artifact exists. This does not validate remote access.</summary>
        public bool HasNativeCredential { get; internal set; }
    }
}
