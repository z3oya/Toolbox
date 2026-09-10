using System.Windows;
using System.Windows.Media;
using Toolbox.Core;
using Toolbox.Ui.WPF;

namespace ExampleWPF;

public partial class MainWindow : Window
{
    private readonly ClickCounter _counter = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        var count = _counter.Increment();
        Title = $"Clicked {count} time(s).";
        Circle.CircleColor = count % 2 == 0
            ? Brushes.DodgerBlue
            : Brushes.OrangeRed;
    }
}
