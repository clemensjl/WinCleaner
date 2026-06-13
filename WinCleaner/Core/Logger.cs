namespace WinCleaner.Core;

public class Logger
{
    public void Info(string msg)  => Write("INFO", msg);
    public void Error(string msg) => Write("ERROR", msg);
    public void Debug(string msg)
    {
#if DEBUG
        Write("DEBUG", msg);
#endif
    }

    private void Write(string level, string msg)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level}  {msg}");
    }
}
