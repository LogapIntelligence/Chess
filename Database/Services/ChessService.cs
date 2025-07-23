using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Database.Services
{
    public class ChessService : IChessService
    {
        private Piece[] _board;
        public Player Turn { get; private set; }
        private bool _whiteKingSideCastle;
        private bool _whiteQueenSideCastle;
        private bool _blackKingSideCastle;
        private bool _blackQueenSideCastle;
        private int? _enPassantTargetSquare;
        private int _halfMoveClock;
        private int _fullMoveNumber;

        public ChessService()
        {

        }

        public void NewGame()
        {
            string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
            LoadFen(fen);
        }

        public string GetFen()
        {
            var sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int emptySquares = 0;
                for (int file = 0; file < 8; file++)
                {
                    int index = rank * 8 + file;
                    var piece = _board[index];
                    if (piece.Type == PieceType.None)
                    {
                        emptySquares++;
                    }
                    else
                    {
                        if (emptySquares > 0)
                        {
                            sb.Append(emptySquares);
                            emptySquares = 0;
                        }
                        sb.Append(GetPieceChar(piece));
                    }
                }
                if (emptySquares > 0)
                {
                    sb.Append(emptySquares);
                }
                if (rank > 0)
                {
                    sb.Append('/');
                }
            }

            sb.Append(Turn == Player.White ? " w " : " b ");

            string castling = "";
            if (_whiteKingSideCastle) castling += 'K';
            if (_whiteQueenSideCastle) castling += 'Q';
            if (_blackKingSideCastle) castling += 'k';
            if (_blackQueenSideCastle) castling += 'q';
            sb.Append(string.IsNullOrEmpty(castling) ? "-" : castling);

            sb.Append(' ');

            if (_enPassantTargetSquare.HasValue)
            {
                sb.Append(SquareToString(_enPassantTargetSquare.Value));
            }
            else
            {
                sb.Append('-');
            }

            sb.Append($" {_halfMoveClock} {_fullMoveNumber}");

            return sb.ToString();
        }

        public bool ApplyMove(string algebraicMove)
        {
            if (!IsValidMove(algebraicMove)) return false;

            var (from, to) = AlgebraicToSquares(algebraicMove);
            var piece = _board[from];

            // Reset en passant target
            _enPassantTargetSquare = null;

            // Handle pawn moves
            if (piece.Type == PieceType.Pawn)
            {
                // En passant capture
                if (Math.Abs(to - from) != 8 && _board[to].Type == PieceType.None)
                {
                    int capturedPawnSquare = to + (Turn == Player.White ? -8 : 8);
                    _board[capturedPawnSquare] = new Piece(PieceType.None, Player.White);
                }
                // Set new en passant target
                if (Math.Abs(to - from) == 16)
                {
                    _enPassantTargetSquare = from + (Turn == Player.White ? 8 : -8);
                }
                _halfMoveClock = 0;
            }
            else if (_board[to].Type != PieceType.None)
            {
                _halfMoveClock = 0; // Capture
            }
            else
            {
                _halfMoveClock++;
            }

            // Handle castling
            if (piece.Type == PieceType.King && Math.Abs(from - to) == 2)
            {
                // Kingside
                if (to > from)
                {
                    _board[to - 1] = _board[to + 1];
                    _board[to + 1] = new Piece(PieceType.None, Player.White);
                }
                // Queenside
                else
                {
                    _board[to + 1] = _board[to - 2];
                    _board[to - 2] = new Piece(PieceType.None, Player.White);
                }
            }

            // Update castling rights
            if (piece.Type == PieceType.King)
            {
                if (Turn == Player.White)
                {
                    _whiteKingSideCastle = _whiteQueenSideCastle = false;
                }
                else
                {
                    _blackKingSideCastle = _blackQueenSideCastle = false;
                }
            }
            if (piece.Type == PieceType.Rook)
            {
                if (from == 0) _whiteQueenSideCastle = false;
                if (from == 7) _whiteKingSideCastle = false;
                if (from == 56) _blackQueenSideCastle = false;
                if (from == 63) _blackKingSideCastle = false;
            }


            // Move piece
            _board[to] = _board[from];
            _board[from] = new Piece(PieceType.None, Player.White);

            // Pawn promotion
            if (piece.Type == PieceType.Pawn && (to / 8 == 7 || to / 8 == 0))
            {
                // Default to Queen promotion for simplicity
                _board[to] = new Piece(PieceType.Queen, Turn);
            }

            if (Turn == Player.Black)
            {
                _fullMoveNumber++;
            }

            Turn = (Turn == Player.White) ? Player.Black : Player.White;

            return true;
        }

        public bool IsValidMove(string algebraicMove)
        {
            return GetLegalMoves().Contains(algebraicMove);
        }

        public bool IsCheckmate()
        {
            return IsInCheck(Turn) && GetLegalMoves().Count == 0;
        }

        public bool IsStalemate()
        {
            return !IsInCheck(Turn) && GetLegalMoves().Count == 0;
        }

        public List<string> GetLegalMoves()
        {
            var legalMoves = new List<string>();
            var pseudoLegalMoves = GeneratePseudoLegalMoves(Turn);

            foreach (var move in pseudoLegalMoves)
            {
                var (from, to) = AlgebraicToSquares(move);
                var originalBoard = (Piece[])_board.Clone();
                var originalTurn = Turn;
                var originalWKS = _whiteKingSideCastle;
                var originalWQS = _whiteQueenSideCastle;
                var originalBKS = _blackKingSideCastle;
                var originalBQS = _blackQueenSideCastle;
                var originalEP = _enPassantTargetSquare;

                // Make the move on a temporary board
                _board[to] = _board[from];
                _board[from] = new Piece(PieceType.None, Player.White);

                // Simplified move for check testing
                if (!IsInCheck(originalTurn))
                {
                    legalMoves.Add(move);
                }

                // Restore board state
                _board = originalBoard;
                Turn = originalTurn;
                _whiteKingSideCastle = originalWKS;
                _whiteQueenSideCastle = originalWQS;
                _blackKingSideCastle = originalBKS;
                _blackQueenSideCastle = originalBQS;
                _enPassantTargetSquare = originalEP;
            }
            return legalMoves;
        }

        // --- Private Helper Methods ---

        private void LoadFen(string fen)
        {
            _board = new Piece[64];
            var parts = fen.Split(' ');
            int rank = 7, file = 0;

            // Piece placement
            foreach (char c in parts[0])
            {
                if (c == '/')
                {
                    rank--;
                    file = 0;
                }
                else if (char.IsDigit(c))
                {
                    file += int.Parse(c.ToString());
                }
                else
                {
                    _board[rank * 8 + file] = GetPieceFromChar(c);
                    file++;
                }
            }

            // Active color
            Turn = (parts[1] == "w") ? Player.White : Player.Black;

            // Castling availability
            _whiteKingSideCastle = parts[2].Contains('K');
            _whiteQueenSideCastle = parts[2].Contains('Q');
            _blackKingSideCastle = parts[2].Contains('k');
            _blackQueenSideCastle = parts[2].Contains('q');

            // En passant target square
            if (parts[3] != "-")
            {
                _enPassantTargetSquare = StringToSquare(parts[3]);
            }
            else
            {
                _enPassantTargetSquare = null;
            }

            // Halfmove clock
            _halfMoveClock = int.Parse(parts[4]);

            // Fullmove number
            _fullMoveNumber = int.Parse(parts[5]);
        }

        private List<string> GeneratePseudoLegalMoves(Player player)
        {
            var moves = new List<string>();
            for (int i = 0; i < 64; i++)
            {
                if (_board[i].Type != PieceType.None && _board[i].Player == player)
                {
                    moves.AddRange(GenerateMovesForPiece(i));
                }
            }
            return moves;
        }

        private IEnumerable<string> GenerateMovesForPiece(int square)
        {
            var piece = _board[square];
            switch (piece.Type)
            {
                case PieceType.Pawn: return GeneratePawnMoves(square, piece.Player);
                case PieceType.Knight: return GenerateSlidingMoves(square, piece.Player, new[] { -17, -15, -10, -6, 6, 10, 15, 17 }, false);
                case PieceType.Bishop: return GenerateSlidingMoves(square, piece.Player, new[] { -9, -7, 7, 9 }, true);
                case PieceType.Rook: return GenerateSlidingMoves(square, piece.Player, new[] { -8, -1, 1, 8 }, true);
                case PieceType.Queen: return GenerateSlidingMoves(square, piece.Player, new[] { -9, -8, -7, -1, 1, 7, 8, 9 }, true);
                case PieceType.King: return GenerateKingMoves(square, piece.Player);
                default: return Enumerable.Empty<string>();
            }
        }

        private IEnumerable<string> GeneratePawnMoves(int square, Player player)
        {
            var moves = new List<string>();
            int direction = player == Player.White ? 1 : -1;
            int startRank = player == Player.White ? 1 : 6;

            // Forward one
            int oneStep = square + (8 * direction);
            if (IsOnBoard(oneStep) && _board[oneStep].Type == PieceType.None)
            {
                moves.Add(SquareToString(square) + SquareToString(oneStep));
                // Forward two from start
                if (square / 8 == startRank)
                {
                    int twoSteps = square + (16 * direction);
                    if (IsOnBoard(twoSteps) && _board[twoSteps].Type == PieceType.None)
                    {
                        moves.Add(SquareToString(square) + SquareToString(twoSteps));
                    }
                }
            }

            // Captures
            int[] captureOffsets = { 7, 9 };
            foreach (var offset in captureOffsets)
            {
                int targetSquare = square + (offset * direction);
                // Check for different file
                if (IsOnBoard(targetSquare) && Math.Abs((square % 8) - (targetSquare % 8)) == 1)
                {
                    if (_board[targetSquare].Type != PieceType.None && _board[targetSquare].Player != player)
                    {
                        moves.Add(SquareToString(square) + SquareToString(targetSquare));
                    }
                    if (targetSquare == _enPassantTargetSquare)
                    {
                        moves.Add(SquareToString(square) + SquareToString(targetSquare));
                    }
                }
            }
            return moves;
        }

        private IEnumerable<string> GenerateKingMoves(int square, Player player)
        {
            var moves = GenerateSlidingMoves(square, player, new[] { -9, -8, -7, -1, 1, 7, 8, 9 }, false).ToList();

            // Castling
            if (player == Player.White)
            {
                if (_whiteKingSideCastle && _board[5].Type == PieceType.None && _board[6].Type == PieceType.None && !IsSquareAttacked(4, Player.Black) && !IsSquareAttacked(5, Player.Black) && !IsSquareAttacked(6, Player.Black))
                    moves.Add("e1g1");
                if (_whiteQueenSideCastle && _board[1].Type == PieceType.None && _board[2].Type == PieceType.None && _board[3].Type == PieceType.None && !IsSquareAttacked(4, Player.Black) && !IsSquareAttacked(3, Player.Black) && !IsSquareAttacked(2, Player.Black))
                    moves.Add("e1c1");
            }
            else
            {
                if (_blackKingSideCastle && _board[61].Type == PieceType.None && _board[62].Type == PieceType.None && !IsSquareAttacked(60, Player.White) && !IsSquareAttacked(61, Player.White) && !IsSquareAttacked(62, Player.White))
                    moves.Add("e8g8");
                if (_blackQueenSideCastle && _board[57].Type == PieceType.None && _board[58].Type == PieceType.None && _board[59].Type == PieceType.None && !IsSquareAttacked(60, Player.White) && !IsSquareAttacked(59, Player.White) && !IsSquareAttacked(58, Player.White))
                    moves.Add("e8c8");
            }

            return moves;
        }

        private IEnumerable<string> GenerateSlidingMoves(int square, Player player, int[] directions, bool isSliding)
        {
            var moves = new List<string>();
            foreach (var direction in directions)
            {
                for (int i = 1; i < 8; i++)
                {
                    int targetSquare = square + direction * i;
                    if (!IsOnBoard(targetSquare) || Math.Abs((targetSquare % 8) - ((square + direction * (i-1)) % 8)) > 1 && Math.Abs(direction) != 8 && Math.Abs(direction) != 16) break;

                    if (_board[targetSquare].Type == PieceType.None)
                    {
                        moves.Add(SquareToString(square) + SquareToString(targetSquare));
                    }
                    else
                    {
                        if (_board[targetSquare].Player != player)
                        {
                            moves.Add(SquareToString(square) + SquareToString(targetSquare));
                        }
                        break;
                    }

                    if (!isSliding) break;
                }
            }
            return moves;
        }

        private bool IsInCheck(Player player)
        {
            int kingSquare = -1;
            for (int i = 0; i < 64; i++)
            {
                if (_board[i].Type == PieceType.King && _board[i].Player == player)
                {
                    kingSquare = i;
                    break;
                }
            }
            return IsSquareAttacked(kingSquare, player == Player.White ? Player.Black : Player.White);
        }

        private bool IsSquareAttacked(int square, Player byPlayer)
        {
            var opponentMoves = GeneratePseudoLegalMoves(byPlayer);
            foreach (var move in opponentMoves)
            {
                if (AlgebraicToSquares(move).to == square)
                {
                    return true;
                }
            }
            return false;
        }

        private (int from, int to) AlgebraicToSquares(string move)
        {
            int fromFile = move[0] - 'a';
            int fromRank = move[1] - '1';
            int toFile = move[2] - 'a';
            int toRank = move[3] - '1';
            return (fromRank * 8 + fromFile, toRank * 8 + toFile);
        }

        private string SquareToString(int square)
        {
            return $"{(char)('a' + (square % 8))}{(char)('1' + (square / 8))}";
        }

        private int StringToSquare(string s)
        {
            return (s[1] - '1') * 8 + (s[0] - 'a');
        }

        private char GetPieceChar(Piece piece)
        {
            char c = piece.Type switch
            {
                PieceType.Pawn => 'p',
                PieceType.Knight => 'n',
                PieceType.Bishop => 'b',
                PieceType.Rook => 'r',
                PieceType.Queen => 'q',
                PieceType.King => 'k',
                _ => ' '
            };
            return piece.Player == Player.White ? char.ToUpper(c) : c;
        }

        private Piece GetPieceFromChar(char c)
        {
            var player = char.IsUpper(c) ? Player.White : Player.Black;
            var type = char.ToLower(c) switch
            {
                'p' => PieceType.Pawn,
                'n' => PieceType.Knight,
                'b' => PieceType.Bishop,
                'r' => PieceType.Rook,
                'q' => PieceType.Queen,
                'k' => PieceType.King,
                _ => PieceType.None
            };
            return new Piece(type, player);
        }

        private bool IsOnBoard(int square) => square >= 0 && square < 64;
    }
}
