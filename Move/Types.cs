using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Move
{
    public interface IColor { Color Value { get; } IColor Opposite(); }
    public struct White : IColor { public Color Value => Color.White; public IColor Opposite() => new Black(); }
    public struct Black : IColor { public Color Value => Color.Black; public IColor Opposite() => new White(); }

    public enum Color : int
    {
        White = 0,
        Black = 1
    }

    public static class ColorExtensions
    {
        public static Color Flip(this Color c) => (Color)((int)c ^ (int)Color.Black);
    }

    public enum Direction : int
    {
        North = 8,
        NorthEast = 9,
        East = 1,
        SouthEast = -7,
        South = -8,
        SouthWest = -9,
        West = -1,
        NorthWest = 7,
        NorthNorth = 16,
        SouthSouth = -16
    }

    public enum PieceType : int
    {
        Pawn = 0,
        Knight = 1,
        Bishop = 2,
        Rook = 3,
        Queen = 4,
        King = 5
    }

    public enum Piece : int
    {
        WhitePawn = 0,
        WhiteKnight = 1,
        WhiteBishop = 2,
        WhiteRook = 3,
        WhiteQueen = 4,
        WhiteKing = 5,
        BlackPawn = 8,
        BlackKnight = 9,
        BlackBishop = 10,
        BlackRook = 11,
        BlackQueen = 12,
        BlackKing = 13,
        NoPiece = 14
    }

    public enum Square : int
    {
        a1, b1, c1, d1, e1, f1, g1, h1,
        a2, b2, c2, d2, e2, f2, g2, h2,
        a3, b3, c3, d3, e3, f3, g3, h3,
        a4, b4, c4, d4, e4, f4, g4, h4,
        a5, b5, c5, d5, e5, f5, g5, h5,
        a6, b6, c6, d6, e6, f6, g6, h6,
        a7, b7, c7, d7, e7, f7, g7, h7,
        a8, b8, c8, d8, e8, f8, g8, h8,
        NoSquare = 64
    }

    public enum File : int
    {
        FileA = 0, FileB = 1, FileC = 2, FileD = 3,
        FileE = 4, FileF = 5, FileG = 6, FileH = 7
    }

    public enum Rank : int
    {
        Rank1 = 0, Rank2 = 1, Rank3 = 2, Rank4 = 3,
        Rank5 = 4, Rank6 = 5, Rank7 = 6, Rank8 = 7
    }

    public enum MoveFlags : int
    {
        Quiet = 0b0000,
        DoublePush = 0b0001,
        OO = 0b0010,
        OOO = 0b0011,
        Capture = 0b1000,
        Captures = 0b1111,
        EnPassant = 0b1010,
        Promotions = 0b0111,
        PromotionCaptures = 0b1100,
        PrKnight = 0b0100,
        PrBishop = 0b0101,
        PrRook = 0b0110,
        PrQueen = 0b0111,
        PcKnight = 0b1100,
        PcBishop = 0b1101,
        PcRook = 0b1110,
        PcQueen = 0b1111
    }

    [Flags]
    public enum CastlingRights : byte
    {
        None = 0,
        WhiteOO = 1,      // Kingside
        WhiteOOO = 2,     // Queenside
        BlackOO = 4,      // Kingside
        BlackOOO = 8,     // Queenside
        White = WhiteOO | WhiteOOO,
        Black = BlackOO | BlackOOO,
        All = White | Black
    }

    public struct Move
    {
        private ushort move;

        public Move(ushort m) => move = m;
        public Move(Square from, Square to) => move = (ushort)(((int)from << 6) | (int)to);
        public Move(Square from, Square to, MoveFlags flags) =>
            move = (ushort)(((int)flags << 12) | ((int)from << 6) | (int)to);

        public Move(string moveStr)
        {
            var from = Types.CreateSquare((File)(moveStr[0] - 'a'), (Rank)(moveStr[1] - '1'));
            var to = Types.CreateSquare((File)(moveStr[2] - 'a'), (Rank)(moveStr[3] - '1'));
            move = (ushort)(((int)from << 6) | (int)to);
        }

        public Square To => (Square)(move & 0x3f);
        public Square From => (Square)((move >> 6) & 0x3f);
        public int ToFrom => move & 0xffff;
        public MoveFlags Flags => (MoveFlags)((move >> 12) & 0xf);
        public bool IsCapture => ((move >> 12) & 0b1000) != 0;

        public override string ToString()
        {
            return Types.SQSTR[(int)From] + Types.SQSTR[(int)To] + Types.MOVE_TYPESTR[(int)Flags];
        }

        public override bool Equals(object? obj) => obj is Move m && ToFrom == m.ToFrom;
        public override int GetHashCode() => ToFrom;
        public static bool operator ==(Move a, Move b) => a.ToFrom == b.ToFrom;
        public static bool operator !=(Move a, Move b) => a.ToFrom != b.ToFrom;
    }

    public static class Types
    {
        public const string PIECE_STR = "PNBRQK~>pnbrqk.";
        public const string DEFAULT_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -";
        public const string KIWIPETE = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq -";

        public static readonly string[] SQSTR = new string[]
        {
            "a1", "b1", "c1", "d1", "e1", "f1", "g1", "h1",
            "a2", "b2", "c2", "d2", "e2", "f2", "g2", "h2",
            "a3", "b3", "c3", "d3", "e3", "f3", "g3", "h3",
            "a4", "b4", "c4", "d4", "e4", "f4", "g4", "h4",
            "a5", "b5", "c5", "d5", "e5", "f5", "g5", "h5",
            "a6", "b6", "c6", "d6", "e6", "f6", "g6", "h6",
            "a7", "b7", "c7", "d7", "e7", "f7", "g7", "h7",
            "a8", "b8", "c8", "d8", "e8", "f8", "g8", "h8",
            "None"
        };

        public static readonly string[] MOVE_TYPESTR = new string[]
        {
            "", "", " O-O", " O-O-O", "N", "B", "R", "Q", " (capture)", "", " e.p.", "",
            "N", "B", "R", "Q"
        };

        public const int NSQUARES = 64;
        public const int NPIECES = 15;
        public const int NPIECE_TYPES = 6;
        public const int NCOLORS = 2;

        public static Piece MakePiece(Color c, PieceType pt) => (Piece)((int)c << 3 | (int)pt);
        public static PieceType TypeOf(Piece pc) => (PieceType)((int)pc & 0b111);
        public static Color ColorOf(Piece pc) => (Color)(((int)pc & 0b1000) >> 3);
        public static Rank RankOf(Square s) => (Rank)((int)s >> 3);
        public static File FileOf(Square s) => (File)((int)s & 0b111);
        public static int DiagonalOf(Square s) => 7 + (int)RankOf(s) - (int)FileOf(s);
        public static int AntiDiagonalOf(Square s) => (int)RankOf(s) + (int)FileOf(s);
        public static Square CreateSquare(File f, Rank r) => (Square)((int)r << 3 | (int)f);

        public static Rank RelativeRank(Color c, Rank r) =>
            c == Color.White ? r : (Rank)((int)Rank.Rank8 - (int)r);

        public static Direction RelativeDir(Color c, Direction d) =>
            (Direction)(c == Color.White ? (int)d : -(int)d);
    }
}
