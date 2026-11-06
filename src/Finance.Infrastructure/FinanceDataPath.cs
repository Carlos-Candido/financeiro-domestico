using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Finance.Infrastructure;

public static class FinanceDataPath
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static string GetDatabasePath()
    {
        var dataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (OperatingSystem.IsLinux())
        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgDataHome))
                dataDirectory = xdgDataHome;
            else if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                    home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrWhiteSpace(home))
                    throw new InvalidOperationException("Não foi possível determinar o diretório local de dados do usuário.");
                dataDirectory = Path.Combine(home, ".local", "share");
            }
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("A privacidade dos dados financeiros só é configurada no Linux e no Windows.");

        if (string.IsNullOrWhiteSpace(dataDirectory) || !Path.IsPathRooted(dataDirectory))
            throw new InvalidOperationException("O diretório local de dados do usuário precisa ser um caminho absoluto.");

        var directory = Path.Combine(dataDirectory, "Finance");
        var databasePath = Path.Combine(directory, "finance.db");
        var walPath = databasePath + "-wal";
        var shmPath = databasePath + "-shm";
        var journalPath = databasePath + "-journal";

        RejectReparsePoint(directory);
        var directoryAttributes = GetAttributesIfPresent(directory);
        if (directoryAttributes is null)
            CreateFinanceDirectory(dataDirectory, directory);
        RejectReparsePoint(directory);
        directoryAttributes = GetAttributesIfPresent(directory);
        if (directoryAttributes is null || (directoryAttributes.Value & FileAttributes.Directory) == 0)
            throw new IOException("Não foi possível criar o diretório de dados financeiros.");


        // Inspect every final entry before changing permissions or opening/creating any file.
        ValidateDatabaseEntry(databasePath);
        ValidateAuxiliaryEntry(walPath);
        ValidateAuxiliaryEntry(shmPath);
        ValidateAuxiliaryEntry(journalPath);

        SetDirectoryPrivacy(directory);

        EnsureDatabaseFile(databasePath);
        SetFilePrivacy(databasePath);
        SetExistingAuxiliaryFilePrivacy(walPath);
        SetExistingAuxiliaryFilePrivacy(shmPath);
        SetExistingAuxiliaryFilePrivacy(journalPath);

        return databasePath;
    }

    private static void CreateFinanceDirectory(string dataDirectory, string directory)
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(directory, PrivateDirectoryMode);
        }
        else
        {
            Directory.CreateDirectory(directory);
        }
    }



    private static void SetDirectoryPrivacy(string directory)
    {
        RejectReparsePoint(directory);
        if (OperatingSystem.IsWindows())
            SetWindowsDirectoryAcl(directory);
        else if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(directory, PrivateDirectoryMode);
        else
            throw new PlatformNotSupportedException("A privacidade do diretório Finance só é configurada no Linux e no Windows.");
    }

    private static void EnsureDatabaseFile(string databasePath)
    {
        RejectReparsePoint(databasePath);
        var attributes = GetAttributesIfPresent(databasePath);
        if (attributes is not null)
        {
            if ((attributes.Value & FileAttributes.Directory) != 0)
                throw new IOException("O caminho do banco de dados existe, mas não é um arquivo.");
            return;
        }

        try
        {
            CreateDatabaseFile(databasePath);
        }
        catch (IOException)
        {
            // Another process may have created the database concurrently. Accept only
            // an ordinary file at the expected path; a link or other entry fails closed.
            RejectReparsePoint(databasePath);
            attributes = GetAttributesIfPresent(databasePath);
            if (attributes is null || (attributes.Value & FileAttributes.Directory) != 0)
                throw;
        }

        RejectReparsePoint(databasePath);
        attributes = GetAttributesIfPresent(databasePath);
        if (attributes is null || (attributes.Value & FileAttributes.Directory) != 0)
            throw new IOException("Não foi possível criar o arquivo do banco de dados financeiro.");
    }


    private static void CreateDatabaseFile(string databasePath)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.ReadWrite | FileShare.Delete
        };
        if (OperatingSystem.IsLinux())
            options.UnixCreateMode = PrivateFileMode;

        using var _ = new FileStream(databasePath, options);
    }

    private static void ValidateDatabaseEntry(string databasePath)
    {
        RejectReparsePoint(databasePath);
        var attributes = GetAttributesIfPresent(databasePath);
        if (attributes is not null && (attributes.Value & FileAttributes.Directory) != 0)
            throw new IOException("O caminho do banco de dados existe, mas não é um arquivo.");
    }

    private static void ValidateAuxiliaryEntry(string auxiliaryPath)
    {
        RejectReparsePoint(auxiliaryPath);
        var attributes = GetAttributesIfPresent(auxiliaryPath);
        if (attributes is not null && (attributes.Value & FileAttributes.Directory) != 0)
            throw new IOException("Um arquivo auxiliar do SQLite existe como diretório.");
    }


    private static void SetFilePrivacy(string path)
    {
        RejectReparsePoint(path);
        if (OperatingSystem.IsWindows())
            SetWindowsFileAcl(path);
        else if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(path, PrivateFileMode);
        else
            throw new PlatformNotSupportedException("A privacidade dos arquivos Finance só é configurada no Linux e no Windows.");
    }

    private static void SetExistingAuxiliaryFilePrivacy(string path)
    {
        RejectReparsePoint(path);
        if (GetAttributesIfPresent(path) is not null)
            SetFilePrivacy(path);
    }

    private static void RejectReparsePoint(string path)
    {
        if (new FileInfo(path).LinkTarget is not null ||
            new DirectoryInfo(path).LinkTarget is not null)
        {
            throw new IOException($"O caminho de dados financeiros não pode ser um link: {path}");
        }

        var attributes = GetAttributesIfPresent(path);
        if (attributes is not null && (attributes.Value & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"O caminho de dados financeiros não pode ser um link: {path}");
    }

    private static FileAttributes? GetAttributesIfPresent(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsDirectoryAcl(string directory)
    {
        RejectReparsePoint(directory);
        var (currentUserSid, allowedSids) = GetWindowsAllowedSids();
        var security = new DirectorySecurity();
        security.SetOwner(currentUserSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in allowedSids)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(directory).SetAccessControl(security);
        RejectReparsePoint(directory);
        VerifyWindowsAcl(
            new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
            currentUserSid,
            allowedSids,
            isDirectory: true);
    }


    [SupportedOSPlatform("windows")]
    private static void SetWindowsFileAcl(string path)
    {
        RejectReparsePoint(path);
        var (currentUserSid, allowedSids) = GetWindowsAllowedSids();
        var security = new FileSecurity();
        security.SetOwner(currentUserSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in allowedSids)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        }

        new FileInfo(path).SetAccessControl(security);
        RejectReparsePoint(path);
        VerifyWindowsAcl(
            new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
            currentUserSid,
            allowedSids,
            isDirectory: false);
    }


    [SupportedOSPlatform("windows")]
    private static (SecurityIdentifier CurrentUserSid, HashSet<SecurityIdentifier> AllowedSids)
        GetWindowsAllowedSids()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentUserSid = identity.User
            ?? throw new IOException("Não foi possível determinar o SID do usuário atual.");
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null);
        var allowedSids = new HashSet<SecurityIdentifier> { currentUserSid, systemSid };
        return (currentUserSid, allowedSids);
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyWindowsAcl(
        FileSystemSecurity security,
        SecurityIdentifier currentUserSid,
        HashSet<SecurityIdentifier> allowedSids,
        bool isDirectory)
    {
        if (!security.AreAccessRulesProtected)
            throw new IOException("A ACL do caminho financeiro ainda herda permissões externas.");

        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier ownerSid ||
            !ownerSid.Equals(currentUserSid))
        {
            throw new IOException("O proprietário do caminho financeiro não é o usuário atual.");
        }

        var foundSids = new HashSet<SecurityIdentifier>();
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));

        foreach (AuthorizationRule authorizationRule in rules)
        {
            if (authorizationRule is not FileSystemAccessRule rule ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                rule.AccessControlType != AccessControlType.Allow ||
                rule.FileSystemRights != FileSystemRights.FullControl ||
                !allowedSids.Contains(sid))
            {
                throw new IOException("A ACL efetiva do caminho financeiro contém permissões inesperadas.");
            }

            var expectedInheritance = isDirectory
                ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
                : InheritanceFlags.None;
            if (rule.InheritanceFlags != expectedInheritance ||
                rule.PropagationFlags != PropagationFlags.None)
            {
                throw new IOException("A ACL do caminho financeiro possui regras de herança inesperadas.");
            }

            foundSids.Add(sid);
        }

        if (!foundSids.SetEquals(allowedSids))
            throw new IOException("A ACL efetiva do caminho financeiro não concede acesso somente ao usuário atual e ao LocalSystem.");
    }
}
