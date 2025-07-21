namespace Chess;

using System;
using System.Runtime.CompilerServices;

public static class Evaluation
{
    // Material values (middlegame, endgame)
    private static readonly int[] PieceValueMg = { 0, 320, 330, 500, 900, 0, 100 }; // None, Knight, Bishop, Rook, Queen, King, Pawn
    private static readonly int[] PieceValueEg = { 0, 320, 330, 500, 900, 0, 100 };

    // Piece-square tables (middlegame)
    private static readonly int[] PawnTableMg = {
         0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
         5,  5, 10, 25, 25, 10,  5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5, -5,-10,  0,  0,-10, -5,  5,
         5, 10, 10,-20,-20, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0
    };

    private static readonly int[] KnightTableMg = {
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50
    };

    private static readonly int[] BishopTableMg = {
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20
    };

    private static readonly int[] RookTableMg = {
         0,  0,  0,  0,  0,  0,  0,  0,
         5, 10, 10, 10, 10, 10, 10,  5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
        -5,  0,  0,  0,  0,  0,  0, -5,
         0,  0,  0,  5,  5,  0,  0,  0
    };

    private static readonly int[] QueenTableMg = {
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20
    };

    private static readonly int[] KingTableMg = {
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20
    };

    // Endgame piece-square tables
    private static readonly int[] KingTableEg = {
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50
    };

    private static readonly int[] PawnTableEg = {
         0,  0,  0,  0,  0,  0,  0,  0,
        80, 80, 80, 80, 80, 80, 80, 80,
        50, 50, 50, 50, 50, 50, 50, 50,
        30, 30, 30, 30, 30, 30, 30, 30,
        20, 20, 20, 20, 20, 20, 20, 20,
        10, 10, 10, 10, 10, 10, 10, 10,
        10, 10, 10, 10, 10, 10, 10, 10,
         0,  0,  0,  0,  0,  0,  0,  0
    };

    // Evaluation weights
    private const int BishopPairBonus = 30;
    private const int RookOnOpenFileBonus = 20;
    private const int RookOnSemiOpenFileBonus = 10;
    private const int DoubledPawnPenalty = -10;
    private const int IsolatedPawnPenalty = -20;
    private const int PassedPawnBonus = 20;
    private const int MobilityBonus = 2;
    private const int KingSafetyWeight = 10;

