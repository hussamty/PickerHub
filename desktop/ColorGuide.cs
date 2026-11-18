using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using System.Diagnostics;
using System.Globalization;

namespace PickerHub;

// Boundary labeling for the image guide: one label per marked color in a column beside the frame,
// and a leader from each mark out to its label.
//
// The marks are split at their median x: the left half is labeled on the left and the right half on the
// right, so a leader never crosses to the other side and no left leader can meet a right one. Each column
// has one evenly spaced slot per label. Marks are matched to slots by minimum total leader length (the
// Hungarian method); a matching of least total length has no two straight leaders crossing, because
// swapping the ends of any crossing pair would shorten it. A leader that would graze another color's
// mark is penalised, and a final pass swaps any pair that then crosses, until none does. Each leader runs
// straight from its mark to an elbow just outside the frame, then level to its label, so the short
// outer tails, which share one x, never meet either.
internal static class Callouts
{
    // Via, when set, is a bend that takes the leader round another color's mark it would otherwise graze.
    internal readonly record struct Placement(int Index, Rect Label, Point Mark, Point Elbow, Point Tip, bool Left, Point? Via = null);
    internal static Point[] Path(Placement p) => p.Via is { } via ? [p.Mark, via, p.Elbow, p.Tip] : [p.Mark, p.Elbow, p.Tip];
    // Whether two leaders cross anywhere along their paths.
    internal static bool Collide(Placement a, Placement b)
    {
        var pa = Path(a); var pb = Path(b);
        for (int i = 0; i + 1 < pa.Length; i++)
            for (int j = 0; j + 1 < pb.Length; j++)
                if (Cross(pa[i], pa[i + 1], pb[j], pb[j + 1])) return true;
        return false;
    }
    // How close a leader passes to a mark other than its own.
    internal static double Clearance(Placement p, Point mark)
    {
        var path = Path(p); double least = double.PositiveInfinity;
        for (int i = 0; i + 1 < path.Length; i++) least = Math.Min(least, SegmentDistance(mark, path[i], path[i + 1]));
        return least;
    }
    public const double Gutter = 36;

