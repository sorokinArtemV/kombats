using Microsoft.EntityFrameworkCore;
using Combats.Infrastructure.Messaging.DependencyInjection;
using Combats.Services.Battle.Consumers;
using Combats.Services.Battle.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure PostgreSQL DbContext for Battle service
builder.Services.AddDbContext<BattleDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection connection string is required");
    options.UseNpgsql(connectionString);
});

// Configure messaging
builder.Services.AddMessaging(
    builder.Configuration,
    "battle",
    x =>
    {
        x.AddConsumer<CreateBattleConsumer>();
        x.AddConsumer<EndBattleConsumer>();
    },
    messagingBuilder =>
    {
        // Specify service DbContext for outbox and inbox
        messagingBuilder.WithServiceDbContext<BattleDbContext>();
    });

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
    
    // Apply migrations for BattleDbContext (includes battles and inbox_messages tables)
    await dbContext.Database.MigrateAsync();
}

app.Run();

