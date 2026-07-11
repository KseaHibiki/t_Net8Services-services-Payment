namespace Payment.Domain.Interfaces;

public interface IPaymentRepository
{
    Task<Entities.Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.Payment payment, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.Payment payment, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
