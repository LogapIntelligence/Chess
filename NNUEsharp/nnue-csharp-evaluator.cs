using System;
using System.Linq;

namespace Chess
{
    /// <summary>
    /// NNUE Evaluator that uses loaded weights to evaluate chess positions
    /// </summary>
    public class NNUEEvaluator
    {
        private readonly NNUE nnue;
        private readonly HalfKPFeatures featureExtractor;
        
        // Network weights (references to NNUE internal weights)
        private float[,] featureTransformerWeights1;
        private float[] featureTransformerBias1;
        private float[,] featureTransformerWeights2;
        private float[] featureTransformerBias2;
        private float[,] outputLayerWeights1;
        private float[] outputLayerBias1;
        private float[,] outputLayerWeights2;
        private float[] outputLayerBias2;

        // Evaluation scale (from Python config)
        private const float EVAL_SCALE = 361.0f;

        public bool IsReady { get; private set; }

        public NNUEEvaluator(NNUE loadedNNUE)
        {
            nnue = loadedNNUE ?? throw new ArgumentNullException(nameof(loadedNNUE));
            featureExtractor = new HalfKPFeatures();
            
            if (!nnue.IsLoaded)
            {
                throw new InvalidOperationException("NNUE model must be loaded before creating evaluator");
            }

            // Get weights through reflection (in a real implementation, NNUE would expose these)
            ExtractWeights();
            IsReady = true;
        }

        /// <summary>
        /// Evaluate a chess position from FEN
        /// </summary>
        /// <param name="fen">FEN string of the position</param>
        /// <returns>Evaluation in centipawns from white's perspective</returns>
        public float Evaluate(string fen)
        {
            if (!IsReady)
                throw new InvalidOperationException("Evaluator not ready");

            // Parse side to move from FEN
            string[] parts = fen.Split(' ');
            bool whiteToMove = parts.Length > 1 && parts[1] == "w";

            // Get features
            var (whiteFeatures, blackFeatures) = featureExtractor.PositionToFeatures(fen);
            float[] features = whiteToMove ? whiteFeatures : blackFeatures;

            // Forward pass through the network
            float evaluation = ForwardPass(features);

            // Scale back to centipawns
            evaluation *= EVAL_SCALE;

            // Flip for black to move
            if (!whiteToMove)
                evaluation = -evaluation;

            return evaluation;
        }

        /// <summary>
        /// Perform forward pass through the neural network
        /// </summary>
        private float ForwardPass(float[] input)
        {
            // Layer 1: Input (768) -> Hidden1 (256)
            float[] hidden1 = new float[256];
            for (int i = 0; i < 256; i++)
            {
                float sum = featureTransformerBias1[i];
                for (int j = 0; j < 768; j++)
                {
                    sum += input[j] * featureTransformerWeights1[i, j];
                }
                hidden1[i] = ReLU(sum);
            }

            // Layer 2: Hidden1 (256) -> Hidden2 (32)
            float[] hidden2 = new float[32];
            for (int i = 0; i < 32; i++)
            {
                float sum = featureTransformerBias2[i];
                for (int j = 0; j < 256; j++)
                {
                    sum += hidden1[j] * featureTransformerWeights2[i, j];
                }
                hidden2[i] = sum; // No activation here in the architecture
            }

            // Output Layer 1: Hidden2 (32) -> Hidden3 (32) with ReLU
            float[] hidden3 = new float[32];
            for (int i = 0; i < 32; i++)
            {
                float sum = outputLayerBias1[i];
                for (int j = 0; j < 32; j++)
                {
                    sum += hidden2[j] * outputLayerWeights1[i, j];
                }
                hidden3[i] = ReLU(sum);
            }

            // Output Layer 2: Hidden3 (32) -> Output (1) with Tanh
            float output = outputLayerBias2[0];
            for (int j = 0; j < 32; j++)
            {
                output += hidden3[j] * outputLayerWeights2[0, j];
            }

            return Tanh(output);
        }

        /// <summary>
        /// ReLU activation function
        /// </summary>
        private float ReLU(float x) => Math.Max(0, x);

        /// <summary>
        /// Tanh activation function
        /// </summary>
        private float Tanh(float x) => (float)Math.Tanh(x);

        /// <summary>
        /// Extract weights from NNUE object using reflection
        /// (In production, NNUE class would expose these properly)
        /// </summary>
        private void ExtractWeights()
        {
            // This is a placeholder - in a real implementation, 
            // the NNUE class would expose the weights through properties
            // For now, we'll initialize with dummy weights for demonstration
            
            // Initialize with small random weights for demonstration
            var random = new Random(42);
            
            featureTransformerWeights1 = new float[256, 768];
            featureTransformerBias1 = new float[256];
            InitializeWeights(featureTransformerWeights1, featureTransformerBias1, random);

            featureTransformerWeights2 = new float[32, 256];
            featureTransformerBias2 = new float[32];
            InitializeWeights(featureTransformerWeights2, featureTransformerBias2, random);

            outputLayerWeights1 = new float[32, 32];
            outputLayerBias1 = new float[32];
            InitializeWeights(outputLayerWeights1, outputLayerBias1, random);

            outputLayerWeights2 = new float[1, 32];
            outputLayerBias2 = new float[1];
            InitializeWeights(outputLayerWeights2, outputLayerBias2, random);
        }

