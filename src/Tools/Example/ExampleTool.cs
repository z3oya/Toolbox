using Svg;
using Toolbox.WinForms;

namespace Toolbox.Tools.Example;

/// <summary>
/// Example tool hosted by the Toolbox main window.
/// </summary>
public partial class ExampleTool : UserControl
{
    private readonly Label _label;
    private readonly Button _button;
    private readonly SvgControl _circle;
    private int _clickCount;

    public ExampleTool()
    {
        _label = new Label
        {
            Text = "Example tool - click the button.",
            AutoSize = true,
            Location = new Point(12, 12),
        };

        _button = new Button
        {
            Text = "Click me",
            AutoSize = true,
            Location = new Point(12, 44),
        };

        _circle = new SvgControl
        {
            Location = new Point(12, 84),
            Size = new Size(128, 128),
        };
        _circle.LoadSvg(Path.Combine(AppContext.BaseDirectory, "Assets", "circle.svg"));

        _button.Click += (sender, e) =>
        {
            _clickCount++;
            _label.Text = $"Clicked {_clickCount} time(s).";
            SetCircleColor(_clickCount % 2 == 0
                ? Color.DodgerBlue
                : Color.OrangeRed);
        };

        Controls.Add(_label);
        Controls.Add(_button);
        Controls.Add(_circle);
    }

    private void SetCircleColor(Color color)
    {
        var circle = _circle.Document?.GetElementById<SvgCircle>("circle");
        if (circle is null)
        {
            return;
        }

        circle.Fill = new SvgColourServer(color);
        _circle.RefreshSvg();
    }
}
