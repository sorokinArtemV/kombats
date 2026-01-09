using Combats.Battle.Infrastructure.DependencyInjection;
using Combats.Battle.Infrastructure.Persistence.EF.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
builder.Logging.ClearProviders();
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Logging.AddSerilog();

// Register shared Battle services
builder.Services.AddBattleApplication(builder.Configuration);
builder.Services.AddBattleInfrastructure(builder.Configuration);
builder.Services.AddBattleWorkers();

var host = builder.Build();

// Ensure database is created/migrated
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
    
    // Apply migrations for BattleDbContext (includes battles, player_profiles, and inbox/outbox tables)
    await dbContext.Database.MigrateAsync();
}

Log.Information("Battle Worker starting...");

try
{
    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}

