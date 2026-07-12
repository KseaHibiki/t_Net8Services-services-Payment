using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Payment.Domain.Interfaces;
using Payment.Domain.Entities;
using MassTransit;
using StackExchange.Redis;

namespace Payment.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IDatabase _redis;

    public PaymentsController(
        IPaymentRepository paymentRepository,
        IPublishEndpoint publishEndpoint,
        IConnectionMultiplexer redis)
    {
        _paymentRepository = paymentRepository;
        _publishEndpoint = publishEndpoint;
        _redis = redis.GetDatabase();
    }

    /// <summary>
    /// 查询订单的支付状态
    /// </summary>
    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetPayment(Guid orderId)
    {
        Log.Information("查询支付状态: OrderId={OrderId}", orderId);

        var payment = await _paymentRepository.GetByOrderIdAsync(orderId);
        if (payment is null)
        {
            return NotFound(new { Message = $"Payment for order {orderId} not found." });
        }

        return Ok(new
        {
            payment.Id,
            payment.OrderId,
            payment.Amount,
            payment.Quantity,
            Status = payment.Status.ToString(),
            payment.CreatedAt,
            payment.PaidAt
        });
    }

    /// <summary>
    /// 处理支付（模拟付款，含 Redis 分布式锁防重复支付）
    /// </summary>
    [HttpPost("{orderId:guid}/pay")]
    public async Task<IActionResult> ProcessPayment(Guid orderId)
    {
        Log.Information("收到付款请求: OrderId={OrderId}", orderId);

        // Redis 分布式锁：防止相同订单并发支付
        var lockKey = $"payment_lock:{orderId}";
        var lockToken = Guid.NewGuid().ToString();
        var locked = await _redis.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(10));

        if (!locked)
        {
            Log.Warning("支付并发冲突: OrderId={OrderId}", orderId);
            return Conflict(new { Message = "该订单正在处理支付中，请勿重复操作" });
        }

        try
        {
            var payment = await _paymentRepository.GetByOrderIdAsync(orderId);
            if (payment is null)
            {
                Log.Warning("支付记录未找到: OrderId={OrderId}", orderId);
                return NotFound(new { Message = "支付记录未找到，请先创建订单" });
            }

            if (payment.Status != PaymentStatus.Pending)
            {
                Log.Warning("支付状态异常，无法重复支付: OrderId={OrderId}, Status={Status}",
                    orderId, payment.Status);
                return BadRequest(new
                {
                    Message = $"支付状态异常，当前状态: {payment.Status}，无法支付",
                    CurrentStatus = payment.Status.ToString()
                });
            }

            // 业务逻辑：标记已付
            payment.MarkAsPaid();
            await _paymentRepository.UpdateAsync(payment);

            // 发布 OrderPaidEvent（WMS 和 Shop 同时消费）
            var paidEvent = payment.ToPaidEvent();
            await _publishEndpoint.Publish(paidEvent);

            await _paymentRepository.SaveChangesAsync();

            Log.Information("支付成功: OrderId={OrderId}, PaymentId={PaymentId}, Amount={Amount}",
                orderId, payment.Id, payment.Amount);

            return Ok(new
            {
                payment.Id,
                payment.OrderId,
                payment.Amount,
                Status = payment.Status.ToString(),
                payment.PaidAt
            });
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "支付处理业务异常: OrderId={OrderId}", orderId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // 乐观锁并发冲突：Payment 记录在读取后被其他请求修改，RowVersion 不匹配
            Log.Warning(ex, "乐观锁并发冲突，支付记录已被其他请求修改: OrderId={OrderId}", orderId);
            return Conflict(new
            {
                Message = "支付记录已被其他请求修改，请刷新页面后重试",
                ConcurrencyConflict = true
            });
        }
        finally
        {
            await _redis.LockReleaseAsync(lockKey, lockToken);
        }
    }
}
