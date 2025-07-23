using System;
using System.IO;
using System.Text;

namespace GameGenerator
{
    public static class GameWriter
    {
        public static void WriteGame(string filePath, Game game)
        {
            var sb = new StringBuilder();

            // Write each position
            foreach (var position in game.Positions)
            {
                sb.AppendLine($"{position.MoveNumber} {position.ZobristHash} {position.StockfishEval} {position.Fen}");
            }

            // Write result
            if (game.Result.HasValue)
            {
                if (game.Result.Value == 1)
                    sb.AppendLine("result 1"); // White wins
                else if (game.Result.Value == 0)
                    sb.AppendLine("result 0"); // Black wins
                else
                    sb.AppendLine("result 0.5"); // Draw
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        public static Game ReadGame(string filePath)
        {
            var game = new Game();
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("result"))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 2 && double.TryParse(parts[1], out double result))
                    {
                        game.Result = result;
                    }
                }
                else
                {
                    var parts = line.Split(' ', 4);
                    if (parts.Length >= 4)
                    {
                        if (int.TryParse(parts[0], out int moveNumber) &&
                            ulong.TryParse(parts[1], out ulong zobristHash) &&
                            int.TryParse(parts[2], out int stockfishEval))
                        {
                            game.Positions.Add(new PositionInfo
                            {
                                MoveNumber = moveNumber,
                                ZobristHash = zobristHash,
                                StockfishEval = stockfishEval,
                                Fen = parts[3]
                            });
                        }
                    }
                }
            }

            return game;
        }
    }
}