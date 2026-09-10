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
        Circle.CircleColor = _counter.IsEven
            ? Brushes.DodgerBlue
            : Brushes.OrangeRed;
    }
}
