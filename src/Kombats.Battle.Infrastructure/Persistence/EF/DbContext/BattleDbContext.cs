using Kombats.Battle.Infrastructure.Persistence.EF.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kombats.Battle.Infrastructure.Persistence.EF.DbContext;

public class BattleDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public BattleDbContext(DbContextOptions<BattleDbContext> options) : base(options)
    {
    }

    public DbSet<BattleEntity> Battles { get; set; } = null!;
    public DbSet<PlayerProfileEntity> PlayerProfiles { get; set; } = null!;

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

        modelBuilder.Entity<PlayerProfileEntity>(entity =>
        {
            entity.ToTable("player_profiles");
            entity.HasKey(e => e.PlayerId);
            entity.Property(e => e.PlayerId).ValueGeneratedNever();
            entity.Property(e => e.Strength).IsRequired();
            entity.Property(e => e.Stamina).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
            entity.Property(e => e.Version).IsRequired();
        });
        
        modelBuilder.AddInboxStateEntity();      // InboxState
        modelBuilder.AddOutboxMessageEntity();   // OutboxMessage
        modelBuilder.AddOutboxStateEntity();     // OutboxState
    }
}

