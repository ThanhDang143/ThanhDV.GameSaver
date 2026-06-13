using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Infrastructure;

namespace ThanhDV.SaveKeeper.Tests
{
    [TestFixture]
    public class LocalStorageProviderTests
    {
        private string _testBasePath;
        private LocalStorageProvider _provider;

        [SetUp]
        public void SetUp()
        {
            // Tạo một thư mục tạm duy nhất cho mỗi lần test để tránh đụng độ
            _testBasePath = Path.Combine(Path.GetTempPath(), "SaveKeeperTests_" + System.Guid.NewGuid());
            _provider = new LocalStorageProvider(_testBasePath);
        }

        [TearDown]
        public void TearDown()
        {
            // Dọn dẹp: Xóa thư mục tạm thời sau khi test kết thúc
            if (Directory.Exists(_testBasePath))
            {
                Directory.Delete(_testBasePath, true);
            }
        }

        [Test]
        public void WriteImmediate_CreatesNewFile_WhenFileDoesNotExist()
        {
            // Arrange
            string profileId = "profile_1";
            string fileName = "save.json";
            string expectedData = "{\"health\": 100}";

            // Act
            _provider.WriteImmediate(profileId, fileName, expectedData);

            // Assert
            string expectedFilePath = Path.Combine(_testBasePath, profileId, fileName);

            // Kiểm tra file có được tạo ra không
            Assert.IsTrue(File.Exists(expectedFilePath), "Save file should be created.");

            // Kiểm tra nội dung có đúng không
            string actualData = File.ReadAllText(expectedFilePath);
            Assert.AreEqual(expectedData, actualData);

            // Đảm bảo không còn tồn tại file rác (temp file)
            Assert.IsFalse(File.Exists(expectedFilePath + Constant.FILE_TEMP_EXTENTION));
        }

        [Test]
        public void WriteAsync_CreatesBackup_WhenFileAlreadyExists()
        {
            // Arrange
            string profileId = "profile_1";
            string fileName = "save.json";
            string expectedFilePath = Path.Combine(_testBasePath, profileId, fileName);
            string backupFilePath = expectedFilePath + Constant.FILE_BACKUP_EXTENTION;

            string oldData = "{\"health\": 50}";
            string newData = "{\"health\": 100}";

            // Act 1: Ghi file lần đầu
            Task.Run(() => _provider.WriteAsync(profileId, fileName, oldData)).Wait();

            // Act 2: Ghi file lần 2 đè lên file cũ
            Task.Run(() => _provider.WriteAsync(profileId, fileName, newData)).Wait();

            // Assert
            // 1. File chính phải tồn tại và chứa nội dung MỚI
            Assert.IsTrue(File.Exists(expectedFilePath));
            Assert.AreEqual(newData, File.ReadAllText(expectedFilePath));

            // 2. File backup phải tồn tại và chứa nội dung CŨ
            Assert.IsTrue(File.Exists(backupFilePath), "Backup file should be created.");
            Assert.AreEqual(oldData, File.ReadAllText(backupFilePath));
        }

        [Test]
        public void WriteAsync_CreatesNewFile_WhenFileDoesNotExist()
        {
            // Arrange
            string profileId = "profile_new_async";
            string fileName = "save_async.json";
            string expectedData = "{\"stamina\": 100}";

            // Act
            Task.Run(() => _provider.WriteAsync(profileId, fileName, expectedData)).Wait();

            // Assert
            string expectedFilePath = Path.Combine(_testBasePath, profileId, fileName);
            string backupFilePath = expectedFilePath + Constant.FILE_BACKUP_EXTENTION;

            Assert.IsTrue(File.Exists(expectedFilePath));
            Assert.AreEqual(expectedData, File.ReadAllText(expectedFilePath));
            Assert.IsFalse(File.Exists(backupFilePath), "Backup should NOT be created for a brand new file.");
        }

        [Test]
        public void WriteImmediate_CreatesBackup_WhenFileAlreadyExists()
        {
            // Arrange
            string profileId = "profile_exist_sync";
            string fileName = "save_sync.json";
            string expectedFilePath = Path.Combine(_testBasePath, profileId, fileName);
            string backupFilePath = expectedFilePath + Constant.FILE_BACKUP_EXTENTION;

            string oldData = "{\"stamina\": 50}";
            string newData = "{\"stamina\": 100}";

            // Act
            _provider.WriteImmediate(profileId, fileName, oldData); // Ghi gốc
            _provider.WriteImmediate(profileId, fileName, newData); // Ghi đè

            // Assert
            Assert.IsTrue(File.Exists(expectedFilePath));
            Assert.AreEqual(newData, File.ReadAllText(expectedFilePath));
            Assert.IsTrue(File.Exists(backupFilePath));
            Assert.AreEqual(oldData, File.ReadAllText(backupFilePath));
        }

