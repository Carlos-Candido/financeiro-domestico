using System.Runtime.InteropServices;

namespace Finance.Infrastructure;

public static class SqliteProviderConfiguration
{
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        SQLitePCL.raw.SetProvider(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new SQLitePCL.SQLite3Provider_winsqlite3()
            : new SQLitePCL.SQLite3Provider_sqlite3());
    }
}
