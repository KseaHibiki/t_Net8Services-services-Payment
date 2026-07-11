using MassTransit;
using Serilog;
using Payment.Domain.Interfaces;
using Shop.Events;

namespace Payment.Application.Consumers;

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IPaymentRepository _paymentRepository;

    public OrderCreatedConsumer(IPaymentRepository paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var evt = context.Message;

        Log.Information("收到订单创建事件，开始创建待支付记录: OrderId={OrderId}, ProductId={ProductId}, Quantity={Quantity}",
            evt.OrderId, evt.ProductId, evt.Quantity);

        var payment = Domain.Entities.Payment.Create(evt.OrderId, evt.ProductId, evt.Quantity);
        await _paymentRepository.AddAsync(payment, context.CancellationToken);
        await _paymentRepository.SaveChangesAsync(context.CancellationToken);

        Log.Information("待支付记录已创建: PaymentId={PaymentId}, OrderId={OrderId}, Amount={Amount}",
            payment.Id, evt.OrderId, payment.Amount);
    }
}
