using System.IO;
using System.Text;

namespace EternalGG.App.Services;

public sealed class LogMonitor : IDisposable
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "NimbleNeuron", "Eternal Return", "Player.log");
    private FileStream? stream;
    private StreamReader? reader;
    private DateTime created;
    private string partial = "";

    public void Open(string path, bool skipExisting)
    {
        Dispose();
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        created = File.GetCreationTimeUtc(path);
        if (skipExisting) stream.Seek(0, SeekOrigin.End);
        reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
    }

    public IEnumerable<string> ReadNew(string path)
    {
        if (!File.Exists(path)) yield break;
        if (stream == null || File.GetCreationTimeUtc(path) != created || new FileInfo(path).Length < stream.Position) Open(path, false);
        var data = partial + reader!.ReadToEnd();
        var lines = data.Split('\n');
        partial = lines[^1];
        foreach (var line in lines[..^1]) yield return line.TrimEnd('\r');
    }

    public void Dispose()
    {
        reader?.Dispose();
        stream?.Dispose();
        reader = null;
        stream = null;
        partial = "";
    }
}
