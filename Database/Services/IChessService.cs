using System.Collections.Generic;

namespace Database.Services
{
    public interface IChessService
    {
        void NewGame();
        string GetFen();
        bool ApplyMove(string algebraicMove);
        bool IsValidMove(string algebraicMove);
        bool IsCheckmate();
        bool IsStalemate();
        Player Turn { get; }
        List<string> GetLegalMoves();
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
