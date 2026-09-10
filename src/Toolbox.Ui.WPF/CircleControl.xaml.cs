using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Toolbox.Ui.WPF;

/// <summary>
/// A minimal WPF vector circle control with a bindable fill color.
/// </summary>
public partial class CircleControl : UserControl
{
    public static readonly DependencyProperty CircleColorProperty =
        DependencyProperty.Register(
            nameof(CircleColor),
            typeof(Brush),
            typeof(CircleControl),
            new PropertyMetadata(Brushes.DodgerBlue, OnCircleColorChanged));

    public CircleControl()
    {
        InitializeComponent();
    }

    public Brush CircleColor
    {
        get => (Brush)GetValue(CircleColorProperty);
        set => SetValue(CircleColorProperty, value);
    }

    private static void OnCircleColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CircleControl)d;
        control.PART_Ellipse.Fill = (Brush)e.NewValue;
    }
}
