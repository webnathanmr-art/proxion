using System.Windows.Forms;
using Proxion.App.UI;

namespace Proxion.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var setupForm = new SetupForm();
        if (setupForm.ShowDialog() != DialogResult.OK || setupForm.Result is null)
        {
            return;
        }

        Application.Run(new TrayContext(setupForm.Result));
    }
}
