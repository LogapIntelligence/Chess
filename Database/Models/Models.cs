using System.ComponentModel.DataAnnotations;

namespace Database.Models
{
    public class Engine
    {
        public long Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string FilePath { get; set; }

        public DateTime DateAdded { get; set; }

        public bool IsActive { get; set; }

        // Navigation property
        public ICollection<Batch> Batches { get; set; }
    }

    public class Batch
    {
        public long Id { get; set; }

        [Required]
        public string BatchId { get; set; } // Unique identifier for the batch

        public long EngineId { get; set; }

        public long Depth { get; set; }

        public long TotalGames { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string Status { get; set; } // Pending, InProgress, Completed

        // Additional parameters stored as JSON
        public string Parameters { get; set; }

        // Navigation properties
        public Engine Engine { get; set; }
        public ICollection<ChessGame> Games { get; set; }
    }

    public class ChessGame
    {
        public long Id { get; set; }

        public long BatchId { get; set; }

        public string Result { get; set; } // 1-0, 0-1, 1/2-1/2

        public long MoveCount { get; set; }

        public DateTime GeneratedAt { get; set; }

        // Navigation properties
        public Batch Batch { get; set; }
        public ICollection<ChessMove> Moves { get; set; }
    }

    public class ChessMove
    {
        public long Id { get; set; }

        public long ChessGameId { get; set; }

        public long MoveNumber { get; set; }

        [Required]
        public string Fen { get; set; }

        public long ZobristHash { get; set; }

        public float Evaluation { get; set; }

        // Navigation property
        public ChessGame ChessGame { get; set; }
    }
}
