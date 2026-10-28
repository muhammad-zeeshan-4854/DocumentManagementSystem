namespace DocumentManagement.Web.Helper;

public class DatabaseSettings
{
    public const string SectionName = "Database";

    public int CommandTimeoutSeconds { get; set; } = 30;

    // SP calls slower than this are logged as a warning.
    public int SlowCallWarningMilliseconds { get; set; } = 1000;
}


public class ParameterSettings
{
    public const string SectionName = "Parameters";

    // Seconds dbo.Parameters stays cached.
    public int CacheSeconds { get; set; } = 60;
}

