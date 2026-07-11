using Shop.Events;

namespace Payment.Domain.Entities;

public enum PaymentStatus
{
    Pending,
    Completed,
    Failed
}

public class Payment
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Payment() { }

    public static Payment Create(Guid orderId, Guid productId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than 0.", nameof(quantity));

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = productId,
            Quantity = quantity,
            Amount = quantity * 100m,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkAsPaid()
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot complete payment in {Status} state. PaymentId={Id}");

        Status = PaymentStatus.Completed;
        PaidAt = DateTime.UtcNow;
    }

    public OrderPaidEvent ToPaidEvent()
    {
        return new OrderPaidEvent(OrderId, ProductId, Quantity, PaidAt ?? DateTime.UtcNow);
    }
}
