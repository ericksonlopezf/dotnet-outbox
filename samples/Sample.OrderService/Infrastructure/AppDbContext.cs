using EricksonLopez.Outbox.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;

namespace Sample.OrderService.Infrastructure;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure the Order entity (defaults to mapping to the Orders table)
        modelBuilder.Entity<Order>().HasKey(x => x.Id);

        // Add the necessary tables for Outbox and Inbox
        modelBuilder.ApplyOutboxEntityConfigurations();
    }
}
