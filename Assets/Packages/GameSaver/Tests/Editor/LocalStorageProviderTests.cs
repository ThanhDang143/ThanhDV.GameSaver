using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.GameSaver.Common;
using ThanhDV.GameSaver.Infrastructure;

namespace ThanhDV.GameSaver.Tests
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
            _testBasePath = Path.Combine(Path.GetTempPath(), "GameSaverTests_" + System.Guid.NewGuid());
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
        public void WriteImmediate_CreatesDirectory_WhenNestedFolderIsUsed()
        {
            // Arrange (Tạo profileId dưới dạng chuỗi thư mục lồng nhau)
            string profileId = "profile_nested/subfolder/deep";
            string fileName = "save.dat";
            string data = "test path creation";

            // Act
            _provider.WriteImmediate(profileId, fileName, data);

            // Assert
            string expectedFilePath = Path.Combine(_testBasePath, profileId, fileName);
            Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(expectedFilePath)), "Nested directories should be created.");
            Assert.IsTrue(File.Exists(expectedFilePath));
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
            string recentProfile = _provider.GetMostRecentProfileId();

            // Assert
            Assert.AreEqual("profile_new", recentProfile, "Should return the profile containing the most recently modified file.");
        }

        [Test]
        public void GetMostRecentProfileId_ReturnsNull_WhenNoProfilesExist()
        {
            // Act
            string recentProfile = _provider.GetMostRecentProfileId();

            // Assert
            Assert.IsNull(recentProfile, "Should return null if no profiles/files exist.");
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
    }
}