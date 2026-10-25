using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Domain;

namespace ShopTest.Core.Data;

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<SavedItem> SavedItems => Set<SavedItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => c.Slug).IsUnique();
            entity.Property(c => c.Name).HasMaxLength(128).IsRequired();
            entity.Property(c => c.Slug).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.Property(i => i.Name).HasMaxLength(200).IsRequired();
            entity.Property(i => i.Description).HasMaxLength(1000);
            entity.Property(i => i.Price).HasPrecision(10, 2);
            entity.HasOne(i => i.Category)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasIndex(c => new { c.UserId, c.ItemId }).IsUnique();
            entity.HasOne(c => c.Item)
                .WithMany()
                .HasForeignKey(c => c.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedItem>(entity =>
        {
            entity.HasIndex(s => new { s.UserId, s.ItemId }).IsUnique();
            entity.HasOne(s => s.Item)
                .WithMany()
                .HasForeignKey(s => s.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(o => o.Subtotal).HasPrecision(10, 2);
            entity.Property(o => o.Shipping).HasPrecision(10, 2);
            entity.Property(o => o.Tax).HasPrecision(10, 2);
            entity.Property(o => o.Total).HasPrecision(10, 2);
            entity.Property(o => o.ShippingAddress).HasMaxLength(500);
            entity.HasMany(o => o.Lines)
                .WithOne()
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(o => o.StatusHistory)
                .WithOne()
                .HasForeignKey(h => h.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.Property(l => l.NameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(l => l.UnitPrice).HasPrecision(10, 2);
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(h => h.ChangedBy).HasMaxLength(128);
            entity.Property(h => h.Reason).HasMaxLength(500);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(p => p.IdempotencyKey).IsUnique();
            entity.Property(p => p.Amount).HasPrecision(10, 2);
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(p => p.CardBrand).HasMaxLength(32);
            entity.Property(p => p.CardLast4).HasMaxLength(4);
            entity.Property(p => p.Reference).HasMaxLength(64);
            entity.Property(p => p.IdempotencyKey).HasMaxLength(128);
            entity.Property(p => p.FailureReason).HasMaxLength(256);
        });
    }
}
