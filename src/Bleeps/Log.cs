using System.Text;

namespace Bleeps;

/// <summary>One log file: Documents\Bleeps\debug\Bleeps.log, rotated at 512 KB (three old copies).</summary>
public static class Log
{
    private static readonly object _lock = new();

    public static string DataFolder
    {
        get
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) docs = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(docs, "Bleeps");
        }
    }

    public static string FilePath => Path.Combine(DataFolder, "debug", "Bleeps.log");

    public static void Info(string m) => Write("INFO ", m);
    public static void Warn(string m) => Write("WARN ", m);
    public static void Error(string m) => Write("ERROR", m);
    public static void Exception(string where, Exception ex) => Write("ERROR", where + ": " + ex);

    private static void Write(string level, string m)
    {
        try
        {
            lock (_lock)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        var from = i == 1 ? path : path + "." + (i - 1);
                        var to = path + "." + i;
                        if (File.Exists(from)) File.Move(from, to, overwrite: true);
                    }
                }
                File.AppendAllText(path, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {level} {m}{Environment.NewLine}", new UTF8Encoding(false));
            }
        }
        catch { }
    }
}
