using Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Database.Context
{
    public class MainContext : DbContext
    {
        public MainContext(DbContextOptions<MainContext> options) : base(options)
        {
        }

        public DbSet<Engine> Engines { get; set; }
        public DbSet<Batch> Batches { get; set; }
        public DbSet<ChessGame> ChessGames { get; set; }
        public DbSet<ChessMove> ChessMoves { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<Batch>()
                .HasOne(b => b.Engine)
                .WithMany(e => e.Batches)
                .HasForeignKey(b => b.EngineId);

            modelBuilder.Entity<ChessGame>()
                .HasOne(g => g.Batch)
                .WithMany(b => b.Games)
                .HasForeignKey(g => g.BatchId);

            modelBuilder.Entity<ChessMove>()
                .HasOne(m => m.ChessGame)
                .WithMany(g => g.Moves)
                .HasForeignKey(m => m.ChessGameId);

            // Create indexes
            modelBuilder.Entity<Batch>()
                .HasIndex(b => b.BatchId)
                .IsUnique();

            modelBuilder.Entity<ChessMove>()
                .HasIndex(m => m.ZobristHash);

            modelBuilder.Entity<ChessMove>()
                .HasIndex(m => new { m.ChessGameId, m.MoveNumber });
        }
    }
}
