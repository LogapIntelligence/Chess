using System;
using System.IO;
using System.Text;

namespace Chess
{
    public static class NNUEAnalyzer
    {
        public static void AnalyzeNNUEFile(string filePath)
        {
            Console.WriteLine($"=== NNUE File Analysis ===");
            Console.WriteLine($"File: {filePath}");

            try
            {
                var fileInfo = new FileInfo(filePath);
                Console.WriteLine($"File size: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");
                Console.WriteLine();

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    // Read first 256 bytes as hex dump
                    Console.WriteLine("First 256 bytes (hex dump):");
                    byte[] header = br.ReadBytes(Math.Min(256, (int)fileInfo.Length));
                    PrintHexDump(header);

                    // Reset to beginning
                    fs.Seek(0, SeekOrigin.Begin);

                    // Try to read as standard NNUE format
                    Console.WriteLine("\nTrying to parse as standard NNUE format:");

                    // Magic bytes
                    byte[] magic = br.ReadBytes(4);
                    string magicStr = Encoding.ASCII.GetString(magic);
                    Console.WriteLine($"Magic: {BitConverter.ToString(magic)} ('{magicStr}')");

                    if (magicStr == "NNUE")
                    {
                        // Version and architecture
                        uint version = br.ReadUInt32();
                        uint architecture = br.ReadUInt32();
                        Console.WriteLine($"Version: 0x{version:X8}");
                        Console.WriteLine($"Architecture: 0x{architecture:X8}");

                        // Description
                        int descLen = br.ReadInt32();
                        Console.WriteLine($"Description length: {descLen}");

                        if (descLen > 0 && descLen < 10000 && fs.Position + descLen <= fs.Length)
                        {
                            byte[] descBytes = br.ReadBytes(descLen);
                            string desc = Encoding.UTF8.GetString(descBytes);
                            Console.WriteLine($"Description: {desc}");
                        }

                        long dataStart = fs.Position;
                        long remainingBytes = fs.Length - dataStart;
                        Console.WriteLine($"\nData section starts at byte: {dataStart}");
                        Console.WriteLine($"Remaining bytes: {remainingBytes:N0}");

                        // Calculate expected sizes for different architectures
                        Console.WriteLine("\nExpected sizes for common architectures:");
                        CalculateExpectedSizes();
                    }
                    else
                    {
                        Console.WriteLine("Not a standard NNUE file (missing 'NNUE' magic bytes)");

                        // Try alternative interpretations
                        fs.Seek(0, SeekOrigin.Begin);
                        Console.WriteLine("\nChecking for alternative formats:");

                        // Check if it's a raw weights file
                        CheckRawWeights(fs);

                        // Check if it might be compressed
                        fs.Seek(0, SeekOrigin.Begin);
                        CheckCompression(br);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing file: {ex.Message}");
            }
        }

        private static void PrintHexDump(byte[] data)
        {
            for (int i = 0; i < data.Length; i += 16)
            {
                // Hex offset
                Console.Write($"{i:X8}  ");

                // Hex bytes
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < data.Length)
                        Console.Write($"{data[i + j]:X2} ");
                    else
                        Console.Write("   ");

                    if (j == 7) Console.Write(" ");
                }

                Console.Write(" |");

                // ASCII representation
                for (int j = 0; j < 16 && i + j < data.Length; j++)
                {
                    byte b = data[i + j];
                    if (b >= 32 && b < 127)
                        Console.Write((char)b);
                    else
                        Console.Write(".");
                }

                Console.WriteLine("|");
            }
        }

        private static void CalculateExpectedSizes()
        {
            // HalfKP -> 256 -> 32 -> 32 -> 1 architecture
            long size1 = CalculateNetworkSize(768, new int[] { 256, 32, 32, 1 });
            Console.WriteLine($"  HalfKP(768)->256->32->32->1: {size1:N0} bytes ({size1/1024.0/1024.0:F2} MB)");

            // HalfKP -> 512 -> 32 -> 32 -> 1 architecture
            long size2 = CalculateNetworkSize(768, new int[] { 512, 32, 32, 1 });
            Console.WriteLine($"  HalfKP(768)->512->32->32->1: {size2:N0} bytes ({size2/1024.0/1024.0:F2} MB)");

            // HalfKP -> 256 -> 32 -> 1 architecture (3 layers)
            long size3 = CalculateNetworkSize(768, new int[] { 256, 32, 1 });
            Console.WriteLine($"  HalfKP(768)->256->32->1: {size3:N0} bytes ({size3/1024.0/1024.0:F2} MB)");
        }

        private static long CalculateNetworkSize(int inputSize, int[] layers)
        {
            long size = 0;
            int prevSize = inputSize;

            foreach (int layerSize in layers)
            {
                size += prevSize * layerSize * 4; // weights
                size += layerSize * 4; // bias
                prevSize = layerSize;
            }

            return size;
        }

        private static void CheckRawWeights(FileStream fs)
        {
            long fileSize = fs.Length;

            // Check if file size matches common weight matrix sizes
            if (fileSize % 4 == 0)
            {
                long floatCount = fileSize / 4;
                Console.WriteLine($"File contains {floatCount:N0} float32 values");

                // Check common matrix dimensions
                CheckMatrixDimensions(floatCount);
            }

            if (fileSize % 2 == 0)
            {
                long halfCount = fileSize / 2;
                Console.WriteLine($"File contains {halfCount:N0} float16 values");
            }
        }

        private static void CheckMatrixDimensions(long elementCount)
        {
            // Common dimensions for chess NNUE
            int[] commonDims = { 768, 512, 256, 128, 64, 32, 16, 8, 1 };

            Console.WriteLine("Possible matrix dimensions:");
            foreach (int dim1 in commonDims)
            {
                foreach (int dim2 in commonDims)
                {
                    if (dim1 * dim2 == elementCount)
                    {
                        Console.WriteLine($"  {dim1} x {dim2}");
                    }
                }
            }
        }

        private static void CheckCompression(BinaryReader br)
        {
            byte[] header = br.ReadBytes(4);

            // Check for common compression signatures
            if (header[0] == 0x1F && header[1] == 0x8B)
            {
                Console.WriteLine("File appears to be gzip compressed");
            }
            else if (header[0] == 0x50 && header[1] == 0x4B)
            {
                Console.WriteLine("File appears to be zip compressed");
            }
            else if (header[0] == 0x42 && header[1] == 0x5A)
            {
                Console.WriteLine("File appears to be bzip2 compressed");
            }
        }
    }
}