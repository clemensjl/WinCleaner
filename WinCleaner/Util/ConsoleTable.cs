namespace WinCleaner.Util;

public static class ConsoleTable
{
    public static ConsoleTableBuilder From(IEnumerable<IEnumerable<string>> rows, params string[] headers)
        => new ConsoleTableBuilder(headers, rows);

    public class ConsoleTableBuilder
    {
        private readonly List<string[]> _rows = new();
        private readonly string[] _headers;

        public ConsoleTableBuilder(string[] headers, IEnumerable<IEnumerable<string>> rows)
        {
            _headers = headers;
            foreach (var r in rows) _rows.Add(r.ToArray());
        }

        public void Write()
        {
            var all = new List<string[]> { _headers }.Concat(_rows).ToList();
            int[] widths = new int[_headers.Length];
            for (int c = 0; c < _headers.Length; c++)
                widths[c] = all.Max(r => r[c].Length) + 2;

            void printRow(string[] r)
            {
                for (int c = 0; c < r.Length; c++)
                {
                    var cell = r[c].PadRight(widths[c]);
                    Console.Write(cell);
                }
                Console.WriteLine();
            }

            printRow(_headers);
            Console.WriteLine(new string('-', widths.Sum()));
            foreach (var r in _rows) printRow(r);
        }
    }
}
