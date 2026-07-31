using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AiProxy.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` at design time to generate/apply migrations; never used at runtime.</summary>
public sealed class AiProxyDbContextFactory : IDesignTimeDbContextFactory<AiProxyDbContext>
{
    public AiProxyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__aiproxydb")
            ?? "Host=localhost;Database=aiproxy;Username=aiproxy;Password=design-time-only";
        var optionsBuilder = new DbContextOptionsBuilder<AiProxyDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AiProxyDbContext(optionsBuilder.Options);
    }
}
