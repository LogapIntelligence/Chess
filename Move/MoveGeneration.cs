using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Move
{
    public static class MoveGeneration
    {
        public static unsafe int GenerateLegalsInto<TUs>(this Position pos, Move* moveList) where TUs : IColor, new()
        {
            var us = new TUs();
            var them = us.Opposite();
            var usColor = us.Value;
            var themColor = them.Value;

            int listIdx = 0;

            ulong usBb = pos.AllPieces(usColor);
            ulong themBb = pos.AllPieces(themColor);
            ulong all = usBb | themBb;

            Square ourKing = Bitboard.Bsf(pos.BitboardOf(usColor, PieceType.King));
            Square theirKing = Bitboard.Bsf(pos.BitboardOf(themColor, PieceType.King));

            if (ourKing == Square.NoSquare)
            {
                return 0;
            }

            if (theirKing == Square.NoSquare)
            {
                return 0;
            }

            ulong ourDiagSliders = pos.DiagonalSliders(usColor);
            ulong theirDiagSliders = pos.DiagonalSliders(themColor);
            ulong ourOrthSliders = pos.OrthogonalSliders(usColor);
            ulong theirOrthSliders = pos.OrthogonalSliders(themColor);

            ulong b1, b2, b3;

            ulong danger = 0;

            danger |= Tables.PawnAttacks(themColor, pos.BitboardOf(themColor, PieceType.Pawn))
                   | Tables.Attacks(PieceType.King, theirKing, all);

            b1 = pos.BitboardOf(themColor, PieceType.Knight);
            while (b1 != 0) danger |= Tables.Attacks(PieceType.Knight, Bitboard.PopLsb(ref b1), all);

            b1 = theirDiagSliders;
            while (b1 != 0) danger |= Tables.Attacks(PieceType.Bishop, Bitboard.PopLsb(ref b1), all ^ Bitboard.SQUARE_BB[(int)ourKing]);

            b1 = theirOrthSliders;
            while (b1 != 0) danger |= Tables.Attacks(PieceType.Rook, Bitboard.PopLsb(ref b1), all ^ Bitboard.SQUARE_BB[(int)ourKing]);

            b1 = Tables.Attacks(PieceType.King, ourKing, all) & ~(usBb | danger);
            MakeQuietInto(ourKing, b1 & ~themBb, moveList, ref listIdx);
            MakeCaptureInto(ourKing, b1 & themBb, moveList, ref listIdx);

            ulong captureask;
            ulong quietMask;
            Square s;

            pos.Checkers = Tables.Attacks(PieceType.Knight, ourKing, all) & pos.BitboardOf(themColor, PieceType.Knight)
                        | Tables.PawnAttacks(usColor, ourKing) & pos.BitboardOf(themColor, PieceType.Pawn);

            ulong candidates = Tables.Attacks(PieceType.Rook, ourKing, themBb) & theirOrthSliders
                            | Tables.Attacks(PieceType.Bishop, ourKing, themBb) & theirDiagSliders;

            pos.Pinned = 0;
            while (candidates != 0)
            {
                s = Bitboard.PopLsb(ref candidates);
                b1 = Tables.SQUARES_BETWEEN_BB[(int)ourKing][(int)s] & usBb;

                if (b1 == 0)
                    pos.Checkers ^= Bitboard.SQUARE_BB[(int)s];
                else if ((b1 & (b1 - 1)) == 0)
                    pos.Pinned ^= b1;
            }

            ulong notPinned = ~pos.Pinned;

            switch (Bitboard.SparsePopCount(pos.Checkers))
            {
                case 2:
                    return listIdx;

                case 1:
                    {
                        Square checkerSquare = Bitboard.Bsf(pos.Checkers);
                        var checkerPiece = pos.At(checkerSquare);

                        if (checkerPiece == Types.MakePiece(themColor, PieceType.Pawn))
                        {
                            if (pos.History[pos.Ply].Epsq != Square.NoSquare)
                            {
                                var epTarget = Bitboard.SQUARE_BB[(int)pos.History[pos.Ply].Epsq];
                                var southDir = Types.RelativeDir(usColor, Direction.South);
                                if (pos.Checkers == Bitboard.Shift(southDir, epTarget))
                                {
                                    b1 = Tables.PawnAttacks(themColor, pos.History[pos.Ply].Epsq)
                                       & pos.BitboardOf(usColor, PieceType.Pawn) & notPinned;
                                    while (b1 != 0)
                                        moveList[listIdx++] = new Move(Bitboard.PopLsb(ref b1), pos.History[pos.Ply].Epsq, MoveFlags.EnPassant);
                                }
                            }
                            captureask = pos.Checkers;
                            quietMask = 0;
                            break;
                        }
                        else if (checkerPiece == Types.MakePiece(themColor, PieceType.Knight))
                        {
                            b1 = pos.AttackersFrom(usColor, checkerSquare, all) & notPinned;
                            while (b1 != 0)
                                moveList[listIdx++] = new Move(Bitboard.PopLsb(ref b1), checkerSquare, MoveFlags.Capture);
                            return listIdx;
                        }
                        else
                        {
                            captureask = pos.Checkers;
                            quietMask = Tables.SQUARES_BETWEEN_BB[(int)ourKing][(int)checkerSquare];
                            break;
                        }
                    }
                case 0:
                default:
                    captureask = themBb;
                    quietMask = ~all;

                    if (pos.History[pos.Ply].Epsq != Square.NoSquare)
                    {
                        HandleEnPassantInto(pos, usColor, notPinned, ourKing, theirOrthSliders, all, moveList, ref listIdx);
                    }

                    HandleCastlingInto(pos, usColor, all, danger, moveList, ref listIdx);

                    HandlePinnedPiecesInto(pos, usColor, notPinned, ourKing, all, captureask, quietMask, moveList, ref listIdx);

                    break;
            }

            HandleNonPinnedPiecesInto(pos, usColor, notPinned, all, captureask, quietMask, moveList, ref listIdx);

            return listIdx;
        }

        private static unsafe void MakeQuietInto(Square from, ulong to, Move* moveList, ref int idx)
        {
            while (to != 0)
            {
                var sq = Bitboard.PopLsb(ref to);
                moveList[idx++] = new Move(from, sq, MoveFlags.Quiet);
            }
        }
        private static unsafe void MakeCaptureInto(Square from, ulong to, Move* moveList, ref int idx)
        {
            while (to != 0)
            {
                var sq = Bitboard.PopLsb(ref to);
                moveList[idx++] = new Move(from, sq, MoveFlags.Capture);
            }
        }

        private static unsafe void MakeDoublePushInto(Square from, ulong to, Move* moveList, ref int idx)
        {
            while (to != 0)
            {
                var sq = Bitboard.PopLsb(ref to);
                moveList[idx++] = new Move(from, sq, MoveFlags.DoublePush);
            }
        }

        private static unsafe void MakePromotionCapturesInto(Square from, ulong to, Move* moveList, ref int idx)
        {
            while (to != 0)
            {
                var sq = Bitboard.PopLsb(ref to);
                moveList[idx++] = new Move(from, sq, MoveFlags.PcKnight);
                moveList[idx++] = new Move(from, sq, MoveFlags.PcBishop);
                moveList[idx++] = new Move(from, sq, MoveFlags.PcRook);
                moveList[idx++] = new Move(from, sq, MoveFlags.PcQueen);
            }
        }

        private static unsafe void HandleEnPassantInto(Position pos, Color usColor, ulong notPinned, Square ourKing,
            ulong theirOrthSliders, ulong all, Move* moveList, ref int listIdx)
        {
            ulong b1, b2;
            Square s;

            if (pos.History[pos.Ply].Epsq == Square.NoSquare)
                return;

            b2 = Tables.PawnAttacks(usColor.Flip(), pos.History[pos.Ply].Epsq) & pos.BitboardOf(usColor, PieceType.Pawn);
            b1 = b2 & notPinned;

            while (b1 != 0)
            {
                s = Bitboard.PopLsb(ref b1);
                var southDir = Types.RelativeDir(usColor, Direction.South);
                var epCaptureSquare = (Square)((int)pos.History[pos.Ply].Epsq + (int)southDir);

                var newOcc = all ^ Bitboard.SQUARE_BB[(int)s] ^ Bitboard.SQUARE_BB[(int)epCaptureSquare];
                var rankMask = Bitboard.MASK_RANK[(int)Types.RankOf(ourKing)];

                if ((Tables.SlidingAttacks(ourKing, newOcc, rankMask) & theirOrthSliders) == 0)
                    moveList[listIdx++] = new Move(s, pos.History[pos.Ply].Epsq, MoveFlags.EnPassant);
            }

            b1 = b2 & pos.Pinned & Tables.LINE[(int)pos.History[pos.Ply].Epsq][(int)ourKing];
            if (b1 != 0)
            {
                moveList[listIdx++] = new Move(Bitboard.Bsf(b1), pos.History[pos.Ply].Epsq, MoveFlags.EnPassant);
            }
        }

        private static unsafe void HandleCastlingInto(Position pos, Color usColor, ulong all, ulong danger, Move* moveList, ref int listIdx)
        {
            var rights = pos.History[pos.Ply].Castling;

            if (usColor == Color.White)
            {
                // Kingside Castling (O-O)
                if ((rights & CastlingRights.WhiteOO) != 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.f1] & all) == 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.g1] & all) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.e1]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.f1]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.g1]) == 0)
                {
                    moveList[listIdx++] = new Move(Square.e1, Square.g1, MoveFlags.OO);
                }

                // Queenside Castling (O-O-O)
                if ((rights & CastlingRights.WhiteOOO) != 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.d1] & all) == 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.c1] & all) == 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.b1] & all) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.e1]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.d1]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.c1]) == 0)
                {
                    moveList[listIdx++] = new Move(Square.e1, Square.c1, MoveFlags.OOO);
                }
            }
            else // Black
            {
                // Kingside Castling (O-O)
                if ((rights & CastlingRights.BlackOO) != 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.f8] & all) == 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.g8] & all) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.e8]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.f8]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.g8]) == 0)
                {
                    moveList[listIdx++] = new Move(Square.e8, Square.g8, MoveFlags.OO);
                }

                // Queenside Castling (O-O-O)
                if ((rights & CastlingRights.BlackOOO) != 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.d8] & all) == 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.c8] & all) == 0 &&
                    (Bitboard.SQUARE_BB[(int)Square.b8] & all) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.e8]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.d8]) == 0 &&
                    (danger & Bitboard.SQUARE_BB[(int)Square.c8]) == 0)
                {
                    moveList[listIdx++] = new Move(Square.e8, Square.c8, MoveFlags.OOO);
                }
            }
        }
        private static unsafe void HandlePinnedPiecesInto(Position pos, Color usColor, ulong notPinned, Square ourKing,
            ulong all, ulong captureask, ulong quietMask, Move* moveList, ref int listIdx)
        {
            ulong b1, b2, b3;
            Square s;

            b1 = ~(notPinned | pos.BitboardOf(usColor, PieceType.Knight));
            while (b1 != 0)
            {
                s = Bitboard.PopLsb(ref b1);
                var pt = Types.TypeOf(pos.At(s));
                if (pt == PieceType.Pawn) continue;
                b2 = Tables.Attacks(pt, s, all) & Tables.LINE[(int)ourKing][(int)s];
                MakeQuietInto(s, b2 & quietMask, moveList, ref listIdx);
                MakeCaptureInto(s, b2 & captureask, moveList, ref listIdx);
            }

            b1 = ~notPinned & pos.BitboardOf(usColor, PieceType.Pawn);
            while (b1 != 0)
            {
                s = Bitboard.PopLsb(ref b1);

                if (Types.RankOf(s) == Types.RelativeRank(usColor, Rank.Rank7))
                {
                    b2 = Tables.PawnAttacks(usColor, s) & captureask & Tables.LINE[(int)ourKing][(int)s];
                    MakePromotionCapturesInto(s, b2, moveList, ref listIdx);

                    var northDir = Types.RelativeDir(usColor, Direction.North);
                    b2 = Bitboard.Shift((Direction)northDir, Bitboard.SQUARE_BB[(int)s]) & ~all & Tables.LINE[(int)ourKing][(int)s];
                    while (b2 != 0)
                    {
                        var to = Bitboard.PopLsb(ref b2);
                        moveList[listIdx++] = new Move(s, to, MoveFlags.PrKnight);
                        moveList[listIdx++] = new Move(s, to, MoveFlags.PrBishop);
                        moveList[listIdx++] = new Move(s, to, MoveFlags.PrRook);
                        moveList[listIdx++] = new Move(s, to, MoveFlags.PrQueen);
                    }
                }
                else
                {
                    b2 = Tables.PawnAttacks(usColor, s) & pos.AllPieces(usColor.Flip()) & Tables.LINE[(int)ourKing][(int)s];

                    MakeCaptureInto(s, b2, moveList, ref listIdx);

                    var northDir = Types.RelativeDir(usColor, Direction.North);
                    b2 = Bitboard.Shift((Direction)northDir, Bitboard.SQUARE_BB[(int)s]) & ~all & Tables.LINE[(int)ourKing][(int)s];

                    b3 = Bitboard.Shift((Direction)northDir, b2 & Bitboard.MASK_RANK[(int)Types.RelativeRank(usColor, Rank.Rank3)])
                       & ~all & Tables.LINE[(int)ourKing][(int)s];

                    MakeQuietInto(s, b2, moveList, ref listIdx);
                    MakeDoublePushInto(s, b3, moveList, ref listIdx);
                }
            }
        }

        private static unsafe void HandleNonPinnedPiecesInto(Position pos, Color usColor, ulong notPinned,
            ulong all, ulong captureask, ulong quietMask, Move* moveList, ref int listIdx)
        {
            ulong b1, b2, b3;
            Square s;

            b1 = pos.BitboardOf(usColor, PieceType.Knight) & notPinned;
            while (b1 != 0)
            {
                s = Bitboard.PopLsb(ref b1);
                b2 = Tables.Attacks(PieceType.Knight, s, all);
                MakeQuietInto(s, b2 & quietMask, moveList, ref listIdx);
                MakeCaptureInto(s, b2 & captureask, moveList, ref listIdx);
            }

            b1 = pos.DiagonalSliders(usColor) & notPinned;
            while (b1 != 0)
            {
                s = Bitboard.PopLsb(ref b1);
                b2 = Tables.Attacks(PieceType.Bishop, s, all);
                MakeQuietInto(s, b2 & quietMask, moveList, ref listIdx);
                MakeCaptureInto(s, b2 & captureask, moveList, ref listIdx);
            }

            b1 = pos.OrthogonalSliders(usColor) & notPinned;
            while (b1 != 0)
            {
                s = Bitboard.PopLsb(ref b1);
                b2 = Tables.Attacks(PieceType.Rook, s, all);
                MakeQuietInto(s, b2 & quietMask, moveList, ref listIdx);
                MakeCaptureInto(s, b2 & captureask, moveList, ref listIdx);
            }

            HandleNonPinnedPawnsInto(pos, usColor, notPinned, all, captureask, quietMask, moveList, ref listIdx);
        }

        private static unsafe void HandleNonPinnedPawnsInto(Position pos, Color usColor, ulong notPinned,
            ulong all, ulong captureask, ulong quietMask, Move* moveList, ref int listIdx)
        {
            ulong b1, b2, b3;
            Square s;
            var northDir = Types.RelativeDir(usColor, Direction.North);
            var northNorthDir = Types.RelativeDir(usColor, Direction.NorthNorth);
            var northWestDir = Types.RelativeDir(usColor, Direction.NorthWest);
            var northEastDir = Types.RelativeDir(usColor, Direction.NorthEast);

            b1 = pos.BitboardOf(usColor, PieceType.Pawn) & notPinned
               & ~Bitboard.MASK_RANK[(int)Types.RelativeRank(usColor, Rank.Rank7)];

            b2 = Bitboard.Shift((Direction)northDir, b1) & ~all;

            b3 = Bitboard.Shift((Direction)northDir, b2 & Bitboard.MASK_RANK[(int)Types.RelativeRank(usColor, Rank.Rank3)]) & quietMask;

            b2 &= quietMask;

            while (b2 != 0)
            {
                s = Bitboard.PopLsb(ref b2);
                moveList[listIdx++] = new Move((Square)((int)s - (int)northDir), s, MoveFlags.Quiet);
            }

            while (b3 != 0)
            {
                s = Bitboard.PopLsb(ref b3);
                moveList[listIdx++] = new Move((Square)((int)s - (int)northNorthDir), s, MoveFlags.DoublePush);
            }

            b2 = Bitboard.Shift((Direction)northWestDir, b1) & captureask;
            b3 = Bitboard.Shift((Direction)northEastDir, b1) & captureask;

            while (b2 != 0)
            {
                s = Bitboard.PopLsb(ref b2);
                moveList[listIdx++] = new Move((Square)((int)s - (int)northWestDir), s, MoveFlags.Capture);
            }

            while (b3 != 0)
            {
                s = Bitboard.PopLsb(ref b3);
                moveList[listIdx++] = new Move((Square)((int)s - (int)northEastDir), s, MoveFlags.Capture);
            }

            b1 = pos.BitboardOf(usColor, PieceType.Pawn) & notPinned
               & Bitboard.MASK_RANK[(int)Types.RelativeRank(usColor, Rank.Rank7)];

            if (b1 != 0)
            {
                b2 = Bitboard.Shift((Direction)northDir, b1) & quietMask;
                while (b2 != 0)
                {
                    s = Bitboard.PopLsb(ref b2);
                    var from = (Square)((int)s - (int)northDir);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PrKnight);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PrBishop);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PrRook);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PrQueen);
                }

                b2 = Bitboard.Shift((Direction)northWestDir, b1) & captureask;
                b3 = Bitboard.Shift((Direction)northEastDir, b1) & captureask;

                while (b2 != 0)
                {
                    s = Bitboard.PopLsb(ref b2);
                    var from = (Square)((int)s - (int)northWestDir);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcKnight);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcBishop);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcRook);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcQueen);
                }

                while (b3 != 0)
                {
                    s = Bitboard.PopLsb(ref b3);
                    var from = (Square)((int)s - (int)northEastDir);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcKnight);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcBishop);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcRook);
                    moveList[listIdx++] = new Move(from, s, MoveFlags.PcQueen);
                }
            }
        }
    }
}