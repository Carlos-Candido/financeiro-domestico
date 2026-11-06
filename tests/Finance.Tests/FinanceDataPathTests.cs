using Finance.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Runtime.Versioning;
using Xunit;

namespace Finance.Tests;

[CollectionDefinition("Finance data path environment", DisableParallelization = true)]
public sealed class FinanceDataPathCollection
{
    public const string Name = "Finance data path environment";
}

[Collection(FinanceDataPathCollection.Name)]
[SupportedOSPlatform("linux")]
public sealed class FinanceDataPathTests
{
    public FinanceDataPathTests()
    {
        SqliteProviderConfiguration.Initialize();
    }

    [LinuxFact]
    public void New_database_is_private_below_traversable_parent()
    {
        InXdgDataHome(dataHome =>
        {
            var databasePath = FinanceDataPath.GetDatabasePath();

            Assert.Equal(Path.Combine(dataHome, "Finance", "finance.db"), databasePath);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(databasePath)!));

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE synthetic_data (value TEXT NOT NULL); INSERT INTO synthetic_data VALUES ('fixture');";
            command.ExecuteNonQuery();

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(databasePath));
        });
    }

    [LinuxFact]
    public void Existing_database_is_restricted_without_losing_saved_data()
    {
        InXdgDataHome(dataHome =>
        {
            var financeDirectory = Path.Combine(dataHome, "Finance");
            Directory.CreateDirectory(financeDirectory);
            var databasePath = Path.Combine(financeDirectory, "finance.db");
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE saved_data (marker TEXT NOT NULL); INSERT INTO saved_data VALUES ('preserved-fixture-marker');";
                command.ExecuteNonQuery();
            }

            File.SetUnixFileMode(databasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            File.SetUnixFileMode(financeDirectory, TraversableDirectoryMode);

            Assert.Equal(databasePath, FinanceDataPath.GetDatabasePath());
            Assert.Equal(PrivateDirectoryMode, File.GetUnixFileMode(financeDirectory));
            Assert.Equal(PrivateFileMode, File.GetUnixFileMode(databasePath));

            using var verifyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
            verifyConnection.Open();
            using var verifyCommand = verifyConnection.CreateCommand();
            verifyCommand.CommandText = "SELECT marker FROM saved_data";
            Assert.Equal("preserved-fixture-marker", verifyCommand.ExecuteScalar());
        });
    }

    [LinuxTheory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    public void Existing_sqlite_auxiliary_files_become_private_without_rewriting_contents(string suffix)
    {
        InXdgDataHome(dataHome =>
        {
            var financeDirectory = Path.Combine(dataHome, "Finance");
            Directory.CreateDirectory(financeDirectory);
            File.SetUnixFileMode(financeDirectory, TraversableDirectoryMode);
            var auxiliaryPath = Path.Combine(financeDirectory, "finance.db" + suffix);
            var originalContents = System.Text.Encoding.UTF8.GetBytes("synthetic auxiliary fixture: " + suffix);
            File.WriteAllBytes(auxiliaryPath, originalContents);
            File.SetUnixFileMode(auxiliaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            FinanceDataPath.GetDatabasePath();

            Assert.Equal(PrivateFileMode, File.GetUnixFileMode(auxiliaryPath));
            Assert.Equal(originalContents, File.ReadAllBytes(auxiliaryPath));
        });
    }

    [LinuxFact]
    public void Database_symlink_is_rejected_without_changing_target()
    {
        InXdgDataHome(dataHome =>
        {
            var financeDirectory = Path.Combine(dataHome, "Finance");
            Directory.CreateDirectory(financeDirectory);
            File.SetUnixFileMode(financeDirectory, TraversableDirectoryMode);
            var targetPath = Path.Combine(dataHome, "foreign-database-fixture");
            var targetContents = System.Text.Encoding.UTF8.GetBytes("foreign synthetic database target");
            File.WriteAllBytes(targetPath, targetContents);
            var targetMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            File.SetUnixFileMode(targetPath, targetMode);
            var databasePath = Path.Combine(financeDirectory, "finance.db");
            File.CreateSymbolicLink(databasePath, targetPath);

            Assert.Throws<IOException>(() => FinanceDataPath.GetDatabasePath());
            Assert.Equal(targetContents, File.ReadAllBytes(targetPath));
            Assert.Equal(targetMode, File.GetUnixFileMode(targetPath));
        });
    }

    [LinuxFact]
    public void Finance_directory_symlink_is_rejected_without_changing_target()
    {
        InXdgDataHome(dataHome =>
        {
            var targetDirectory = Path.Combine(dataHome, "foreign-directory-fixture");
            Directory.CreateDirectory(targetDirectory);
            var targetMode = TraversableDirectoryMode;
            File.SetUnixFileMode(targetDirectory, targetMode);
            var targetFile = Path.Combine(targetDirectory, "sentinel");
            var targetContents = System.Text.Encoding.UTF8.GetBytes("foreign synthetic directory target");
            File.WriteAllBytes(targetFile, targetContents);
            File.SetUnixFileMode(targetFile, PrivateFileMode);
            var financeDirectory = Path.Combine(dataHome, "Finance");
            Directory.CreateSymbolicLink(financeDirectory, targetDirectory);

            Assert.Throws<IOException>(() => FinanceDataPath.GetDatabasePath());
            Assert.Equal(targetContents, File.ReadAllBytes(targetFile));
            Assert.Equal(targetMode, File.GetUnixFileMode(targetDirectory));
            Assert.Equal(PrivateFileMode, File.GetUnixFileMode(targetFile));
        });
    }

    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode TraversableDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static void InXdgDataHome(Action<string> action)
    {
        var previousXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataHome = Path.Combine(Path.GetTempPath(), "finance-data-path-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataHome);
        try
        {
            File.SetUnixFileMode(dataHome, TraversableDirectoryMode);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataHome);
            action(dataHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdgDataHome);
            Directory.Delete(dataHome, recursive: true);
        }
    }
}

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "FinanceDataPath filesystem permission tests require Linux.";
    }
}

public sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "FinanceDataPath filesystem permission tests require Linux.";
    }
}
