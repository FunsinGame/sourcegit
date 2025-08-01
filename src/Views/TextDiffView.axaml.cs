using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using AvaloniaEdit.Utils;

namespace SourceGit.Views
{
    public class TextDiffViewChunk
    {
        public double Y { get; set; } = 0.0;
        public double Height { get; set; } = 0.0;
        public int StartIdx { get; set; } = 0;
        public int EndIdx { get; set; } = 0;
        public bool Combined { get; set; } = true;
        public bool IsOldSide { get; set; } = false;

        public bool ShouldReplace(TextDiffViewChunk old)
        {
            if (old == null)
                return true;

            return Math.Abs(Y - old.Y) > 0.001 ||
                Math.Abs(Height - old.Height) > 0.001 ||
                StartIdx != old.StartIdx ||
                EndIdx != old.EndIdx ||
                Combined != old.Combined ||
                IsOldSide != old.IsOldSide;
        }
    }

    public record TextDiffViewRange
    {
        public int StartIdx { get; set; } = 0;
        public int EndIdx { get; set; } = 0;

        public TextDiffViewRange(int startIdx, int endIdx)
        {
            StartIdx = startIdx;
            EndIdx = endIdx;
        }
    }

    public class ThemedTextDiffPresenter : TextEditor
    {
        public class VerticalSeparatorMargin : AbstractMargin
        {
            public override void Render(DrawingContext context)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter != null)
                {
                    var pen = new Pen(presenter.LineBrush);
                    context.DrawLine(pen, new Point(0, 0), new Point(0, Bounds.Height));
                }
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(1, 0);
            }
        }

        public class LineNumberMargin : AbstractMargin
        {
            public LineNumberMargin(bool usePresenter, bool isOld)
            {
                _usePresenter = usePresenter;
                _isOld = isOld;

                Margin = new Thickness(8, 0);
                ClipToBounds = true;
            }

            public override void Render(DrawingContext context)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter == null)
                    return;

                var isOld = _isOld;
                if (_usePresenter)
                    isOld = presenter.IsOld;

                var lines = presenter.GetLines();
                var view = TextView;
                if (view is { VisualLinesValid: true })
                {
                    var typeface = view.CreateTypeface();
                    foreach (var line in view.VisualLines)
                    {
                        if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                            continue;

                        var index = line.FirstDocumentLine.LineNumber;
                        if (index > lines.Count)
                            break;

                        var info = lines[index - 1];
                        var lineNumber = isOld ? info.OldLine : info.NewLine;
                        if (string.IsNullOrEmpty(lineNumber))
                            continue;

                        var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineMiddle) - view.VerticalOffset;
                        var txt = new FormattedText(
                            lineNumber,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            presenter.FontSize,
                            presenter.Foreground);
                        context.DrawText(txt, new Point(Bounds.Width - txt.Width, y - (txt.Height * 0.5)));
                    }
                }
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter == null)
                    return new Size(32, 0);

                var maxLineNumber = presenter.GetMaxLineNumber();
                var typeface = TextView.CreateTypeface();
                var test = new FormattedText(
                    $"{maxLineNumber}",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    presenter.FontSize,
                    Brushes.White);
                return new Size(test.Width, 0);
            }

            protected override void OnDataContextChanged(EventArgs e)
            {
                base.OnDataContextChanged(e);
                InvalidateMeasure();
            }

            private bool _usePresenter = false;
            private bool _isOld = false;
        }

        public class LineModifyTypeMargin : AbstractMargin
        {
            public LineModifyTypeMargin()
            {
                Margin = new Thickness(1, 0);
                ClipToBounds = true;
            }

            public override void Render(DrawingContext context)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter == null)
                    return;

                var lines = presenter.GetLines();
                var view = TextView;
                if (view is { VisualLinesValid: true })
                {
                    var typeface = view.CreateTypeface();
                    foreach (var line in view.VisualLines)
                    {
                        if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                            continue;

                        var index = line.FirstDocumentLine.LineNumber;
                        if (index > lines.Count)
                            break;

                        var info = lines[index - 1];
                        var y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineMiddle) - view.VerticalOffset;
                        FormattedText indicator = null;
                        if (info.Type == Models.TextDiffLineType.Added)
                        {
                            indicator = new FormattedText(
                                "+",
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                presenter.FontSize,
                                Brushes.Green);
                        }
                        else if (info.Type == Models.TextDiffLineType.Deleted)
                        {
                            indicator = new FormattedText(
                                "-",
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                presenter.FontSize,
                                Brushes.Red);
                        }

                        if (indicator != null)
                            context.DrawText(indicator, new Point(0, y - (indicator.Height * 0.5)));
                    }
                }
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var presenter = this.FindAncestorOfType<ThemedTextDiffPresenter>();
                if (presenter == null)
                    return new Size(0, 0);

                var typeface = TextView.CreateTypeface();
                var test = new FormattedText(
                    "-",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    presenter.FontSize,
                    Brushes.White);
                return new Size(test.Width, 0);
            }

            protected override void OnDataContextChanged(EventArgs e)
            {
                base.OnDataContextChanged(e);
                InvalidateMeasure();
            }
        }

        public class LineBackgroundRenderer : IBackgroundRenderer
        {
            public KnownLayer Layer => KnownLayer.Background;

            public LineBackgroundRenderer(ThemedTextDiffPresenter presenter)
            {
                _presenter = presenter;
            }

            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                if (_presenter.Document == null || !textView.VisualLinesValid)
                    return;

                var changeBlock = _presenter.BlockNavigation?.GetCurrentBlock();
                Brush changeBlockBG = new SolidColorBrush(Colors.Gray, 0.25);
                Pen changeBlockFG = new Pen(Brushes.Gray);

                var lines = _presenter.GetLines();
                var width = textView.Bounds.Width;
                foreach (var line in textView.VisualLines)
                {
                    if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                        continue;

                    var index = line.FirstDocumentLine.LineNumber;
                    if (index > lines.Count)
                        break;

                    var info = lines[index - 1];

                    var startY = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - textView.VerticalOffset;
                    var endY = line.GetTextLineVisualYPosition(line.TextLines[^1], VisualYPosition.LineBottom) - textView.VerticalOffset;

                    var bg = GetBrushByLineType(info.Type);
                    if (bg != null)
                    {
                        drawingContext.DrawRectangle(bg, null, new Rect(0, startY, width, endY - startY));

                        if (info.Highlights.Count > 0)
                        {
                            var highlightBG = info.Type == Models.TextDiffLineType.Added ? _presenter.AddedHighlightBrush : _presenter.DeletedHighlightBrush;
                            var processingIdxStart = 0;
                            var processingIdxEnd = 0;
                            var nextHighlight = 0;

                            foreach (var tl in line.TextLines)
                            {
                                processingIdxEnd += tl.Length;

                                var y = line.GetTextLineVisualYPosition(tl, VisualYPosition.LineTop) - textView.VerticalOffset;
                                var h = line.GetTextLineVisualYPosition(tl, VisualYPosition.LineBottom) - textView.VerticalOffset - y;

                                while (nextHighlight < info.Highlights.Count)
                                {
                                    var highlight = info.Highlights[nextHighlight];
                                    if (highlight.Start >= processingIdxEnd)
                                        break;

                                    var start = line.GetVisualColumn(highlight.Start < processingIdxStart ? processingIdxStart : highlight.Start);
                                    var end = line.GetVisualColumn(highlight.End >= processingIdxEnd ? processingIdxEnd : highlight.End + 1);

                                    var x = line.GetTextLineVisualXPosition(tl, start) - textView.HorizontalOffset;
                                    var w = line.GetTextLineVisualXPosition(tl, end) - textView.HorizontalOffset - x;
                                    var rect = new Rect(x, y, w, h);
                                    drawingContext.DrawRectangle(highlightBG, null, rect);

                                    if (highlight.End >= processingIdxEnd)
                                        break;

                                    nextHighlight++;
                                }

                                processingIdxStart = processingIdxEnd;
                            }
                        }
                    }

                    if (changeBlock != null && changeBlock.IsInRange(index))
                    {
                        drawingContext.DrawRectangle(changeBlockBG, null, new Rect(0, startY, width, endY - startY));
                        if (index == changeBlock.Start)
                            drawingContext.DrawLine(changeBlockFG, new Point(0, startY), new Point(width, startY));
                        if (index == changeBlock.End)
                            drawingContext.DrawLine(changeBlockFG, new Point(0, endY), new Point(width, endY));
                    }
                }
            }

            private IBrush GetBrushByLineType(Models.TextDiffLineType type)
            {
                return type switch
                {
                    Models.TextDiffLineType.None => _presenter.EmptyContentBackground,
                    Models.TextDiffLineType.Added => _presenter.AddedContentBackground,
                    Models.TextDiffLineType.Deleted => _presenter.DeletedContentBackground,
                    _ => null,
                };
            }

            private ThemedTextDiffPresenter _presenter = null;
        }

        public class LineStyleTransformer(ThemedTextDiffPresenter presenter) : DocumentColorizingTransformer
        {
            protected override void ColorizeLine(DocumentLine line)
            {
                var lines = presenter.GetLines();
                var idx = line.LineNumber;
                if (idx > lines.Count)
                    return;

                var info = lines[idx - 1];
                if (info.Type == Models.TextDiffLineType.Indicator)
                {
                    ChangeLinePart(line.Offset, line.EndOffset, v =>
                    {
                        v.TextRunProperties.SetForegroundBrush(presenter.IndicatorForeground);
                        v.TextRunProperties.SetTypeface(new Typeface(presenter.FontFamily, FontStyle.Italic));
                    });
                }
            }
        }

        public static readonly StyledProperty<string> FileNameProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, string>(nameof(FileName), string.Empty);

        public string FileName
        {
            get => GetValue(FileNameProperty);
            set => SetValue(FileNameProperty, value);
        }

        public static readonly StyledProperty<bool> IsOldProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(IsOld));

        public bool IsOld
        {
            get => GetValue(IsOldProperty);
            set => SetValue(IsOldProperty, value);
        }

        public static readonly StyledProperty<IBrush> LineBrushProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(LineBrush), new SolidColorBrush(Colors.DarkGray));

        public IBrush LineBrush
        {
            get => GetValue(LineBrushProperty);
            set => SetValue(LineBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> EmptyContentBackgroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(EmptyContentBackground), new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)));

        public IBrush EmptyContentBackground
        {
            get => GetValue(EmptyContentBackgroundProperty);
            set => SetValue(EmptyContentBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> AddedContentBackgroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(AddedContentBackground), new SolidColorBrush(Color.FromArgb(60, 0, 255, 0)));

        public IBrush AddedContentBackground
        {
            get => GetValue(AddedContentBackgroundProperty);
            set => SetValue(AddedContentBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> DeletedContentBackgroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(DeletedContentBackground), new SolidColorBrush(Color.FromArgb(60, 255, 0, 0)));

        public IBrush DeletedContentBackground
        {
            get => GetValue(DeletedContentBackgroundProperty);
            set => SetValue(DeletedContentBackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> AddedHighlightBrushProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(AddedHighlightBrush), new SolidColorBrush(Color.FromArgb(90, 0, 255, 0)));

        public IBrush AddedHighlightBrush
        {
            get => GetValue(AddedHighlightBrushProperty);
            set => SetValue(AddedHighlightBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> DeletedHighlightBrushProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(DeletedHighlightBrush), new SolidColorBrush(Color.FromArgb(80, 255, 0, 0)));

        public IBrush DeletedHighlightBrush
        {
            get => GetValue(DeletedHighlightBrushProperty);
            set => SetValue(DeletedHighlightBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> IndicatorForegroundProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, IBrush>(nameof(IndicatorForeground), Brushes.Gray);

        public IBrush IndicatorForeground
        {
            get => GetValue(IndicatorForegroundProperty);
            set => SetValue(IndicatorForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> UseSyntaxHighlightingProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(UseSyntaxHighlighting));

        public bool UseSyntaxHighlighting
        {
            get => GetValue(UseSyntaxHighlightingProperty);
            set => SetValue(UseSyntaxHighlightingProperty, value);
        }

        public static readonly StyledProperty<bool> ShowHiddenSymbolsProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(ShowHiddenSymbols));

        public bool ShowHiddenSymbols
        {
            get => GetValue(ShowHiddenSymbolsProperty);
            set => SetValue(ShowHiddenSymbolsProperty, value);
        }

        public static readonly StyledProperty<int> TabWidthProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, int>(nameof(TabWidth), 4);

        public int TabWidth
        {
            get => GetValue(TabWidthProperty);
            set => SetValue(TabWidthProperty, value);
        }

        public static readonly StyledProperty<bool> EnableChunkSelectionProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, bool>(nameof(EnableChunkSelection));

        public bool EnableChunkSelection
        {
            get => GetValue(EnableChunkSelectionProperty);
            set => SetValue(EnableChunkSelectionProperty, value);
        }

        public static readonly StyledProperty<TextDiffViewChunk> SelectedChunkProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, TextDiffViewChunk>(nameof(SelectedChunk));

        public TextDiffViewChunk SelectedChunk
        {
            get => GetValue(SelectedChunkProperty);
            set => SetValue(SelectedChunkProperty, value);
        }

        public static readonly StyledProperty<TextDiffViewRange> DisplayRangeProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, TextDiffViewRange>(nameof(DisplayRange), new TextDiffViewRange(0, 0));

        public TextDiffViewRange DisplayRange
        {
            get => GetValue(DisplayRangeProperty);
            set => SetValue(DisplayRangeProperty, value);
        }

        public static readonly StyledProperty<ViewModels.BlockNavigation> BlockNavigationProperty =
            AvaloniaProperty.Register<ThemedTextDiffPresenter, ViewModels.BlockNavigation>(nameof(BlockNavigation));

        public ViewModels.BlockNavigation BlockNavigation
        {
            get => GetValue(BlockNavigationProperty);
            set => SetValue(BlockNavigationProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(TextEditor);

        public ThemedTextDiffPresenter(TextArea area, TextDocument doc) : base(area, doc)
        {
            IsReadOnly = true;
            ShowLineNumbers = false;
            BorderThickness = new Thickness(0);

            Options.IndentationSize = TabWidth;
            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;
            Options.ShowEndOfLine = false;

            _lineStyleTransformer = new LineStyleTransformer(this);

            TextArea.TextView.Margin = new Thickness(2, 0);
            TextArea.TextView.BackgroundRenderers.Add(new LineBackgroundRenderer(this));
            TextArea.TextView.LineTransformers.Add(_lineStyleTransformer);
        }

        public virtual List<Models.TextDiffLine> GetLines()
        {
            return [];
        }

        public virtual int GetMaxLineNumber()
        {
            return 0;
        }

        public virtual void UpdateSelectedChunk(double y)
        {
        }

        public virtual void GotoFirstChange()
        {
            var blockNavigation = BlockNavigation;
            var prev = blockNavigation?.GotoFirst();
            if (prev != null)
            {
                TextArea.Caret.Line = prev.Start;
                ScrollToLine(prev.Start);
            }
        }

        public virtual void GotoPrevChange()
        {
            var blockNavigation = BlockNavigation;
            if (blockNavigation != null)
            {
                var prev = blockNavigation.GotoPrev();
                if (prev != null)
                {
                    TextArea.Caret.Line = prev.Start;
                    ScrollToLine(prev.Start);
                }

                return;
            }

            var firstLineIdx = DisplayRange.StartIdx;
            if (firstLineIdx <= 1)
                return;

            var lines = GetLines();
            var firstLineType = lines[firstLineIdx].Type;
            var prevLineType = lines[firstLineIdx - 1].Type;
            var isChangeFirstLine = firstLineType != Models.TextDiffLineType.Normal && firstLineType != Models.TextDiffLineType.Indicator;
            var isChangePrevLine = prevLineType != Models.TextDiffLineType.Normal && prevLineType != Models.TextDiffLineType.Indicator;
            if (isChangeFirstLine && isChangePrevLine)
            {
                for (var i = firstLineIdx - 2; i >= 0; i--)
                {
                    var prevType = lines[i].Type;
                    if (prevType == Models.TextDiffLineType.Normal || prevType == Models.TextDiffLineType.Indicator)
                    {
                        ScrollToLine(i + 2);
                        return;
                    }
                }
            }

            var findChange = false;
            for (var i = firstLineIdx - 1; i >= 0; i--)
            {
                var prevType = lines[i].Type;
                if (prevType == Models.TextDiffLineType.Normal || prevType == Models.TextDiffLineType.Indicator)
                {
                    if (findChange)
                    {
                        ScrollToLine(i + 2);
                        return;
                    }
                }
                else if (!findChange)
                {
                    findChange = true;
                }
            }
        }

        public virtual void GotoNextChange()
        {
            var blockNavigation = BlockNavigation;
            if (blockNavigation != null)
            {
                var next = blockNavigation.GotoNext();
                if (next != null)
                {
                    TextArea.Caret.Line = next.Start;
                    ScrollToLine(next.Start);
                }

                return;
            }

            var lines = GetLines();
            var lastLineIdx = DisplayRange.EndIdx;
            if (lastLineIdx >= lines.Count - 1)
                return;

            var lastLineType = lines[lastLineIdx].Type;
            var findNormalLine = lastLineType == Models.TextDiffLineType.Normal || lastLineType == Models.TextDiffLineType.Indicator;
            for (var idx = lastLineIdx + 1; idx < lines.Count; idx++)
            {
                var nextType = lines[idx].Type;
                if (nextType is Models.TextDiffLineType.None or Models.TextDiffLineType.Added or Models.TextDiffLineType.Deleted)
                {
                    if (findNormalLine)
                    {
                        ScrollToLine(idx + 1);
                        return;
                    }
                }
                else if (!findNormalLine)
                {
                    findNormalLine = true;
                }
            }
        }

        public virtual void GotoLastChange()
        {
            var blockNavigation = BlockNavigation;
            var next = blockNavigation?.GotoLast();
            if (next != null)
            {
                TextArea.Caret.Line = next.Start;
                ScrollToLine(next.Start);
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var chunk = SelectedChunk;
            if (chunk == null || (!chunk.Combined && chunk.IsOldSide != IsOld))
                return;

            var color = (Color)this.FindResource("SystemAccentColor")!;
            var brush = new SolidColorBrush(color, 0.1);
            var pen = new Pen(color.ToUInt32());
            var rect = new Rect(0, chunk.Y, Bounds.Width, chunk.Height);

            context.DrawRectangle(brush, null, rect);
            context.DrawLine(pen, rect.TopLeft, rect.TopRight);
            context.DrawLine(pen, rect.BottomLeft, rect.BottomRight);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            TextArea.TextView.ContextRequested += OnTextViewContextRequested;
            TextArea.TextView.PointerEntered += OnTextViewPointerChanged;
            TextArea.TextView.PointerMoved += OnTextViewPointerChanged;
            TextArea.TextView.PointerWheelChanged += OnTextViewPointerWheelChanged;
            TextArea.TextView.VisualLinesChanged += OnTextViewVisualLinesChanged;

            TextArea.AddHandler(KeyDownEvent, OnTextAreaKeyDown, RoutingStrategies.Tunnel);

            UpdateTextMate();
            OnTextViewVisualLinesChanged(null, null);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);

            TextArea.RemoveHandler(KeyDownEvent, OnTextAreaKeyDown);

            TextArea.TextView.ContextRequested -= OnTextViewContextRequested;
            TextArea.TextView.PointerEntered -= OnTextViewPointerChanged;
            TextArea.TextView.PointerMoved -= OnTextViewPointerChanged;
            TextArea.TextView.PointerWheelChanged -= OnTextViewPointerWheelChanged;
            TextArea.TextView.VisualLinesChanged -= OnTextViewVisualLinesChanged;

            if (_textMate != null)
            {
                _textMate.Dispose();
                _textMate = null;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == UseSyntaxHighlightingProperty)
            {
                UpdateTextMate();
            }
            else if (change.Property == ShowHiddenSymbolsProperty)
            {
                var val = ShowHiddenSymbols;
                Options.ShowTabs = val;
                Options.ShowSpaces = val;
            }
            else if (change.Property == TabWidthProperty)
            {
                Options.IndentationSize = TabWidth;
            }
            else if (change.Property == FileNameProperty)
            {
                Models.TextMateHelper.SetGrammarByFileName(_textMate, FileName);
            }
            else if (change.Property.Name == "ActualThemeVariant" && change.NewValue != null)
            {
                Models.TextMateHelper.SetThemeByApp(_textMate);
            }
            else if (change.Property == SelectedChunkProperty)
            {
                InvalidateVisual();
            }
            else if (change.Property == BlockNavigationProperty)
            {
                if (change.OldValue is ViewModels.BlockNavigation oldValue)
                    oldValue.PropertyChanged -= OnBlockNavigationPropertyChanged;

                if (change.NewValue is ViewModels.BlockNavigation newValue)
                    newValue.PropertyChanged += OnBlockNavigationPropertyChanged;

                TextArea?.TextView?.Redraw();
            }
        }

        private async void OnTextAreaKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyModifiers.Equals(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                if (e.Key == Key.C)
                {
                    await CopyWithoutIndicatorsAsync();
                    e.Handled = true;
                }
            }

            if (!e.Handled)
                base.OnKeyDown(e);
        }

        private void OnBlockNavigationPropertyChanged(object _1, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Current")
                TextArea?.TextView?.Redraw();
        }

        private void OnTextViewContextRequested(object sender, ContextRequestedEventArgs e)
        {
            var selection = TextArea.Selection;
            if (selection.IsEmpty)
                return;

            var copy = new MenuItem();
            copy.Header = App.Text("Copy");
            copy.Icon = App.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, ev) =>
            {
                await CopyWithoutIndicatorsAsync();
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(copy);
            menu.Open(TextArea.TextView);

            e.Handled = true;
        }

        private void OnTextViewPointerChanged(object sender, PointerEventArgs e)
        {
            if (EnableChunkSelection && sender is TextView view)
            {
                var selection = TextArea.Selection;
                if (selection == null || selection.IsEmpty)
                {
                    if (_lastSelectStart != _lastSelectEnd)
                    {
                        _lastSelectStart = TextLocation.Empty;
                        _lastSelectEnd = TextLocation.Empty;
                    }

                    var chunk = SelectedChunk;
                    if (chunk != null)
                    {
                        var rect = new Rect(0, chunk.Y, Bounds.Width, chunk.Height);
                        if (rect.Contains(e.GetPosition(this)))
                            return;
                    }

                    UpdateSelectedChunk(e.GetPosition(view).Y + view.VerticalOffset);
                    return;
                }

                var start = selection.StartPosition.Location;
                var end = selection.EndPosition.Location;
                if (_lastSelectStart != start || _lastSelectEnd != end)
                {
                    _lastSelectStart = start;
                    _lastSelectEnd = end;
                    UpdateSelectedChunk(e.GetPosition(view).Y + view.VerticalOffset);
                    return;
                }

                if (SelectedChunk == null)
                    UpdateSelectedChunk(e.GetPosition(view).Y + view.VerticalOffset);
            }
        }

        private void OnTextViewPointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            if (EnableChunkSelection && sender is TextView view)
            {
                var y = e.GetPosition(view).Y + view.VerticalOffset;
                Dispatcher.UIThread.Post(() => UpdateSelectedChunk(y));
            }
        }

        private void OnTextViewVisualLinesChanged(object sender, EventArgs e)
        {
            if (!TextArea.TextView.VisualLinesValid)
            {
                SetCurrentValue(DisplayRangeProperty, new TextDiffViewRange(0, 0));
                return;
            }

            var lines = GetLines();
            var start = int.MaxValue;
            var count = 0;
            foreach (var line in TextArea.TextView.VisualLines)
            {
                if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                    continue;

                var index = line.FirstDocumentLine.LineNumber - 1;
                if (index >= lines.Count)
                    continue;

                count++;
                if (start > index)
                    start = index;
            }

            SetCurrentValue(DisplayRangeProperty, new TextDiffViewRange(start, start + count));
        }

        protected void TrySetChunk(TextDiffViewChunk chunk)
        {
            var old = SelectedChunk;
            if (chunk == null)
            {
                if (old != null)
                    SetCurrentValue(SelectedChunkProperty, null);

                return;
            }

            if (chunk.ShouldReplace(old))
                SetCurrentValue(SelectedChunkProperty, chunk);
        }

        protected (int, int) FindRangeByIndex(List<Models.TextDiffLine> lines, int lineIdx)
        {
            var startIdx = -1;
            var endIdx = -1;

            var normalLineCount = 0;
            var modifiedLineCount = 0;

            for (int i = lineIdx; i >= 0; i--)
            {
                var line = lines[i];
                if (line.Type == Models.TextDiffLineType.Indicator)
                {
                    startIdx = i;
                    break;
                }

                if (line.Type == Models.TextDiffLineType.Normal)
                {
                    normalLineCount++;
                    if (normalLineCount >= 2)
                    {
                        startIdx = i;
                        break;
                    }
                }
                else
                {
                    normalLineCount = 0;
                    modifiedLineCount++;
                }
            }

            normalLineCount = lines[lineIdx].Type == Models.TextDiffLineType.Normal ? 1 : 0;
            for (int i = lineIdx + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Type == Models.TextDiffLineType.Indicator)
                {
                    endIdx = i;
                    break;
                }

                if (line.Type == Models.TextDiffLineType.Normal)
                {
                    normalLineCount++;
                    if (normalLineCount >= 2)
                    {
                        endIdx = i;
                        break;
                    }
                }
                else
                {
                    normalLineCount = 0;
                    modifiedLineCount++;
                }
            }

            if (endIdx == -1)
                endIdx = lines.Count - 1;

            return modifiedLineCount > 0 ? (startIdx, endIdx) : (-1, -1);
        }

        private void UpdateTextMate()
        {
            if (UseSyntaxHighlighting)
            {
                if (_textMate == null)
                {
                    TextArea.TextView.LineTransformers.Remove(_lineStyleTransformer);
                    _textMate = Models.TextMateHelper.CreateForEditor(this);
                    TextArea.TextView.LineTransformers.Add(_lineStyleTransformer);
                    Models.TextMateHelper.SetGrammarByFileName(_textMate, FileName);
                }
            }
            else
            {
                if (_textMate != null)
                {
                    _textMate.Dispose();
                    _textMate = null;
                    GC.Collect();

                    TextArea.TextView.Redraw();
                }
            }
        }

        private async Task CopyWithoutIndicatorsAsync()
        {
            var selection = TextArea.Selection;
            if (selection.IsEmpty)
            {
                await App.CopyTextAsync(string.Empty);
                return;
            }

            var lines = GetLines();

            var startPosition = selection.StartPosition;
            var endPosition = selection.EndPosition;

            if (startPosition.Location > endPosition.Location)
                (startPosition, endPosition) = (endPosition, startPosition);

            var startIdx = startPosition.Line - 1;
            var endIdx = endPosition.Line - 1;

            if (startIdx == endIdx)
            {
                if (lines[startIdx].Type is Models.TextDiffLineType.Indicator or Models.TextDiffLineType.None)
                    await App.CopyTextAsync(string.Empty);
                else
                    await App.CopyTextAsync(SelectedText);
                return;
            }

            var builder = new StringBuilder();
            for (var i = startIdx; i <= endIdx && i <= lines.Count - 1; i++)
            {
                var line = lines[i];
                if (line.Type is Models.TextDiffLineType.Indicator or Models.TextDiffLineType.None)
                    continue;

                // The first selected line (partial selection)
                if (i == startIdx && startPosition.Column > 1)
                {
                    builder.Append(line.Content.AsSpan(startPosition.Column - 1));
                    builder.Append(Environment.NewLine);
                    continue;
                }

                // The selection range is larger than original source.
                if (i == lines.Count - 1 && i < endIdx)
                {
                    builder.Append(line.Content);
                    break;
                }

                // For the last line (selection range is within original source)
                if (i == endIdx)
                {
                    if (endPosition.Column - 1 < line.Content.Length)
                    {
                        builder.Append(line.Content.AsSpan(0, endPosition.Column - 1));
                    }
                    else
                    {
                        builder.Append(line.Content);
                    }
                    break;
                }

                // Other lines.
                builder.AppendLine(line.Content);
            }

            await App.CopyTextAsync(builder.ToString());
        }

        private TextMate.Installation _textMate = null;
        private TextLocation _lastSelectStart = TextLocation.Empty;
        private TextLocation _lastSelectEnd = TextLocation.Empty;
        private LineStyleTransformer _lineStyleTransformer = null;
    }

    public class CombinedTextDiffPresenter : ThemedTextDiffPresenter
    {
        public CombinedTextDiffPresenter() : base(new TextArea(), new TextDocument())
        {
            TextArea.LeftMargins.Add(new LineNumberMargin(false, true));
            TextArea.LeftMargins.Add(new VerticalSeparatorMargin());
            TextArea.LeftMargins.Add(new LineNumberMargin(false, false));
            TextArea.LeftMargins.Add(new VerticalSeparatorMargin());
            TextArea.LeftMargins.Add(new LineModifyTypeMargin());
        }

        public override List<Models.TextDiffLine> GetLines()
        {
            if (DataContext is Models.TextDiff diff)
                return diff.Lines;
            return [];
        }

        public override int GetMaxLineNumber()
        {
            if (DataContext is Models.TextDiff diff)
                return diff.MaxLineNumber;
            return 0;
        }

        public override void UpdateSelectedChunk(double y)
        {
            if (DataContext is not Models.TextDiff diff)
                return;

            var view = TextArea.TextView;
            var selection = TextArea.Selection;
            if (!selection.IsEmpty)
            {
                var startIdx = Math.Min(selection.StartPosition.Line - 1, diff.Lines.Count - 1);
                var endIdx = Math.Min(selection.EndPosition.Line - 1, diff.Lines.Count - 1);

                if (startIdx > endIdx)
                    (startIdx, endIdx) = (endIdx, startIdx);

                var hasChanges = false;
                for (var i = startIdx; i <= endIdx; i++)
                {
                    var line = diff.Lines[i];
                    if (line.Type == Models.TextDiffLineType.Added || line.Type == Models.TextDiffLineType.Deleted)
                    {
                        hasChanges = true;
                        break;
                    }
                }

                if (!hasChanges)
                {
                    TrySetChunk(null);
                    return;
                }

                var firstLineIdx = view.VisualLines[0].FirstDocumentLine.LineNumber - 1;
                var lastLineIdx = view.VisualLines[^1].FirstDocumentLine.LineNumber - 1;
                if (endIdx < firstLineIdx || startIdx > lastLineIdx)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = startIdx,
                    EndIdx = endIdx,
                    Combined = true,
                    IsOldSide = false,
                });
            }
            else
            {
                var lineIdx = -1;
                foreach (var line in view.VisualLines)
                {
                    if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                        continue;

                    var index = line.FirstDocumentLine.LineNumber;
                    if (index > diff.Lines.Count)
                        break;

                    var endY = line.GetTextLineVisualYPosition(line.TextLines[^1], VisualYPosition.TextBottom);
                    if (endY > y)
                    {
                        lineIdx = index - 1;
                        break;
                    }
                }

                if (lineIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var (startIdx, endIdx) = FindRangeByIndex(diff.Lines, lineIdx);
                if (startIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = startIdx,
                    EndIdx = endIdx,
                    Combined = true,
                    IsOldSide = false,
                });
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _scrollViewer = this.FindDescendantOfType<ScrollViewer>();
            if (_scrollViewer != null)
            {
                _scrollViewer.Bind(ScrollViewer.OffsetProperty, new Binding("ScrollOffset", BindingMode.TwoWay));
                _scrollViewer.ScrollChanged += OnTextViewScrollChanged;
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged -= OnTextViewScrollChanged;

            base.OnUnloaded(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is Models.TextDiff textDiff)
            {
                var builder = new StringBuilder();
                foreach (var line in textDiff.Lines)
                {
                    if (line.Content.Length > 10000)
                    {
                        builder.Append(line.Content.AsSpan(0, 1000));
                        builder.Append($"...({line.Content.Length - 1000} character trimmed)");
                    }
                    else
                    {
                        builder.Append(line.Content);
                    }

                    if (line.NoNewLineEndOfFile)
                        builder.Append("\u26D4");

                    builder.AppendLine();
                }

                Text = builder.ToString();
            }
            else
            {
                Text = string.Empty;
            }

            GC.Collect();
        }

        private void OnTextViewScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!TextArea.TextView.IsPointerOver)
                TrySetChunk(null);
        }

        private ScrollViewer _scrollViewer = null;
    }

    public class SingleSideTextDiffPresenter : ThemedTextDiffPresenter
    {
        public SingleSideTextDiffPresenter() : base(new TextArea(), new TextDocument())
        {
            TextArea.LeftMargins.Add(new LineNumberMargin(true, false));
            TextArea.LeftMargins.Add(new VerticalSeparatorMargin());
            TextArea.LeftMargins.Add(new LineModifyTypeMargin());
        }

        public override List<Models.TextDiffLine> GetLines()
        {
            if (DataContext is ViewModels.TwoSideTextDiff diff)
                return IsOld ? diff.Old : diff.New;
            return [];
        }

        public override int GetMaxLineNumber()
        {
            if (DataContext is ViewModels.TwoSideTextDiff diff)
                return diff.MaxLineNumber;
            return 0;
        }

        public override void GotoFirstChange()
        {
            base.GotoFirstChange();
            DirectSyncScrollOffset();
        }

        public override void GotoPrevChange()
        {
            base.GotoPrevChange();
            DirectSyncScrollOffset();
        }

        public override void GotoNextChange()
        {
            base.GotoNextChange();
            DirectSyncScrollOffset();
        }

        public override void GotoLastChange()
        {
            base.GotoLastChange();
            DirectSyncScrollOffset();
        }

        public override void UpdateSelectedChunk(double y)
        {
            if (DataContext is not ViewModels.TwoSideTextDiff diff)
                return;

            var parent = this.FindAncestorOfType<TextDiffView>();
            if (parent == null)
                return;

            var view = TextArea.TextView;
            var lines = IsOld ? diff.Old : diff.New;
            var selection = TextArea.Selection;
            if (!selection.IsEmpty)
            {
                var startIdx = Math.Min(selection.StartPosition.Line - 1, lines.Count - 1);
                var endIdx = Math.Min(selection.EndPosition.Line - 1, lines.Count - 1);

                if (startIdx > endIdx)
                    (startIdx, endIdx) = (endIdx, startIdx);

                var hasChanges = false;
                for (var i = startIdx; i <= endIdx; i++)
                {
                    var line = lines[i];
                    if (line.Type == Models.TextDiffLineType.Added || line.Type == Models.TextDiffLineType.Deleted)
                    {
                        hasChanges = true;
                        break;
                    }
                }

                if (!hasChanges)
                {
                    TrySetChunk(null);
                    return;
                }

                var firstLineIdx = view.VisualLines[0].FirstDocumentLine.LineNumber - 1;
                var lastLineIdx = view.VisualLines[^1].FirstDocumentLine.LineNumber - 1;
                if (endIdx < firstLineIdx || startIdx > lastLineIdx)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                diff.ConvertsToCombinedRange(parent.DataContext as Models.TextDiff, ref startIdx, ref endIdx, IsOld);

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = startIdx,
                    EndIdx = endIdx,
                    Combined = false,
                    IsOldSide = IsOld,
                });

                return;
            }

            if (this.FindAncestorOfType<TextDiffView>()?.DataContext is Models.TextDiff textDiff)
            {
                var lineIdx = -1;
                foreach (var line in view.VisualLines)
                {
                    if (line.IsDisposed || line.FirstDocumentLine == null || line.FirstDocumentLine.IsDeleted)
                        continue;

                    var index = line.FirstDocumentLine.LineNumber;
                    if (index > lines.Count)
                        break;

                    var endY = line.GetTextLineVisualYPosition(line.TextLines[^1], VisualYPosition.TextBottom);
                    if (endY > y)
                    {
                        lineIdx = index - 1;
                        break;
                    }
                }

                if (lineIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var (startIdx, endIdx) = FindRangeByIndex(lines, lineIdx);
                if (startIdx == -1)
                {
                    TrySetChunk(null);
                    return;
                }

                var startLine = view.GetVisualLine(startIdx + 1);
                var endLine = view.GetVisualLine(endIdx + 1);

                var rectStartY = startLine != null ?
                    startLine.GetTextLineVisualYPosition(startLine.TextLines[0], VisualYPosition.TextTop) - view.VerticalOffset :
                    0;
                var rectEndY = endLine != null ?
                    endLine.GetTextLineVisualYPosition(endLine.TextLines[^1], VisualYPosition.TextBottom) - view.VerticalOffset :
                    view.Bounds.Height;

                TrySetChunk(new TextDiffViewChunk()
                {
                    Y = rectStartY,
                    Height = rectEndY - rectStartY,
                    StartIdx = textDiff.Lines.IndexOf(lines[startIdx]),
                    EndIdx = endIdx == lines.Count - 1 ? textDiff.Lines.Count - 1 : textDiff.Lines.IndexOf(lines[endIdx]),
                    Combined = true,
                    IsOldSide = false,
                });
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _scrollViewer = this.FindDescendantOfType<ScrollViewer>();
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnTextViewScrollChanged;
                _scrollViewer.Bind(ScrollViewer.OffsetProperty, new Binding("SyncScrollOffset", BindingMode.OneWay));
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged -= OnTextViewScrollChanged;
                _scrollViewer = null;
            }

            base.OnUnloaded(e);
            GC.Collect();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (DataContext is ViewModels.TwoSideTextDiff diff)
            {
                var builder = new StringBuilder();
                var lines = IsOld ? diff.Old : diff.New;
                foreach (var line in lines)
                {
                    if (line.Content.Length > 1000)
                    {
                        builder.Append(line.Content.AsSpan(0, 1000));
                        builder.Append($"...({line.Content.Length - 1000} characters trimmed)");
                    }
                    else
                    {
                        builder.Append(line.Content);
                    }

                    if (line.NoNewLineEndOfFile)
                        builder.Append("\u26D4");

                    builder.AppendLine();
                }

                Text = builder.ToString();
            }
            else
            {
                Text = string.Empty;
            }
        }

        private void OnTextViewScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (IsPointerOver && DataContext is ViewModels.TwoSideTextDiff diff)
            {
                diff.SyncScrollOffset = _scrollViewer?.Offset ?? Vector.Zero;

                if (!TextArea.TextView.IsPointerOver)
                    TrySetChunk(null);
            }
        }

        private void DirectSyncScrollOffset()
        {
            if (_scrollViewer is not null && DataContext is ViewModels.TwoSideTextDiff diff)
                diff.SyncScrollOffset = _scrollViewer?.Offset ?? Vector.Zero;
        }

        private ScrollViewer _scrollViewer = null;
    }

    public class TextDiffViewMinimap : Control
    {
        public static readonly StyledProperty<IBrush> AddedLineBrushProperty =
            AvaloniaProperty.Register<TextDiffViewMinimap, IBrush>(nameof(AddedLineBrush), new SolidColorBrush(Color.FromArgb(60, 0, 255, 0)));

        public IBrush AddedLineBrush
        {
            get => GetValue(AddedLineBrushProperty);
            set => SetValue(AddedLineBrushProperty, value);
        }

        public static readonly StyledProperty<IBrush> DeletedLineBrushProperty =
            AvaloniaProperty.Register<TextDiffViewMinimap, IBrush>(nameof(DeletedLineBrush), new SolidColorBrush(Color.FromArgb(60, 255, 0, 0)));

        public IBrush DeletedLineBrush
        {
            get => GetValue(DeletedLineBrushProperty);
            set => SetValue(DeletedLineBrushProperty, value);
        }

        public static readonly StyledProperty<TextDiffViewRange> DisplayRangeProperty =
            AvaloniaProperty.Register<TextDiffViewMinimap, TextDiffViewRange>(nameof(DisplayRange), new TextDiffViewRange(0, 0));

        public TextDiffViewRange DisplayRange
        {
            get => GetValue(DisplayRangeProperty);
            set => SetValue(DisplayRangeProperty, value);
        }

        public static readonly StyledProperty<Color> DisplayRangeColorProperty =
            AvaloniaProperty.Register<TextDiffViewMinimap, Color>(nameof(DisplayRangeColor), Colors.RoyalBlue);

        public Color DisplayRangeColor
        {
            get => GetValue(DisplayRangeColorProperty);
            set => SetValue(DisplayRangeColorProperty, value);
        }

        static TextDiffViewMinimap()
        {
            AffectsRender<TextDiffViewMinimap>(
                AddedLineBrushProperty,
                DeletedLineBrushProperty,
                DisplayRangeProperty,
                DisplayRangeColorProperty);
        }

        public override void Render(DrawingContext context)
        {
            var total = 0;
            if (DataContext is ViewModels.TwoSideTextDiff twoSideDiff)
            {
                var halfWidth = Bounds.Width * 0.5;
                total = Math.Max(twoSideDiff.Old.Count, twoSideDiff.New.Count);
                RenderSingleSide(context, twoSideDiff.Old, 0, halfWidth);
                RenderSingleSide(context, twoSideDiff.New, halfWidth, halfWidth);
            }
            else if (DataContext is Models.TextDiff diff)
            {
                total = diff.Lines.Count;
                RenderSingleSide(context, diff.Lines, 0, Bounds.Width);
            }

            var range = DisplayRange;
            if (range.EndIdx == 0)
                return;

            var startY = range.StartIdx / (total * 1.0) * Bounds.Height;
            var endY = range.EndIdx / (total * 1.0) * Bounds.Height;
            var color = DisplayRangeColor;
            var brush = new SolidColorBrush(color, 0.2);
            var pen = new Pen(color.ToUInt32());
            var rect = new Rect(0, startY, Bounds.Width, endY - startY);

            context.DrawRectangle(brush, null, rect);
            context.DrawLine(pen, rect.TopLeft, rect.TopRight);
            context.DrawLine(pen, rect.BottomLeft, rect.BottomRight);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            InvalidateVisual();
        }

        private void RenderSingleSide(DrawingContext context, List<Models.TextDiffLine> lines, double x, double width)
        {
            var total = lines.Count;
            var lastLineType = Models.TextDiffLineType.Indicator;
            var lastLineTypeStart = 0;

            for (int i = 0; i < total; i++)
            {
                var line = lines[i];
                if (line.Type != lastLineType)
                {
                    RenderBlock(context, lastLineType, lastLineTypeStart, i - lastLineTypeStart, total, x, width);

                    lastLineType = line.Type;
                    lastLineTypeStart = i;
                }
            }

            RenderBlock(context, lastLineType, lastLineTypeStart, total - lastLineTypeStart, total, x, width);
        }

        private void RenderBlock(DrawingContext context, Models.TextDiffLineType type, int start, int count, int total, double x, double width)
        {
            if (type == Models.TextDiffLineType.Added || type == Models.TextDiffLineType.Deleted)
            {
                var brush = type == Models.TextDiffLineType.Added ? AddedLineBrush : DeletedLineBrush;
                var y = start / (total * 1.0) * Bounds.Height;
                var h = Math.Max(0.5, count / (total * 1.0) * Bounds.Height);
                context.DrawRectangle(brush, null, new Rect(x, y, width, h));
            }
        }
    }

    public partial class TextDiffView : UserControl
    {
        public static readonly StyledProperty<bool> UseSideBySideDiffProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(UseSideBySideDiff));

        public bool UseSideBySideDiff
        {
            get => GetValue(UseSideBySideDiffProperty);
            set => SetValue(UseSideBySideDiffProperty, value);
        }

        public static readonly StyledProperty<TextDiffViewChunk> SelectedChunkProperty =
            AvaloniaProperty.Register<TextDiffView, TextDiffViewChunk>(nameof(SelectedChunk));

        public TextDiffViewChunk SelectedChunk
        {
            get => GetValue(SelectedChunkProperty);
            set => SetValue(SelectedChunkProperty, value);
        }

        public static readonly StyledProperty<bool> IsUnstagedChangeProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(IsUnstagedChange));

        public bool IsUnstagedChange
        {
            get => GetValue(IsUnstagedChangeProperty);
            set => SetValue(IsUnstagedChangeProperty, value);
        }

        public static readonly StyledProperty<bool> EnableChunkSelectionProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(EnableChunkSelection));

        public bool EnableChunkSelection
        {
            get => GetValue(EnableChunkSelectionProperty);
            set => SetValue(EnableChunkSelectionProperty, value);
        }

        public static readonly StyledProperty<bool> UseBlockNavigationProperty =
            AvaloniaProperty.Register<TextDiffView, bool>(nameof(UseBlockNavigation));

        public bool UseBlockNavigation
        {
            get => GetValue(UseBlockNavigationProperty);
            set => SetValue(UseBlockNavigationProperty, value);
        }

        public static readonly StyledProperty<ViewModels.BlockNavigation> BlockNavigationProperty =
            AvaloniaProperty.Register<TextDiffView, ViewModels.BlockNavigation>(nameof(BlockNavigation));

        public ViewModels.BlockNavigation BlockNavigation
        {
            get => GetValue(BlockNavigationProperty);
            set => SetValue(BlockNavigationProperty, value);
        }

        public static readonly RoutedEvent<RoutedEventArgs> BlockNavigationChangedEvent =
            RoutedEvent.Register<TextDiffView, RoutedEventArgs>(nameof(BlockNavigationChanged), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        public event EventHandler<RoutedEventArgs> BlockNavigationChanged
        {
            add { AddHandler(BlockNavigationChangedEvent, value); }
            remove { RemoveHandler(BlockNavigationChangedEvent, value); }
        }

        static TextDiffView()
        {
            UseSideBySideDiffProperty.Changed.AddClassHandler<TextDiffView>((v, _) =>
            {
                v.RefreshContent(v.DataContext as Models.TextDiff, false);
            });

            UseBlockNavigationProperty.Changed.AddClassHandler<TextDiffView>((v, _) =>
            {
                v.RefreshBlockNavigation();
            });

            SelectedChunkProperty.Changed.AddClassHandler<TextDiffView>((v, _) =>
            {
                var chunk = v.SelectedChunk;
                if (chunk == null)
                {
                    v.Popup.IsVisible = false;
                    return;
                }

                var top = chunk.Y + (chunk.Height >= 36 ? 8 : 2);
                var right = (chunk.Combined || !chunk.IsOldSide) ? 26 : (v.Bounds.Width * 0.5f) + 26;
                v.Popup.Margin = new Thickness(0, top, right, 0);
                v.Popup.IsVisible = true;
            });
        }

        public TextDiffView()
        {
            InitializeComponent();
        }

        public void GotoFirstChange()
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoFirstChange();
            TryRaiseBlockNavigationChanged();
        }

        public void GotoPrevChange()
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoPrevChange();
            TryRaiseBlockNavigationChanged();
        }

        public void GotoNextChange()
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoNextChange();
            TryRaiseBlockNavigationChanged();
        }

        public void GotoLastChange()
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoLastChange();
            TryRaiseBlockNavigationChanged();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            RefreshContent(DataContext as Models.TextDiff);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            if (SelectedChunk != null)
                SetCurrentValue(SelectedChunkProperty, null);
        }

        private void RefreshContent(Models.TextDiff diff, bool keepScrollOffset = true)
        {
            if (SelectedChunk != null)
                SetCurrentValue(SelectedChunkProperty, null);

            if (diff == null)
            {
                Editor.Content = null;
                GC.Collect();
                return;
            }

            if (UseSideBySideDiff)
            {
                var previousContent = Editor.Content as ViewModels.TwoSideTextDiff;
                Editor.Content = new ViewModels.TwoSideTextDiff(diff, keepScrollOffset ? previousContent : null);
            }
            else
            {
                if (!keepScrollOffset)
                    diff.ScrollOffset = Vector.Zero;
                Editor.Content = diff;
            }

            RefreshBlockNavigation();

            IsUnstagedChange = diff.Option.IsUnstaged;
            EnableChunkSelection = diff.Option.WorkingCopyChange != null;
        }

        private void RefreshBlockNavigation()
        {
            if (UseBlockNavigation)
                BlockNavigation = new ViewModels.BlockNavigation(Editor.Content);
            else
                BlockNavigation = null;

            TryRaiseBlockNavigationChanged();
        }

        private async void OnStageChunk(object _1, RoutedEventArgs _2)
        {
            var chunk = SelectedChunk;
            if (chunk == null)
                return;

            var diff = DataContext as Models.TextDiff;

            var change = diff?.Option.WorkingCopyChange;
            if (change == null)
                return;

            var selection = diff.MakeSelection(chunk.StartIdx + 1, chunk.EndIdx + 1, chunk.Combined, chunk.IsOldSide);
            if (!selection.HasChanges)
                return;

            var repoView = this.FindAncestorOfType<Repository>();

            if (repoView?.DataContext is not ViewModels.Repository repo)
                return;

            repo.SetWatcherEnabled(false);

            if (!selection.HasLeftChanges)
            {
                await new Commands.Add(repo.FullPath, change).ExecAsync();
            }
            else
            {
                var tmpFile = Path.GetTempFileName();
                if (change.WorkTree == Models.ChangeState.Untracked)
                {
                    diff.GenerateNewPatchFromSelection(change, null, selection, false, tmpFile);
                }
                else if (chunk.Combined)
                {
                    var treeGuid = await new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).GetResultAsync();
                    diff.GeneratePatchFromSelection(change, treeGuid, selection, false, tmpFile);
                }
                else
                {
                    var treeGuid = await new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).GetResultAsync();
                    diff.GeneratePatchFromSelectionSingleSide(change, treeGuid, selection, false, chunk.IsOldSide, tmpFile);
                }

                await new Commands.Apply(diff.Repo, tmpFile, true, "nowarn", "--cache --index").ExecAsync();
                File.Delete(tmpFile);
            }

            repo.MarkWorkingCopyDirtyManually();
            repo.SetWatcherEnabled(true);
        }

        private async void OnUnstageChunk(object _1, RoutedEventArgs _2)
        {
            var chunk = SelectedChunk;
            if (chunk == null)
                return;

            var diff = DataContext as Models.TextDiff;

            var change = diff?.Option.WorkingCopyChange;
            if (change == null)
                return;

            var selection = diff.MakeSelection(chunk.StartIdx + 1, chunk.EndIdx + 1, chunk.Combined, chunk.IsOldSide);
            if (!selection.HasChanges)
                return;

            var repoView = this.FindAncestorOfType<Repository>();

            if (repoView?.DataContext is not ViewModels.Repository repo)
                return;

            repo.SetWatcherEnabled(false);

            if (!selection.HasLeftChanges)
            {
                if (change.DataForAmend != null)
                    await new Commands.UnstageChangesForAmend(repo.FullPath, [change]).ExecAsync();
                else
                    await new Commands.Restore(repo.FullPath, change).ExecAsync();
            }
            else
            {
                var treeGuid = await new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).GetResultAsync();
                var tmpFile = Path.GetTempFileName();
                if (change.Index == Models.ChangeState.Added)
                    diff.GenerateNewPatchFromSelection(change, treeGuid, selection, true, tmpFile);
                else if (chunk.Combined)
                    diff.GeneratePatchFromSelection(change, treeGuid, selection, true, tmpFile);
                else
                    diff.GeneratePatchFromSelectionSingleSide(change, treeGuid, selection, true, chunk.IsOldSide, tmpFile);

                await new Commands.Apply(diff.Repo, tmpFile, true, "nowarn", "--cache --index --reverse").ExecAsync();
                File.Delete(tmpFile);
            }

            repo.MarkWorkingCopyDirtyManually();
            repo.SetWatcherEnabled(true);
        }

        private async void OnDiscardChunk(object _1, RoutedEventArgs _2)
        {
            var chunk = SelectedChunk;
            if (chunk == null)
                return;

            var diff = DataContext as Models.TextDiff;

            var change = diff?.Option.WorkingCopyChange;
            if (change == null)
                return;

            var selection = diff.MakeSelection(chunk.StartIdx + 1, chunk.EndIdx + 1, chunk.Combined, chunk.IsOldSide);
            if (!selection.HasChanges)
                return;

            var repoView = this.FindAncestorOfType<Repository>();

            if (repoView?.DataContext is not ViewModels.Repository repo)
                return;

            repo.SetWatcherEnabled(false);

            if (!selection.HasLeftChanges)
            {
                await Commands.Discard.ChangesAsync(repo.FullPath, [change], null);
            }
            else
            {
                var tmpFile = Path.GetTempFileName();
                if (change.Index == Models.ChangeState.Added)
                {
                    diff.GenerateNewPatchFromSelection(change, null, selection, true, tmpFile);
                }
                else if (chunk.Combined)
                {
                    var treeGuid = await new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).GetResultAsync();
                    diff.GeneratePatchFromSelection(change, treeGuid, selection, true, tmpFile);
                }
                else
                {
                    var treeGuid = await new Commands.QueryStagedFileBlobGuid(diff.Repo, change.Path).GetResultAsync();
                    diff.GeneratePatchFromSelectionSingleSide(change, treeGuid, selection, true, chunk.IsOldSide, tmpFile);
                }

                await new Commands.Apply(diff.Repo, tmpFile, true, "nowarn", "--reverse").ExecAsync();
                File.Delete(tmpFile);
            }

            repo.MarkWorkingCopyDirtyManually();
            repo.SetWatcherEnabled(true);
        }

        private void TryRaiseBlockNavigationChanged()
        {
            if (UseBlockNavigation)
                RaiseEvent(new RoutedEventArgs(BlockNavigationChangedEvent));
        }
    }
}
