namespace Chess
{
    public class PositionHistory
    {
        private readonly List<ulong> _history = new List<ulong>();
        private readonly Board _rootPosition;

        public PositionHistory(Board rootPosition)
        {
            _rootPosition = rootPosition;
            _history.Add(Zobrist.ComputeHash(ref rootPosition));
        }

        public void AddPosition(Board board)
        {
            _history.Add(Zobrist.ComputeHash(ref board));
        }

        public void RemoveLastPosition()
        {
            if (_history.Count > 1)
                _history.RemoveAt(_history.Count - 1);
        }

        public bool IsRepetition(Board board)
        {
            ulong hash = Zobrist.ComputeHash(ref board);
            int count = 0;

            // Count from the end backwards, stopping at irreversible moves
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i] == hash)
                {
                    count++;
                    if (count >= 2)
                        return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            _history.Clear();
            _history.Add(Zobrist.ComputeHash(ref _rootPosition));
        }
    }
}
