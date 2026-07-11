using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence;

public class PaymentDbContext : DbContext
{
    public DbSet<Payment.Domain.Entities.Payment> Payments => Set<Payment.Domain.Entities.Payment>();

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Payment.Domain.Entities.Payment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.OrderId).IsUnique(); // 一个订单只能有一条支付记录
            b.Property(p => p.ProductId).IsRequired();
            b.Property(p => p.Quantity).IsRequired();
            b.Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(p => p.CreatedAt).IsRequired();
            b.Property(p => p.PaidAt);
            b.Property(p => p.RowVersion).IsRowVersion().IsRequired();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