        [Test]
        public void WriteImmediate_WithNestedProfileId_ThrowsArgumentException()
        {
            // Arrange (Cố tình tạo ProfileId chứa dấu gạch chéo)
            string invalidProfileId = "profile_nested/subfolder/deep";
            string fileName = "save.dat";
            string data = "test path creation";

            // Act & Assert (Phải ném ra ArgumentException)
            Assert.Throws<System.ArgumentException>(() =>
                _provider.WriteImmediate(invalidProfileId, fileName, data),
                "System should throw ArgumentException when a nested profile ID is provided."
            );
        }

        #region Read Tests

        [Test]
        public void ReadAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
        {
            // Arrange
            string profileId = "profile_missing";
            string fileName = "save.json";

            // Act & Assert
            // Sử dụng GetAwaiter().GetResult() để lấy chính xác Exception (tránh AggregateException của Task.Wait)
            Assert.Throws<FileNotFoundException>(() =>
                Task.Run(() => _provider.ReadAsync(profileId, fileName)).GetAwaiter().GetResult()
            );
        }

        [Test]
        public void ReadAsync_ReturnsFileContent_WhenFileExists()
        {
            // Arrange
            string profileId = "profile_exist";
            string fileName = "save.json";
            string expectedData = "{\"stamina\": 150}";

            // Ghi file trước
            _provider.WriteImmediate(profileId, fileName, expectedData);

            // Act
            string actualData = Task.Run(() => _provider.ReadAsync(profileId, fileName)).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(expectedData, actualData, "The read content should match the written content.");
        }

        [Test]
        public void ReadBackupAsync_ThrowsFileNotFoundException_WhenBackupDoesNotExist()
        {
            // Arrange
            string profileId = "profile_no_backup";
            string fileName = "save.json";

            // Act & Assert
            Assert.Throws<FileNotFoundException>(() =>
                Task.Run(() => _provider.ReadBackupAsync(profileId, fileName)).GetAwaiter().GetResult()
            );
        }

        [Test]
        public void ReadBackupAsync_ReturnsBackupContent_WhenBackupExists()
        {
            // Arrange
            string profileId = "profile_has_backup";
            string fileName = "save.json";

            string oldData = "{\"stamina\": 50}";
            string newData = "{\"stamina\": 100}";

            // Ghi file 2 lần để tạo ra bản backup chứa `oldData`
            _provider.WriteImmediate(profileId, fileName, oldData);
            _provider.WriteImmediate(profileId, fileName, newData);

            // Act
            string backupData = Task.Run(() => _provider.ReadBackupAsync(profileId, fileName)).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(oldData, backupData, "The read backup content should match the older written content.");
        }

        #endregion Read Tests

        #region Profile Management Tests

        [Test]
        public void DeleteProfile_RemovesEntireFolder_WhenProfileExists()
        {
            // Arrange
            string profileId = "profile_to_delete";
            string fileName = "save.json";
            _provider.WriteImmediate(profileId, fileName, "some data");

            string profilePath = Path.Combine(_testBasePath, profileId);
            Assert.IsTrue(Directory.Exists(profilePath)); // Verify creation

            // Act
            _provider.DeleteProfile(profileId);

            // Assert
            Assert.IsFalse(Directory.Exists(profilePath), "The entire profile directory should be removed.");
        }

        [Test]
        public void GetAllProfileIds_ReturnsAllFolderNames()
        {
            // Arrange
            _provider.WriteImmediate("profile_A", "save.json", "data");
            _provider.WriteImmediate("profile_B", "save.json", "data");

            // Act
            var profiles = new System.Collections.Generic.List<string>(_provider.GetAllProfileIds());

            // Assert
            Assert.AreEqual(2, profiles.Count);
            Assert.IsTrue(profiles.Contains("profile_A"));
            Assert.IsTrue(profiles.Contains("profile_B"));
        }

        [Test]
        public void GetAllProfileIds_ReturnsEmpty_WhenNoProfilesExist()
        {
            // Ensure empty directory to simulate a fresh state
            if (Directory.Exists(_testBasePath)) Directory.Delete(_testBasePath, true);

            // Act
            var profiles = new System.Collections.Generic.List<string>(_provider.GetAllProfileIds());

            // Assert
            Assert.IsEmpty(profiles, "It should return an empty list if the base folder is missing or empty.");
        }

