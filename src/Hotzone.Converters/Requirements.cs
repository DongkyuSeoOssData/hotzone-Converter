namespace Hotzone.Converters;

/// <summary><see cref="Core.ConvertsAttribute.Requires"/> 이름 → 설치 여부.</summary>
public static class Requirements
{
    public const string Office = "Office";
    public const string LibreOffice = "LibreOffice";
    public const string FFmpeg = "FFmpeg";

    public static bool IsMet(string name) => name switch
    {
        Office => OperatingSystem.IsWindows()
            && Type.GetTypeFromProgID("Word.Application") is not null
            && Type.GetTypeFromProgID("PowerPoint.Application") is not null
            && Type.GetTypeFromProgID("Excel.Application") is not null,
        LibreOffice => LibreOfficeConverter.FindSoffice() is not null,
        FFmpeg => MediaConverter.FindFfmpeg() is not null,
        _ => false,
    };
}
