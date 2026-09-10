namespace Toolbox.Tools.Example;

/// <summary>
/// Example tool hosted by the Toolbox main window.
/// </summary>
public partial class ExampleTool : UserControl
{
    private readonly Label _label;
    private readonly Button _button;
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
        _button.Click += (sender, e) =>
        {
            _clickCount++;
            _label.Text = $"Clicked {_clickCount} time(s).";
        };

        Controls.Add(_label);
        Controls.Add(_button);
    }
}
