using Hotzone.Core;

namespace Hotzone.Converters;

public static class DefaultConverters
{
    public static ConverterRegistry CreateRegistry() =>
        new ConverterRegistry(Requirements.IsMet)
            .Register(new ImageConverter())
            .Register(new ImageToPdfConverter())
            .Register(new PdfRenderConverter())
            .Register(new OfficeComConverter())
            .Register(new LibreOfficeConverter());
}
