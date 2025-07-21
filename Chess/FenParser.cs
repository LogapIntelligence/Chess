namespace Chess;

using System;

public static class FenParser
{
    private const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static Board ParseFen(string fen)
    {
        var board = new Board();
        string[] parts = fen.Split(' ');

        if (parts.Length < 4)
            throw new ArgumentException("Invalid FEN string");

        // Parse piece placement
        string placement = parts[0];
        int square = 56; // Start from a8

        foreach (char c in placement)
        {
            if (c == '/')
            {
                square -= 16; // Go to next rank
            }
            else if (char.IsDigit(c))
            {
                square += c - '0';
            }
            else
            {
                PieceType piece = char.ToLower(c) switch
                {
                    'p' => PieceType.Pawn,
                    'n' => PieceType.Knight,
                    'b' => PieceType.Bishop,
                    'r' => PieceType.Rook,
                    'q' => PieceType.Queen,
                    'k' => PieceType.King,
                    _ => PieceType.None
                };

                if (piece != PieceType.None)
                {
                    Color color = char.IsUpper(c) ? Color.White : Color.Black;
                    PlacePiece(ref board, piece, color, square);
                }
                square++;
            }
        }

        // Parse side to move
        board.SideToMove = parts[1] == "w" ? Color.White : Color.Black;

        // Parse castling rights
        board.CastlingRights = CastlingRights.None;
        foreach (char c in parts[2])
        {
            switch (c)
            {
                case 'K': board.CastlingRights |= CastlingRights.WhiteKingside; break;
                case 'Q': board.CastlingRights |= CastlingRights.WhiteQueenside; break;
                case 'k': board.CastlingRights |= CastlingRights.BlackKingside; break;
                case 'q': board.CastlingRights |= CastlingRights.BlackQueenside; break;
            }
        }

        // Parse en passant square
        if (parts[3] != "-")
        {
            int file = parts[3][0] - 'a';
            int rank = parts[3][1] - '1';
            board.EnPassantSquare = rank * 8 + file;
        }
        else
        {
            board.EnPassantSquare = -1;
        }

        // Parse halfmove clock
        if (parts.Length > 4)
            board.HalfmoveClock = int.Parse(parts[4]);

        // Parse fullmove number
        if (parts.Length > 5)
            board.FullmoveNumber = int.Parse(parts[5]);

        board.UpdateAggregateBitboards();
        return board;
    }

    private static void PlacePiece(ref Board board, PieceType piece, Color color, int square)
    {
        ulong bit = 1UL << square;

        if (color == Color.White)
        {
            switch (piece)
            {
                case PieceType.Pawn: board.WhitePawns |= bit; break;
                case PieceType.Knight: board.WhiteKnights |= bit; break;
                case PieceType.Bishop: board.WhiteBishops |= bit; break;
                case PieceType.Rook: board.WhiteRooks |= bit; break;
                case PieceType.Queen: board.WhiteQueens |= bit; break;
                case PieceType.King: board.WhiteKing |= bit; break;
            }
        }
        else
        {
            switch (piece)
            {
                case PieceType.Pawn: board.BlackPawns |= bit; break;
                case PieceType.Knight: board.BlackKnights |= bit; break;
                case PieceType.Bishop: board.BlackBishops |= bit; break;
                case PieceType.Rook: board.BlackRooks |= bit; break;
                case PieceType.Queen: board.BlackQueens |= bit; break;
                case PieceType.King: board.BlackKing |= bit; break;
            }
        }
    }

    public static string ToFen(ref Board board)
    {
        var sb = new System.Text.StringBuilder();

        // Piece placement
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                int square = rank * 8 + file;
                var (piece, color) = board.GetPieceAt(square);

                if (piece == PieceType.None)
                {
                    empty++;
                }
                else
                {
                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }

                    char pieceChar = piece switch
                    {
                        PieceType.Pawn => 'p',
                        PieceType.Knight => 'n',
                        PieceType.Bishop => 'b',
                        PieceType.Rook => 'r',
                        PieceType.Queen => 'q',
                        PieceType.King => 'k',
                        _ => ' '
                    };

                    if (color == Color.White)
                        pieceChar = char.ToUpper(pieceChar);

                    sb.Append(pieceChar);
                }
            }

            if (empty > 0)
                sb.Append(empty);

            if (rank > 0)
                sb.Append('/');
        }

        // Side to move
        sb.Append(' ');
        sb.Append(board.SideToMove == Color.White ? 'w' : 'b');

        // Castling rights
        sb.Append(' ');
        if (board.CastlingRights == CastlingRights.None)
        {
            sb.Append('-');
        }
        else
        {
            if ((board.CastlingRights & CastlingRights.WhiteKingside) != 0) sb.Append('K');
            if ((board.CastlingRights & CastlingRights.WhiteQueenside) != 0) sb.Append('Q');
            if ((board.CastlingRights & CastlingRights.BlackKingside) != 0) sb.Append('k');
            if ((board.CastlingRights & CastlingRights.BlackQueenside) != 0) sb.Append('q');
        }

        // En passant
        sb.Append(' ');
        if (board.EnPassantSquare >= 0)
        {
            sb.Append((char)('a' + board.EnPassantSquare % 8));
            sb.Append(board.EnPassantSquare / 8 + 1);
        }
        else
        {
            sb.Append('-');
        }

        // Halfmove clock and fullmove number
        sb.Append(' ');
        sb.Append(board.HalfmoveClock);
        sb.Append(' ');
        sb.Append(board.FullmoveNumber);

        return sb.ToString();
    }
}