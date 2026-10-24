using Hotzone.Core;

namespace Hotzone.Tests;

public class RegistryTests
{
    private abstract class Fake : IConverter
    {
        public string Id => GetType().Name;
        public Task<ConversionResult> ConvertAsync(ConversionRequest r, IProgress<double>? p, CancellationToken ct) =>
            Task.FromResult(new ConversionResult([]));
    }

    [Converts("jpeg, png, heic -> jpeg, png, pdf", Cost = 1)] private sealed class Image : Fake;
    [Converts("pdf -> jpeg, png", Cost = 2)] private sealed class Pdf : Fake;
    [Converts("pptx, xlsx -> pdf", Cost = 2, Requires = "Office")]
    [Converts("pptx -> jpeg, png", Cost = 2, Requires = "Office")] private sealed class Office : Fake;
    [Converts("pptx, xlsx -> pdf", Cost = 4, Requires = "LibreOffice")] private sealed class Libre : Fake;
    [Converts("x -> y")] private sealed class XY : Fake;
    [Converts("y -> z")] private sealed class YZ : Fake;
    [Converts("z -> w")] private sealed class ZW : Fake;

    private static ConverterRegistry All(Func<string, bool>? req = null) =>
        new ConverterRegistry(req).Register(new Image()).Register(new Pdf()).Register(new Libre()).Register(new Office());

    [Fact]
    public void Cheaper_converter_wins()
    {
        var path = All().FindPath("pptx", "pdf")!;
        Assert.IsType<Office>(Assert.Single(path).Converter);
    }

    [Fact]
    public void Unmet_requirement_hides_edges()
    {
        var reg = All(r => r != "Office" && r != "LibreOffice");
        Assert.Empty(reg.TargetsFor("pptx"));
        Assert.IsType<Libre>(All(r => r == "LibreOffice").FindPath("xlsx", "pdf")![0].Converter);
    }

    [Fact]
    public void Two_step_chain()
    {
        var path = All().FindPath("xlsx", "png")!;
        Assert.Equal([("xlsx", "pdf"), ("pdf", "png")], path.Select(e => (e.From, e.To)));
    }

    [Fact]
    public void Direct_edge_beats_chain()
    {
        var path = All().FindPath("pptx", "jpeg")!;
        Assert.IsType<Office>(Assert.Single(path).Converter);
    }

    [Fact]
    public void No_three_step_chain()
    {
        var reg = new ConverterRegistry().Register(new XY()).Register(new YZ()).Register(new ZW());
        Assert.NotNull(reg.FindPath("x", "z"));
        Assert.Null(reg.FindPath("x", "w"));
    }

    [Fact]
    public void Common_targets_is_intersection_without_self()
    {
        var reg = All();
        Assert.Equal(["png"], reg.CommonTargets(["jpeg", "pdf"]));
        Assert.DoesNotContain("jpeg", reg.CommonTargets(["jpeg", "jpeg"]));
    }
}
