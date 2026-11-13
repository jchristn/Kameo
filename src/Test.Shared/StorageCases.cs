namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Kameo.Core;
    using Microsoft.Data.Sqlite;
    using Touchstone.Core;

    internal static class StorageCases
    {
        internal static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string name, Action<Fixture> execute) => cases.Add(KameoSuites.Case("Storage", name, execute));
            Add("EmptyStore", fixture => Fixture.Check(fixture.Store.List().Count == 0, "Store must start empty."));
            Add("CreateAndReopen", fixture =>
            {
                Profile profile = fixture.Add();
                Profile saved = new ProfileStore(fixture.Root).Get(profile.Id);
                Fixture.Check(saved.Name == "Work" && saved.ApiKey == "synthetic-test-key" && saved.IsActive, "Profile must round trip.");
                Fixture.Check(saved.Id.StartsWith("prf_", StringComparison.Ordinal) && saved.CreatedUtc <= DateTimeOffset.UtcNow, "Stable ID and timestamp required.");
            });
            Add("ListRedactsKeys", fixture => { fixture.Add(); Fixture.Check(fixture.Store.List()[0].ApiKey == "", "Lists must redact secrets."); });
            Add("CaseInsensitiveDuplicateName", fixture => { fixture.Add(); Fixture.Throws<InvalidOperationException>(() => fixture.Add(name: "work")); });
            Add("NamesCanRepeatAcrossHarnesses", fixture => { fixture.Add(); fixture.Add(HarnessKind.Claude); Fixture.Check(fixture.Store.List().Count == 2, "Harness names are separate."); });
            Add("NameTrimAndUnicode", fixture => { Profile profile = fixture.Add(name: "  Client 日本語  "); Fixture.Check(profile.Name == "Client 日本語", "Names should retain Unicode and trim edges."); });
            Add("FirstSelectedOnly", fixture => { Profile first = fixture.Add(); fixture.Add(name: "Personal"); Fixture.Check(fixture.Store.Selected(HarnessKind.Codex).Id == first.Id, "Adding a profile must not silently switch accounts."); });
            Add("SwitchPersists", fixture => { fixture.Add(); Profile next = fixture.Add(name: "Personal"); fixture.Store.Select(next.Id); Fixture.Check(new ProfileStore(fixture.Root).Selected(HarnessKind.Codex).Id == next.Id, "Selection must persist."); });
            Add("IndependentSelections", fixture => { Profile claude = fixture.Add(HarnessKind.Claude); fixture.Add(); Profile next = fixture.Add(name: "Other"); fixture.Store.Select(next.Id); Fixture.Check(fixture.Store.Selected(HarnessKind.Claude).Id == claude.Id, "One harness must not affect another."); });
            Add("EditRoundTrip", fixture => { Profile profile = fixture.Add(); profile.Name = "Renamed"; profile.ApiKey = "replacement-test-key"; profile.Executable = "custom-codex"; fixture.Store.Update(profile); Profile saved = fixture.Store.Get(profile.Id); Fixture.Check(saved.Name == "Renamed" && saved.ApiKey == "replacement-test-key" && saved.Executable == "custom-codex", "Edits must persist."); });
            Add("DuplicateRenameRollsBack", fixture => { fixture.Add(); Profile other = fixture.Add(name: "Other"); other.Name = "Work"; Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(other)); Fixture.Check(fixture.Store.Get(other.Id).Name == "Other", "Failed edit must leave stored name intact."); });
            Add("CannotChangeHarness", fixture => { Profile profile = fixture.Add(); profile.Harness = HarnessKind.Claude; Fixture.Throws<ArgumentException>(() => fixture.Store.Update(profile)); });
            Add("CannotChangeAuthKind", fixture => { Profile profile = fixture.Add(); profile.Kind = CredentialKind.Native; profile.ApiKey = ""; Fixture.Throws<ArgumentException>(() => fixture.Store.Update(profile)); });
            Add("DeleteSelectedClearsSelection", fixture => { Profile profile = fixture.Add(); fixture.Add(name: "Other"); fixture.Store.Delete(profile.Id); Fixture.Throws<InvalidOperationException>(() => fixture.Store.Selected(HarnessKind.Codex)); Fixture.Check(!Directory.Exists(fixture.Store.ProfileDirectory(profile.Id)), "Native data must be removed."); });
            Add("DeleteOtherPreservesSelection", fixture => { Profile first = fixture.Add(); Profile other = fixture.Add(name: "Other"); fixture.Store.Delete(other.Id); Fixture.Check(fixture.Store.Selected(HarnessKind.Codex).Id == first.Id, "Selected profile must survive."); });
            Add("UnknownProfileRejected", fixture => { Fixture.Throws<KeyNotFoundException>(() => fixture.Store.Get("prf_missing123")); Fixture.Throws<KeyNotFoundException>(() => fixture.Store.Select("prf_missing123")); });
            Add("TraversalRejected", fixture => { foreach (string id in new[] { "../outside", "prf_../../foo", "prf_/tmp/test", "prf_bad\\path" }) Fixture.Throws<ArgumentException>(() => fixture.Store.ProfileDirectory(id)); });
            Add("LeaseBlocksEditDeleteAndSecondLease", fixture => { Profile profile = fixture.Add(); using FileStream lease = fixture.Store.AcquireLease(profile.Id); Fixture.Throws<InvalidOperationException>(() => fixture.Store.Update(profile)); Fixture.Throws<InvalidOperationException>(() => fixture.Store.Delete(profile.Id)); Fixture.Throws<InvalidOperationException>(() => fixture.Store.AcquireLease(profile.Id)); });
            Add("LeaseAllowsSelection", fixture => { Profile profile = fixture.Add(); Profile next = fixture.Add(name: "Other"); using FileStream lease = fixture.Store.AcquireLease(profile.Id); fixture.Store.Select(next.Id); Fixture.Check(fixture.Store.Selected(HarnessKind.Codex).Id == next.Id, "Running profile cannot block future selection."); });
            Add("LeaseReleaseAllowsEdit", fixture => { Profile profile = fixture.Add(); fixture.Store.AcquireLease(profile.Id).Dispose(); profile.Name = "New"; fixture.Store.Update(profile); });
            Add("PreferencesRoundTrip", fixture => { fixture.Store.SetPreference("theme", "light"); fixture.Store.SetPreference("theme", "dark"); Fixture.Check(new ProfileStore(fixture.Root).Preference("theme") == "dark", "Preference upsert failed."); });
            Add("FutureSchemaRejected", fixture =>
            {
                using (SqliteConnection connection = new SqliteConnection("Data Source=" + Path.Combine(fixture.Root, "kameo.db") + ";Pooling=False"))
                {
                    connection.Open(); using SqliteCommand command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99;"; command.ExecuteNonQuery();
                }
                Fixture.Throws<InvalidDataException>(() => new ProfileStore(fixture.Root));
            });
            Add("CorruptDatabaseNotReplaced", fixture =>
            {
                string path = Path.Combine(fixture.Root, "kameo.db"); File.WriteAllText(path, "corrupt-database-fixture");
                Fixture.Throws<SqliteException>(() => new ProfileStore(fixture.Root));
                Fixture.Check(File.ReadAllText(path) == "corrupt-database-fixture", "A corrupt database must not silently reset user data.");
            });
            foreach (string invalid in new[] { "", " ", new string('x', 81), "bad\nname", "bad\0name" })
            {
                string name = "InvalidName" + cases.Count;
                Add(name, fixture => Fixture.Throws<ArgumentException>(() => fixture.Add(name: invalid)));
            }
            Add("BlankKeyRejected", fixture => Fixture.Throws<ArgumentException>(() => fixture.Store.Create(new Profile { Name = "Bad", ApiKey = " " })));
            Add("ControlKeyRejected", fixture => Fixture.Throws<ArgumentException>(() => fixture.Store.Create(new Profile { Name = "Bad", ApiKey = "key\nsecret" })));
            Add("InvalidEnumRejected", fixture => Fixture.Throws<ArgumentException>(() => fixture.Store.Create(new Profile { Harness = (HarnessKind)99, ApiKey = "test" })));
            Add("NativeKeyRejected", fixture => Fixture.Throws<ArgumentException>(() => fixture.Store.Create(new Profile { Kind = CredentialKind.Native, ApiKey = "test" })));
            Add("PrivatePermissions", fixture =>
            {
                if (OperatingSystem.IsWindows()) return;
                Fixture.Check(File.GetUnixFileMode(fixture.Root) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "Directory permissions are not private.");
                Fixture.Check(File.GetUnixFileMode(Path.Combine(fixture.Root, "kameo.db")) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Database permissions are not private.");
            });
            cases.Add(KameoSuites.Case("Storage", "ConcurrentCreatesAndSelections", async (fixture, cancellationToken) =>
            {
                Task[] tasks = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
                {
                    ProfileStore store = new ProfileStore(fixture.Root);
                    Profile profile = store.Create(new Profile { Harness = HarnessKind.Codex, Name = "Parallel " + index, ApiKey = "synthetic" });
                    store.Select(profile.Id);
                }, cancellationToken)).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
                Fixture.Check(fixture.Store.List().Count == 12 && fixture.Store.List().Count(profile => profile.IsActive) == 1, "Concurrent changes lost records or created multiple selections.");
            }));
            return new TestSuiteDescriptor("Storage", "Profile storage and concurrency", cases);
        }
    }
}
