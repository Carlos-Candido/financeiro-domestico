namespace Finance.Infrastructure;

public static class FinanceDataPath
{
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

        if (string.IsNullOrWhiteSpace(dataDirectory) || !Path.IsPathRooted(dataDirectory))
            throw new InvalidOperationException("O diretório local de dados do usuário precisa ser um caminho absoluto.");

        var directory = Path.Combine(dataDirectory, "Finance");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "finance.db");
    }
}
