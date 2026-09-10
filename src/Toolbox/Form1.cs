namespace Toolbox;

public partial class Form1 : Form
{
    public Form1()
    {
        Text = "Toolbox";
        ClientSize = new Size(800, 450);

        var exampleTool = new Toolbox.Tools.Example.ExampleTool
        {
            Dock = DockStyle.Fill,
        };

        Controls.Add(exampleTool);
    }
}
