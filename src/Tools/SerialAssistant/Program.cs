using System.Text;

namespace Toolbox.Tools.SerialAssistant;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // GBK (cp936) for the encoding selector
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
