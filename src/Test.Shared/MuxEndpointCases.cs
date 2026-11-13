namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Kameo.Core;
    using Microsoft.Data.Sqlite;
    using Touchstone.Core;

    internal static class MuxEndpointCases
    {
        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Action<Fixture> execute) => cases.Add(KameoSuites.Case("MuxEndpoints", name, execute));
            Add("LinkPersistsAndSharesAcrossProfiles", fixture =>
            {
                string source = Source(fixture);
                Profile first = Linked(fixture, source, "Work");
                Profile second = Linked(fixture, source, "Personal");
                ProfileStore reopened = new ProfileStore(fixture.Root);
                Fixture.Check(reopened.Get(first.Id).MuxEndpointsFile == source && reopened.Get(second.Id).MuxEndpointsFile == source, "Shared link was not persisted for both profiles.");
                Fixture.Check(reopened.Get(first.Id).HasNativeCredential, "Linked endpoints were not reflected in profile status.");
                Launch(fixture, first); Launch(fixture, second);
                Fixture.Check(File.ReadAllText(Snapshot(fixture, first)) == File.ReadAllText(source), "First profile did not load the shared source.");
                Fixture.Check(File.ReadAllText(Snapshot(fixture, second)) == File.ReadAllText(source), "Second profile did not load the shared source.");
            });
            Add("SourceChangesApplyAtNextLaunch", fixture =>
            {
                string source = Source(fixture); Profile profile = Linked(fixture, source);
                Launch(fixture, profile);
                string updated = CredentialCases.Document(HarnessKind.Mux).Replace("test-model", "updated-model", StringComparison.Ordinal);
                File.WriteAllText(source, updated); Launch(fixture, profile);
                Fixture.Check(File.ReadAllText(Snapshot(fixture, profile)) == updated, "Launch reused an outdated snapshot.");
            });
            Add("LocalChangesNeverOverwriteSharedSource", fixture =>
            {
                string source = Source(fixture); string original = File.ReadAllText(source); Profile profile = Linked(fixture, source);
                Launch(fixture, profile); PrivateFiles.Write(Snapshot(fixture, profile), "local edits"); Launch(fixture, profile);
                Fixture.Check(File.ReadAllText(source) == original && File.ReadAllText(Snapshot(fixture, profile)) == original, "Shared source was changed or local edits were retained over it.");
                fixture.Store.Delete(profile.Id);
                Fixture.Check(File.ReadAllText(source) == original, "Deleting a linked profile removed the shared source.");
            });
            Add("MissingSourceFailsWithoutUsingStaleCredentials", fixture =>
            {
                string source = Source(fixture); Profile profile = Linked(fixture, source); Launch(fixture, profile);
                File.Delete(source);
                Fixture.Throws<FileNotFoundException>(() => Launch(fixture, profile));
                Fixture.Check(!fixture.Store.Get(profile.Id).HasNativeCredential, "Missing linked source was presented as ready.");
            });
            Add("InvalidSourcePreservesLastSnapshot", fixture =>
            {
                string source = Source(fixture); Profile profile = Linked(fixture, source); Launch(fixture, profile);
                string original = File.ReadAllText(Snapshot(fixture, profile)); File.WriteAllText(source, "{invalid-synthetic-secret");
                Fixture.Throws<InvalidDataException>(() => Launch(fixture, profile));
                Fixture.Check(File.ReadAllText(Snapshot(fixture, profile)) == original, "Invalid input replaced the last snapshot.");
            });
            Add("FailedLinkEditPreservesPreviousLink", fixture =>
            {
                string source = Source(fixture); Profile profile = Linked(fixture, source);
                string invalid = Path.Combine(fixture.Root, "invalid.json"); File.WriteAllText(invalid, "{}");
                profile.MuxEndpointsFile = invalid;
                Fixture.Throws<InvalidDataException>(() => fixture.Store.Update(profile));
                Fixture.Check(fixture.Store.Get(profile.Id).MuxEndpointsFile == source, "A failed edit replaced the existing link.");
            });
            Add("LinkRequiresMuxNativeProfile", fixture =>
            {
                string source = Source(fixture);
                foreach (Profile profile in new[] { fixture.Add(), fixture.Add(HarnessKind.Mux), fixture.Add(HarnessKind.Claude, kind: CredentialKind.Native) })
                {
                    profile.MuxEndpointsFile = source; Fixture.Throws<ArgumentException>(() => fixture.Store.Update(profile));
                }
            });
            Add("ManagedProfileSourceIsRejected", fixture =>
            {
                Profile profile = fixture.Add(HarnessKind.Mux, kind: CredentialKind.Native);
                string source = Snapshot(fixture, profile); PrivateFiles.Write(source, CredentialCases.Document(HarnessKind.Mux));
                Fixture.Throws<ArgumentException>(() => Linked(fixture, source));
            });
            Add("LinkedProfileCannotImportOverSource", fixture =>
            {
                Profile profile = Linked(fixture, Source(fixture));
                Fixture.Throws<InvalidOperationException>(() => new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(HarnessKind.Mux)));
                profile.MuxEndpointsFile = ""; fixture.Store.Update(profile);
                new CredentialFiles(fixture.Store).ImportText(profile.Id, CredentialCases.Document(HarnessKind.Mux));
                Fixture.Check(fixture.Store.Get(profile.Id).MuxEndpointsFile.Length == 0, "Clearing a link did not enable local configuration.");
            });
            Add("BusyProfileCannotChangeLink", fixture =>
            {
                Profile profile = Linked(fixture, Source(fixture));
                using FileStream lease = fixture.Store.AcquireLease(profile.Id);
                profile.MuxEndpointsFile = "";
                Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile));
            });
            Add("SchemaOneUpgradePreservesProfilesAndSelection", fixture =>
            {
                Profile profile = fixture.Add();
                Sql(fixture, "ALTER TABLE profiles DROP COLUMN mux_endpoints_file; PRAGMA user_version=1;");
                ProfileStore upgraded = new ProfileStore(fixture.Root);
                Profile retained = upgraded.Get(profile.Id);
                Fixture.Check(retained.Name == profile.Name && retained.ApiKey == profile.ApiKey && retained.MuxEndpointsFile == "", "Migration lost existing profile fields.");
                Fixture.Check(upgraded.Selected(profile.Harness).Id == profile.Id, "Migration lost the selected profile.");
                retained.Name = "Migrated"; upgraded.Update(retained);
                Fixture.Check(new ProfileStore(fixture.Root).Get(profile.Id).Name == "Migrated", "Migrated store cannot be reopened or updated.");
            });
            Add("LegacyGeminiLoginIsPreservedButCannotLaunch", fixture =>
            {
                Profile profile = fixture.Add(kind: CredentialKind.Native);
                Sql(fixture, "DELETE FROM selections; UPDATE profiles SET harness=2; INSERT INTO selections SELECT harness,id FROM profiles;");
                string file = Path.Combine(fixture.Store.ProfileDirectory(profile.Id), ".gemini", "oauth_creds.json");
                PrivateFiles.Write(file, "{\"refresh_token\":\"synthetic-legacy\"}");
                Profile retained = new ProfileStore(fixture.Root).Get(profile.Id);
                Fixture.Check(retained.Harness == HarnessKind.Antigravity && retained.HasNativeCredential && File.Exists(file), "Migration lost legacy Google login data.");
                Fixture.Throws<InvalidOperationException>(() => new LaunchService(fixture.Store).Prepare(retained, Array.Empty<string>()));
                Fixture.Check(File.ReadAllText(file).Contains("synthetic-legacy", StringComparison.Ordinal), "Rejected launch modified legacy credentials.");
            });
            return new TestSuiteDescriptor("MuxEndpoints", "Shared endpoints and schema migration", cases);
        }

        private static string Source(Fixture fixture)
        {
            string path = Path.Combine(fixture.Root, "Shared endpoints.json");
            File.WriteAllText(path, CredentialCases.Document(HarnessKind.Mux)); return path;
        }
        private static Profile Linked(Fixture fixture, string source, string name = "Linked") => fixture.Store.Create(new Profile
        { Harness = HarnessKind.Mux, Kind = CredentialKind.Native, Name = name, MuxEndpointsFile = source });
        private static string Snapshot(Fixture fixture, Profile profile) => Path.Combine(fixture.Store.ProfileDirectory(profile.Id), "endpoints.json");
        private static void Launch(Fixture fixture, Profile profile)
        {
            using FileStream lease = fixture.Store.AcquireLease(profile.Id);
            new LaunchService(fixture.Store).Prepare(profile, Array.Empty<string>());
        }
        private static void Sql(Fixture fixture, string sql)
        {
            using SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(fixture.Root, "kameo.db"), Pooling = false }.ToString());
            connection.Open(); using SqliteCommand command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
    }
}
