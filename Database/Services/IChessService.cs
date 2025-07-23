namespace Database.Services
{
    public interface IChessService
    {
        void LoadFen(string fen);
        void LoadStartingPosition();
        string GetFen();
        bool TryApplyMove(string algebraicMove);
        bool IsGameOver { get; }
        string GameResult { get; }
        Player Turn { get; }
    }

    public enum Player
    {
        White,
        Black
    }

    public enum PieceType
    {
        None,
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King
    }

    public struct Piece
    {
        public PieceType Type { get; }
        public Player Player { get; }

        public Piece(PieceType type, Player player)
        {
            Type = type;
            Player = player;
        }
    }
}