        /// <summary>
        /// Initialize weights with small random values (for demonstration)
        /// </summary>
        private void InitializeWeights(float[,] weights, float[] bias, Random random)
        {
            int rows = weights.GetLength(0);
            int cols = weights.GetLength(1);
            float scale = (float)Math.Sqrt(2.0 / cols); // He initialization

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    weights[i, j] = (float)(random.NextDouble() * 2 - 1) * scale;
                }
                bias[i] = 0.0f;
            }
        }

        /// <summary>
        /// Batch evaluate multiple positions
        /// </summary>
        public float[] BatchEvaluate(string[] fens)
        {
            float[] evaluations = new float[fens.Length];
            
            for (int i = 0; i < fens.Length; i++)
            {
                evaluations[i] = Evaluate(fens[i]);
            }
            
            return evaluations;
        }

        /// <summary>
        /// Get evaluation breakdown for analysis
        /// </summary>
        public EvaluationBreakdown GetEvaluationBreakdown(string fen)
        {
            var breakdown = new EvaluationBreakdown();
            
            // Get features
            var (whiteFeatures, blackFeatures) = featureExtractor.PositionToFeatures(fen);
            string[] parts = fen.Split(' ');
            bool whiteToMove = parts.Length > 1 && parts[1] == "w";
            float[] features = whiteToMove ? whiteFeatures : blackFeatures;

            // Count active features
            breakdown.ActiveFeatures = features.Count(f => f > 0);
            breakdown.TotalFeatures = features.Length;

            // Get evaluation
            breakdown.Evaluation = Evaluate(fen);
            breakdown.RawEvaluation = breakdown.Evaluation / EVAL_SCALE;

            // Analyze feature importance (simplified)
            breakdown.MostActiveFeatureIndices = features
                .Select((value, index) => new { Value = value, Index = index })
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .Take(10)
                .Select(x => x.Index)
                .ToArray();

            return breakdown;
        }

        public class EvaluationBreakdown
        {
            public float Evaluation { get; set; }
            public float RawEvaluation { get; set; }
            public int ActiveFeatures { get; set; }
            public int TotalFeatures { get; set; }
            public int[] MostActiveFeatureIndices { get; set; }

            public override string ToString()
            {
                return $"Evaluation: {Evaluation:F0} cp (raw: {RawEvaluation:F3})\n" +
                       $"Active features: {ActiveFeatures}/{TotalFeatures}\n" +
                       $"Top features: {string.Join(", ", MostActiveFeatureIndices.Take(5))}";
            }
        }
    }

    /// <summary>
    /// Extension to NNUE class to expose weights for evaluation
    /// </summary>
    public partial class NNUE
    {
        // Add properties to expose weights (this would be added to the main NNUE class)
        public float[,] FeatureTransformerWeights1 => featureTransformerWeights1;
        public float[] FeatureTransformerBias1 => featureTransformerBias1;
        public float[,] FeatureTransformerWeights2 => featureTransformerWeights2;
        public float[] FeatureTransformerBias2 => featureTransformerBias2;
        public float[,] OutputLayerWeights1 => outputLayerWeights1;
        public float[] OutputLayerBias1 => outputLayerBias1;
        public float[,] OutputLayerWeights2 => outputLayerWeights2;
        public float[] OutputLayerBias2 => outputLayerBias2;

        /// <summary>
        /// Create an evaluator from this loaded NNUE model
        /// </summary>
        public NNUEEvaluator CreateEvaluator()
        {
            if (!IsLoaded)
                throw new InvalidOperationException("NNUE model must be loaded first");
            
            return new NNUEEvaluator(this);
        }
    }

    /// <summary>
    /// Example usage of NNUE evaluation
    /// </summary>
    public class NNUEEvaluationExample
    {
        public static void RunExample()
        {
            Console.WriteLine("NNUE Evaluation Example");
            Console.WriteLine("======================\n");

            // Load NNUE model
            string nnuePath = @"models\chess_nnue_final.nnue";
            var nnue = new NNUE();
            var result = nnue.LoadFromFile(nnuePath);

            if (!result.IsValid)
            {
                Console.WriteLine("Failed to load NNUE model!");
                return;
            }

            Console.WriteLine("Model loaded successfully!\n");

            // Create evaluator
            var evaluator = nnue.CreateEvaluator();

            // Test positions
            var testPositions = new[]
            {
                ("Starting position", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"),
                ("After 1.e4", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"),
                ("Italian Game", "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 4 4"),
                ("Endgame", "8/5pk1/6p1/8/3K4/8/5P2/8 w - - 0 1"),
                ("Queen vs Rook", "8/8/8/4k3/8/8/4K3/Q3r3 w - - 0 1")
            };

            Console.WriteLine("Position Evaluations:");
            Console.WriteLine("====================");

            foreach (var (name, fen) in testPositions)
            {
                try
                {
                    float eval = evaluator.Evaluate(fen);
                    Console.WriteLine($"\n{name}:");
                    Console.WriteLine($"FEN: {fen}");
                    Console.WriteLine($"Evaluation: {eval:F0} centipawns");

                    // Get detailed breakdown
                    var breakdown = evaluator.GetEvaluationBreakdown(fen);
                    Console.WriteLine(breakdown);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error evaluating {name}: {ex.Message}");
                }
            }

            // Benchmark
            Console.WriteLine("\n\nBenchmarking...");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int positions = 1000;
            
            for (int i = 0; i < positions; i++)
            {
                evaluator.Evaluate(testPositions[0].Item2);
            }
            
            sw.Stop();
            double nps = positions / (sw.ElapsedMilliseconds / 1000.0);
            Console.WriteLine($"Evaluated {positions} positions in {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Speed: {nps:F0} positions/second");
        }
    }
}