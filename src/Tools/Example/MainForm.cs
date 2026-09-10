namespace Toolbox.Tools.Example;

public partial class MainForm : Form
{
    public MainForm()
    {
        Text = "Example (WinForms)";
        ClientSize = new Size(420, 300);

        Controls.Add(new ExampleTool
        {
            Dock = DockStyle.Fill,
        });
    }
}
