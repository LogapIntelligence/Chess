namespace Chess;

using System.Runtime.CompilerServices;

public static class MoveGenerator
{
    // Pre-calculated ray masks for sliding pieces
    private static readonly ulong[,] RayMasks = new ulong[64, 64];

    static MoveGenerator()
    {
        InitializeRayMasks();
    }

    private static void InitializeRayMasks()
    {
        for (int from = 0; from < 64; from++)
        {
            for (int to = 0; to < 64; to++)
            {
                RayMasks[from, to] = CalculateRayBetween(from, to);
            }
        }
    }

    private static ulong CalculateRayBetween(int from, int to)
    {
        int fromRank = from / 8;
        int fromFile = from % 8;
        int toRank = to / 8;
        int toFile = to % 8;

        int rankDiff = toRank - fromRank;
        int fileDiff = toFile - fromFile;

        // Not on same ray
        if (rankDiff == 0 && fileDiff == 0) return 0;
        if (rankDiff != 0 && fileDiff != 0 && Math.Abs(rankDiff) != Math.Abs(fileDiff)) return 0;
        if (rankDiff == 0 && fileDiff != 0 && Math.Abs(fileDiff) > 1) // Horizontal
        {
            int step = fileDiff > 0 ? 1 : -1;
            ulong ray = 0;
            for (int f = fromFile + step; f != toFile; f += step)
                ray |= 1UL << (fromRank * 8 + f);
            return ray;
        }
        if (fileDiff == 0 && rankDiff != 0 && Math.Abs(rankDiff) > 1) // Vertical
        {
            int step = rankDiff > 0 ? 8 : -8;
            ulong ray = 0;
            for (int sq = from + step; sq != to; sq += step)
                ray |= 1UL << sq;
            return ray;
        }
        if (Math.Abs(rankDiff) == Math.Abs(fileDiff) && Math.Abs(rankDiff) > 1) // Diagonal
        {
            int step = (rankDiff > 0 ? 8 : -8) + (fileDiff > 0 ? 1 : -1);
            ulong ray = 0;
            for (int sq = from + step; sq != to; sq += step)
                ray |= 1UL << sq;
            return ray;
        }

        return 0;
    }

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
    public static void GenerateLegalMoves(ref Board board, ref MoveList moves)
    {
        moves.Clear();

        Color us = board.SideToMove;
        Color them = us == Color.White ? Color.Black : Color.White;

        // Find king position
        ulong kingBB = us == Color.White ? board.WhiteKing : board.BlackKing;
        int kingSquare = BitboardConstants.BitScanForward(kingBB);

        // Calculate checkers
        ulong checkers = GetAttackers(ref board, kingSquare, them);
        int numCheckers = BitboardConstants.PopCount(checkers);

        // If double check, only king moves are legal
        if (numCheckers > 1)
        {
            GenerateKingMoves(ref board, ref moves, kingSquare, us);
            return;
        }

        // Calculate pinned pieces
        ulong pinned = GetPinnedPieces(ref board, kingSquare, us);

        // If in check, we need to either:
        // 1. Move the king
        // 2. Block the check (if single check from sliding piece)
        // 3. Capture the checking piece
        ulong blockSquares = 0;
        ulong checkMask = ~0UL; // All squares allowed if not in check

        if (numCheckers == 1)
        {
            int checkerSquare = BitboardConstants.BitScanForward(checkers);
            checkMask = checkers; // Can capture checker

            // If checker is a sliding piece, we can also block
            var (checkerType, _) = board.GetPieceAt(checkerSquare);
            if (checkerType == PieceType.Bishop || checkerType == PieceType.Rook || checkerType == PieceType.Queen)
            {
                blockSquares = GetRayBetween(kingSquare, checkerSquare);
                checkMask |= blockSquares;
            }
        }

        // Generate moves for all pieces
        if (us == Color.White)
        {
            GeneratePawnMovesLegal(ref board, ref moves, board.WhitePawns & ~pinned, checkMask, us);
            GeneratePinnedPawnMoves(ref board, ref moves, board.WhitePawns & pinned, kingSquare, us);

            GenerateKnightMovesLegal(ref board, ref moves, board.WhiteKnights & ~pinned, checkMask, us);
            GenerateSlidingMovesLegal(ref board, ref moves, board.WhiteBishops & ~pinned, checkMask, true, false, us);
            GenerateSlidingMovesLegal(ref board, ref moves, board.WhiteRooks & ~pinned, checkMask, false, true, us);
            GenerateSlidingMovesLegal(ref board, ref moves, board.WhiteQueens & ~pinned, checkMask, true, true, us);

            // Pinned sliding pieces can only move along the pin ray
            GeneratePinnedSlidingMoves(ref board, ref moves, board.WhiteBishops & pinned, kingSquare, true, false, us);
            GeneratePinnedSlidingMoves(ref board, ref moves, board.WhiteRooks & pinned, kingSquare, false, true, us);
            GeneratePinnedSlidingMoves(ref board, ref moves, board.WhiteQueens & pinned, kingSquare, true, true, us);
        }
        else
        {
            GeneratePawnMovesLegal(ref board, ref moves, board.BlackPawns & ~pinned, checkMask, us);
            GeneratePinnedPawnMoves(ref board, ref moves, board.BlackPawns & pinned, kingSquare, us);

            GenerateKnightMovesLegal(ref board, ref moves, board.BlackKnights & ~pinned, checkMask, us);
            GenerateSlidingMovesLegal(ref board, ref moves, board.BlackBishops & ~pinned, checkMask, true, false, us);
            GenerateSlidingMovesLegal(ref board, ref moves, board.BlackRooks & ~pinned, checkMask, false, true, us);
            GenerateSlidingMovesLegal(ref board, ref moves, board.BlackQueens & ~pinned, checkMask, true, true, us);

            // Pinned sliding pieces
            GeneratePinnedSlidingMoves(ref board, ref moves, board.BlackBishops & pinned, kingSquare, true, false, us);
            GeneratePinnedSlidingMoves(ref board, ref moves, board.BlackRooks & pinned, kingSquare, false, true, us);
            GeneratePinnedSlidingMoves(ref board, ref moves, board.BlackQueens & pinned, kingSquare, true, true, us);
        }

        // King moves are always generated (king can't be pinned)
        GenerateKingMoves(ref board, ref moves, kingSquare, us);

        // Castling (only if not in check)
        if (numCheckers == 0)
        {
            GenerateCastlingMovesLegal(ref board, ref moves, us);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetAttackers(ref Board board, int square, Color byColor)
    {
        ulong attackers = 0;
        ulong occupancy = board.AllPieces;

        if (byColor == Color.White)
        {
            // Pawn attacks
            attackers |= BitboardConstants.BlackPawnAttacks[square] & board.WhitePawns;

            // Knight attacks
            attackers |= BitboardConstants.KnightMoves[square] & board.WhiteKnights;

            // Bishop/Queen attacks
            ulong bishopAttacks = MagicBitboards.GetBishopAttacks(square, occupancy);
            attackers |= bishopAttacks & (board.WhiteBishops | board.WhiteQueens);

            // Rook/Queen attacks
            ulong rookAttacks = MagicBitboards.GetRookAttacks(square, occupancy);
            attackers |= rookAttacks & (board.WhiteRooks | board.WhiteQueens);

            // King attacks
            attackers |= BitboardConstants.KingMoves[square] & board.WhiteKing;
        }
        else
        {
            // Pawn attacks
            attackers |= BitboardConstants.WhitePawnAttacks[square] & board.BlackPawns;

            // Knight attacks
            attackers |= BitboardConstants.KnightMoves[square] & board.BlackKnights;

            // Bishop/Queen attacks
            ulong bishopAttacks = MagicBitboards.GetBishopAttacks(square, occupancy);
            attackers |= bishopAttacks & (board.BlackBishops | board.BlackQueens);

            // Rook/Queen attacks
            ulong rookAttacks = MagicBitboards.GetRookAttacks(square, occupancy);
            attackers |= rookAttacks & (board.BlackRooks | board.BlackQueens);

            // King attacks
            attackers |= BitboardConstants.KingMoves[square] & board.BlackKing;
        }

        return attackers;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetRayBetween(int from, int to)
    {
        return RayMasks[from, to];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateKingMoves(ref Board board, ref MoveList moves, int kingSquare, Color us)
    {
        ulong enemies = us == Color.White ? board.BlackPieces : board.WhitePieces;
        ulong notAllies = us == Color.White ? ~board.WhitePieces : ~board.BlackPieces;
        Color them = us == Color.White ? Color.Black : Color.White;

        ulong kingMoves = BitboardConstants.KingMoves[kingSquare] & notAllies;

        while (kingMoves != 0)
        {
            int to = BitboardConstants.BitScanForward(kingMoves);
            kingMoves = BitboardConstants.ClearBit(kingMoves, to);

            // Check if king would be safe on this square
            if (!board.IsSquareAttacked(to, them))
            {
                MoveFlags flags = ((1UL << to) & enemies) != 0 ? MoveFlags.Capture : MoveFlags.None;
                moves.Add(new Move(kingSquare, to, flags));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratePawnMovesLegal(ref Board board, ref MoveList moves, ulong pawns, ulong checkMask, Color us)
    {
        if (pawns == 0) return;

        ulong enemies = us == Color.White ? board.BlackPieces : board.WhitePieces;
        ulong empty = ~board.AllPieces;

        if (us == Color.White)
        {
            // Single push
            ulong singlePush = (pawns << 8) & empty & checkMask;
            ulong singlePushTargets = singlePush & ~BitboardConstants.Rank8;
            while (singlePushTargets != 0)
            {
                int to = BitboardConstants.BitScanForward(singlePushTargets);
                singlePushTargets = BitboardConstants.ClearBit(singlePushTargets, to);
                moves.Add(new Move(to - 8, to));
            }

            // Double push
            ulong doublePush = ((((pawns & BitboardConstants.Rank2) << 8) & empty) << 8) & empty & checkMask;
            while (doublePush != 0)
            {
                int to = BitboardConstants.BitScanForward(doublePush);
                doublePush = BitboardConstants.ClearBit(doublePush, to);
                moves.Add(new Move(to - 16, to, MoveFlags.DoublePush));
            }

            // Promotions
            ulong promotions = (pawns << 8) & empty & BitboardConstants.Rank8 & checkMask;
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
            ulong leftCaptures = ((pawns & ~BitboardConstants.FileA) << 7) & enemies & checkMask;
            ulong rightCaptures = ((pawns & ~BitboardConstants.FileH) << 9) & enemies & checkMask;

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
            if (board.EnPassantSquare >= 0 && ((1UL << board.EnPassantSquare) & checkMask) != 0)
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
        else
        {
            // Black pawn moves (similar logic, reversed)
            // Single push
            ulong singlePush = (pawns >> 8) & empty & checkMask;
            ulong singlePushTargets = singlePush & ~BitboardConstants.Rank1;
            while (singlePushTargets != 0)
            {
                int to = BitboardConstants.BitScanForward(singlePushTargets);
                singlePushTargets = BitboardConstants.ClearBit(singlePushTargets, to);
                moves.Add(new Move(to + 8, to));
            }

            // Double push
            ulong doublePush = ((((pawns & BitboardConstants.Rank7) >> 8) & empty) >> 8) & empty & checkMask;
            while (doublePush != 0)
            {
                int to = BitboardConstants.BitScanForward(doublePush);
                doublePush = BitboardConstants.ClearBit(doublePush, to);
                moves.Add(new Move(to + 16, to, MoveFlags.DoublePush));
            }

            // Promotions
            ulong promotions = (pawns >> 8) & empty & BitboardConstants.Rank1 & checkMask;
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
            ulong leftCaptures = ((pawns & ~BitboardConstants.FileA) >> 9) & enemies & checkMask;
            ulong rightCaptures = ((pawns & ~BitboardConstants.FileH) >> 7) & enemies & checkMask;

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
            if (board.EnPassantSquare >= 0 && ((1UL << board.EnPassantSquare) & checkMask) != 0)
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
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratePinnedPawnMoves(ref Board board, ref MoveList moves, ulong pawns, int kingSquare, Color us)
    {
        if (pawns == 0) return;

        // Pinned pawns can only move along the pin ray
        while (pawns != 0)
        {
            int pawnSquare = BitboardConstants.BitScanForward(pawns);
            pawns = BitboardConstants.ClearBit(pawns, pawnSquare);

            // Find the pinning piece
            ulong pinRay = GetPinRay(ref board, kingSquare, pawnSquare, us);

            if (pinRay == 0) continue; // Not actually pinned

            // Generate pawn moves that stay on the pin ray
            GenerateSinglePinnedPawnMoves(ref board, ref moves, pawnSquare, pinRay, us);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateSinglePinnedPawnMoves(ref Board board, ref MoveList moves, int pawnSquare, ulong pinRay, Color us)
    {
        ulong pawnBB = 1UL << pawnSquare;
        ulong enemies = us == Color.White ? board.BlackPieces : board.WhitePieces;
        ulong empty = ~board.AllPieces;

        if (us == Color.White)
        {
            // Single push
            int pushSquare = pawnSquare + 8;
            if (pushSquare < 64 && ((1UL << pushSquare) & empty & pinRay) != 0)
            {
                if (pushSquare >= 56) // Promotion
                {
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Queen));
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Rook));
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Bishop));
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Knight));
                }
                else
                {
                    moves.Add(new Move(pawnSquare, pushSquare));

                    // Double push
                    if ((pawnBB & BitboardConstants.Rank2) != 0)
                    {
                        int doublePushSquare = pawnSquare + 16;
                        if (((1UL << doublePushSquare) & empty & pinRay) != 0)
                        {
                            moves.Add(new Move(pawnSquare, doublePushSquare, MoveFlags.DoublePush));
                        }
                    }
                }
            }

            // Captures
            if ((pawnBB & ~BitboardConstants.FileA) != 0)
            {
                int captureSquare = pawnSquare + 7;
                if (captureSquare < 64 && ((1UL << captureSquare) & enemies & pinRay) != 0)
                {
                    if (captureSquare >= 56) // Promotion capture
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Queen));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Rook));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Bishop));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Knight));
                    }
                    else
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture));
                    }
                }
            }

            if ((pawnBB & ~BitboardConstants.FileH) != 0)
            {
                int captureSquare = pawnSquare + 9;
                if (captureSquare < 64 && ((1UL << captureSquare) & enemies & pinRay) != 0)
                {
                    if (captureSquare >= 56) // Promotion capture
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Queen));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Rook));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Bishop));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Knight));
                    }
                    else
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture));
                    }
                }
            }
        }
        else
        {
            // Black pawns (similar logic, reversed)
            int pushSquare = pawnSquare - 8;
            if (pushSquare >= 0 && ((1UL << pushSquare) & empty & pinRay) != 0)
            {
                if (pushSquare < 8) // Promotion
                {
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Queen));
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Rook));
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Bishop));
                    moves.Add(new Move(pawnSquare, pushSquare, MoveFlags.None, PieceType.Knight));
                }
                else
                {
                    moves.Add(new Move(pawnSquare, pushSquare));

                    // Double push
                    if ((pawnBB & BitboardConstants.Rank7) != 0)
                    {
                        int doublePushSquare = pawnSquare - 16;
                        if (((1UL << doublePushSquare) & empty & pinRay) != 0)
                        {
                            moves.Add(new Move(pawnSquare, doublePushSquare, MoveFlags.DoublePush));
                        }
                    }
                }
            }

            // Captures
            if ((pawnBB & ~BitboardConstants.FileA) != 0)
            {
                int captureSquare = pawnSquare - 9;
                if (captureSquare >= 0 && ((1UL << captureSquare) & enemies & pinRay) != 0)
                {
                    if (captureSquare < 8) // Promotion capture
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Queen));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Rook));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Bishop));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Knight));
                    }
                    else
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture));
                    }
                }
            }

            if ((pawnBB & ~BitboardConstants.FileH) != 0)
            {
                int captureSquare = pawnSquare - 7;
                if (captureSquare >= 0 && ((1UL << captureSquare) & enemies & pinRay) != 0)
                {
                    if (captureSquare < 8) // Promotion capture
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Queen));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Rook));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Bishop));
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture, PieceType.Knight));
                    }
                    else
                    {
                        moves.Add(new Move(pawnSquare, captureSquare, MoveFlags.Capture));
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateKnightMovesLegal(ref Board board, ref MoveList moves, ulong knights, ulong checkMask, Color us)
    {
        if (knights == 0) return;

        ulong enemies = us == Color.White ? board.BlackPieces : board.WhitePieces;
        ulong notAllies = us == Color.White ? ~board.WhitePieces : ~board.BlackPieces;

        while (knights != 0)
        {
            int from = BitboardConstants.BitScanForward(knights);
            knights = BitboardConstants.ClearBit(knights, from);

            ulong attacks = BitboardConstants.KnightMoves[from] & notAllies & checkMask;

            while (attacks != 0)
            {
                int to = BitboardConstants.BitScanForward(attacks);
                attacks = BitboardConstants.ClearBit(attacks, to);

                MoveFlags flags = ((1UL << to) & enemies) != 0 ? MoveFlags.Capture : MoveFlags.None;
                moves.Add(new Move(from, to, flags));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateSlidingMovesLegal(ref Board board, ref MoveList moves, ulong pieces, ulong checkMask, bool diagonal, bool straight, Color us)
    {
        if (pieces == 0) return;

        ulong enemies = us == Color.White ? board.BlackPieces : board.WhitePieces;
        ulong notAllies = us == Color.White ? ~board.WhitePieces : ~board.BlackPieces;
        ulong occupancy = board.AllPieces;

        while (pieces != 0)
        {
            int from = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, from);

            ulong attacks = 0;

            if (diagonal)
                attacks |= MagicBitboards.GetBishopAttacks(from, occupancy);

            if (straight)
                attacks |= MagicBitboards.GetRookAttacks(from, occupancy);

            attacks &= notAllies & checkMask;

            while (attacks != 0)
            {
                int to = BitboardConstants.BitScanForward(attacks);
                attacks = BitboardConstants.ClearBit(attacks, to);

                MoveFlags flags = ((1UL << to) & enemies) != 0 ? MoveFlags.Capture : MoveFlags.None;
                moves.Add(new Move(from, to, flags));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GeneratePinnedSlidingMoves(ref Board board, ref MoveList moves, ulong pieces, int kingSquare, bool diagonal, bool straight, Color us)
    {
        if (pieces == 0) return;

        while (pieces != 0)
        {
            int pieceSquare = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, pieceSquare);

            // Find the pin ray
            ulong pinRay = GetPinRay(ref board, kingSquare, pieceSquare, us);

            if (pinRay == 0) continue; // Not actually pinned

            // Generate moves along the pin ray
            GenerateSinglePinnedSlidingMoves(ref board, ref moves, pieceSquare, pinRay, diagonal, straight, us);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateSinglePinnedSlidingMoves(ref Board board, ref MoveList moves, int pieceSquare, ulong pinRay, bool diagonal, bool straight, Color us)
    {
        ulong enemies = us == Color.White ? board.BlackPieces : board.WhitePieces;
        ulong occupancy = board.AllPieces;

        ulong attacks = 0;

        if (diagonal)
            attacks |= MagicBitboards.GetBishopAttacks(pieceSquare, occupancy);

        if (straight)
            attacks |= MagicBitboards.GetRookAttacks(pieceSquare, occupancy);

        // Only moves along the pin ray are legal
        attacks &= pinRay;

        while (attacks != 0)
        {
            int to = BitboardConstants.BitScanForward(attacks);
            attacks = BitboardConstants.ClearBit(attacks, to);

            MoveFlags flags = ((1UL << to) & enemies) != 0 ? MoveFlags.Capture : MoveFlags.None;
            moves.Add(new Move(pieceSquare, to, flags));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetPinRay(ref Board board, int kingSquare, int pieceSquare, Color us)
    {
        // Get the ray from king through the piece
        int kingRank = kingSquare / 8;
        int kingFile = kingSquare % 8;
        int pieceRank = pieceSquare / 8;
        int pieceFile = pieceSquare % 8;

        int rankDiff = pieceRank - kingRank;
        int fileDiff = pieceFile - kingFile;

        // Check if piece is on a ray from king
        if (rankDiff == 0 && fileDiff == 0) return 0;
        if (rankDiff != 0 && fileDiff != 0 && Math.Abs(rankDiff) != Math.Abs(fileDiff)) return 0;

        // Extend ray beyond piece to find potential pinner
        int rankDir = rankDiff == 0 ? 0 : rankDiff / Math.Abs(rankDiff);
        int fileDir = fileDiff == 0 ? 0 : fileDiff / Math.Abs(fileDiff);

        ulong ray = 1UL << pieceSquare;
        ulong enemyQueens = us == Color.White ? board.BlackQueens : board.WhiteQueens;
        ulong enemyRooks = us == Color.White ? board.BlackRooks : board.WhiteRooks;
        ulong enemyBishops = us == Color.White ? board.BlackBishops : board.WhiteBishops;

        // Check beyond the piece for pinning pieces
        int r = pieceRank + rankDir;
        int f = pieceFile + fileDir;

        while (r >= 0 && r < 8 && f >= 0 && f < 8)
        {
            int sq = r * 8 + f;
            ulong sqBB = 1UL << sq;

            ray |= sqBB;

            // Check if we hit a piece
            if ((board.AllPieces & sqBB) != 0)
            {
                // Check if it's an enemy piece that can pin
                bool isPinner = false;

                if (rankDir == 0 || fileDir == 0) // Horizontal/Vertical
                {
                    isPinner = ((enemyRooks | enemyQueens) & sqBB) != 0;
                }
                else // Diagonal
                {
                    isPinner = ((enemyBishops | enemyQueens) & sqBB) != 0;
                }

                if (isPinner)
                {
                    // Include the ray from king to pinner
                    return ray | GetRayBetween(kingSquare, pieceSquare);
                }

                break; // Hit a piece that can't pin
            }

            r += rankDir;
            f += fileDir;
        }

        return 0; // Not pinned
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GenerateCastlingMovesLegal(ref Board board, ref MoveList moves, Color us)
    {
        if (us == Color.White)
        {
            if ((board.CastlingRights & CastlingRights.WhiteKingside) != 0 &&
                (board.AllPieces & BitboardConstants.WhiteKingsideCastleMask) == 0 &&
                !board.IsSquareAttacked(4, Color.Black) &&
                !board.IsSquareAttacked(5, Color.Black) &&
                !board.IsSquareAttacked(6, Color.Black))
            {
                moves.Add(new Move(4, 6, MoveFlags.Castling));
            }

            if ((board.CastlingRights & CastlingRights.WhiteQueenside) != 0 &&
                (board.AllPieces & BitboardConstants.WhiteQueensideCastleMask) == 0 &&
                !board.IsSquareAttacked(4, Color.Black) &&
                !board.IsSquareAttacked(3, Color.Black) &&
                !board.IsSquareAttacked(2, Color.Black))
            {
                moves.Add(new Move(4, 2, MoveFlags.Castling));
            }
        }
        else
        {
            if ((board.CastlingRights & CastlingRights.BlackKingside) != 0 &&
                (board.AllPieces & BitboardConstants.BlackKingsideCastleMask) == 0 &&
                !board.IsSquareAttacked(60, Color.White) &&
                !board.IsSquareAttacked(61, Color.White) &&
                !board.IsSquareAttacked(62, Color.White))
            {
                moves.Add(new Move(60, 62, MoveFlags.Castling));
            }

            if ((board.CastlingRights & CastlingRights.BlackQueenside) != 0 &&
                (board.AllPieces & BitboardConstants.BlackQueensideCastleMask) == 0 &&
                !board.IsSquareAttacked(60, Color.White) &&
                !board.IsSquareAttacked(59, Color.White) &&
                !board.IsSquareAttacked(58, Color.White))
            {
                moves.Add(new Move(60, 58, MoveFlags.Castling));
            }
        }
    }

    private static ulong GetPinnedPieces(ref Board board, int kingSquare, Color us)
    {
        ulong pinned = 0;
        Color them = us == Color.White ? Color.Black : Color.White;
        ulong ourPieces = us == Color.White ? board.WhitePieces : board.BlackPieces;
        ulong theirPieces = us == Color.White ? board.BlackPieces : board.WhitePieces;

        // Check for pins from bishops/queens
        ulong bishopAttackers = us == Color.White ?
            (board.BlackBishops | board.BlackQueens) :
            (board.WhiteBishops | board.WhiteQueens);

        ulong potentialPins = MagicBitboards.GetBishopAttacks(kingSquare, theirPieces) & ourPieces;

        while (potentialPins != 0)
        {
            int pinnedSquare = BitboardConstants.BitScanForward(potentialPins);
            potentialPins = BitboardConstants.ClearBit(potentialPins, pinnedSquare);

            ulong withoutPinned = board.AllPieces ^ (1UL << pinnedSquare);
            ulong attacks = MagicBitboards.GetBishopAttacks(kingSquare, withoutPinned);

            if ((attacks & bishopAttackers) != 0)
            {
                pinned |= 1UL << pinnedSquare;
            }
        }

        // Check for pins from rooks/queens
        ulong rookAttackers = us == Color.White ?
            (board.BlackRooks | board.BlackQueens) :
            (board.WhiteRooks | board.WhiteQueens);

        potentialPins = MagicBitboards.GetRookAttacks(kingSquare, theirPieces) & ourPieces;

        while (potentialPins != 0)
        {
            int pinnedSquare = BitboardConstants.BitScanForward(potentialPins);
            potentialPins = BitboardConstants.ClearBit(potentialPins, pinnedSquare);

            ulong withoutPinned = board.AllPieces ^ (1UL << pinnedSquare);
            ulong attacks = MagicBitboards.GetRookAttacks(kingSquare, withoutPinned);

            if ((attacks & rookAttackers) != 0)
            {
                pinned |= 1UL << pinnedSquare;
            }
        }

        return pinned;
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
        if (board.IsInCheckFast()) return;

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
        if (board.IsInCheckFast()) return;

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