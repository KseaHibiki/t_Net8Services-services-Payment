# Payment Microservice

订单支付微服务，基于 .NET 8 构建，采用 DDD 分层架构，集成 MassTransit 消息队列、Redis 分布式锁、MySQL 持久化、Serilog 日志和 Swagger 文档。

## 架构分层

```
src/
├── Payment.API           # 表现层：Web API 控制器、启动配置
├── Payment.Application   # 应用层：消息消费者、业务编排
├── Payment.Domain        # 领域层：实体、枚举、仓储接口
└── Payment.Infrastructure # 基础设施层：EF Core 持久化、仓储实现
```

## 技术栈

| 组件 | 技术 |
|------|------|
| 运行时 | .NET 8 (ASP.NET Core) |
| 数据库 | MySQL 8.0 (via Pomelo.EntityFrameworkCore) |
| 消息队列 | RabbitMQ (via MassTransit) |
| 分布式锁 | Redis (StackExchange.Redis) |
| 发件箱模式 | MassTransit EntityFramework Outbox |
| 日志 | Serilog (控制台 + 文件) |
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
| RowVersion | byte[] | 乐观并发控制 |

### PaymentStatus（枚举）

- `Pending` — 待支付
- `Completed` — 已支付
- `Failed` — 支付失败

## API 接口

### GET `/api/payments/{orderId}`

查询订单的支付状态。

**响应示例：**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 200.00,
  "quantity": 2,
  "status": "Pending",
  "createdAt": "2024-01-01T00:00:00Z",
  "paidAt": null
}
```

### POST `/api/payments/{orderId}/pay`

发起支付（含 Redis 分布式锁防重复支付）。

**响应示例（成功）：**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 200.00,
  "status": "Completed",
  "paidAt": "2024-01-01T00:00:05Z"
}
```

## 事件驱动

### 消费事件

| 事件 | 来源 | 消费者 | 行为 |
|------|------|--------|------|
| OrderCreatedEvent | Shop 服务 | OrderCreatedConsumer | 创建待支付记录 |

### 发布事件

| 事件 | 目标 | 触发时机 |
|------|------|----------|
| OrderPaidEvent | WMS 服务、Shop 服务 | 支付成功 |

### 消息可靠性

- 使用 MassTransit **EntityFramework Outbox** 模式
- 支付记录保存和事件发布在一个数据库事务中原子提交
- 消息消费支持重试（3 次间隔 5 秒）

## 分布式锁

支付接口使用 Redis 分布式锁防止相同订单的并发支付：

- 锁 key：`payment_lock:{orderId}`
- 锁超时：10 秒
- 请求返回 `409 Conflict` 表示正在处理中

## 启动指南

### 前置依赖

- .NET 8 SDK
- MySQL 8.0+
- RabbitMQ
- Redis

### 连接字符串配置

编辑 `appsettings.json`：

```json
{
  "ConnectionStrings": {
    "payment": "Server=localhost;Port=3306;Database=payment_db;User=root;Password=114514;",
    "rabbitmq": "amqp://guest:guest@localhost:5672/",
    "redis": "localhost:6379"
  }
}
```

### 运行

```bash
# 还原依赖
dotnet restore Payment.sln

# 编译
dotnet build Payment.sln

# 运行
dotnet run --project src/Payment.API
```

### Docker

```bash
docker build -f src/Payment.API/Dockerfile -t payment-api .
```

## 数据库

- 数据库：payment_db
- 表：payments
- ORM：Entity Framework Core（Code-First）
- 启动时自动创建表（EnsureCreated）
- 支持并发控制（RowVersion 乐观锁）

## 依赖关系

```
Payment.API
  ├── Payment.Infrastructure
  │     ├── Payment.Application
  │     │     └── Payment.Domain
  │     └── Payment.Domain
  └── shared/Shop.Events

Payment.Infrastructure
  └── Payment.Application
        └── Payment.Domain

Payment.Application
  └── Payment.Domain

Payment.Domain
  └── shared/Shop.Events
```

## 项目依赖包

| 项目 | NuGet 包 |
|------|----------|
| Payment.API | StackExchange.Redis, Swashbuckle.AspNetCore, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Sinks.File |
| Payment.Application | MassTransit, Serilog |
| Payment.Infrastructure | MassTransit.RabbitMQ, MassTransit.EntityFrameworkCore, Microsoft.EntityFrameworkCore, Pomelo.EntityFrameworkCore.MySql |
| Payment.Domain | 无（依赖 Shop.Events 共享项目） |
