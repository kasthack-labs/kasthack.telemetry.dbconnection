using Microsoft.EntityFrameworkCore;

namespace kasthack.telemetry.dbconnection.sample;

/// <summary>Minimal EF Core context backed by SQLite in-memory.</summary>
public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>().HasKey(p => p.Id);
}

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
