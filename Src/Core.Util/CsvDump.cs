using System.Collections.Concurrent;
using System.IO;

namespace DcsAutopilot;

public static class CsvDump
{
    public static ConcurrentQueue<string> Lines = new();

    public static void AddLine(string line)
    {
        Lines.Enqueue(line);
        if (Lines.Count < 100)
            return;
        Task.Run(() =>
        {
            var lines = new List<string>();
            lock (Lines) // only from other instances of this same task to avoid interleaved dequeue or file conflicts
            {
                while (Lines.TryDequeue(out var s))
                    lines.Add(s);
                File.AppendAllLines("dump.csv", lines);
            }
        });
    }
}
