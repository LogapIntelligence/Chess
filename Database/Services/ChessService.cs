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

        public bool IsGameOver { get; private set; }
        public string GameResult { get; private set; }

        public ChessService()
        {
            // Initialize with empty board by default
            _board = new Piece[64];
        }

        public void LoadStartingPosition()
        {
            LoadFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        }

        public void LoadFen(string fen)
        {
            _board = new Piece[64];
            var parts = fen.Split(' ');

            if (parts.Length < 4)
                throw new ArgumentException("Invalid FEN string");

            // Reset game state
            IsGameOver = false;
            GameResult = "*";

            // Parse piece placement
            int rank = 7, file = 0;
            foreach (char c in parts[0])
            {
                if (c == '/')
                {
                    rank--;
                    file = 0;
                }
                else if (char.IsDigit(c))
                {
                    int emptySquares = c - '0';
                    for (int i = 0; i < emptySquares; i++)
                    {
                        _board[rank * 8 + file] = new Piece(PieceType.None, Player.White);
                        file++;
                    }
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
                _enPassantTargetSquare = AlgebraicToSquare(parts[3]);
            }
            else
            {
                _enPassantTargetSquare = null;
            }

            // Halfmove clock and fullmove number
            _halfMoveClock = parts.Length > 4 ? int.Parse(parts[4]) : 0;
            _fullMoveNumber = parts.Length > 5 ? int.Parse(parts[5]) : 1;

            // Check for draw conditions after loading position
            CheckDrawConditions();
        }

        public string GetFen()
        {
            var sb = new StringBuilder();

            // Piece placement
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

            // Active color
            sb.Append(Turn == Player.White ? " w " : " b ");

            // Castling availability
            string castling = "";
            if (_whiteKingSideCastle) castling += 'K';
            if (_whiteQueenSideCastle) castling += 'Q';
            if (_blackKingSideCastle) castling += 'k';
            if (_blackQueenSideCastle) castling += 'q';
            sb.Append(string.IsNullOrEmpty(castling) ? "-" : castling);

            // En passant
            sb.Append(' ');
            if (_enPassantTargetSquare.HasValue)
            {
                sb.Append(SquareToAlgebraic(_enPassantTargetSquare.Value));
            }
            else
            {
                sb.Append('-');
            }

            // Halfmove clock and fullmove number
            sb.Append($" {_halfMoveClock} {_fullMoveNumber}");

            return sb.ToString();
        }

        public bool TryApplyMove(string moveStr)
        {
            if (string.IsNullOrEmpty(moveStr) || moveStr.Length < 4)
                return false;

            // Basic move parsing (e.g., e2e4, e7e8q for promotion)
            string from = moveStr.Substring(0, 2);
            string to = moveStr.Substring(2, 2);
            char promotionPiece = moveStr.Length > 4 ? moveStr[4] : '\0';

            int fromSquare = AlgebraicToSquare(from);
            int toSquare = AlgebraicToSquare(to);

            if (fromSquare < 0 || fromSquare >= 64 || toSquare < 0 || toSquare >= 64)
                return false;

            var movingPiece = _board[fromSquare];
            var capturedPiece = _board[toSquare];

            if (movingPiece.Type == PieceType.None || movingPiece.Player != Turn)
                return false;

            // Update castling rights
            UpdateCastlingRights(fromSquare, toSquare, movingPiece);

            // Handle en passant
            int? previousEnPassant = _enPassantTargetSquare;
            _enPassantTargetSquare = null;

            // Check for en passant capture
            if (movingPiece.Type == PieceType.Pawn && toSquare == previousEnPassant)
            {
                int capturedPawnSquare = toSquare + (Turn == Player.White ? -8 : 8);
                _board[capturedPawnSquare] = new Piece(PieceType.None, Player.White);
            }

            // Set en passant target for two-square pawn moves
            if (movingPiece.Type == PieceType.Pawn && Math.Abs(toSquare - fromSquare) == 16)
            {
                _enPassantTargetSquare = fromSquare + (Turn == Player.White ? 8 : -8);
            }

            // Handle castling
            if (movingPiece.Type == PieceType.King && Math.Abs(toSquare - fromSquare) == 2)
            {
                // Kingside castling
                if (toSquare > fromSquare)
                {
                    _board[toSquare - 1] = _board[toSquare + 1];
                    _board[toSquare + 1] = new Piece(PieceType.None, Player.White);
                }
                // Queenside castling
                else
                {
                    _board[toSquare + 1] = _board[toSquare - 2];
                    _board[toSquare - 2] = new Piece(PieceType.None, Player.White);
                }
            }

            // Move the piece
            _board[toSquare] = movingPiece;
            _board[fromSquare] = new Piece(PieceType.None, Player.White);

            // Handle pawn promotion
            if (movingPiece.Type == PieceType.Pawn && (toSquare / 8 == 7 || toSquare / 8 == 0))
            {
                PieceType promotionType = promotionPiece switch
                {
                    'q' or 'Q' => PieceType.Queen,
                    'r' or 'R' => PieceType.Rook,
                    'b' or 'B' => PieceType.Bishop,
                    'n' or 'N' => PieceType.Knight,
                    _ => PieceType.Queen
                };
                _board[toSquare] = new Piece(promotionType, Turn);
            }

            // Update halfmove clock
            if (movingPiece.Type == PieceType.Pawn || capturedPiece.Type != PieceType.None)
            {
                _halfMoveClock = 0;
            }
            else
            {
                _halfMoveClock++;
            }

            // Update fullmove number
            if (Turn == Player.Black)
            {
                _fullMoveNumber++;
            }

            // Switch turns
            Turn = (Turn == Player.White) ? Player.Black : Player.White;

            // Check for draw conditions
            CheckDrawConditions();

            return true;
        }

        private void UpdateCastlingRights(int from, int to, Piece piece)
        {
            // King moves
            if (piece.Type == PieceType.King)
            {
                if (piece.Player == Player.White)
                {
                    _whiteKingSideCastle = false;
                    _whiteQueenSideCastle = false;
                }
                else
                {
                    _blackKingSideCastle = false;
                    _blackQueenSideCastle = false;
                }
            }

            // Rook moves or captures
            if (from == 0 || to == 0) _whiteQueenSideCastle = false;
            if (from == 7 || to == 7) _whiteKingSideCastle = false;
            if (from == 56 || to == 56) _blackQueenSideCastle = false;
            if (from == 63 || to == 63) _blackKingSideCastle = false;
        }

        private void CheckDrawConditions()
        {
            // 50-move rule
            if (_halfMoveClock >= 100)
            {
                IsGameOver = true;
                GameResult = "1/2-1/2";
                return;
            }

            // Insufficient material (simplified check)
            var pieces = new List<(PieceType type, Player player)>();
            for (int i = 0; i < 64; i++)
            {
                if (_board[i].Type != PieceType.None && _board[i].Type != PieceType.King)
                {
                    pieces.Add((_board[i].Type, _board[i].Player));
                }
            }

            // King vs King
            if (pieces.Count == 0)
            {
                IsGameOver = true;
                GameResult = "1/2-1/2";
                return;
            }

            // King + minor piece vs King
            if (pieces.Count == 1 && (pieces[0].type == PieceType.Bishop || pieces[0].type == PieceType.Knight))
            {
                IsGameOver = true;
                GameResult = "1/2-1/2";
                return;
            }
        }

        // Helper methods
        private int AlgebraicToSquare(string algebraic)
        {
            if (algebraic.Length < 2) return -1;
            int file = algebraic[0] - 'a';
            int rank = algebraic[1] - '1';
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return -1;
            return rank * 8 + file;
        }

        private string SquareToAlgebraic(int square)
        {
            return $"{(char)('a' + (square % 8))}{(char)('1' + (square / 8))}";
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
    }
}