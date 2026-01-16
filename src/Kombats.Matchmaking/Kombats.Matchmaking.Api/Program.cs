using Combats.Infrastructure.Messaging.DependencyInjection;
using Kombats.Contracts.Battle;
using Kombats.Matchmaking.Api.Controllers;
using Kombats.Matchmaking.Api.Workers;
using Kombats.Matchmaking.Application.Abstractions;
using Kombats.Matchmaking.Application.UseCases;
using Kombats.Matchmaking.Infrastructure.Data;
using Kombats.Matchmaking.Infrastructure.Messaging.Consumers;
using Kombats.Matchmaking.Infrastructure.Options;
using Kombats.Matchmaking.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Configure PostgreSQL DbContext for Matchmaking service
builder.Services.AddDbContext<MatchmakingDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                           ?? throw new InvalidOperationException("DefaultConnection connection string is required");
    options.UseNpgsql(connectionString);
});

// Configure Redis
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

// Configure Matchmaking Redis options
builder.Services.Configure<MatchmakingRedisOptions>(
    builder.Configuration.GetSection(MatchmakingRedisOptions.SectionName));

// Configure Matchmaking Worker options
builder.Services.Configure<MatchmakingWorkerOptions>(
    builder.Configuration.GetSection(MatchmakingWorkerOptions.SectionName));

// Configure Match Timeout Worker options
builder.Services.Configure<MatchTimeoutWorkerOptions>(
    builder.Configuration.GetSection(MatchTimeoutWorkerOptions.SectionName));

// Register Application ports (implemented by Infrastructure)
builder.Services.AddScoped<IMatchQueueStore, RedisMatchQueueStore>();
builder.Services.AddScoped<IPlayerMatchStatusStore, RedisPlayerMatchStatusStore>();
builder.Services.AddScoped<IMatchRepository, MatchRepository>();

// Register Application services
builder.Services.AddScoped<QueueService>();
builder.Services.AddScoped<MatchmakingService>();

// Configure messaging with typed DbContext for outbox/inbox support
builder.Services.AddMessaging<MatchmakingDbContext>(
    builder.Configuration,
    "matchmaking",
    x =>
    {
        x.AddConsumer<BattleCreatedConsumer>();
    },
    messagingBuilder =>
    {
        // Register entity name mappings
        messagingBuilder.Map<CreateBattle>("CreateBattle");
        messagingBuilder.Map<BattleCreated>("BattleCreated");
    });

// Register background workers
builder.Services.AddHostedService<MatchmakingWorker>();
builder.Services.AddHostedService<MatchTimeoutWorker>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MatchmakingDbContext>();

    // Apply migrations for MatchmakingDbContext (includes matches table)
    await dbContext.Database.MigrateAsync();
}

app.Run();