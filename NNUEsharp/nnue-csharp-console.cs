using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Chess
{
    /// <summary>
    /// HalfKP feature extractor for NNUE evaluation
    /// Matches the Python implementation for compatibility
    /// </summary>
    public class HalfKPFeatures
    {
        private const int FEATURE_SIZE = 768;
        
        // Piece type indices matching Python implementation
        private readonly Dictionary<char, int> pieceToIndex = new Dictionary<char, int>
        {
            ['P'] = 0, ['N'] = 1, ['B'] = 2, ['R'] = 3, ['Q'] = 4, ['K'] = 5,
            ['p'] = 0, ['n'] = 1, ['b'] = 2, ['r'] = 3, ['q'] = 4, ['k'] = 5
        };

        /// <summary>
        /// Convert a FEN position to HalfKP features
        /// </summary>
        public (float[] whiteFeatures, float[] blackFeatures) PositionToFeatures(string fen)
        {
            var board = ParseFEN(fen);
            var whiteFeatures = GetHalfKPFeatures(board, true);
            var blackFeatures = GetHalfKPFeatures(board, false);
            
            return (whiteFeatures, blackFeatures);
        }

        /// <summary>
        /// Get HalfKP features for one side
        /// </summary>
        private float[] GetHalfKPFeatures(Dictionary<int, (char piece, bool isWhite)> board, bool forWhite)
        {
            var features = new float[FEATURE_SIZE];
            
            // Find king position
            int kingSquare = -1;
            foreach (var kvp in board)
            {
                if (kvp.Value.piece == (forWhite ? 'K' : 'k'))
                {
                    kingSquare = kvp.Key;
                    break;
                }
            }
            
            if (kingSquare == -1)
                return features;
            
            // Mirror for black
            if (!forWhite)
                kingSquare = MirrorSquare(kingSquare);
            
            // Encode all pieces relative to king
            foreach (var kvp in board)
            {
                int square = kvp.Key;
                char piece = kvp.Value.piece;
                bool pieceIsWhite = kvp.Value.isWhite;
                
                // Skip kings
                if (piece == 'K' || piece == 'k')
                    continue;
                
                // Mirror square for black perspective
                if (!forWhite)
                    square = MirrorSquare(square);
                
                // Calculate feature index
                int pieceColor = (pieceIsWhite == forWhite) ? 0 : 1;
                int pieceType = pieceToIndex[char.ToUpper(piece)];
                
                // HalfKP encoding: king_square * 10 * 64 + piece_color * 6 * 64 + piece_type * 64 + square
                int featureIdx = kingSquare * 10 * 64 + pieceColor * 6 * 64 + pieceType * 64 + square;
                
                if (featureIdx >= 0 && featureIdx < FEATURE_SIZE)
                {
                    features[featureIdx] = 1.0f;
                }
            }
            
            return features;
        }

        /// <summary>
        /// Parse FEN string to board representation
        /// </summary>
        private Dictionary<int, (char piece, bool isWhite)> ParseFEN(string fen)
        {
            var board = new Dictionary<int, (char piece, bool isWhite)>();
            string[] parts = fen.Split(' ');
            string position = parts[0];
            
            int rank = 7;
            int file = 0;
            
            foreach (char c in position)
            {
                if (c == '/')
                {
                    rank--;
                    file = 0;
                }
                else if (char.IsDigit(c))
                {
                    file += c - '0';
                }
                else
                {
                    int square = rank * 8 + file;
                    board[square] = (c, char.IsUpper(c));
                    file++;
                }
            }
            
            return board;
        }

        /// <summary>
        /// Mirror square for black perspective
        /// </summary>
        private int MirrorSquare(int square)
        {
            int rank = square / 8;
            int file = square % 8;
            return (7 - rank) * 8 + file;
        }
    }

    /// <summary>
    /// Console application to demonstrate NNUE loading and validation
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("         NNUE Loader and Validator");
            Console.WriteLine("==============================================\n");

            if (args.Length == 0)
            {
                ShowInteractiveMenu();
            }
            else
            {
                ProcessCommandLineArgs(args);
            }
        }

        static void ShowInteractiveMenu()
        {
            while (true)
            {
                Console.WriteLine("\nOptions:");
                Console.WriteLine("1. Load and validate NNUE file");
                Console.WriteLine("2. Compare multiple NNUE files");
                Console.WriteLine("3. Test HalfKP feature extraction");
                Console.WriteLine("4. Batch validate directory");
                Console.WriteLine("5. Exit");
                Console.Write("\nEnter choice (1-5): ");

                string choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        LoadSingleFile();
                        break;
                    case "2":
                        CompareMultipleFiles();
                        break;
                    case "3":
                        TestFeatureExtraction();
                        break;
                    case "4":
                        BatchValidateDirectory();
                        break;
                    case "5":
                        return;
                    default:
                        Console.WriteLine("Invalid choice!");
                        break;
                }
            }
        }

        static void LoadSingleFile()
        {
            Console.Write("\nEnter NNUE file path: ");
            string filePath = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                Console.WriteLine("No file path provided!");
                return;
            }

            var nnue = new NNUE();
            var result = nnue.LoadFromFile(filePath);

            DisplayValidationResult(filePath, result);

            if (result.IsValid)
            {
                Console.WriteLine("\n" + nnue.GetSummary());
            }
        }

        static void CompareMultipleFiles()
        {
            Console.WriteLine("\nEnter NNUE file paths (one per line, empty line to finish):");
            var filePaths = new List<string>();

            while (true)
            {
                string path = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(path))
                    break;
                filePaths.Add(path);
            }

            if (filePaths.Count == 0)
            {
                Console.WriteLine("No files provided!");
                return;
            }

            var results = NNUE.ValidateMultipleFiles(filePaths.ToArray());

            Console.WriteLine("\n=== Comparison Results ===");
            foreach (var kvp in results)
            {
                Console.WriteLine($"\n{Path.GetFileName(kvp.Key)}:");
                DisplayValidationResult(kvp.Key, kvp.Value);
            }

            // Compare parameters if all valid
            var validResults = results.Where(r => r.Value.IsValid).ToList();
            if (validResults.Count > 1)
            {
                Console.WriteLine("\n=== Parameter Comparison ===");
                foreach (var param in new[] { "TotalParameters", "MinWeight", "MaxWeight", "MeanWeight" })
                {
                    Console.WriteLine($"\n{param}:");
                    foreach (var kvp in validResults)
                    {
                        if (kvp.Value.Info.ContainsKey(param))
                        {
                            Console.WriteLine($"  {Path.GetFileName(kvp.Key)}: {kvp.Value.Info[param]}");
                        }
                    }
                }
            }
        }

        static void TestFeatureExtraction()
        {
            Console.WriteLine("\nTesting HalfKP Feature Extraction");
            Console.WriteLine("==================================");

            var extractor = new HalfKPFeatures();
            
            // Test positions
            var testPositions = new[]
            {
                ("Starting position", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"),
                ("After e4", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"),
                ("Italian Game", "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 4 4")
            };

            foreach (var (name, fen) in testPositions)
            {
                Console.WriteLine($"\n{name}:");
                Console.WriteLine($"FEN: {fen}");
                
                var (whiteFeatures, blackFeatures) = extractor.PositionToFeatures(fen);
                
                int whiteActive = whiteFeatures.Count(f => f > 0);
                int blackActive = blackFeatures.Count(f => f > 0);
                
                Console.WriteLine($"White features: {whiteActive} active out of {whiteFeatures.Length}");
                Console.WriteLine($"Black features: {blackActive} active out of {blackFeatures.Length}");
            }
        }

        static void BatchValidateDirectory()
        {
            Console.Write("\nEnter directory path: ");
            string dirPath = Console.ReadLine();

            if (!Directory.Exists(dirPath))
            {
                Console.WriteLine("Directory not found!");
                return;
            }

            var nnueFiles = Directory.GetFiles(dirPath, "*.nnue", SearchOption.AllDirectories);
            
            if (nnueFiles.Length == 0)
            {
                Console.WriteLine("No .nnue files found in directory!");
                return;
            }

            Console.WriteLine($"\nFound {nnueFiles.Length} NNUE files. Validating...\n");

            int validCount = 0;
            int invalidCount = 0;
            var errorSummary = new Dictionary<string, int>();

            foreach (var filePath in nnueFiles)
            {
                var nnue = new NNUE();
                var result = nnue.LoadFromFile(filePath);

                Console.Write($"{Path.GetFileName(filePath)}: ");
                
                if (result.IsValid)
                {
                    Console.WriteLine("VALID ✓");
                    validCount++;
                }
                else
                {
                    Console.WriteLine("INVALID ✗");
                    invalidCount++;
                    
                    foreach (var error in result.Errors)
                    {
                        string errorType = error.Split(':')[0];
                        errorSummary[errorType] = errorSummary.GetValueOrDefault(errorType, 0) + 1;
                    }
                }
            }

            Console.WriteLine("\n=== Summary ===");
            Console.WriteLine($"Total files: {nnueFiles.Length}");
            Console.WriteLine($"Valid: {validCount}");
            Console.WriteLine($"Invalid: {invalidCount}");
            
            if (errorSummary.Count > 0)
            {
                Console.WriteLine("\nError types:");
                foreach (var kvp in errorSummary.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
            }
        }

        static void ProcessCommandLineArgs(string[] args)
        {
            if (args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return;
            }

            if (args[0] == "--validate" || args[0] == "-v")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: No file specified!");
                    return;
                }

                for (int i = 1; i < args.Length; i++)
                {
                    var nnue = new NNUE();
                    var result = nnue.LoadFromFile(args[i]);
                    DisplayValidationResult(args[i], result);
                    
                    if (result.IsValid && args.Contains("--summary"))
                    {
                        Console.WriteLine("\n" + nnue.GetSummary());
                    }
                }
            }
            else if (args[0] == "--batch" || args[0] == "-b")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: No directory specified!");
                    return;
                }

                BatchValidateDirectory(args[1]);
            }
            else
            {
                // Assume it's a file path
                var nnue = new NNUE();
                var result = nnue.LoadFromFile(args[0]);
                DisplayValidationResult(args[0], result);
            }
        }

        static void BatchValidateDirectory(string dirPath)
        {
            if (!Directory.Exists(dirPath))
            {
                Console.WriteLine($"Error: Directory not found: {dirPath}");
                return;
            }

            var nnueFiles = Directory.GetFiles(dirPath, "*.nnue", SearchOption.AllDirectories);
            Console.WriteLine($"Found {nnueFiles.Length} NNUE files in {dirPath}\n");

            foreach (var file in nnueFiles)
            {
                var nnue = new NNUE();
                var result = nnue.LoadFromFile(file);
                
                string status = result.IsValid ? "VALID" : "INVALID";
                Console.WriteLine($"{Path.GetFileName(file)}: {status}");
                
                if (!result.IsValid && result.Errors.Any())
                {
                    Console.WriteLine($"  Error: {result.Errors.First()}");
                }
            }
        }

        static void DisplayValidationResult(string filePath, NNUE.ValidationResult result)
        {
            Console.WriteLine($"\n=== Validation Result for {Path.GetFileName(filePath)} ===");
            Console.WriteLine($"Valid: {result.IsValid}");

            if (result.Errors.Any())
            {
                Console.WriteLine("\nErrors:");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  ✗ {error}");
                }
            }

            if (result.Warnings.Any())
            {
                Console.WriteLine("\nWarnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"  ⚠ {warning}");
                }
            }

            if (result.Info.Any())
            {
                Console.WriteLine("\nInfo:");
                foreach (var kvp in result.Info)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("NNUE Loader and Validator");
            Console.WriteLine("\nUsage:");
            Console.WriteLine("  NNUELoader.exe                      - Interactive mode");
            Console.WriteLine("  NNUELoader.exe <file>               - Validate single file");
            Console.WriteLine("  NNUELoader.exe --validate <files>   - Validate multiple files");
            Console.WriteLine("  NNUELoader.exe --batch <directory>  - Validate all .nnue files in directory");
            Console.WriteLine("  NNUELoader.exe --help               - Show this help");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  --summary    Show detailed summary for valid files");
        }
    }
}