        [Test]
        public void GetMostRecentProfileId_ReturnsProfileWithLatestFile()
        {
            // Arrange
            _provider.WriteImmediate("profile_old", "save.json", "old_data");
            _provider.WriteImmediate("profile_new", "save.json", "new_data");

            // Cố tình đẩy thời gian Access/Write của file cũ lùi về quá khứ 1 ngày để đảm bảo tính ổn định của test
            string oldPath = Path.Combine(_testBasePath, "profile_old", "save.json");
            System.DateTime pastDate = System.DateTime.UtcNow.AddDays(-1);
            File.SetLastAccessTimeUtc(oldPath, pastDate);
            File.SetLastWriteTimeUtc(oldPath, pastDate);

            // Act
            // Hàm sẽ dựa theo LastAccessTimeUtc mới nhất
            string recentProfile = _provider.GetMostRecentProfileId("save.json");

            // Assert
            Assert.AreEqual("profile_new", recentProfile, "Should return the profile containing the most recently modified file.");
        }

        [Test]
        public void GetMostRecentProfileId_ReturnsNull_WhenNoProfilesExist()
        {
            // Act
            string recentProfile = _provider.GetMostRecentProfileId("save.json");

            // Assert
            Assert.IsNull(recentProfile, "Should return null if no profiles/files exist.");
        }

        [Test]
        public void GetMostRecentProfileId_IgnoresNonSaveFiles()
        {
            _provider.WriteImmediate("profile_A", "save.json", "a");
            _provider.WriteImmediate("profile_B", "save.json", "b");

            System.DateTime past = System.DateTime.UtcNow.AddDays(-1);
            System.DateTime future = System.DateTime.UtcNow.AddDays(1);
            string bDir = Path.Combine(_testBasePath, "profile_B");

            // B's primary save is OLDER than A's save.
            File.SetLastWriteTimeUtc(Path.Combine(bDir, "save.json"), past);

            // B also has a meta sidecar (e.g. a metadata self-heal write) and a leftover temp file, both NEWER
            // than A's save. Neither must influence the ranking — only the primary save's mtime counts.
            string bMeta = Path.Combine(bDir, "save.meta");
            File.WriteAllText(bMeta, "meta");
            File.SetLastWriteTimeUtc(bMeta, future);

            string bTmp = Path.Combine(bDir, "save.json.tmp");
            File.WriteAllText(bTmp, "tmp");
            File.SetLastWriteTimeUtc(bTmp, future);

            string recent = _provider.GetMostRecentProfileId("save.json");

            Assert.AreEqual("profile_A", recent,
                "Only the primary save file's mtime should rank profiles; .meta/.tmp must be ignored.");
        }

        [Test]
        public void GetMostRecentProfileId_FallsBackToBackup_WhenPrimaryMissing()
        {
            string aDir = Path.Combine(_testBasePath, "profile_A");
            Directory.CreateDirectory(aDir);

            // Only a backup exists (the primary save was lost) — the profile should still be considered.
            File.WriteAllText(Path.Combine(aDir, "save.json" + Constant.FILE_BACKUP_EXTENTION), "backup");

            string recent = _provider.GetMostRecentProfileId("save.json");

            Assert.AreEqual("profile_A", recent,
                "A profile whose primary save is missing but has a backup should still be returned.");
        }

        [Test]
        public void GetMostRecentProfileId_SkipsProfileWithNoSaveOrBackup()
        {
            string aDir = Path.Combine(_testBasePath, "profile_A");
            Directory.CreateDirectory(aDir);

            // Only a sidecar — no save, no backup → not loadable, must be skipped.
            File.WriteAllText(Path.Combine(aDir, "save.meta"), "meta");

            string recent = _provider.GetMostRecentProfileId("save.json");

            Assert.IsNull(recent,
                "A profile with only a sidecar (no save or backup) must not be returned.");
        }

        [Test]
        public void Exists_ReturnsExpectedResult_BasedOnFilesPresent()
        {
            // Arrange
            string profileId = "profile_exists_test";
            string fileName = "save.json";
            string fullPath = Path.Combine(_testBasePath, profileId, fileName);
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            // Assert 1: Mặc định chưa có = False
            Assert.IsFalse(_provider.Exists(profileId, fileName), "Should return false initially.");

            // Ghi file chính
            _provider.WriteImmediate(profileId, fileName, "data");

            // Assert 2: Có file chính = True
            Assert.IsTrue(_provider.Exists(profileId, fileName), "Should return true when primary file exists.");

            // Ghi lần 2 đè lên để sinh ra file backup, sau đó cố tình xóa file chính đi
            _provider.WriteImmediate(profileId, fileName, "data2");
            File.Delete(fullPath);

            // Assert 3: Không có file chính nhưng có file backup = True
            Assert.IsTrue(File.Exists(backupPath), "Sanity check: Backup file should exist now.");
            Assert.IsTrue(_provider.Exists(profileId, fileName), "Should return true even if only the backup file exists.");
        }

