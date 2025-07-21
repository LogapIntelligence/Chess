namespace Chess;

using System.Runtime.CompilerServices;

public static class MoveGenerator
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GenerateMoves(ref Board board, ref MoveList moves)
    {
        moves.Clear();

        if (board.SideToMove == Color.White)
            GenerateWhiteMoves(ref board, ref moves);
        else
            GenerateBlackMoves(ref board, ref moves);

        // Remove illegal moves
        FilterLegalMoves(ref board, ref moves);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateWhiteMoves(ref Board board, ref MoveList moves)
    {
        ulong occupancy = board.AllPieces;
        ulong enemies = board.BlackPieces;
        ulong notAllies = ~board.WhitePieces;

        // Generate pawn moves
        GenerateWhitePawnMoves(ref board, ref moves, enemies);

        // Generate knight moves
        ulong knights = board.WhiteKnights;
        while (knights != 0)
        {
            int from = BitboardConstants.BitScanForward(knights);
            knights = BitboardConstants.ClearBit(knights, from);

            ulong attacks = BitboardConstants.KnightMoves[from] & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate bishop moves
        ulong bishops = board.WhiteBishops;
        while (bishops != 0)
        {
            int from = BitboardConstants.BitScanForward(bishops);
            bishops = BitboardConstants.ClearBit(bishops, from);

            ulong attacks = MagicBitboards.GetBishopAttacks(from, occupancy) & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate rook moves
        ulong rooks = board.WhiteRooks;
        while (rooks != 0)
        {
            int from = BitboardConstants.BitScanForward(rooks);
            rooks = BitboardConstants.ClearBit(rooks, from);

            ulong attacks = MagicBitboards.GetRookAttacks(from, occupancy) & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate queen moves
        ulong queens = board.WhiteQueens;
        while (queens != 0)
        {
            int from = BitboardConstants.BitScanForward(queens);
            queens = BitboardConstants.ClearBit(queens, from);

            ulong attacks = MagicBitboards.GetQueenAttacks(from, occupancy) & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate king moves
        int kingSquare = BitboardConstants.BitScanForward(board.WhiteKing);
        ulong kingAttacks = BitboardConstants.KingMoves[kingSquare] & notAllies;
        GenerateMovesFromAttacks(kingSquare, kingAttacks, enemies, ref moves);

        // Generate castling moves
        GenerateWhiteCastlingMoves(ref board, ref moves);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateBlackMoves(ref Board board, ref MoveList moves)
    {
        ulong occupancy = board.AllPieces;
        ulong enemies = board.WhitePieces;
        ulong notAllies = ~board.BlackPieces;

        // Generate pawn moves
        GenerateBlackPawnMoves(ref board, ref moves, enemies);

        // Generate knight moves
        ulong knights = board.BlackKnights;
        while (knights != 0)
        {
            int from = BitboardConstants.BitScanForward(knights);
            knights = BitboardConstants.ClearBit(knights, from);

            ulong attacks = BitboardConstants.KnightMoves[from] & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate bishop moves
        ulong bishops = board.BlackBishops;
        while (bishops != 0)
        {
            int from = BitboardConstants.BitScanForward(bishops);
            bishops = BitboardConstants.ClearBit(bishops, from);

            ulong attacks = MagicBitboards.GetBishopAttacks(from, occupancy) & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate rook moves
        ulong rooks = board.BlackRooks;
        while (rooks != 0)
        {
            int from = BitboardConstants.BitScanForward(rooks);
            rooks = BitboardConstants.ClearBit(rooks, from);

            ulong attacks = MagicBitboards.GetRookAttacks(from, occupancy) & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate queen moves
        ulong queens = board.BlackQueens;
        while (queens != 0)
        {
            int from = BitboardConstants.BitScanForward(queens);
            queens = BitboardConstants.ClearBit(queens, from);

            ulong attacks = MagicBitboards.GetQueenAttacks(from, occupancy) & notAllies;
            GenerateMovesFromAttacks(from, attacks, enemies, ref moves);
        }

        // Generate king moves
        int kingSquare = BitboardConstants.BitScanForward(board.BlackKing);
        ulong kingAttacks = BitboardConstants.KingMoves[kingSquare] & notAllies;
        GenerateMovesFromAttacks(kingSquare, kingAttacks, enemies, ref moves);

        // Generate castling moves
        GenerateBlackCastlingMoves(ref board, ref moves);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateWhitePawnMoves(ref Board board, ref MoveList moves, ulong enemies)
    {
        ulong pawns = board.WhitePawns;
        ulong occupancy = board.AllPieces;
        ulong empty = ~occupancy;

        // Single push
        ulong singlePush = pawns << 8 & empty;
        ulong singlePushTargets = singlePush & ~BitboardConstants.Rank8;
        while (singlePushTargets != 0)
        {
            int to = BitboardConstants.BitScanForward(singlePushTargets);
            singlePushTargets = BitboardConstants.ClearBit(singlePushTargets, to);
            moves.Add(new Move(to - 8, to));
        }

        // Double push
        ulong doublePush = (singlePush & BitboardConstants.Rank3) << 8 & empty;
        while (doublePush != 0)
        {
            int to = BitboardConstants.BitScanForward(doublePush);
            doublePush = BitboardConstants.ClearBit(doublePush, to);
            moves.Add(new Move(to - 16, to, MoveFlags.DoublePush));
        }

        // Promotions
        ulong promotions = pawns << 8 & empty & BitboardConstants.Rank8;
        while (promotions != 0)
        {
            int to = BitboardConstants.BitScanForward(promotions);
            promotions = BitboardConstants.ClearBit(promotions, to);
            int from = to - 8;
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Queen));
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Rook));
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Bishop));
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Knight));
        }

        // Captures
        ulong leftCaptures = (pawns & ~BitboardConstants.FileA) << 7 & enemies;
        ulong rightCaptures = (pawns & ~BitboardConstants.FileH) << 9 & enemies;

        // Normal captures
        ulong leftNormal = leftCaptures & ~BitboardConstants.Rank8;
        while (leftNormal != 0)
        {
            int to = BitboardConstants.BitScanForward(leftNormal);
            leftNormal = BitboardConstants.ClearBit(leftNormal, to);
            moves.Add(new Move(to - 7, to, MoveFlags.Capture));
        }

        ulong rightNormal = rightCaptures & ~BitboardConstants.Rank8;
        while (rightNormal != 0)
        {
            int to = BitboardConstants.BitScanForward(rightNormal);
            rightNormal = BitboardConstants.ClearBit(rightNormal, to);
            moves.Add(new Move(to - 9, to, MoveFlags.Capture));
        }

        // Promotion captures
        ulong leftPromo = leftCaptures & BitboardConstants.Rank8;
        while (leftPromo != 0)
        {
            int to = BitboardConstants.BitScanForward(leftPromo);
            leftPromo = BitboardConstants.ClearBit(leftPromo, to);
            int from = to - 7;
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Queen));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Rook));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Bishop));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Knight));
        }

        ulong rightPromo = rightCaptures & BitboardConstants.Rank8;
        while (rightPromo != 0)
        {
            int to = BitboardConstants.BitScanForward(rightPromo);
            rightPromo = BitboardConstants.ClearBit(rightPromo, to);
            int from = to - 9;
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Queen));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Rook));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Bishop));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Knight));
        }

        // En passant
        if (board.EnPassantSquare >= 0 && board.EnPassantSquare < 64)
        {
            ulong epTarget = 1UL << board.EnPassantSquare;
            ulong epAttackers = pawns & BitboardConstants.BlackPawnAttacks[board.EnPassantSquare];
            while (epAttackers != 0)
            {
                int from = BitboardConstants.BitScanForward(epAttackers);
                epAttackers = BitboardConstants.ClearBit(epAttackers, from);
                moves.Add(new Move(from, board.EnPassantSquare, MoveFlags.EnPassant | MoveFlags.Capture));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateBlackPawnMoves(ref Board board, ref MoveList moves, ulong enemies)
    {
        ulong pawns = board.BlackPawns;
        ulong occupancy = board.AllPieces;
        ulong empty = ~occupancy;

        // Single push
        ulong singlePush = pawns >> 8 & empty;
        ulong singlePushTargets = singlePush & ~BitboardConstants.Rank1;
        while (singlePushTargets != 0)
        {
            int to = BitboardConstants.BitScanForward(singlePushTargets);
            singlePushTargets = BitboardConstants.ClearBit(singlePushTargets, to);
            moves.Add(new Move(to + 8, to));
        }

        // Double push
        ulong doublePush = (singlePush & BitboardConstants.Rank6) >> 8 & empty;
        while (doublePush != 0)
        {
            int to = BitboardConstants.BitScanForward(doublePush);
            doublePush = BitboardConstants.ClearBit(doublePush, to);
            moves.Add(new Move(to + 16, to, MoveFlags.DoublePush));
        }

        // Promotions
        ulong promotions = pawns >> 8 & empty & BitboardConstants.Rank1;
        while (promotions != 0)
        {
            int to = BitboardConstants.BitScanForward(promotions);
            promotions = BitboardConstants.ClearBit(promotions, to);
            int from = to + 8;
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Queen));
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Rook));
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Bishop));
            moves.Add(new Move(from, to, MoveFlags.None, PieceType.Knight));
        }

        // Captures
        ulong leftCaptures = (pawns & ~BitboardConstants.FileA) >> 9 & enemies;
        ulong rightCaptures = (pawns & ~BitboardConstants.FileH) >> 7 & enemies;

        // Normal captures
        ulong leftNormal = leftCaptures & ~BitboardConstants.Rank1;
        while (leftNormal != 0)
        {
            int to = BitboardConstants.BitScanForward(leftNormal);
            leftNormal = BitboardConstants.ClearBit(leftNormal, to);
            moves.Add(new Move(to + 9, to, MoveFlags.Capture));
        }

        ulong rightNormal = rightCaptures & ~BitboardConstants.Rank1;
        while (rightNormal != 0)
        {
            int to = BitboardConstants.BitScanForward(rightNormal);
            rightNormal = BitboardConstants.ClearBit(rightNormal, to);
            moves.Add(new Move(to + 7, to, MoveFlags.Capture));
        }

        // Promotion captures
        ulong leftPromo = leftCaptures & BitboardConstants.Rank1;
        while (leftPromo != 0)
        {
            int to = BitboardConstants.BitScanForward(leftPromo);
            leftPromo = BitboardConstants.ClearBit(leftPromo, to);
            int from = to + 9;
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Queen));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Rook));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Bishop));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Knight));
        }

        ulong rightPromo = rightCaptures & BitboardConstants.Rank1;
        while (rightPromo != 0)
        {
            int to = BitboardConstants.BitScanForward(rightPromo);
            rightPromo = BitboardConstants.ClearBit(rightPromo, to);
            int from = to + 7;
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Queen));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Rook));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Bishop));
            moves.Add(new Move(from, to, MoveFlags.Capture, PieceType.Knight));
        }

        // En passant
        if (board.EnPassantSquare >= 0)
        {
            ulong epTarget = 1UL << board.EnPassantSquare;
            ulong epAttackers = pawns & BitboardConstants.WhitePawnAttacks[board.EnPassantSquare];
            while (epAttackers != 0)
            {
                int from = BitboardConstants.BitScanForward(epAttackers);
                epAttackers = BitboardConstants.ClearBit(epAttackers, from);
                moves.Add(new Move(from, board.EnPassantSquare, MoveFlags.EnPassant | MoveFlags.Capture));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateMovesFromAttacks(int from, ulong attacks, ulong enemies, ref MoveList moves)
    {
        while (attacks != 0)
        {
            int to = BitboardConstants.BitScanForward(attacks);
            attacks = BitboardConstants.ClearBit(attacks, to);

            MoveFlags flags = (1UL << to & enemies) != 0 ? MoveFlags.Capture : MoveFlags.None;
            moves.Add(new Move(from, to, flags));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateWhiteCastlingMoves(ref Board board, ref MoveList moves)
    {
        if (board.IsInCheck()) return;

        if ((board.CastlingRights & CastlingRights.WhiteKingside) != 0 &&
            (board.AllPieces & BitboardConstants.WhiteKingsideCastleMask) == 0 &&
            !board.IsSquareAttacked(5, Color.Black) &&
            !board.IsSquareAttacked(6, Color.Black))
        {
            moves.Add(new Move(4, 6, MoveFlags.Castling));
        }

        if ((board.CastlingRights & CastlingRights.WhiteQueenside) != 0 &&
            (board.AllPieces & BitboardConstants.WhiteQueensideCastleMask) == 0 &&
            !board.IsSquareAttacked(3, Color.Black) &&
            !board.IsSquareAttacked(2, Color.Black))
        {
            moves.Add(new Move(4, 2, MoveFlags.Castling));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateBlackCastlingMoves(ref Board board, ref MoveList moves)
    {
        if (board.IsInCheck()) return;

        if ((board.CastlingRights & CastlingRights.BlackKingside) != 0 &&
            (board.AllPieces & BitboardConstants.BlackKingsideCastleMask) == 0 &&
            !board.IsSquareAttacked(61, Color.White) &&
            !board.IsSquareAttacked(62, Color.White))
        {
            moves.Add(new Move(60, 62, MoveFlags.Castling));
        }

        if ((board.CastlingRights & CastlingRights.BlackQueenside) != 0 &&
            (board.AllPieces & BitboardConstants.BlackQueensideCastleMask) == 0 &&
            !board.IsSquareAttacked(59, Color.White) &&
            !board.IsSquareAttacked(58, Color.White))
        {
            moves.Add(new Move(60, 58, MoveFlags.Castling));
        }
    }

    private static void FilterLegalMoves(ref Board board, ref MoveList moves)
    {
        MoveList legalMoves = new MoveList();
        Color movingSide = board.SideToMove;

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];
            Board testBoard = board;
            testBoard.MakeMove(move);

            // Check if the moving side's king is still safe (not the opponent's)
            int kingSquare = movingSide == Color.White
                ? BitboardConstants.BitScanForward(testBoard.WhiteKing)
                : BitboardConstants.BitScanForward(testBoard.BlackKing);

            if (!testBoard.IsSquareAttacked(kingSquare, movingSide == Color.White ? Color.Black : Color.White))
            {
                legalMoves.Add(move);
            }
        }

        moves = legalMoves;
    }
}