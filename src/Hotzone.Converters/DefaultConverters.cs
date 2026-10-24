using Hotzone.Core;

namespace Hotzone.Converters;

public static class DefaultConverters
{
    public static ConverterRegistry CreateRegistry() =>
        new ConverterRegistry(Requirements.IsMet)
            .Register(new ImageConverter())
            .Register(new ImageToPdfConverter())
            .Register(new IcoConverter())
            .Register(new PdfRenderConverter())
            .Register(new PdfTextConverter())
            .Register(new MediaConverter())
            .Register(new OfficeComConverter())
            .Register(new LibreOfficeConverter());
}
