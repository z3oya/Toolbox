using System.Windows;
using System.Windows.Media;

namespace Toolbox.Tools.SerialAssistant;

/// <summary>Themed replacement for MessageBox: the system dialog cannot pick up the
/// app styles. A severity dot (blue/amber/red) carries the MessageBoxImage role.</summary>
public partial class MessageWindow : Window
{
    public string Message { get; set; } = "";
    public MessageBoxImage Severity { get; set; } = MessageBoxImage.None;

    public MessageWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MessageText.Text = Message;
            string brushKey = Severity switch
            {
                MessageBoxImage.Error => "ErrorBrush",
                MessageBoxImage.Warning => "WarningBrush",
                _ => "AccentBrush",
            };
            Dot.Fill = TryFindResource(brushKey) as Brush;
        };
    }
}
