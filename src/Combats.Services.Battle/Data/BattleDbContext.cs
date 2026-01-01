using Microsoft.EntityFrameworkCore;
using Combats.Infrastructure.Messaging.Inbox;
using Combats.Services.Battle.Data.Entities;

namespace Combats.Services.Battle.Data;

public class BattleDbContext : DbContext
{
    public BattleDbContext(DbContextOptions<BattleDbContext> options) : base(options)
    {
    }

    public DbSet<BattleEntity> Battles { get; set; } = null!;
    public DbSet<InboxMessage> InboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BattleEntity>(entity =>
        {
            entity.ToTable("battles");
            entity.HasKey(e => e.BattleId);
            entity.Property(e => e.BattleId).ValueGeneratedNever();
            entity.Property(e => e.MatchId).IsRequired();
            entity.Property(e => e.PlayerAId).IsRequired();
            entity.Property(e => e.PlayerBId).IsRequired();
            entity.Property(e => e.State).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.EndedAt).IsRequired(false);
            entity.Property(e => e.EndReason).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.WinnerPlayerId).IsRequired(false);
            entity.HasIndex(e => e.MatchId);
        });

        // Configure inbox entity
        modelBuilder.Entity<InboxMessage>(entity => entity.ConfigureInboxMessage());
    }
}