    // Passed pawn bonus by rank
    private static readonly int[] PassedPawnBonusByRank = { 0, 10, 20, 30, 50, 80, 120, 0 };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Evaluate(ref Board board)
    {
        int mgScore = 0;
        int egScore = 0;
        int phase = 0;

        // Material and piece-square tables
        EvaluateMaterial(ref board, ref mgScore, ref egScore, ref phase);

        // Pawn structure
        EvaluatePawnStructure(ref board, ref mgScore, ref egScore);

        // Piece activity
        EvaluatePieceActivity(ref board, ref mgScore, ref egScore);

        // King safety
        EvaluateKingSafety(ref board, ref mgScore);

        // Interpolate between middlegame and endgame
        int totalPhase = 24; // 4 knights + 4 bishops + 4 rooks + 2 queens = 24
        phase = Math.Min(phase, totalPhase);
        int score = (mgScore * phase + egScore * (totalPhase - phase)) / totalPhase;

        // Return score from white's perspective
        return board.SideToMove == Color.White ? score : -score;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvaluateMaterial(ref Board board, ref int mgScore, ref int egScore, ref int phase)
    {
        // White pieces
        mgScore += BitboardConstants.PopCount(board.WhitePawns) * PieceValueMg[6];
        egScore += BitboardConstants.PopCount(board.WhitePawns) * PieceValueEg[6];

        int whiteKnights = BitboardConstants.PopCount(board.WhiteKnights);
        mgScore += whiteKnights * PieceValueMg[1];
        egScore += whiteKnights * PieceValueEg[1];
        phase += whiteKnights;

        int whiteBishops = BitboardConstants.PopCount(board.WhiteBishops);
        mgScore += whiteBishops * PieceValueMg[2];
        egScore += whiteBishops * PieceValueEg[2];
        phase += whiteBishops;

        int whiteRooks = BitboardConstants.PopCount(board.WhiteRooks);
        mgScore += whiteRooks * PieceValueMg[3];
        egScore += whiteRooks * PieceValueEg[3];
        phase += whiteRooks * 2;

        int whiteQueens = BitboardConstants.PopCount(board.WhiteQueens);
        mgScore += whiteQueens * PieceValueMg[4];
        egScore += whiteQueens * PieceValueEg[4];
        phase += whiteQueens * 4;

        // Black pieces
        mgScore -= BitboardConstants.PopCount(board.BlackPawns) * PieceValueMg[6];
        egScore -= BitboardConstants.PopCount(board.BlackPawns) * PieceValueEg[6];

        int blackKnights = BitboardConstants.PopCount(board.BlackKnights);
        mgScore -= blackKnights * PieceValueMg[1];
        egScore -= blackKnights * PieceValueEg[1];
        phase += blackKnights;

        int blackBishops = BitboardConstants.PopCount(board.BlackBishops);
        mgScore -= blackBishops * PieceValueMg[2];
        egScore -= blackBishops * PieceValueEg[2];
        phase += blackBishops;

        int blackRooks = BitboardConstants.PopCount(board.BlackRooks);
        mgScore -= blackRooks * PieceValueMg[3];
        egScore -= blackRooks * PieceValueEg[3];
        phase += blackRooks * 2;

        int blackQueens = BitboardConstants.PopCount(board.BlackQueens);
        mgScore -= blackQueens * PieceValueMg[4];
        egScore -= blackQueens * PieceValueEg[4];
        phase += blackQueens * 4;

        // Bishop pair bonus
        if (whiteBishops >= 2) mgScore += BishopPairBonus;
        if (blackBishops >= 2) mgScore -= BishopPairBonus;

        // Piece-square tables
        EvaluatePieceSquareTables(ref board, ref mgScore, ref egScore);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvaluatePieceSquareTables(ref Board board, ref int mgScore, ref int egScore)
    {
        // White pieces
        ulong pieces = board.WhitePawns;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore += PawnTableMg[sq];
            egScore += PawnTableEg[sq];
        }

        pieces = board.WhiteKnights;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore += KnightTableMg[sq];
        }

        pieces = board.WhiteBishops;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore += BishopTableMg[sq];
        }

