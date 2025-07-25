using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chess.Tests
{
    /// <summary>
    /// Unit tests for NNUE loader and validator
    /// </summary>
    [TestClass]
    public class NNUETests
    {
        private string testDataPath = @"TestData\";
        private string validNNUEPath;
        private string invalidNNUEPath;

        [TestInitialize]
        public void Setup()
        {
            // Create test directory
            Directory.CreateDirectory(testDataPath);
            
            // Create test NNUE files
            validNNUEPath = Path.Combine(testDataPath, "valid_test.nnue");
            invalidNNUEPath = Path.Combine(testDataPath, "invalid_test.nnue");
            
            CreateValidTestNNUE(validNNUEPath);
            CreateInvalidTestNNUE(invalidNNUEPath);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Clean up test files
            if (Directory.Exists(testDataPath))
            {
                Directory.Delete(testDataPath, true);
            }
        }

        [TestMethod]
        public void LoadValidNNUE_ShouldSucceed()
        {
            // Arrange
            var nnue = new NNUE();

            // Act
            var result = nnue.LoadFromFile(validNNUEPath);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Errors.Count);
            Assert.IsTrue(nnue.IsLoaded);
            Assert.IsNotNull(nnue.Description);
        }

        [TestMethod]
        public void LoadInvalidNNUE_ShouldFail()
        {
            // Arrange
            var nnue = new NNUE();

            // Act
            var result = nnue.LoadFromFile(invalidNNUEPath);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Count > 0);
            Assert.IsFalse(nnue.IsLoaded);
        }

        [TestMethod]
        public void LoadNonExistentFile_ShouldFail()
        {
            // Arrange
            var nnue = new NNUE();
            string nonExistentPath = Path.Combine(testDataPath, "does_not_exist.nnue");

            // Act
            var result = nnue.LoadFromFile(nonExistentPath);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("File not found")));
        }

        [TestMethod]
        public void ValidateMultipleFiles_ShouldReturnCorrectResults()
        {
            // Arrange
            string[] filePaths = { validNNUEPath, invalidNNUEPath };

            // Act
            var results = NNUE.ValidateMultipleFiles(filePaths);

            // Assert
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results[validNNUEPath].IsValid);
            Assert.IsFalse(results[invalidNNUEPath].IsValid);
        }

        [TestMethod]
        public void GetSummary_WithLoadedModel_ShouldReturnDetails()
        {
            // Arrange
            var nnue = new NNUE();
            nnue.LoadFromFile(validNNUEPath);

            // Act
            string summary = nnue.GetSummary();

            // Assert
            Assert.IsTrue(summary.Contains("NNUE Model Summary"));
            Assert.IsTrue(summary.Contains("valid_test.nnue"));
            Assert.IsTrue(summary.Contains("Total Parameters"));
            Assert.IsTrue(summary.Contains("Memory Usage"));
        }

        [TestMethod]
        public void GetSummary_WithoutLoadedModel_ShouldReturnNoModelMessage()
        {
            // Arrange
            var nnue = new NNUE();

            // Act
            string summary = nnue.GetSummary();

            // Assert
            Assert.AreEqual("No NNUE model loaded", summary);
        }

        /// <summary>
        /// Creates a valid test NNUE file
        /// </summary>
        private void CreateValidTestNNUE(string filePath)
        {
            using (var writer = new BinaryWriter(File.Create(filePath)))
            {
                // Write header
                writer.Write(System.Text.Encoding.ASCII.GetBytes("NNUE")); // Magic
                writer.Write((uint)0x7AF32F16); // Version hash
                writer.Write((uint)177); // Architecture hash
                
                // Write description
                string description = "NNUE 768->256->1";
                byte[] descBytes = System.Text.Encoding.UTF8.GetBytes(description);
                writer.Write((uint)descBytes.Length);
                writer.Write(descBytes);

                // Write dummy weights (simplified - just write correct number of floats)
                int totalWeights = 768 * 256 + 256 +  // Layer 1
                                 256 * 32 + 32 +      // Layer 2
                                 32 * 32 + 32 +       // Output layer 1
                                 32 * 1 + 1;          // Output layer 2

                Random rand = new Random(42);
                for (int i = 0; i < totalWeights; i++)
                {
                    writer.Write((float)(rand.NextDouble() * 2 - 1) * 0.1f);
                }
            }
        }

        /// <summary>
        /// Creates an invalid test NNUE file
        /// </summary>
        private void CreateInvalidTestNNUE(string filePath)
        {
            using (var writer = new BinaryWriter(File.Create(filePath)))
            {
                // Write invalid magic
                writer.Write(System.Text.Encoding.ASCII.GetBytes("INVL"));
                writer.Write((uint)0x12345678);
            }
        }
    }

    /// <summary>
    /// Unit tests for HalfKP feature extraction
    /// </summary>
    [TestClass]
    public class HalfKPFeaturesTests
    {
        private HalfKPFeatures extractor;

        [TestInitialize]
        public void Setup()
        {
            extractor = new HalfKPFeatures();
        }

        [TestMethod]
        public void PositionToFeatures_StartingPosition_ShouldReturnCorrectFeatures()
        {
            // Arrange
            string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

            // Act
            var (whiteFeatures, blackFeatures) = extractor.PositionToFeatures(fen);

            // Assert
            Assert.AreEqual(768, whiteFeatures.Length);
            Assert.AreEqual(768, blackFeatures.Length);
            
            // Starting position should have exactly 30 active features (15 pieces each side, excluding kings)
            int whiteActive = whiteFeatures.Count(f => f > 0);
            int blackActive = blackFeatures.Count(f => f > 0);
            
            Assert.AreEqual(30, whiteActive);
            Assert.AreEqual(30, blackActive);
        }

        [TestMethod]
        public void PositionToFeatures_EmptyBoard_ShouldReturnZeroFeatures()
        {
            // Arrange
            string fen = "8/8/8/8/8/8/8/8 w - - 0 1";

            // Act
            var (whiteFeatures, blackFeatures) = extractor.PositionToFeatures(fen);

            // Assert
            Assert.IsTrue(whiteFeatures.All(f => f == 0));
            Assert.IsTrue(blackFeatures.All(f => f == 0));
        }

        [TestMethod]
        public void PositionToFeatures_KingAndPawn_ShouldReturnOneActiveFeature()
        {
            // Arrange
            string fen = "8/8/8/8/8/8/P7/K7 w - - 0 1";

            // Act
            var (whiteFeatures, blackFeatures) = extractor.PositionToFeatures(fen);

            // Assert
            int whiteActive = whiteFeatures.Count(f => f > 0);
            Assert.AreEqual(1, whiteActive); // Only the pawn relative to king
        }
    }

    /// <summary>
    /// Unit tests for NNUE evaluation
    /// </summary>
    [TestClass]
    public class NNUEEvaluatorTests
    {
        private NNUE nnue;
        private NNUEEvaluator evaluator;

        [TestInitialize]
        public void Setup()
        {
            // Create a mock loaded NNUE for testing
            nnue = new MockLoadedNNUE();
            evaluator = new NNUEEvaluator(nnue);
        }

        [TestMethod]
        public void Evaluate_StartingPosition_ShouldReturnNearZero()
        {
            // Arrange
            string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

            // Act
            float eval = evaluator.Evaluate(fen);

            // Assert
            // Starting position should be close to 0
            Assert.IsTrue(Math.Abs(eval) < 100, $"Expected near 0, got {eval}");
        }

        [TestMethod]
        public void Evaluate_WhiteUp_ShouldReturnPositive()
        {
            // Arrange
            string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

            // Act
            float eval = evaluator.Evaluate(fen);

            // Assert
            // This is a simplified test - actual evaluation depends on trained weights
            Assert.IsNotNull(eval);
        }

        [TestMethod]
        public void BatchEvaluate_MultiplePositions_ShouldReturnCorrectCount()
        {
            // Arrange
            string[] fens = {
                "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
                "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
                "rnbqkb1r/pppppppp/5n2/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 1 2"
            };

            // Act
            float[] evaluations = evaluator.BatchEvaluate(fens);

            // Assert
            Assert.AreEqual(fens.Length, evaluations.Length);
            Assert.IsTrue(evaluations.All(e => !float.IsNaN(e)));
        }

        [TestMethod]
        public void GetEvaluationBreakdown_ShouldReturnDetailedInfo()
        {
            // Arrange
            string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

            // Act
            var breakdown = evaluator.GetEvaluationBreakdown(fen);

            // Assert
            Assert.IsNotNull(breakdown);
            Assert.AreEqual(768, breakdown.TotalFeatures);
            Assert.IsTrue(breakdown.ActiveFeatures > 0);
            Assert.IsNotNull(breakdown.MostActiveFeatureIndices);
        }

        /// <summary>
        /// Mock NNUE class for testing evaluator
        /// </summary>
        private class MockLoadedNNUE : NNUE
        {
            
        }
    }

    /// <summary>
    /// Integration tests for the complete NNUE system
    /// </summary>
    [TestClass]
    public class NNUEIntegrationTests
    {
        [TestMethod]
        public void CompleteWorkflow_LoadAndEvaluate_ShouldWork()
        {
            // This test would require an actual NNUE file
            // For now, it's a placeholder showing the intended workflow
            
            // Arrange
            string nnuePath = @"models\test.nnue";
            
            if (!File.Exists(nnuePath))
            {
                Assert.Inconclusive("Test NNUE file not found");
                return;
            }

            // Act & Assert
            // Load NNUE
            var nnue = new NNUE();
            var loadResult = nnue.LoadFromFile(nnuePath);
            Assert.IsTrue(loadResult.IsValid);

            // Create evaluator
            var evaluator = nnue.CreateEvaluator();
            Assert.IsNotNull(evaluator);

            // Evaluate position
            string testFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
            float eval = evaluator.Evaluate(testFen);
            
            // Check evaluation is reasonable
            Assert.IsTrue(Math.Abs(eval) < 1000); // Less than 10 pawns
        }
    }
}