namespace GPOSettingsExplorer.Services;

public static class StoragePaths
{
    public static string Root
    {
        get
        {
            var portable = Path.Combine(AppContext.BaseDirectory, "Data");
            try
            {
                Directory.CreateDirectory(portable);
                var probe = Path.Combine(portable, $".write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return portable;
            }
            catch
            {
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GPOSettingsExplorer");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }
    }

    public static string GpoBackups
    {
        get
        {
            var path = Path.Combine(Root, "Backups", "GPO");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string WmiBackups
    {
        get
        {
            var path = Path.Combine(Root, "Backups", "WMI");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string Audit
    {
        get
        {
            var path = Path.Combine(Root, "Audit");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string Exports
    {
        get
        {
            var path = Path.Combine(Root, "Exports");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
