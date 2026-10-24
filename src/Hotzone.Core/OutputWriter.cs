namespace Hotzone.Core;

/// <summary>출력 경로 결정, 이름 충돌 처리, 임시 파일 → 원자적 이동 (설계 4.3).</summary>
public static class OutputWriter
{
    /// <summary><c>이름.ext</c>가 있으면 <c>이름 (1).ext</c>, <c>이름 (2).ext</c> ... 폴더도 동일.</summary>
    public static string UniquePath(string path, bool isDirectory = false)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = isDirectory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var ext = isDirectory ? "" : Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>총 페이지 수에 맞춘 0 채움 자릿수 (10장 → 2, 100장 → 3). 최소 2.</summary>
    public static int PageNumberWidth(int count) => Math.Max(2, count.ToString().Length);

    /// <summary>
    /// 임시 폴더의 결과물을 원본 옆으로 옮기고 최종 경로(파일 또는 폴더)를 돌려준다.
    /// 1개면 원본 옆 단일 파일, 2개 이상이면 원본 이름 폴더에 <c>_01</c>, <c>_02</c> ...
    /// </summary>
    public static string Write(string sourcePath, string targetFormat, IReadOnlyList<string> files)
    {
        if (files.Count == 0) throw new InvalidOperationException("변환 결과가 없습니다");
        var parent = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Formats.FileExtension(targetFormat);

        if (files.Count == 1)
        {
            var dest = UniquePath(Path.Combine(parent, $"{stem}.{ext}"));
            File.Move(files[0], dest);
            return dest;
        }

        // 임시 폴더에 전부 만든 뒤 폴더째 이동 (일부만 남는 상태 방지)
        var width = PageNumberWidth(files.Count);
        var staging = Path.Combine(Path.GetDirectoryName(files[0])!, $"__pages_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        for (var i = 0; i < files.Count; i++)
        {
            var number = (i + 1).ToString().PadLeft(width, '0');
            File.Move(files[i], Path.Combine(staging, $"{stem}_{number}.{ext}"));
        }
        var destDir = UniquePath(Path.Combine(parent, stem), isDirectory: true);
        MoveDirectory(staging, destDir);
        return destDir;
    }

    public static void MoveDirectory(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
        }
        catch (IOException) when (Path.GetPathRoot(from) != Path.GetPathRoot(to))
        {
            // %TEMP%와 원본이 다른 드라이브면 Directory.Move가 실패한다: 옆에 복사한 뒤 이름만 바꾼다
            var partial = to + ".part";
            Directory.CreateDirectory(partial);
            foreach (var f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(partial, Path.GetFileName(f)));
            Directory.Move(partial, to);
            Directory.Delete(from, recursive: true);
        }
    }
}
