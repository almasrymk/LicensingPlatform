namespace Licensing.Tests.Infrastructure;

/// <summary>A fact that needs the SQL Server provider (migrations are SQL Server only); reported as skipped on SQLite.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!TestApp.UseSqlServer)
            Skip = "Needs SQL Server: set LICENSING_TEST_SQLSERVER=1 (migrations exist for SQL Server only).";
    }
}
