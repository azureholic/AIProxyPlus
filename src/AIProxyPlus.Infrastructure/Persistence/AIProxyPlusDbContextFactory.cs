using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIProxyPlus.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` at design time to generate/apply migrations; never used at runtime.</summary>
public sealed class AIProxyPlusDbContextFactory : IDesignTimeDbContextFactory<AIProxyPlusDbContext>
{
    public AIProxyPlusDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__aiproxyplusdb")
            ?? "Host=localhost;Database=aiproxyplus;Username=aiproxyplus;Password=design-time-only";
        var optionsBuilder = new DbContextOptionsBuilder<AIProxyPlusDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AIProxyPlusDbContext(optionsBuilder.Options);
    }
}
