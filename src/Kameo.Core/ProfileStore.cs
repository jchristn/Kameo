namespace Kameo.Core
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;

    /// <summary>Transactional local profile storage. Independent connections make instances safe for concurrent callers.</summary>
    public sealed class ProfileStore
    {
        private readonly string _ConnectionString;

        /// <summary>Absolute Kameo data directory.</summary>
        public string Root { get; }

        /// <summary>Creates or opens a store. KAMEO_HOME overrides the default ~/.kameo directory.</summary>
        public ProfileStore(string? root = null)
        {
            Root = Path.GetFullPath(root ?? Environment.GetEnvironmentVariable("KAMEO_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kameo"));
            PrivateFiles.DirectoryCreate(Root);
            PrivateFiles.DirectoryCreate(Path.Combine(Root, "profiles"));
            PrivateFiles.DirectoryCreate(Path.Combine(Root, "locks"));
            string database = Path.Combine(Root, "kameo.db");
            PrivateFiles.CheckPath(database);
            if (!File.Exists(database))
            {
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.Write, Share = FileShare.ReadWrite };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using FileStream initial = new FileStream(database, options);
            }
            _ConnectionString = new SqliteConnectionStringBuilder { DataSource = database, ForeignKeys = true, Pooling = false, DefaultTimeout = 5 }.ToString();
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            int schemaVersion = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (schemaVersion > 2)
                throw new InvalidDataException("This Kameo database was created by a newer version. Update Kameo before opening it.");
            command.CommandText = "PRAGMA journal_mode=DELETE;";
            command.ExecuteNonQuery();
            using SqliteTransaction schemaTransaction = connection.BeginTransaction();
            command.Transaction = schemaTransaction;
            command.CommandText = "PRAGMA user_version;";
            schemaVersion = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (schemaVersion > 2) throw new InvalidDataException("This Kameo database was created by a newer version. Update Kameo before opening it.");
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS profiles (
                    id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE,
                    harness INTEGER NOT NULL CHECK(harness BETWEEN 0 AND 3),
                    kind INTEGER NOT NULL CHECK(kind BETWEEN 0 AND 1),
                    api_key TEXT NOT NULL, executable TEXT NOT NULL, created_utc TEXT NOT NULL,
                    UNIQUE(harness, name), UNIQUE(harness, id));
                CREATE TABLE IF NOT EXISTS selections (
                    harness INTEGER PRIMARY KEY, profile_id TEXT NOT NULL,
                    FOREIGN KEY(harness, profile_id) REFERENCES profiles(harness, id) ON DELETE CASCADE);
                CREATE TABLE IF NOT EXISTS preferences (name TEXT PRIMARY KEY, value TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
            if (schemaVersion < 2)
            {
                command.CommandText = "ALTER TABLE profiles ADD COLUMN mux_endpoints_file TEXT NOT NULL DEFAULT ''; PRAGMA user_version=2;";
                command.ExecuteNonQuery();
            }
            schemaTransaction.Commit();
        }

        /// <summary>Returns independent profile snapshots, without secret values unless explicitly requested.</summary>
        public IReadOnlyList<Profile> List(bool includeSecrets = false)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT p.id,p.name,p.harness,p.kind," + (includeSecrets ? "p.api_key" : "''") + ",p.executable,p.created_utc,s.profile_id IS NOT NULL,p.mux_endpoints_file FROM profiles p LEFT JOIN selections s ON s.profile_id=p.id ORDER BY p.harness,p.name COLLATE NOCASE;";
            using SqliteDataReader reader = command.ExecuteReader();
            List<Profile> profiles = new List<Profile>();
            while (reader.Read())
            {
                Profile profile = new Profile
                {
                    Id = reader.GetString(0), Name = reader.GetString(1), Harness = (HarnessKind)reader.GetInt32(2),
                    Kind = (CredentialKind)reader.GetInt32(3), ApiKey = reader.GetString(4), Executable = reader.GetString(5),
                    CreatedUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture), IsActive = reader.GetBoolean(7), MuxEndpointsFile = reader.GetString(8)
                };
                profile.HasNativeCredential = profile.MuxEndpointsFile.Length > 0 ? File.Exists(profile.MuxEndpointsFile)
                    : File.Exists(Path.Combine(ProfileDirectory(profile.Id), HarnessCatalog.CredentialFile(profile.Harness)));
                profiles.Add(profile);
            }
            return profiles;
        }

        /// <summary>Returns a profile, including its secret, by ID.</summary>
        /// <exception cref="KeyNotFoundException">No such profile exists.</exception>
        public Profile Get(string id)
        {
            ValidateId(id);
            return List(true).FirstOrDefault(profile => profile.Id == id) ?? throw new KeyNotFoundException("The profile no longer exists. Refresh the profile list.");
        }

        /// <summary>Gets the selected profile, including its secret.</summary>
        public Profile Selected(HarnessKind harness) => List(true).FirstOrDefault(profile => profile.Harness == harness && profile.IsActive)
            ?? throw new InvalidOperationException("Select a profile for " + HarnessCatalog.Name(harness) + " first.");

        /// <summary>Creates a profile; the first profile of a harness becomes selected.</summary>
        /// <exception cref="ArgumentException">Profile values are invalid.</exception>
        /// <exception cref="InvalidOperationException">A duplicate name exists.</exception>
        public Profile Create(Profile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            Validate(profile);
            string id = new PrettyId.IdGenerator().GenerateKSortable("prf_", 24);
            string directory = ProfileDirectory(id);
            PrivateFiles.DirectoryCreate(directory);
            try
            {
                using SqliteConnection connection = Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO profiles(id,name,harness,kind,api_key,executable,created_utc,mux_endpoints_file) VALUES($id,$name,$harness,$kind,$key,$exe,$created,$muxFile); INSERT OR IGNORE INTO selections VALUES($harness,$id);";
                AddParameters(command, profile);
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
                transaction.Commit();
                return Get(id);
            }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            {
                Directory.Delete(directory, true);
                throw new InvalidOperationException("A profile with that name already exists for this harness.");
            }
        }

        /// <summary>Updates name, key and executable. Running profiles cannot be edited.</summary>
        public void Update(Profile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            Validate(profile);
            using FileStream lease = AcquireLease(profile.Id);
            Profile existing = Get(profile.Id);
            if (existing.Harness != profile.Harness || existing.Kind != profile.Kind)
                throw new ArgumentException("Create a new profile to change its harness or authentication method.", nameof(profile));
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE profiles SET name=$name,api_key=$key,executable=$exe,mux_endpoints_file=$muxFile WHERE id=$id;";
            AddParameters(command, profile);
            command.Parameters.AddWithValue("$id", profile.Id);
            try { command.ExecuteNonQuery(); }
            catch (SqliteException error) when (error.SqliteErrorCode == 19)
            { throw new InvalidOperationException("A profile with that name already exists for this harness."); }
        }

        /// <summary>Selects a profile atomically. Existing processes keep their current directory and credentials.</summary>
        public void Select(string id)
        {
            ValidateId(id);
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO selections SELECT harness,id FROM profiles WHERE id=$id ON CONFLICT(harness) DO UPDATE SET profile_id=excluded.profile_id;";
            command.Parameters.AddWithValue("$id", id);
            if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException("The selected profile no longer exists.");
        }

        /// <summary>Deletes a profile and its native data. Deleting the selected profile leaves that harness unselected.</summary>
        public void Delete(string id)
        {
            using FileStream lease = AcquireLease(id);
            Profile profile = Get(id);
            string directory = ProfileDirectory(id);
            PrivateFiles.CheckPath(directory);
            using SqliteConnection connection = Open();
            using SqliteTransaction transaction = connection.BeginTransaction();
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM profiles WHERE id=$id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
            if (profile.Harness == HarnessKind.Claude && profile.Kind == CredentialKind.Native)
                MacCredentialBridge.DeleteScoped(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            transaction.Commit();
        }

        /// <summary>Returns the stable directory for an ID, rejecting path traversal.</summary>
        public string ProfileDirectory(string id)
        {
            ValidateId(id);
            string directory = Path.Combine(Root, "profiles", id);
            PrivateFiles.CheckPath(directory);
            return directory;
        }

        /// <summary>Holds an exclusive profile lease. Callers must dispose it when their operation ends.</summary>
        /// <exception cref="InvalidOperationException">Another operation is using this profile.</exception>
        public FileStream AcquireLease(string id) => AcquireProfileLease(id, FileShare.None);

        /// <summary>Holds a shared running-session lease. Other sessions may join; edits and login require all sessions to exit.</summary>
        /// <exception cref="InvalidOperationException">An exclusive operation is using this profile.</exception>
        public FileStream AcquireSessionLease(string id) => AcquireProfileLease(id, FileShare.ReadWrite);

        internal async Task<FileStream> AcquirePreparationLeaseAsync(string id, CancellationToken cancellationToken)
        {
            ValidateId(id);
            string path = Path.Combine(Root, "locks", id + ".prepare.lock");
            PrivateFiles.CheckPath(path);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try { return OpenLease(path, FileShare.None); }
                catch (IOException)
                {
                    await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                }
            }
        }

        private FileStream AcquireProfileLease(string id, FileShare share)
        {
            ValidateId(id);
            string path = Path.Combine(Root, "locks", id + ".lock");
            PrivateFiles.CheckPath(path);
            try
            {
                return OpenLease(path, share);
            }
            catch (IOException) { throw new InvalidOperationException("This profile is in use. Close sessions for this profile and try again. Sessions for other profiles can stay open."); }
        }

        private static FileStream OpenLease(string path, FileShare share)
        {
            FileStreamOptions options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = share };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return new FileStream(path, options);
        }

        /// <summary>Reads a non-secret desktop preference.</summary>
        public string? Preference(string name)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM preferences WHERE name=$name;";
            command.Parameters.AddWithValue("$name", name);
            return command.ExecuteScalar() as string;
        }

        /// <summary>Writes a non-secret desktop preference.</summary>
        public void SetPreference(string name, string value)
        {
            using SqliteConnection connection = Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT INTO preferences VALUES($name,$value) ON CONFLICT(name) DO UPDATE SET value=excluded.value;";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        private SqliteConnection Open()
        {
            PrivateFiles.CheckPath(Path.Combine(Root, "kameo.db"));
            SqliteConnection connection = new SqliteConnection(_ConnectionString);
            try { connection.Open(); return connection; }
            catch { connection.Dispose(); throw; }
        }

        private static void ValidateId(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (!id.StartsWith("prf_", StringComparison.Ordinal) || id.Length > 80 || id.Length < 8 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw new ArgumentException("Invalid profile identifier.", nameof(id));
        }

        private void Validate(Profile profile)
        {
            if (!Enum.IsDefined(profile.Harness) || !Enum.IsDefined(profile.Kind)) throw new ArgumentException("Unsupported harness or credential type.");
            if (profile.Kind == CredentialKind.ApiKey && string.IsNullOrWhiteSpace(profile.ApiKey)) throw new ArgumentException("An API key is required.");
            if (profile.ApiKey.Any(char.IsControl) || profile.ApiKey.Length > 65536) throw new ArgumentException("The API key contains invalid characters or is too long.");
            if (profile.Executable.Any(char.IsControl)) throw new ArgumentException("The executable contains invalid characters.");
            if (profile.Kind == CredentialKind.Native && profile.ApiKey.Length > 0) throw new ArgumentException("Native profiles cannot contain an API key.");
            if (profile.MuxEndpointsFile.Length > 0)
            {
                if (profile.Harness != HarnessKind.Mux || profile.Kind != CredentialKind.Native)
                    throw new ArgumentException("A shared endpoints file requires a Mux endpoints-file profile.");
                profile.MuxEndpointsFile = MuxEndpointLink.Validate(Root, profile.MuxEndpointsFile);
            }
        }

        private static void AddParameters(SqliteCommand command, Profile profile)
        {
            command.Parameters.AddWithValue("$name", profile.Name);
            command.Parameters.AddWithValue("$harness", (int)profile.Harness);
            command.Parameters.AddWithValue("$kind", (int)profile.Kind);
            command.Parameters.AddWithValue("$key", profile.ApiKey);
            command.Parameters.AddWithValue("$exe", profile.Executable);
            command.Parameters.AddWithValue("$muxFile", profile.MuxEndpointsFile);
        }
    }
}
