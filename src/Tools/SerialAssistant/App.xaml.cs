using System.Text;
using System.Windows;

namespace Toolbox.Tools.SerialAssistant;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Must run before base.OnStartup creates the StartupUri window: the encoding
        // selector offers GBK (cp936), which needs the CodePages provider registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
    }
}
