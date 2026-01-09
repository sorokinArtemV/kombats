using Combats.Battle.Api.DevOnly;
using Combats.Battle.Infrastructure.DependencyInjection;
using Combats.Battle.Infrastructure.Persistence.EF.DbContext;
using Combats.Battle.Infrastructure.Realtime.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// Register shared Battle services
builder.Services.AddBattleApplication(builder.Configuration);
builder.Services.AddBattleInfrastructure(builder.Configuration);
builder.Services.AddBattleApi(builder.Configuration);

// DEV-ONLY: Register dev controllers only in Development
#if DEBUG
if (builder.Environment.IsDevelopment())
{
    // Dev controllers are in DevOnly folder and will be discovered by AddControllers()
}
#endif

var app = builder.Build();

app.UseRouting();

app.UseCors("AllowAll");

// DEV-ONLY: Add dev SignalR auth middleware (only in Development)
if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<DevSignalRAuthMiddleware>();
}

// Configure the HTTP request pipeline
app.UseHttpsRedirection();

// Enable static files (for production static content if needed)
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

// Map SignalR hub (now in Infrastructure)
app.MapHub<BattleHub>("/battlehub");

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BattleDbContext>();
    
    // Apply migrations for BattleDbContext (includes battles, player_profiles, and inbox/outbox tables)
    await dbContext.Database.MigrateAsync();
}

app.Run();