        #endregion Profile Management Tests

        #region Path security (#12)

        // -----------------------------------------------------------------
        // Validation strict — null/empty/whitespace/reserved names
        // -----------------------------------------------------------------

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        public void Validation_NullEmptyOrWhitespaceProfileId_Throws(string invalidId)
        {
            Assert.Throws<System.ArgumentException>(() =>
                _provider.WriteImmediate(invalidId, "save.dat", "data"),
                "ProfileId null/empty/whitespace phải reject.");
        }

        [TestCase(".")]
        [TestCase("..")]
        public void Validation_ReservedProfileId_Throws(string reserved)
        {
            Assert.Throws<System.ArgumentException>(() =>
                _provider.WriteImmediate(reserved, "save.dat", "data"),
                $"ProfileId '{reserved}' là reserved name, phải reject để chống path traversal.");
        }

        [Test]
        public void Validation_DoubleDot_RejectedAcrossAllReadWriteEntryPoints()
        {
            // Đảm bảo cả các entry point Read cũng reject '..' — chống vector audit gốc nêu.
            Assert.Throws<System.ArgumentException>(() => _provider.WriteImmediate("..", "save.dat", "data"));
            Assert.Throws<System.ArgumentException>(() =>
                Task.Run(() => _provider.WriteAsync("..", "save.dat", "data")).GetAwaiter().GetResult());
            Assert.Throws<System.ArgumentException>(() =>
                Task.Run(() => _provider.ReadAsync("..", "save.dat")).GetAwaiter().GetResult());
            Assert.Throws<System.ArgumentException>(() =>
                Task.Run(() => _provider.ReadBackupAsync("..", "save.dat")).GetAwaiter().GetResult());
            Assert.Throws<System.ArgumentException>(() => _provider.Exists("..", "save.dat"));
            Assert.Throws<System.ArgumentException>(() => _provider.DeleteProfile(".."));
        }

        // -----------------------------------------------------------------
        // Containment check — fileName traversal (vector audit không tracked)
        // -----------------------------------------------------------------

        [Test]
        public void Containment_FilenameWithParentTraversal_OnWriteImmediate_Throws()
        {
            // ValidateProfileId không bắt vì traversal ở fileName, không phải profileId.
            // Containment check sau Path.GetFullPath phải catch.
            Assert.Throws<System.ArgumentException>(() =>
                _provider.WriteImmediate("valid-profile", "../../escape.txt", "data"));
        }

        [Test]
        public void Containment_FilenameWithParentTraversal_OnWriteAsync_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                Task.Run(() => _provider.WriteAsync("valid-profile", "../../escape.txt", "data"))
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void Containment_FilenameWithParentTraversal_OnReadAsync_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                Task.Run(() => _provider.ReadAsync("valid-profile", "../../etc/passwd"))
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void Containment_FilenameWithParentTraversal_OnReadBackupAsync_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                Task.Run(() => _provider.ReadBackupAsync("valid-profile", "../../etc/passwd"))
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void Containment_FilenameAsAbsolutePath_Throws()
        {
            // Path.Combine(base, "validprofile", "/tmp/x") = "/tmp/x" — Path.Combine drop earlier parts
            // khi gặp absolute path. Containment check phát hiện vì /tmp/x không nằm trong base.
            string absolutePath = Path.Combine(Path.GetTempPath(), "absolute_escape.txt");

            Assert.Throws<System.ArgumentException>(() =>
                _provider.WriteImmediate("valid-profile", absolutePath, "data"));
        }

        [Test]
        public void Containment_FilenameInExistsCheck_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                _provider.Exists("valid-profile", "../../escape.txt"));
        }

        // -----------------------------------------------------------------
        // Sanity — valid inputs không throw sau khi siết validation
        // -----------------------------------------------------------------

        [Test]
        public void ValidationAndContainment_NormalCase_PassesThrough()
        {
            // Đảm bảo strict validation không phá hành vi bình thường.
            Assert.DoesNotThrow(() => _provider.WriteImmediate("normal-profile", "save.json", "data"));
            Assert.DoesNotThrow(() => _provider.Exists("normal-profile", "save.json"));
            Assert.DoesNotThrow(() =>
                Task.Run(() => _provider.ReadAsync("normal-profile", "save.json"))
                    .GetAwaiter().GetResult());
        }

        #endregion Path security
    }
}