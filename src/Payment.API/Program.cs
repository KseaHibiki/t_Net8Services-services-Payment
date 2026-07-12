using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Payment.Application.Consumers;
using Payment.Domain.Interfaces;
using Payment.Infrastructure.Persistence;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Payment.API")
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} | {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/payment-api-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate:
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Service} | {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Redis
var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));

// Database
var connectionString = builder.Configuration.GetConnectionString("payment")
    ?? "Server=localhost;Port=3309;Database=payment_db;User=root;Password=114514;";
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Repositories
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

// MassTransit
// 注册 MassTransit 到依赖注入容器
builder.Services.AddMassTransit(x =>
{
    // 添加 EntityFramework 事务性发件箱，使用 PaymentDbContext 的数据库
    x.AddEntityFrameworkOutbox<PaymentDbContext>(o =>
    {
        // 后台服务每隔 1 秒查询一次发件箱表，检查是否有待发送的消息
        o.QueryDelay = TimeSpan.FromSeconds(1);
        // 使用 MySQL 数据库提供程序（适配建表和 SQL 语法）
        o.UseMySql();
    });
    // 注册消费者 OrderCreatedConsumer，稍后会绑定到具体的接收端点
    x.AddConsumer<OrderCreatedConsumer>();

    // 配置使用 RabbitMQ 作为消息传输通道
    x.UsingRabbitMq((context, cfg) =>
    {
        // 设置 RabbitMQ 连接地址，优先读取配置文件，回退到本地默认地址
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq")
            ?? "amqp://guest:guest@localhost:5672/");

        // 全局消息重试策略（可被端点级重写）：失败后间隔 5 秒，最多重试 3 次
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

        // 定义一个接收端点（即消费者监听的队列）
        cfg.ReceiveEndpoint("payment-order-created-queue", e =>
        {
            // 队列持久化：RabbitMQ 重启后队列本身不丢失
            e.Durable = true; // RabbitMQ 重启后队列不丢失

            // 禁用自动删除：即使没有消费者连接，队列也不会被自动删除
            e.AutoDelete = false; // 没消费者时也不自动删除

            // 启动时不自动清空队列中的旧消息（false 表示保留消息）
            e.PurgeOnStartup = false; // 启动时不清空消息

            // 端点级消息重试策略（会覆盖全局设置）：同样间隔 5 秒重试 3 次
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

            // 将 OrderCreatedConsumer 绑定到这个队列，处理接收到的消息
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });
    });
});

var app = builder.Build();

// Auto-migrate
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    db.Database.EnsureCreated();
    Log.Information("数据库初始化完成 (payment_db)");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.MapControllers();

Log.Information("Payment.API 启动完成，监听端口 5004");
app.Run();
