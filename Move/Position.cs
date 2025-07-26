using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Move
{
    public struct UndoInfo
    {
        public ulong Entry;

        public Piece Captured;

        public Square Epsq;

        public UndoInfo()
        {
            Entry = 0;
            Captured = Piece.NoPiece;
            Epsq = Square.NoSquare;
        }

        public UndoInfo(UndoInfo prev)
        {
            Entry = prev.Entry;
            Captured = Piece.NoPiece;
            Epsq = Square.NoSquare;
        }
    }

    public class Position
    {
        private readonly ulong[] pieceBB = new ulong[Types.NPIECES];

        private readonly Piece[] board = new Piece[Types.NSQUARES];

        private Color sideToPlay;

        private int gamePly;

        private ulong hash;

        public readonly UndoInfo[] History = new UndoInfo[256];

        public ulong Checkers { get; internal set; }

        public ulong Pinned { get; internal set; }

        public Position()
        {
            sideToPlay = Color.White;
            gamePly = 0;
            hash = 0;
            Pinned = 0;
            Checkers = 0;

            for (int i = 0; i < 64; i++)
                board[i] = Piece.NoPiece;

            History[0] = new UndoInfo();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PutPiece(Piece pc, Square s)
        {
            board[(int)s] = pc;
            pieceBB[(int)pc] |= Bitboard.SQUARE_BB[(int)s];
            hash ^= Zobrist.ZobristTable[(int)pc, (int)s];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemovePiece(Square s)
        {
            hash ^= Zobrist.ZobristTable[(int)board[(int)s], (int)s];
            pieceBB[(int)board[(int)s]] &= ~Bitboard.SQUARE_BB[(int)s];
            board[(int)s] = Piece.NoPiece;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MovePiece(Square from, Square to)
        {
            hash ^= Zobrist.ZobristTable[(int)board[(int)from], (int)from]
                 ^ Zobrist.ZobristTable[(int)board[(int)from], (int)to]
                 ^ Zobrist.ZobristTable[(int)board[(int)to], (int)to];

            ulong mask = Bitboard.SQUARE_BB[(int)from] | Bitboard.SQUARE_BB[(int)to];
            pieceBB[(int)board[(int)from]] ^= mask;
            pieceBB[(int)board[(int)to]] &= ~mask;
            board[(int)to] = board[(int)from];
            board[(int)from] = Piece.NoPiece;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MovePieceQuiet(Square from, Square to)
        {
            hash ^= Zobrist.ZobristTable[(int)board[(int)from], (int)from]
                 ^ Zobrist.ZobristTable[(int)board[(int)from], (int)to];
            pieceBB[(int)board[(int)from]] ^= (Bitboard.SQUARE_BB[(int)from] | Bitboard.SQUARE_BB[(int)to]);
            board[(int)to] = board[(int)from];
            board[(int)from] = Piece.NoPiece;
        }
        public ulong BitboardOf(Piece pc) => pieceBB[(int)pc];
        public ulong BitboardOf(Color c, PieceType pt) => pieceBB[(int)Types.MakePiece(c, pt)];
        public Piece At(Square sq) => board[(int)sq];
        public Color Turn => sideToPlay;
        public int Ply => gamePly;
        public ulong GetHash() => hash;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong DiagonalSliders(Color c)
        {
            return c == Color.White ?
                pieceBB[(int)Piece.WhiteBishop] | pieceBB[(int)Piece.WhiteQueen] :
                pieceBB[(int)Piece.BlackBishop] | pieceBB[(int)Piece.BlackQueen];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong OrthogonalSliders(Color c)
        {
            return c == Color.White ?
                pieceBB[(int)Piece.WhiteRook] | pieceBB[(int)Piece.WhiteQueen] :
                pieceBB[(int)Piece.BlackRook] | pieceBB[(int)Piece.BlackQueen];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AllPieces(Color c)
        {
            return c == Color.White ?
                pieceBB[(int)Piece.WhitePawn] | pieceBB[(int)Piece.WhiteKnight] |
                pieceBB[(int)Piece.WhiteBishop] | pieceBB[(int)Piece.WhiteRook] |
                pieceBB[(int)Piece.WhiteQueen] | pieceBB[(int)Piece.WhiteKing] :
                pieceBB[(int)Piece.BlackPawn] | pieceBB[(int)Piece.BlackKnight] |
                pieceBB[(int)Piece.BlackBishop] | pieceBB[(int)Piece.BlackRook] |
                pieceBB[(int)Piece.BlackQueen] | pieceBB[(int)Piece.BlackKing];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong AttackersFrom(Color c, Square s, ulong occ)
        {
            return c == Color.White ?
                (Tables.PawnAttacks(Color.Black, s) & pieceBB[(int)Piece.WhitePawn]) |
                (Tables.Attacks(PieceType.Knight, s, occ) & pieceBB[(int)Piece.WhiteKnight]) |
                (Tables.Attacks(PieceType.Bishop, s, occ) & (pieceBB[(int)Piece.WhiteBishop] | pieceBB[(int)Piece.WhiteQueen])) |
                (Tables.Attacks(PieceType.Rook, s, occ) & (pieceBB[(int)Piece.WhiteRook] | pieceBB[(int)Piece.WhiteQueen])) :
                (Tables.PawnAttacks(Color.White, s) & pieceBB[(int)Piece.BlackPawn]) |
                (Tables.Attacks(PieceType.Knight, s, occ) & pieceBB[(int)Piece.BlackKnight]) |
                (Tables.Attacks(PieceType.Bishop, s, occ) & (pieceBB[(int)Piece.BlackBishop] | pieceBB[(int)Piece.BlackQueen])) |
                (Tables.Attacks(PieceType.Rook, s, occ) & (pieceBB[(int)Piece.BlackRook] | pieceBB[(int)Piece.BlackQueen]));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InCheck(Color c)
        {
            var kingSquare = Bitboard.Bsf(BitboardOf(c, PieceType.King));
            return AttackersFrom(c.Flip(), kingSquare, AllPieces(Color.White) | AllPieces(Color.Black)) != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Play(Color us, Move m)
        {
            sideToPlay = sideToPlay.Flip();
            ++gamePly;
            History[gamePly] = new UndoInfo(History[gamePly - 1]);

            var type = m.Flags;
            History[gamePly].Entry |= Bitboard.SQUARE_BB[(int)m.To] | Bitboard.SQUARE_BB[(int)m.From];

            switch (type)
            {
                case MoveFlags.Quiet:
                    MovePieceQuiet(m.From, m.To);
                    break;

                case MoveFlags.DoublePush:
                    MovePieceQuiet(m.From, m.To);
                    History[gamePly].Epsq = (Square)((int)m.From + (int)Types.RelativeDir(us, Direction.North));
                    break;

                case MoveFlags.OO:
                    if (us == Color.White)
                    {
                        MovePieceQuiet(Square.e1, Square.g1);
                        MovePieceQuiet(Square.h1, Square.f1);
                    }
                    else
                    {
                        MovePieceQuiet(Square.e8, Square.g8);
                        MovePieceQuiet(Square.h8, Square.f8);
                    }
                    break;

                case MoveFlags.OOO:
                    if (us == Color.White)
                    {
                        MovePieceQuiet(Square.e1, Square.c1);
                        MovePieceQuiet(Square.a1, Square.d1);
                    }
                    else
                    {
                        MovePieceQuiet(Square.e8, Square.c8);
                        MovePieceQuiet(Square.a8, Square.d8);
                    }
                    break;

                case MoveFlags.EnPassant:
                    MovePieceQuiet(m.From, m.To);
                    RemovePiece((Square)((int)m.To + (int)Types.RelativeDir(us, Direction.South)));
                    break;

                case MoveFlags.PrKnight:
                    RemovePiece(m.From);
                    PutPiece(Types.MakePiece(us, PieceType.Knight), m.To);
                    break;

                case MoveFlags.PrBishop:
                    RemovePiece(m.From);
                    PutPiece(Types.MakePiece(us, PieceType.Bishop), m.To);
                    break;

                case MoveFlags.PrRook:
                    RemovePiece(m.From);
                    PutPiece(Types.MakePiece(us, PieceType.Rook), m.To);
                    break;

                case MoveFlags.PrQueen:
                    RemovePiece(m.From);
                    PutPiece(Types.MakePiece(us, PieceType.Queen), m.To);
                    break;

                case MoveFlags.PcKnight:
                    RemovePiece(m.From);
                    History[gamePly].Captured = board[(int)m.To];
                    RemovePiece(m.To);
                    PutPiece(Types.MakePiece(us, PieceType.Knight), m.To);
                    break;

                case MoveFlags.PcBishop:
                    RemovePiece(m.From);
                    History[gamePly].Captured = board[(int)m.To];
                    RemovePiece(m.To);
                    PutPiece(Types.MakePiece(us, PieceType.Bishop), m.To);
                    break;

                case MoveFlags.PcRook:
                    RemovePiece(m.From);
                    History[gamePly].Captured = board[(int)m.To];
                    RemovePiece(m.To);
                    PutPiece(Types.MakePiece(us, PieceType.Rook), m.To);
                    break;

                case MoveFlags.PcQueen:
                    RemovePiece(m.From);
                    History[gamePly].Captured = board[(int)m.To];
                    RemovePiece(m.To);
                    PutPiece(Types.MakePiece(us, PieceType.Queen), m.To);
                    break;

                case MoveFlags.Capture:
                    History[gamePly].Captured = board[(int)m.To];
                    MovePiece(m.From, m.To);
                    break;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Undo(Color us, Move m)
        {
            var type = m.Flags;
            switch (type)
            {
                case MoveFlags.Quiet:
                    MovePieceQuiet(m.To, m.From);
                    break;

                case MoveFlags.DoublePush:
                    MovePieceQuiet(m.To, m.From);
                    break;

                case MoveFlags.OO:
                    if (us == Color.White)
                    {
                        MovePieceQuiet(Square.g1, Square.e1);
                        MovePieceQuiet(Square.f1, Square.h1);
                    }
                    else
                    {
                        MovePieceQuiet(Square.g8, Square.e8);
                        MovePieceQuiet(Square.f8, Square.h8);
                    }
                    break;

                case MoveFlags.OOO:
                    if (us == Color.White)
                    {
                        MovePieceQuiet(Square.c1, Square.e1);
                        MovePieceQuiet(Square.d1, Square.a1);
                    }
                    else
                    {
                        MovePieceQuiet(Square.c8, Square.e8);
                        MovePieceQuiet(Square.d8, Square.a8);
                    }
                    break;

                case MoveFlags.EnPassant:
                    MovePieceQuiet(m.To, m.From);
                    PutPiece(Types.MakePiece(us.Flip(), PieceType.Pawn),
                            (Square)((int)m.To + (int)Types.RelativeDir(us, Direction.South)));
                    break;

                case MoveFlags.PrKnight:
                case MoveFlags.PrBishop:
                case MoveFlags.PrRook:
                case MoveFlags.PrQueen:
                    RemovePiece(m.To);
                    PutPiece(Types.MakePiece(us, PieceType.Pawn), m.From);
                    break;

                case MoveFlags.PcKnight:
                case MoveFlags.PcBishop:
                case MoveFlags.PcRook:
                case MoveFlags.PcQueen:
                    RemovePiece(m.To);
                    PutPiece(Types.MakePiece(us, PieceType.Pawn), m.From);
                    PutPiece(History[gamePly].Captured, m.To);
                    break;

                case MoveFlags.Capture:
                    MovePieceQuiet(m.To, m.From);
                    PutPiece(History[gamePly].Captured, m.To);
                    break;
            }

            sideToPlay = sideToPlay.Flip();
            --gamePly;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            const string s = "   +---+---+---+---+---+---+---+---+\n";
            const string t = "     A   B   C   D   E   F   G   H\n";

            sb.Append(t);
            for (int i = 56; i >= 0; i -= 8)
            {
                sb.Append(s);
                sb.Append($" {i / 8 + 1} ");
                for (int j = 0; j < 8; j++)
                    sb.Append($"| {Types.PIECE_STR[(int)board[i + j]]} ");
                sb.Append($"| {i / 8 + 1}\n");
            }
            sb.Append(s);
            sb.Append(t);
            sb.Append("\n");

            sb.Append($"FEN: {Fen()}\n");
            sb.Append($"Hash: 0x{hash:X}\n");

            return sb.ToString();
        }

        public string Fen()
        {
            var fen = new StringBuilder();
            int empty;

            for (int i = 56; i >= 0; i -= 8)
            {
                empty = 0;
                for (int j = 0; j < 8; j++)
                {
                    Piece p = board[i + j];
                    if (p == Piece.NoPiece)
                    {
                        empty++;
                    }
                    else
                    {
                        if (empty != 0)
                        {
                            fen.Append(empty);
                            empty = 0;
                        }
                        fen.Append(Types.PIECE_STR[(int)p]);
                    }
                }

                if (empty != 0) fen.Append(empty);
                if (i > 0) fen.Append('/');
            }

            fen.Append(sideToPlay == Color.White ? " w " : " b ");

            if ((History[gamePly].Entry & Bitboard.ALL_CASTLING_MASK) == 0)
            {
                fen.Append("- ");
            }
            else
            {
                if ((History[gamePly].Entry & Bitboard.WHITE_OO_MASK) == 0) fen.Append('K');
                if ((History[gamePly].Entry & Bitboard.WHITE_OOO_MASK) == 0) fen.Append('Q');
                if ((History[gamePly].Entry & Bitboard.BLACK_OO_MASK) == 0) fen.Append('k');
                if ((History[gamePly].Entry & Bitboard.BLACK_OOO_MASK) == 0) fen.Append('q');
                fen.Append(' ');
            }

            fen.Append(History[gamePly].Epsq == Square.NoSquare ? "-" : Types.SQSTR[(int)History[gamePly].Epsq]);

            return fen.ToString();
        }

        public static void Set(string fen, Position p)
        {
            for (int i = 0; i < Types.NSQUARES; i++)
                p.board[i] = Piece.NoPiece;
            for (int i = 0; i < Types.NPIECES; i++)
                p.pieceBB[i] = 0;
            p.hash = 0;

            int square = (int)Square.a8;
            int fenIdx = 0;

            while (fenIdx < fen.Length && fen[fenIdx] != ' ')
            {
                char ch = fen[fenIdx++];
                if (char.IsDigit(ch))
                {
                    square += (ch - '0') * (int)Direction.East;
                }
                else if (ch == '/')
                {
                    square += 2 * (int)Direction.South;
                }
                else
                {
                    int pieceIdx = Types.PIECE_STR.IndexOf(ch);
                    if (pieceIdx >= 0)
                    {
                        p.PutPiece((Piece)pieceIdx, (Square)square);
                        square++;
                    }
                }
            }

            if (fenIdx < fen.Length) fenIdx++;

            if (fenIdx < fen.Length)
            {
                p.sideToPlay = fen[fenIdx] == 'w' ? Color.White : Color.Black;
                fenIdx++;
                if (fenIdx < fen.Length) fenIdx++;
            }

            p.History[p.gamePly].Entry = Bitboard.ALL_CASTLING_MASK;
            while (fenIdx < fen.Length && fen[fenIdx] != ' ')
            {
                switch (fen[fenIdx])
                {
                    case 'K':
                        p.History[p.gamePly].Entry &= ~Bitboard.WHITE_OO_MASK;
                        break;
                    case 'Q':
                        p.History[p.gamePly].Entry &= ~Bitboard.WHITE_OOO_MASK;
                        break;
                    case 'k':
                        p.History[p.gamePly].Entry &= ~Bitboard.BLACK_OO_MASK;
                        break;
                    case 'q':
                        p.History[p.gamePly].Entry &= ~Bitboard.BLACK_OOO_MASK;
                        break;
                }
                fenIdx++;
            }

            if (fenIdx < fen.Length) fenIdx++;

            if (fenIdx < fen.Length && fen[fenIdx] != '-')
            {
                if (fenIdx + 1 < fen.Length)
                {
                    File f = (File)(fen[fenIdx] - 'a');
                    Rank r = (Rank)(fen[fenIdx + 1] - '1');
                    p.History[p.gamePly].Epsq = Types.CreateSquare(f, r);
                }
            }
        }
    }
}