        pieces = board.WhiteRooks;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore += RookTableMg[sq];
        }

        pieces = board.WhiteQueens;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore += QueenTableMg[sq];
        }

        int whiteKingSquare = BitboardConstants.BitScanForward(board.WhiteKing);
        mgScore += KingTableMg[whiteKingSquare];
        egScore += KingTableEg[whiteKingSquare];

        // Black pieces (mirrored)
        pieces = board.BlackPawns;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore -= PawnTableMg[sq ^ 56]; // Mirror square
            egScore -= PawnTableEg[sq ^ 56];
        }

        pieces = board.BlackKnights;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore -= KnightTableMg[sq ^ 56];
        }

        pieces = board.BlackBishops;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore -= BishopTableMg[sq ^ 56];
        }

        pieces = board.BlackRooks;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore -= RookTableMg[sq ^ 56];
        }

        pieces = board.BlackQueens;
        while (pieces != 0)
        {
            int sq = BitboardConstants.BitScanForward(pieces);
            pieces = BitboardConstants.ClearBit(pieces, sq);
            mgScore -= QueenTableMg[sq ^ 56];
        }

        int blackKingSquare = BitboardConstants.BitScanForward(board.BlackKing);
        mgScore -= KingTableMg[blackKingSquare ^ 56];
        egScore -= KingTableEg[blackKingSquare ^ 56];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvaluatePawnStructure(ref Board board, ref int mgScore, ref int egScore)
    {
        // Evaluate white pawns
        for (int file = 0; file < 8; file++)
        {
            ulong fileMask = BitboardConstants.FileA << file;
            ulong whitePawnsOnFile = board.WhitePawns & fileMask;
            ulong blackPawnsOnFile = board.BlackPawns & fileMask;

            // Doubled pawns
            if (BitboardConstants.PopCount(whitePawnsOnFile) > 1)
                mgScore += DoubledPawnPenalty;

            // Isolated pawns
            ulong adjacentFiles = 0;
            if (file > 0) adjacentFiles |= BitboardConstants.FileA << (file - 1);
            if (file < 7) adjacentFiles |= BitboardConstants.FileA << (file + 1);

            if (whitePawnsOnFile != 0 && (board.WhitePawns & adjacentFiles) == 0)
                mgScore += IsolatedPawnPenalty;

            // Passed pawns
            ulong whitePassedPawn = whitePawnsOnFile;
            while (whitePassedPawn != 0)
            {
                int sq = BitboardConstants.BitScanForward(whitePassedPawn);
                whitePassedPawn = BitboardConstants.ClearBit(whitePassedPawn, sq);
                int rank = sq / 8;

                // Check if pawn is passed
                ulong frontSpan = 0;
                for (int r = rank + 1; r < 8; r++)
                {
                    frontSpan |= 1UL << (r * 8 + file);
                    if (file > 0) frontSpan |= 1UL << (r * 8 + file - 1);
                    if (file < 7) frontSpan |= 1UL << (r * 8 + file + 1);
                }

                if ((frontSpan & board.BlackPawns) == 0)
                {
                    mgScore += PassedPawnBonus + PassedPawnBonusByRank[rank];
                    egScore += PassedPawnBonus + PassedPawnBonusByRank[rank] * 2;
                }
            }
        }

        // Evaluate black pawns
        for (int file = 0; file < 8; file++)
        {
            ulong fileMask = BitboardConstants.FileA << file;
            ulong blackPawnsOnFile = board.BlackPawns & fileMask;

            // Doubled pawns
            if (BitboardConstants.PopCount(blackPawnsOnFile) > 1)
                mgScore -= DoubledPawnPenalty;

            // Isolated pawns
            ulong adjacentFiles = 0;
            if (file > 0) adjacentFiles |= BitboardConstants.FileA << (file - 1);
            if (file < 7) adjacentFiles |= BitboardConstants.FileA << (file + 1);

            if (blackPawnsOnFile != 0 && (board.BlackPawns & adjacentFiles) == 0)
                mgScore -= IsolatedPawnPenalty;

            // Passed pawns
            ulong blackPassedPawn = blackPawnsOnFile;
            while (blackPassedPawn != 0)
            {
                int sq = BitboardConstants.BitScanForward(blackPassedPawn);
                blackPassedPawn = BitboardConstants.ClearBit(blackPassedPawn, sq);
                int rank = sq / 8;

                // Check if pawn is passed
                ulong frontSpan = 0;
                for (int r = rank - 1; r >= 0; r--)
                {
                    frontSpan |= 1UL << (r * 8 + file);
                    if (file > 0) frontSpan |= 1UL << (r * 8 + file - 1);
                    if (file < 7) frontSpan |= 1UL << (r * 8 + file + 1);
                }

                if ((frontSpan & board.WhitePawns) == 0)
                {
                    mgScore -= PassedPawnBonus + PassedPawnBonusByRank[7 - rank];
                    egScore -= PassedPawnBonus + PassedPawnBonusByRank[7 - rank] * 2;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvaluatePieceActivity(ref Board board, ref int mgScore, ref int egScore)
    {
        // Rooks on open/semi-open files
        ulong whiteRooks = board.WhiteRooks;
        while (whiteRooks != 0)
        {
            int sq = BitboardConstants.BitScanForward(whiteRooks);
            whiteRooks = BitboardConstants.ClearBit(whiteRooks, sq);
            int file = sq % 8;
            ulong fileMask = BitboardConstants.FileA << file;

            if ((board.WhitePawns & fileMask) == 0)
            {
                if ((board.BlackPawns & fileMask) == 0)
                    mgScore += RookOnOpenFileBonus;
                else
                    mgScore += RookOnSemiOpenFileBonus;
            }
        }

        ulong blackRooks = board.BlackRooks;
        while (blackRooks != 0)
        {
            int sq = BitboardConstants.BitScanForward(blackRooks);
            blackRooks = BitboardConstants.ClearBit(blackRooks, sq);
            int file = sq % 8;
            ulong fileMask = BitboardConstants.FileA << file;

            if ((board.BlackPawns & fileMask) == 0)
            {
                if ((board.WhitePawns & fileMask) == 0)
                    mgScore -= RookOnOpenFileBonus;
                else
                    mgScore -= RookOnSemiOpenFileBonus;
            }
        }

        // Simple mobility evaluation
        ulong occupancy = board.AllPieces;

        // White mobility
        ulong whiteKnights = board.WhiteKnights;
        while (whiteKnights != 0)
        {
            int sq = BitboardConstants.BitScanForward(whiteKnights);
            whiteKnights = BitboardConstants.ClearBit(whiteKnights, sq);
            ulong moves = BitboardConstants.KnightMoves[sq] & ~board.WhitePieces;
            mgScore += BitboardConstants.PopCount(moves) * MobilityBonus;
        }

        ulong whiteBishops = board.WhiteBishops;
        while (whiteBishops != 0)
        {
            int sq = BitboardConstants.BitScanForward(whiteBishops);
            whiteBishops = BitboardConstants.ClearBit(whiteBishops, sq);
            ulong moves = MagicBitboards.GetBishopAttacks(sq, occupancy) & ~board.WhitePieces;
            mgScore += BitboardConstants.PopCount(moves) * MobilityBonus;
        }

        // Black mobility
        ulong blackKnights = board.BlackKnights;
        while (blackKnights != 0)
        {
            int sq = BitboardConstants.BitScanForward(blackKnights);
            blackKnights = BitboardConstants.ClearBit(blackKnights, sq);
            ulong moves = BitboardConstants.KnightMoves[sq] & ~board.BlackPieces;
            mgScore -= BitboardConstants.PopCount(moves) * MobilityBonus;
        }

        ulong blackBishops = board.BlackBishops;
        while (blackBishops != 0)
        {
            int sq = BitboardConstants.BitScanForward(blackBishops);
            blackBishops = BitboardConstants.ClearBit(blackBishops, sq);
            ulong moves = MagicBitboards.GetBishopAttacks(sq, occupancy) & ~board.BlackPieces;
            mgScore -= BitboardConstants.PopCount(moves) * MobilityBonus;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EvaluateKingSafety(ref Board board, ref int mgScore)
    {
        // Simple king safety - count attacking pieces near king
        int whiteKingSquare = BitboardConstants.BitScanForward(board.WhiteKing);
        int blackKingSquare = BitboardConstants.BitScanForward(board.BlackKing);

        // King safety zones (3x3 square around king)
        ulong whiteKingZone = BitboardConstants.KingMoves[whiteKingSquare] | (1UL << whiteKingSquare);
        ulong blackKingZone = BitboardConstants.KingMoves[blackKingSquare] | (1UL << blackKingSquare);

        // Count enemy pieces attacking king zone
        int whiteKingDanger = 0;
        int blackKingDanger = 0;

        // Black pieces attacking white king
        ulong blackAttackers = board.BlackKnights;
        while (blackAttackers != 0)
        {
            int sq = BitboardConstants.BitScanForward(blackAttackers);
            blackAttackers = BitboardConstants.ClearBit(blackAttackers, sq);
            if ((BitboardConstants.KnightMoves[sq] & whiteKingZone) != 0)
                whiteKingDanger++;
        }

        blackAttackers = board.BlackBishops | board.BlackQueens;
        while (blackAttackers != 0)
        {
            int sq = BitboardConstants.BitScanForward(blackAttackers);
            blackAttackers = BitboardConstants.ClearBit(blackAttackers, sq);
            if ((MagicBitboards.GetBishopAttacks(sq, board.AllPieces) & whiteKingZone) != 0)
                whiteKingDanger++;
        }

        blackAttackers = board.BlackRooks | board.BlackQueens;
        while (blackAttackers != 0)
        {
            int sq = BitboardConstants.BitScanForward(blackAttackers);
            blackAttackers = BitboardConstants.ClearBit(blackAttackers, sq);
            if ((MagicBitboards.GetRookAttacks(sq, board.AllPieces) & whiteKingZone) != 0)
                whiteKingDanger++;
        }

        // White pieces attacking black king
        ulong whiteAttackers = board.WhiteKnights;
        while (whiteAttackers != 0)
        {
            int sq = BitboardConstants.BitScanForward(whiteAttackers);
            whiteAttackers = BitboardConstants.ClearBit(whiteAttackers, sq);
            if ((BitboardConstants.KnightMoves[sq] & blackKingZone) != 0)
                blackKingDanger++;
        }

        whiteAttackers = board.WhiteBishops | board.WhiteQueens;
        while (whiteAttackers != 0)
        {
            int sq = BitboardConstants.BitScanForward(whiteAttackers);
            whiteAttackers = BitboardConstants.ClearBit(whiteAttackers, sq);
            if ((MagicBitboards.GetBishopAttacks(sq, board.AllPieces) & blackKingZone) != 0)
                blackKingDanger++;
        }

        whiteAttackers = board.WhiteRooks | board.WhiteQueens;
        while (whiteAttackers != 0)
        {
            int sq = BitboardConstants.BitScanForward(whiteAttackers);
            whiteAttackers = BitboardConstants.ClearBit(whiteAttackers, sq);
            if ((MagicBitboards.GetRookAttacks(sq, board.AllPieces) & blackKingZone) != 0)
                blackKingDanger++;
        }

        mgScore -= whiteKingDanger * KingSafetyWeight;
        mgScore += blackKingDanger * KingSafetyWeight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEndgame(ref Board board)
    {
        // Simple endgame detection: no queens or total material is low
        return board.WhiteQueens == 0 && board.BlackQueens == 0 ||
               BitboardConstants.PopCount(board.AllPieces) <= 10;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDrawnEndgame(ref Board board)
    {
        // Insufficient material detection
        ulong whitePieces = board.WhitePieces;
        ulong blackPieces = board.BlackPieces;

        // King vs King
        if (BitboardConstants.PopCount(whitePieces) == 1 && BitboardConstants.PopCount(blackPieces) == 1)
            return true;

        // King and minor piece vs King
        if (BitboardConstants.PopCount(whitePieces) == 2 && BitboardConstants.PopCount(blackPieces) == 1)
        {
            if (board.WhiteKnights != 0 || board.WhiteBishops != 0)
                return true;
        }
        if (BitboardConstants.PopCount(blackPieces) == 2 && BitboardConstants.PopCount(whitePieces) == 1)
        {
            if (board.BlackKnights != 0 || board.BlackBishops != 0)
                return true;
        }

        // King and Bishop vs King and Bishop (same color)
        if (BitboardConstants.PopCount(whitePieces) == 2 && BitboardConstants.PopCount(blackPieces) == 2 &&
            BitboardConstants.PopCount(board.WhiteBishops) == 1 && BitboardConstants.PopCount(board.BlackBishops) == 1)
        {
            int whiteBishopSquare = BitboardConstants.BitScanForward(board.WhiteBishops);
            int blackBishopSquare = BitboardConstants.BitScanForward(board.BlackBishops);

            // Same color bishops
            if (((whiteBishopSquare + whiteBishopSquare / 8) & 1) == ((blackBishopSquare + blackBishopSquare / 8) & 1))
                return true;
        }

        return false;
    }
}