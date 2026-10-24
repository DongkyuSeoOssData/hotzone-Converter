namespace Hotzone.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("hotzone_test_").FullName;

    public string File(string name, byte[] content)
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(p, content);
        return p;
    }

    public string Sub(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
