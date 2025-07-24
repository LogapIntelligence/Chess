using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chess
{
    /// <summary>
    /// Alternative NNUE loader that can handle different file formats
    /// </summary>
    public class FlexibleNNUELoader
    {
        public static bool TryLoadNNUE(string filePath, out NNUEWeights weights)
        {
            weights = null;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    // Read header
                    byte[] magic = br.ReadBytes(4);
                    string magicStr = Encoding.ASCII.GetString(magic);

                    if (magicStr != "NNUE")
                    {
                        Console.WriteLine("Not an NNUE file");
                        return false;
                    }

                    uint version = br.ReadUInt32();
                    uint architecture = br.ReadUInt32();

                    Console.WriteLine($"NNUE Version: 0x{version:X8}");
                    Console.WriteLine($"Architecture: 0x{architecture:X8}");

                    // Try different loading strategies based on file size and architecture
                    long headerSize = fs.Position;
                    long dataSize = fs.Length - headerSize;

                    // Strategy 1: Check if next 4 bytes could be a description length
                    long savedPos = fs.Position;
                    uint firstUint = br.ReadUInt32();

                    // If the value is reasonable for a description length, try to skip it
                    if (firstUint > 0 && firstUint < 1000)
                    {
                        // Try to skip description
                        fs.Seek(savedPos + 4 + firstUint, SeekOrigin.Begin);
                        dataSize = fs.Length - fs.Position;
                    }
                    else
                    {
                        // No description, go back
                        fs.Seek(savedPos, SeekOrigin.Begin);
                        dataSize = fs.Length - savedPos;
                    }

                    // Now try to match data size to known architectures
                    Console.WriteLine($"Data section size: {dataSize} bytes");

                    // Try different architectures
                    if (TryLoadAsArchitecture1(br, dataSize, out weights))
                    {
                        Console.WriteLine("Loaded as Architecture 1: 768->256->32->32->1");
                        return true;
                    }

                    // Reset and try without description field
                    fs.Seek(12, SeekOrigin.Begin); // Skip magic, version, architecture
                    if (TryLoadAsArchitecture1(br, fs.Length - 12, out weights))
                    {
                        Console.WriteLine("Loaded as Architecture 1 (no description): 768->256->32->32->1");
                        return true;
                    }

                    // Try other architectures...
                    Console.WriteLine("Could not determine NNUE architecture");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading NNUE: {ex.Message}");
                return false;
            }
        }

        private static bool TryLoadAsArchitecture1(BinaryReader br, long availableBytes, out NNUEWeights weights)
        {
            weights = null;

            // Architecture: 768->256->32->32->1
            const int expectedBytes = 768 * 256 * 4 + 256 * 4 +  // Layer 1
                                     256 * 32 * 4 + 32 * 4 +    // Layer 2
                                     32 * 32 * 4 + 32 * 4 +     // Layer 3
                                     32 * 1 * 4 + 1 * 4;        // Layer 4

            // Allow some flexibility in size (within 1KB)
            if (Math.Abs(availableBytes - expectedBytes) > 1024)
            {
                Console.WriteLine($"Size mismatch for Architecture 1: expected ~{expectedBytes}, got {availableBytes}");
                return false;
            }

            try
            {
                weights = new NNUEWeights();

                // Read Layer 1: 768->256
                weights.Layer1Weights = new float[256, 768];
                weights.Layer1Bias = new float[256];
                ReadMatrix(br, weights.Layer1Weights);
                ReadVector(br, weights.Layer1Bias);

                // Read Layer 2: 256->32
                weights.Layer2Weights = new float[32, 256];
                weights.Layer2Bias = new float[32];
                ReadMatrix(br, weights.Layer2Weights);
                ReadVector(br, weights.Layer2Bias);

                // Read Layer 3: 32->32
                weights.Layer3Weights = new float[32, 32];
                weights.Layer3Bias = new float[32];
                ReadMatrix(br, weights.Layer3Weights);
                ReadVector(br, weights.Layer3Bias);

                // Read Layer 4: 32->1
                weights.Layer4Weights = new float[1, 32];
                weights.Layer4Bias = new float[1];
                ReadMatrix(br, weights.Layer4Weights);
                ReadVector(br, weights.Layer4Bias);

                return true;
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("Unexpected end of file while reading weights");
                weights = null;
                return false;
            }
        }

        private static void ReadMatrix(BinaryReader br, float[,] matrix)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    matrix[i, j] = br.ReadSingle();
                }
            }
        }

        private static void ReadVector(BinaryReader br, float[] vector)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = br.ReadSingle();
            }
        }
    }

    /// <summary>
    /// Container for NNUE weights
    /// </summary>
    public class NNUEWeights
    {
        public float[,] Layer1Weights { get; set; }
        public float[] Layer1Bias { get; set; }
        public float[,] Layer2Weights { get; set; }
        public float[] Layer2Bias { get; set; }
        public float[,] Layer3Weights { get; set; }
        public float[] Layer3Bias { get; set; }
        public float[,] Layer4Weights { get; set; }
        public float[] Layer4Bias { get; set; }
    }

    /// <summary>
    /// Simple NNUE evaluator using loaded weights
    /// </summary>
    public class SimpleNNUEEvaluator : INNUEEvaluator
    {
        private readonly NNUEWeights _weights;
        private readonly HalfKPFeatureEncoder _encoder;
        private const float EvalScale = 361.0f;

        public SimpleNNUEEvaluator(NNUEWeights weights)
        {
            _weights = weights;
            _encoder = new HalfKPFeatureEncoder();
        }

        public bool IsLoaded => _weights != null;

        public int Evaluate(ref Board board)
        {
            if (!IsLoaded)
                return Evaluation.Evaluate(ref board);

            // Get features
            float[] features = _encoder.GetFeatures(ref board, board.SideToMove);

            // Forward pass
            float[] h1 = Forward(_weights.Layer1Weights, _weights.Layer1Bias, features, true);
            float[] h2 = Forward(_weights.Layer2Weights, _weights.Layer2Bias, h1, true);
            float[] h3 = Forward(_weights.Layer3Weights, _weights.Layer3Bias, h2, true);
            float[] output = Forward(_weights.Layer4Weights, _weights.Layer4Bias, h3, false);

            // Apply tanh and scale
            float eval = (float)Math.Tanh(output[0]) * EvalScale;

            return (int)eval;
        }

        private float[] Forward(float[,] weights, float[] bias, float[] input, bool useRelu)
        {
            int outputSize = weights.GetLength(0);
            float[] output = new float[outputSize];

            // Matrix multiply + bias
            for (int i = 0; i < outputSize; i++)
            {
                float sum = bias[i];
                for (int j = 0; j < input.Length; j++)
                {
                    sum += weights[i, j] * input[j];
                }
                output[i] = useRelu ? Math.Max(0, sum) : sum;
            }

            return output;
        }
    }
}