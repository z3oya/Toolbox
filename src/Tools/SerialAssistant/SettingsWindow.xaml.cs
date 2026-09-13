using System.Windows;
using System.Windows.Controls;
using Toolbox.Core.SerialComm;

namespace Toolbox.Tools.SerialAssistant;

/// <summary>Live-applied settings; also carries the About info at the bottom.
/// Encoding changes are pushed out through EncodingChanged as they happen.</summary>
public partial class SettingsWindow : Window
{
    // Decouples combo labels from values, same idea as MainWindow.Choice<T>.
    private sealed record KindChoice(string Label, TextEncodingKind Value)
    {
        public override string ToString() => Label;
    }

    public Action<TextEncodingKind>? EncodingChanged { get; set; }

    public SettingsWindow(TextEncodingKind current)
    {
        InitializeComponent();
        var kinds = new[]
        {
            new KindChoice("UTF-8", TextEncodingKind.Utf8),
            new KindChoice("ASCII", TextEncodingKind.Ascii),
            new KindChoice("Latin1", TextEncodingKind.Latin1),
            new KindChoice("GBK", TextEncodingKind.Gbk),
        };
        EncodingBox.ItemsSource = kinds;
        EncodingBox.SelectedItem = kinds.First(k => k.Value == current);
        EncodingBox.SelectionChanged += (_, _) =>
        {
            if (EncodingBox.SelectedItem is KindChoice choice)
                EncodingChanged?.Invoke(choice.Value);
        };
    }
}