    public static Placement[] Layout(IReadOnlyList<Point> marks, Rect frame, Size label, bool twoSided, bool oneSideRight, double spacing, double clearance)
    {
        int n = marks.Count;
        var placements = new Placement[n];
        if (n == 0) return placements;
        var byX = Enumerable.Range(0, n).OrderBy(i => marks[i].X).ThenBy(i => i).ToArray();
        int left = twoSided ? n / 2 : oneSideRight ? 0 : n;
        Side(byX[..left], true);
        Side(byX[left..], false);
        return placements;

        void Side(int[] ids, bool isLeft)
        {
            int k = ids.Length;
            if (k == 0) return;
            // Evenly spread over the frame's height when there is room; packed and centred on it when not.
            double pitch = Math.Max(label.Height + spacing, frame.Height / k), top = frame.Center.Y - pitch * k / 2;
            double elbowX = isLeft ? frame.Left - Gutter * .5 : frame.Right + Gutter * .5, tipX = isLeft ? frame.Left - Gutter : frame.Right + Gutter;
            var slots = Enumerable.Range(0, k).Select(s => new Point(elbowX, top + pitch * (s + .5))).ToArray();
            var cost = new double[k, k];
            for (int a = 0; a < k; a++)
                for (int s = 0; s < k; s++)
                    cost[a, s] = Distance(marks[ids[a]], slots[s]) + (Grazes(marks[ids[a]], slots[s], ids[a]) ? 10_000 : 0);
            var assign = Hungarian(cost);
            for (int pass = 0, swaps = 1; pass < 64 && swaps > 0; pass++)
            {
                swaps = 0;
                for (int i = 0; i < k; i++)
                    for (int j = i + 1; j < k; j++)
                        if (Cross(marks[ids[i]], slots[assign[i]], marks[ids[j]], slots[assign[j]])) { (assign[i], assign[j]) = (assign[j], assign[i]); swaps++; }
            }
            for (int a = 0; a < k; a++)
            {
                var slot = slots[assign[a]];
                var rect = new Rect(isLeft ? tipX - label.Width : tipX, slot.Y - label.Height / 2, label.Width, label.Height);
                placements[ids[a]] = new Placement(ids[a], rect, marks[ids[a]], slot, new Point(tipX, slot.Y), isLeft);
            }
            // Where straight leaders cannot keep clear of every mark (marks in a line, say), a leader bends once to
            // pass the mark it grazes at a safe distance, on the side it was already passing. The bend is kept only
            // if the new path crosses no other leader and grazes no other mark.
            foreach (int id in ids)
            {
                var p = placements[id];
                int grazed = -1; double worst = clearance;
                for (int j = 0; j < n; j++)
                {
                    if (j == id) continue;
                    double d = SegmentDistance(marks[j], p.Mark, p.Elbow);
                    if (d < worst) { worst = d; grazed = j; }
                }
                if (grazed < 0) continue;
                var obstacle = marks[grazed];
                var foot = Foot(obstacle, p.Mark, p.Elbow);
                double ax = foot.X - obstacle.X, ay = foot.Y - obstacle.Y, length = Math.Sqrt(ax * ax + ay * ay);
                if (length < .01) { ax = 0; ay = p.Elbow.Y < p.Mark.Y ? -1 : 1; } else { ax /= length; ay /= length; }
                var bent = p with { Via = new Point(obstacle.X + ax * (clearance + 2), obstacle.Y + ay * (clearance + 2)) };
                bool clean = ids.All(other => other == id || !Collide(bent, placements[other]))
                    && Enumerable.Range(0, n).All(j => j == id || Clearance(bent, marks[j]) >= clearance - .5);
                if (clean) placements[id] = bent;
            }
        }
        static Point Foot(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
            double t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
            return new Point(a.X + t * dx, a.Y + t * dy);
        }
        bool Grazes(Point from, Point to, int self)
        {
            for (int i = 0; i < n; i++) if (i != self && SegmentDistance(marks[i], from, to) < clearance) return true;
            return false;
        }
    }
    static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    internal static double SegmentDistance(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
        double t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }
    // Whether two segments cross at a point inside both (touching ends do not count).
    internal static bool Cross(Point a, Point b, Point c, Point d)
    {
        static double Turn(Point p, Point q, Point r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double d1 = Turn(c, d, a), d2 = Turn(c, d, b), d3 = Turn(a, b, c), d4 = Turn(a, b, d);
        return (d1 > 0 && d2 < 0 || d1 < 0 && d2 > 0) && (d3 > 0 && d4 < 0 || d3 < 0 && d4 > 0);
    }
    // Minimum-cost assignment of rows to columns of a square matrix; returns the column for each row.
    static int[] Hungarian(double[,] a)
    {
        int n = a.GetLength(0);
        var u = new double[n + 1]; var v = new double[n + 1]; var p = new int[n + 1]; var way = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i; int j0 = 0;
            var minv = Enumerable.Repeat(double.PositiveInfinity, n + 1).ToArray(); var used = new bool[n + 1];
            do
            {
                used[j0] = true; int i0 = p[j0], j1 = 0; double delta = double.PositiveInfinity;
                for (int j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    double cur = a[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (int j = 0; j <= n; j++) { if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else minv[j] -= delta; }
                j0 = j1;
            } while (p[j0] != 0);
            do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
        }
        var assign = new int[n];
        for (int j = 1; j <= n; j++) assign[p[j] - 1] = j - 1;
        return assign;
    }
}

// The image guide: the picture in a frame of frosted glass, a glass mark on each of its colors, and a
// leader from every mark out to a glass label naming the color, its code and its share. The leaders draw
// outward one after another, largest color first, and each label settles in as its leader reaches it.
// Pointing at a label lights its leader and mark; clicking it opens that color's actions. The picture
// inside stays the image sampler, so any pixel can still be picked by hand.
internal sealed class ColorGuide : Panel
{
    internal sealed record Entry(string Hex, string Name, double Percent, Point At);
    // Compact view: a picture with more colors than this shows only its most dominant ones at first, so it
    // is not buried under leaders; "Show all" brings back the rest.
    internal const int CompactCount = 5;
    // The colors to show, most dominant (largest share) first, and only the first five in compact view.
    // Equal shares keep the engine's order, so the choice never shuffles.
    internal static Entry[] Visible(IReadOnlyList<Entry> all, bool showAll) =>
        all.Select((entry, index) => (entry, index)).OrderByDescending(x => x.entry.Percent).ThenBy(x => x.index)
            .Select(x => x.entry).Take(showAll ? all.Count : CompactCount).ToArray();
    const double Pad = 12, Disc = 11, MaxImageHeight = 460;
    readonly Glass frame;
    readonly Leaders leaders;
    readonly List<Glass> labels = [];
    readonly List<TextBlock> names = [];
    readonly Size bitmap;
    readonly Action<string> open;
    IReadOnlyList<Entry> entries = [];
    readonly bool motion;
    // animate: draw out at the next layout; pending: drawn out soon, so nothing shows at rest until then.
    bool animate, pending, running;
    readonly Stopwatch clock = new();
    Size frameSize, labelSize;
    bool twoSided;
    internal Callouts.Placement[] Placements { get; private set; } = [];
    internal Rect FrameRect { get; private set; }
    internal int Hot { get; private set; } = -1;
    internal IReadOnlyList<Entry> Entries => entries;
    internal IReadOnlyList<Glass> Labels => labels;

    public ColorGuide(Control picture, Size bitmap, IReadOnlyList<Entry> entries, bool motion, bool animate, Action<string> open)
    {
        this.bitmap = bitmap; this.motion = motion; this.open = open;
        FlowDirection = FlowDirection.LeftToRight;
        picture.MaxHeight = double.PositiveInfinity;
        frame = new Glass("CardTint") { Radius = 22, Blur = 18, Padding = new Thickness(Pad), Child = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = picture } };
        leaders = new Leaders(this) { IsHitTestVisible = false };
        Children.Add(frame); Children.Add(leaders);
        Show(entries, animate);
    }

    // Shows these colors in place of the current ones: the picture stays, the labels are rebuilt and laid
    // out afresh, and with motion the leaders draw out again, largest color first.
    internal void Show(IReadOnlyList<Entry> next, bool animate)
    {
        foreach (var label in labels) Children.Remove(label);
        labels.Clear(); names.Clear();
        entries = next; Hot = -1; running = false; Placements = [];
        for (int i = 0; i < entries.Count; i++) { var label = Label(entries[i], i); labels.Add(label); Children.Add(label); }
        this.animate = pending = animate && motion;
        if (this.animate) foreach (var label in labels) label.Opacity = 0;
        InvalidateMeasure(); leaders.InvalidateVisual();
    }

    Glass Label(Entry e, int index)
    {
        string share = e.Percent.ToString(e.Percent < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + "%";
        var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.Parse(e.Hex)), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        swatch[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        var name = new TextBlock { Text = e.Name, FontSize = Typography.Label, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        // A code keeps its own left-to-right order, and sits at the reading start: the label row runs in the
        // language's direction, where Left is the start of the line in either language.
        var code = new TextBlock { Text = e.Hex + "  " + share, FontFamily = Typography.Code, FontSize = Typography.Caption, FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Left };
        code[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Muted");
        names.Add(name);
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        text.Children.Add(name); text.Children.Add(code);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), FlowDirection = L.Flow };
        row.Children.Add(swatch); Grid.SetColumn(text, 1); row.Children.Add(text);
        var pane = new Glass("SheetTint") { Radius = 14, Blur = 16, Padding = new Thickness(10, 6), Child = row, Focusable = true, Cursor = new Cursor(StandardCursorType.Hand) };
        AutomationProperties.SetName(pane, $"{e.Name} {e.Hex} {share}");
        ToolTip.SetTip(pane, L.T("إجراءات اللون"));
        pane.PointerEntered += (_, _) => Light(index);
        pane.PointerExited += (_, _) => { if (Hot == index && !pane.IsFocused) Light(-1); };
        pane.GotFocus += (_, _) => Light(index);
        pane.LostFocus += (_, _) => { if (Hot == index) Light(-1); };
        pane.PointerPressed += (_, ev) => { if (ev.GetCurrentPoint(pane).Properties.IsLeftButtonPressed) { ev.Handled = true; pane.Focus(); open(e.Hex); } };
        pane.KeyDown += (_, ev) => { if (ev.Key is Key.Enter or Key.Space) { ev.Handled = true; open(e.Hex); } };
        return pane;
    }
    void Light(int index) { if (Hot == index) return; Hot = index; leaders.InvalidateVisual(); }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 900 : available.Width;
        int n = entries.Count;
        twoSided = width >= 780 && n > 1;
        bool compact = width < 560;
        // Narrow windows keep the swatch and the code; the name is in the label's accessible name and tooltip.
        labelSize = compact ? new Size(146, 40) : new Size(196, 48);
        foreach (var name in names) name.IsVisible = !compact;
        double reserve = labelSize.Width + Callouts.Gutter + 4;
        double roomW = Math.Max(80, width - (twoSided ? 2 : 1) * reserve - 2 * Pad);
        double scale = Math.Min(roomW / bitmap.Width, MaxImageHeight / bitmap.Height);
        frameSize = new Size(bitmap.Width * scale + 2 * Pad, bitmap.Height * scale + 2 * Pad);
        int perSide = twoSided ? n - n / 2 : n;
        double column = perSide * (labelSize.Height + 8);
        double height = Math.Max(frameSize.Height, column) + 12;
        frame.Measure(frameSize);
        foreach (var label in labels) label.Measure(labelSize);
        leaders.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        // One column follows the picture in reading order: on its right in English, on its left in Arabic.
        bool labelsRight = L.Language != "ar";
        double reserve = labelSize.Width + Callouts.Gutter + 4;
        double x = twoSided ? (size.Width - frameSize.Width) / 2
            : Math.Max(0, (size.Width - frameSize.Width - reserve) / 2) + (labelsRight ? 0 : reserve);
        FrameRect = new Rect(x, (size.Height - frameSize.Height) / 2, frameSize.Width, frameSize.Height);
        frame.Arrange(FrameRect);
        var inner = FrameRect.Deflate(Pad);
        var marks = entries.Select(e => new Point(inner.X + e.At.X * inner.Width, inner.Y + e.At.Y * inner.Height)).ToList();
        Placements = Callouts.Layout(marks, FrameRect, labelSize, twoSided, labelsRight, 8, Disc + 5);
        for (int i = 0; i < labels.Count; i++) labels[i].Arrange(Placements[i].Label);
        leaders.Arrange(new Rect(size));
        if (animate) { animate = false; Avalonia.Threading.Dispatcher.UIThread.Post(Start, Avalonia.Threading.DispatcherPriority.Render); }
        return size;
    }

    // ---- Motion: marks pop in, leaders draw outward, labels settle as their leaders arrive. ----
    const double PopFor = .26, DrawFor = .58, Stagger = .075;
    static double PopAt(int i) => i * Stagger;
    static double DrawAt(int i) => .14 + i * Stagger;
    double Elapsed => running ? clock.Elapsed.TotalSeconds : pending ? 0 : double.PositiveInfinity;
    internal double MarkScale(int i)
    {
        double t = Math.Clamp((Elapsed - PopAt(i)) / PopFor, 0, 1);
        if (t >= 1) return 1;
        const double back = 1.7;                                   // a small overshoot, then rest
        double u = t - 1; return 1 + u * u * ((back + 1) * u + back);
    }
    internal double LeaderProgress(int i)
    {
        double t = Math.Clamp((Elapsed - DrawAt(i)) / DrawFor, 0, 1);
        return 1 - Math.Pow(1 - t, 3);                              // eases out as it reaches the label
    }
    internal bool Settled => !running && !pending;
    void Start()
    {
        pending = false;
        if (VisualRoot == null) { foreach (var label in labels) label.Opacity = 1; return; }
        running = true; clock.Restart();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Frame());
    }
    void Frame()
    {
        if (!running) return;
        double t = clock.Elapsed.TotalSeconds;
        for (int i = 0; i < labels.Count && i < Placements.Length; i++)
        {
            double r = Math.Clamp((t - DrawAt(i) - DrawFor * .7) / .24, 0, 1), eased = 1 - Math.Pow(1 - r, 3);
            labels[i].Opacity = eased;
            double from = Placements[i].Left ? 10 : -10;           // each label slides in from the frame's side
            labels[i].RenderTransform = r >= 1 ? null : TransformOperations.Parse(FormattableString.Invariant($"translateX({from * (1 - eased):0.##}px)"));
        }
        leaders.InvalidateVisual();
        if (t > DrawAt(Math.Max(0, labels.Count - 1)) + DrawFor + .3 || VisualRoot == null)
        {
            running = false;
            foreach (var label in labels) { label.Opacity = 1; label.RenderTransform = null; }
            leaders.InvalidateVisual();
            return;
        }
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => Frame());
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (running || pending) { running = pending = false; foreach (var label in labels) { label.Opacity = 1; label.RenderTransform = null; } }
    }

    // The marks and leaders, drawn over the picture and the page in one layer.
    sealed class Leaders(ColorGuide guide) : Control
    {
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            bool dark = ActualThemeVariant == ThemeVariant.Dark;
            // Ink of the theme with a halo of its opposite: a leader reads on any picture and on the page.
            var ink = dark ? Color.FromRgb(255, 255, 255) : Color.FromRgb(17, 17, 17);
            var halo = dark ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255);
            var signal = Color.FromRgb(255, 240, 0);
            var placements = guide.Placements;
            for (int i = 0; i < placements.Length; i++)
            {
                double progress = guide.LeaderProgress(i);
                if (progress <= 0) continue;
                bool hot = guide.Hot == i;
                Leader(context, placements[i], progress, hot ? signal : ink, hot ? Color.FromArgb(190, 17, 17, 17) : halo, hot ? 2.4 : 1.6);
            }
            for (int i = 0; i < placements.Length; i++)
            {
                double scale = guide.MarkScale(i);
                if (scale <= 0) continue;
                Mark(context, placements[i].Mark, guide.Entries[i].Hex, scale, dark, guide.Hot == i);
            }
        }
        static void Leader(DrawingContext context, Callouts.Placement p, double progress, Color ink, Color halo, double width)
        {
            var path = Callouts.Path(p);
            var dir = path[1] - path[0]; double reach = Length(dir);
            if (reach < 1) return;
            // Leave the mark's glass disc clear: the leader starts at its rim, then grows outward along its path.
            path[0] += dir / reach * Math.Min(reach * .5, Disc + 1.5);
            double total = 0; for (int i = 0; i + 1 < path.Length; i++) total += Length(path[i + 1] - path[i]);
            double along = progress * total;
            var geometry = new StreamGeometry();
            using (var draw = geometry.Open())
            {
                draw.BeginFigure(path[0], false);
                for (int i = 0; i + 1 < path.Length && along > 0; i++)
                {
                    double piece = Length(path[i + 1] - path[i]);
                    draw.LineTo(along >= piece ? path[i + 1] : path[i] + (path[i + 1] - path[i]) * (along / Math.Max(.001, piece)));
                    along -= piece;
                }
                draw.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(halo), width + 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(ink), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            if (progress < .999) return;
            // The arrowhead points into the label.
            double sign = p.Left ? -1 : 1, size = 6 + width;
            var head = new StreamGeometry();
            using (var draw = head.Open())
            {
                draw.BeginFigure(p.Tip, true);
                draw.LineTo(new Point(p.Tip.X - sign * size, p.Tip.Y - size * .62));
                draw.LineTo(new Point(p.Tip.X - sign * size, p.Tip.Y + size * .62));
                draw.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(ink), new Pen(new SolidColorBrush(halo), 1.5, lineJoin: PenLineJoin.Round), head);
        }
        static double Length(Vector v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
        // A disc of glass with the color's pixel at its centre, framed so it shows on any color.
        static void Mark(DrawingContext context, Point at, string hex, double scale, bool dark, bool hot)
        {
            double r = Disc * scale;
            context.DrawEllipse(new RadialGradientBrush
            {
                GradientStops = [new(Color.FromArgb(90, 0, 0, 0), 0), new(Color.FromArgb(0, 0, 0, 0), 1)],
                Center = new RelativePoint(.5, .5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative)
            }, null, at + new Vector(0, 2 * scale), r + 6 * scale, r + 6 * scale);
            var rim = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new(Color.FromArgb(230, 255, 255, 255), 0), new(Color.FromArgb(60, 255, 255, 255), 1)]
            };
            context.DrawEllipse(new SolidColorBrush(dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(140, 255, 255, 255)), new Pen(rim, 1.2), at, r, r);
            if (hot) context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(255, 240, 0)), 2.2), at, r + 3, r + 3);
            double half = 4.5 * scale;
            var pixel = new Rect(at.X - half, at.Y - half, half * 2, half * 2);
            context.DrawRectangle(new SolidColorBrush(Color.Parse(hex)), new Pen(new SolidColorBrush(Color.FromRgb(255, 255, 255)), 1.6), pixel);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(220, 17, 17, 17)), .9), pixel.Inflate(1.3));
        }
    }
}
