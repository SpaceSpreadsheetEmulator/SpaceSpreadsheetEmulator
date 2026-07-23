using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.CaptureInspector.Controls;

public sealed class HexViewer : TemplatedControl
{
    private const int BytesPerLine = 16;
    private const double HorizontalPadding = 6;
    private const double VerticalPadding = 4;

    public static readonly StyledProperty<IReadOnlyList<byte>> BytesProperty =
        AvaloniaProperty.Register<HexViewer, IReadOnlyList<byte>>(
            nameof(Bytes),
            Array.Empty<byte>());

    public static readonly StyledProperty<IReadOnlyList<WireByteRange>> SelectedRangesProperty =
        AvaloniaProperty.Register<HexViewer, IReadOnlyList<WireByteRange>>(
            nameof(SelectedRanges),
            Array.Empty<WireByteRange>());

    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        AvaloniaProperty.Register<HexViewer, IBrush>(
            nameof(SelectionBrush),
            new SolidColorBrush(Color.FromRgb(43, 133, 177)));

    static HexViewer()
    {
        AffectsMeasure<HexViewer>(BytesProperty, FontFamilyProperty, FontSizeProperty);
        AffectsRender<HexViewer>(
            BytesProperty,
            SelectedRangesProperty,
            SelectionBrushProperty,
            ForegroundProperty,
            FontFamilyProperty,
            FontSizeProperty);
    }

    public IReadOnlyList<byte> Bytes
    {
        get => GetValue(BytesProperty);
        set => SetValue(BytesProperty, value);
    }

    public IReadOnlyList<WireByteRange> SelectedRanges
    {
        get => GetValue(SelectedRangesProperty);
        set => SetValue(SelectedRangesProperty, value);
    }

    public IBrush SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Metrics metrics = GetMetrics();
        int lineCount = Math.Max(1, (Bytes.Count + BytesPerLine - 1) / BytesPerLine);
        return new Size(
            HorizontalPadding * 2 + metrics.CharacterWidth * 77,
            VerticalPadding * 2 + metrics.LineHeight * lineCount);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        IReadOnlyList<byte> bytes = Bytes;
        if (bytes.Count == 0)
        {
            DrawText(context, "No bytes are available for this frame.", new Point(HorizontalPadding, VerticalPadding));
            return;
        }

        Metrics metrics = GetMetrics();
        double hexStart = HorizontalPadding + metrics.CharacterWidth * 10;
        double asciiStart = hexStart + metrics.CharacterWidth * 50;
        var selected = BuildSelection(bytes.Count);

        for (var lineIndex = 0; lineIndex * BytesPerLine < bytes.Count; lineIndex++)
        {
            int offset = lineIndex * BytesPerLine;
            int count = Math.Min(BytesPerLine, bytes.Count - offset);
            double y = VerticalPadding + lineIndex * metrics.LineHeight;

            for (var index = 0; index < count; index++)
            {
                if (!selected[offset + index])
                {
                    continue;
                }

                context.FillRectangle(
                    SelectionBrush,
                    new Rect(
                        hexStart + index * metrics.CharacterWidth * 3 - 1,
                        y,
                        metrics.CharacterWidth * 2 + 2,
                        metrics.LineHeight));
                context.FillRectangle(
                    SelectionBrush,
                    new Rect(
                        asciiStart + index * metrics.CharacterWidth - 1,
                        y,
                        metrics.CharacterWidth + 2,
                        metrics.LineHeight));
            }

            DrawText(context, offset.ToString("X8", CultureInfo.InvariantCulture), new Point(HorizontalPadding, y));
            string hex = string.Join(
                ' ',
                Enumerable.Range(0, count)
                    .Select(index => bytes[offset + index].ToString("X2", CultureInfo.InvariantCulture)));
            DrawText(context, hex, new Point(hexStart, y));
            string ascii = new(
                Enumerable.Range(0, count)
                    .Select(index =>
                    {
                        byte value = bytes[offset + index];
                        return value is >= 32 and <= 126 ? (char)value : '.';
                    })
                    .ToArray());
            DrawText(context, ascii, new Point(asciiStart, y));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SelectedRangesProperty || SelectedRanges.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Metrics metrics = GetMetrics();
            long offset = Math.Max(0, SelectedRanges[0].Offset);
            double y = VerticalPadding + (offset / BytesPerLine) * metrics.LineHeight;
            this.BringIntoView(new Rect(0, y, Bounds.Width, metrics.LineHeight));
        });
    }

    private bool[] BuildSelection(int byteCount)
    {
        var selected = new bool[byteCount];
        foreach (WireByteRange range in SelectedRanges)
        {
            long start = Math.Max(0, range.Offset);
            long end = Math.Min(byteCount, range.End);
            for (long offset = start; offset < end; offset++)
            {
                selected[offset] = true;
            }
        }

        return selected;
    }

    private void DrawText(DrawingContext context, string value, Point origin)
    {
        var text = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            FontSize,
            Foreground ?? Brushes.White);
        context.DrawText(text, origin);
    }

    private Metrics GetMetrics()
    {
        var sample = new FormattedText(
            "0",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            FontSize,
            Foreground ?? Brushes.White);
        return new Metrics(sample.Width, Math.Ceiling(sample.Height + 2));
    }

    private readonly record struct Metrics(double CharacterWidth, double LineHeight);
}
