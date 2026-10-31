using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ultron.Infrastructure.Persistence;

/// <summary>
/// Sadece `dotnet ef migrations add` komutu için: aracın DbContext'i uygulamayı çalıştırmadan oluşturabilmesini sağlar.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UltronDbContext>
{
    public UltronDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<UltronDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
