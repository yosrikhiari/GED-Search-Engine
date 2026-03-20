using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using GED.Infrastructure.Data;

namespace GED.Infrastructure.Data;

public class GedDbContextFactory : IDesignTimeDbContextFactory<GedDbContext>
{
    public GedDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GedDbContext>();

        var connectionString = "Server=localhost,1433;Database=ged_db;User Id=sa;Password=GedPass_2024!;TrustServerCertificate=True;";
        optionsBuilder.UseSqlServer(connectionString);

        return new GedDbContext(optionsBuilder.Options);
    }
}
