using System.IO;
using System.Text;

namespace EternalGG.App.Services;

/** 공유 로그 파일의 증분 읽기 */
public sealed class LogMonitor : IDisposable
{
    /** 이터널리턴 기본 로그 경로 */
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "NimbleNeuron", "Eternal Return", "Player.log");
    private FileStream? stream;
    private StreamReader? reader;
    private DateTime created;
    private string partial = "";

    /** 공유 읽기 연결 및 기존 내용 건너뛰기 */
    public void Open(string path, bool skipExisting)
    {
        Dispose();
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        created = File.GetCreationTimeUtc(path);
        if (skipExisting) stream.Seek(0, SeekOrigin.End);
        reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
    }

    /** 파일 교체와 잘림 복구 후 새 로그 추출 */
    public IEnumerable<string> ReadNew(string path)
    {
        if (!File.Exists(path)) yield break;
        if (stream == null || File.GetCreationTimeUtc(path) != created || new FileInfo(path).Length < stream.Position) Open(path, false);

        /** 미완성 줄 보존 및 완성된 줄 반환 */
        var data = partial + reader!.ReadToEnd();
        var lines = data.Split('\n');
        partial = lines[^1];
        foreach (var line in lines[..^1]) yield return line.TrimEnd('\r');
    }

    /** 로그 연결과 잔여 버퍼 정리 */
    public void Dispose()
    {
        reader?.Dispose();
        stream?.Dispose();
        reader = null;
        stream = null;
        partial = "";
    }
}
