using System;
using System.Collections.Generic;
using Chess;

namespace GameGenerator
{
    public class GameGenerator
    {
        private readonly StockfishHelper _stockfish;
        private readonly int _searchDepth;
        private readonly Random _random;
        private readonly Search _engine;

        // Temperature for move selection (higher = more random)
        private const double Temperature = 0.5;
        private const int MaxMoves = 300; // Prevent infinite games

        public GameGenerator(StockfishHelper stockfish, int searchDepth)
        {
            _stockfish = stockfish;
            _searchDepth = searchDepth;
            _random = new Random();
            _engine = new Search(16); // Small hash table for game generation
        }

        public Game GenerateGame()
        {
            var game = new Game();
            var board = Board.StartingPosition();
            var moveCount = 0;

            while (moveCount < MaxMoves)
            {
                // Generate legal moves
                var moves = new MoveList();
                MoveGenerator.GenerateMoves(ref board, ref moves);

                if (moves.Count == 0)
                {
                    // Game over - checkmate or stalemate
                    if (board.IsInCheck())
                    {
                        // Checkmate - previous player wins
                        game.Result = board.SideToMove == Color.White ? 0 : 1;
                    }
                    else
                    {
                        // Stalemate - draw
                        game.Result = 0.5;
                    }
                    break;
                }

                // Check for draws
                if (board.HalfmoveClock >= 100)
                {
                    // 50-move rule
                    game.Result = 0.5;
                    break;
                }

                // Get position info
                string fen = FenParser.ToFen(ref board);
                ulong zobristHash = board.GetZobristHash();

                // Get Stockfish evaluation
                int stockfishEval = _stockfish.EvaluatePosition(fen, _searchDepth);

                // Choose move using engine with some randomness
                Move chosenMove = SelectMove(ref board, moves);

                // Store position info
                game.Positions.Add(new PositionInfo
                {
                    MoveNumber = moveCount + 1,
                    ZobristHash = zobristHash,
                    StockfishEval = stockfishEval,
                    Fen = fen
                });

                // Make the move
                board.MakeMove(chosenMove);
                moveCount++;

                // Check for insufficient material
                if (IsInsufficientMaterial(ref board))
                {
                    game.Result = 0.5;
                    break;
                }
            }

            // If we hit max moves, it's a draw
            if (moveCount >= MaxMoves && !game.Result.HasValue)
            {
                game.Result = 0.5;
            }

            return game;
        }

        private Move SelectMove(ref Board board, MoveList moves)
        {
            // Use engine to evaluate moves
            var moveScores = new List<(Move move, int score)>();

            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                var tempBoard = board;
                tempBoard.MakeMove(move);

                // Quick evaluation - could use full search for better play
                int score = -Evaluation.Evaluate(ref tempBoard);

                // Add some randomness based on temperature
                int noise = (int)(_random.NextDouble() * 100 * Temperature);
                score += _random.Next(2) == 0 ? noise : -noise;

                moveScores.Add((move, score));
            }

            // Sort by score and select from top moves
            moveScores.Sort((a, b) => b.score.CompareTo(a.score));

            // Select from top moves with some randomness
            int topN = Math.Min(3, moveScores.Count);
            int selectedIndex = _random.Next(topN);

            return moveScores[selectedIndex].move;
        }

        private bool IsInsufficientMaterial(ref Board board)
        {
            // King vs King
            if (BitboardConstants.PopCount(board.AllPieces) == 2)
                return true;

            // King and minor piece vs King
            if (BitboardConstants.PopCount(board.AllPieces) == 3)
            {
                if (board.WhiteKnights != 0 || board.BlackKnights != 0 ||
                    board.WhiteBishops != 0 || board.BlackBishops != 0)
                    return true;
            }

            // King and Bishop vs King and Bishop (same color)
            if (BitboardConstants.PopCount(board.AllPieces) == 4 &&
                BitboardConstants.PopCount(board.WhiteBishops) == 1 &&
                BitboardConstants.PopCount(board.BlackBishops) == 1)
            {
                int whiteBishopSquare = BitboardConstants.BitScanForward(board.WhiteBishops);
                int blackBishopSquare = BitboardConstants.BitScanForward(board.BlackBishops);

                // Same color bishops
                if (((whiteBishopSquare + whiteBishopSquare / 8) & 1) ==
                    ((blackBishopSquare + blackBishopSquare / 8) & 1))
                    return true;
            }

            return false;
        }
    }

    public class Game
    {
        public List<PositionInfo> Positions { get; } = new List<PositionInfo>();
        public double? Result { get; set; } // 1 = white wins, 0 = black wins, 0.5 = draw
    }

    public class PositionInfo
    {
        public int MoveNumber { get; set; }
        public ulong ZobristHash { get; set; }
        public int StockfishEval { get; set; }
        public string Fen { get; set; }
    }
}