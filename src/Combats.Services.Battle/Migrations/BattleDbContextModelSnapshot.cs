using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Combats.Services.Battle.Data;

#nullable disable

namespace Combats.Services.Battle.Migrations
{
    [DbContext(typeof(BattleDbContext))]
    partial class BattleDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Combats.Services.Battle.Data.Entities.BattleEntity", b =>
                {
                    b.Property<Guid>("BattleId")
                        .HasColumnType("uuid")
                        .HasColumnName("battle_id");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid>("MatchId")
                        .HasColumnType("uuid")
                        .HasColumnName("match_id");

                    b.Property<Guid>("PlayerAId")
                        .HasColumnType("uuid")
                        .HasColumnName("player_a_id");

                    b.Property<Guid>("PlayerBId")
                        .HasColumnType("uuid")
                        .HasColumnName("player_b_id");

                    b.Property<string>("State")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("state");

                    b.HasKey("BattleId")
                        .HasName("pk_battles");

                    b.HasIndex("MatchId")
                        .HasDatabaseName("ix_battles_match_id");

                    b.ToTable("battles", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}



