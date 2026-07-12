# Payment 微服务

订单支付微服务，采用 DDD 四层架构，集成 MassTransit 消息队列、Redis 分布式锁、RowVersion 乐观并发控制和 MySQL 持久化。系统中并发保护机制最丰富的服务。

## 架构

| 层 | 项目 | 职责 |
|---|---|---|
| Domain | `Payment.Domain` | `Payment` 聚合根（含 RowVersion）、`PaymentStatus` 枚举、`IPaymentRepository` 接口 |
| Application | `Payment.Application` | `OrderCreatedConsumer`（创建待支付记录） |
| Infrastructure | `Payment.Infrastructure` | EF Core `PaymentDbContext`、`PaymentRepository` 实现、MassTransit Outbox 配置 |
| API | `Payment.API` | `PaymentsController`（含分布式锁 + 乐观锁异常处理）、DI 注册、Swagger |

## 技术栈

| 组件 | 技术 |
|------|------|
| 运行时 | .NET 8 (ASP.NET Core) |
| 数据库 | MySQL 8.0 (via Pomelo.EntityFrameworkCore) |
| 消息队列 | MassTransit 8.3.0 + RabbitMQ |
| 分布式锁 | Redis (StackExchange.Redis 2.8.16) |
| 发件箱模式 | MassTransit EntityFramework Outbox |
| 乐观并发控制 | EF Core RowVersion |
| 日志 | Serilog (控制台 + 按天滚动文件) |
| API 文档 | Swagger / Swashbuckle |
| 部署 | Docker |

## 领域模型

### Payment（支付聚合根）

| 属性 | 类型 | 说明 |
|------|------|------|
| Id | Guid | 支付记录唯一标识 |
| OrderId | Guid | 关联订单 ID（唯一索引） |
| ProductId | Guid | 商品 ID |
| Quantity | int | 数量 |
| Amount | decimal(18,2) | 金额（Quantity * 100） |
| Status | PaymentStatus | 状态：Pending / Completed / Failed |
| CreatedAt | DateTime | 创建时间 |
| PaidAt | DateTime? | 支付完成时间 |
| RowVersion | byte[] | EF Core 乐观并发控制（`IsRowVersion()`） |

### PaymentStatus（枚举）

- `Pending` — 待支付
- `Completed` — 已支付
- `Failed` — 支付失败

## API 接口

### GET `/api/payments/{orderId}` — 查询支付状态

**响应 200**:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 200.00,
  "quantity": 2,
  "status": "Pending",
  "createdAt": "2026-07-10T00:00:00Z",
  "paidAt": null
}
```

### POST `/api/payments/{orderId}/pay` — 发起支付

**响应 200**:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 200.00,
  "status": "Completed",
  "paidAt": "2026-07-10T00:00:05Z"
}
```

**错误响应**:
- `404 Not Found` — 支付记录不存在
- `409 Conflict` — 乐观锁并发冲突（支付记录已被其他请求修改）或 Redis 分布式锁获取失败（正在处理中）
- `400 Bad Request` — 状态非 Pending（已支付或已失败）

## 事件驱动

### 消费事件

| 事件 | 来源 | 消费者 | 行为 |
|------|------|--------|------|
| OrderCreatedEvent | Shop 服务 | OrderCreatedConsumer | 创建待支付记录（Amount = Quantity * 100） |

### 发布事件

| 事件 | 目标 | 触发时机 |
|------|------|----------|
| OrderPaidEvent | WMS 服务 + Shop 服务 | 支付成功（通过 `IPublishEndpoint`，Outbox 事务性发送） |

### 消息可靠性

- MassTransit **EntityFramework Outbox** 模式（MySQL）
- `AddInboxStateEntity()` 入站消息去重
- `AddOutboxMessageEntity()` 出站消息持久化
- 重试策略：3 次间隔 5 秒
- 队列：Durable=true, AutoDelete=false, PurgeOnStartup=false

## 并发保护（系统中最丰富）

| 机制 | 位置 | 说明 |
|------|------|------|
| Redis 分布式锁 | `PaymentsController` | `payment_lock:{orderId}`, 10 秒 TTL，防同一订单并发支付 |
| RowVersion 乐观锁 | `Payment` 实体 | EF Core `IsRowVersion()`，自动检测并发冲突 |
| DbUpdateConcurrencyException | `PaymentsController` | 捕获乐观锁异常返回 409 Conflict + 重试提示 |
| 状态机保护 | `Payment.MarkAsPaid()` | Status != Pending 时抛 InvalidOperationException |
| 业务状态二次校验 | `PaymentsController` | 获取分布式锁后再次检查 `Status != Pending` |
| OrderId 唯一索引 | 数据库层 | 一个订单只能有一条支付记录 |
| MassTransit Inbox | 全部消费者 | 消息去重 |

## 依赖关系

```
Payment.API
  ├── Payment.Infrastructure
  │     ├── Payment.Application
  │     │     └── Payment.Domain
  │     └── Payment.Domain
  └── shared/Shop.Events
```

## 环境配置

| 配置文件 | 适用环境 | 日志级别 | 数据库连接 |
|----------|:--------:|:--------:|:----------:|
| `appsettings.json` | 基础公共 | — | Docker 内部 `payment-mysql` |
| `appsettings.Development.json` | Development | Debug | `localhost:3309` |
| `appsettings.Production.json` | Production | Warning | Docker 内部 (环境变量覆写) |

## 本地运行

```bash
dotnet run --project services/Payment/src/Payment.API/Payment.API.csproj
```

Docker Compose 方式（推荐）：

```bash
docker-compose up -d payment-api
```

## 数据库

- 数据库：payment_db（MySQL 8.0，端口 3309）
- 表：payments, MassTransitInboxState, MassTransitOutboxMessage, MassTransitOutboxState
- ORM：Entity Framework Core（Code-First）
- 启动时自动创建表（`EnsureCreated`）
- 支持并发控制（RowVersion 乐观锁）