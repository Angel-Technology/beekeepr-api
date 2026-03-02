namespace BuzzKeepr.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; init; } = string.Empty;
    public string Provider { get; init; } = "Postgres";
}
