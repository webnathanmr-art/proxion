using System.Windows.Forms;
using Proxion.Personal.UI;

namespace Proxion.Personal;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        try
        {
            Application.Run(new PersonalAppContext());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Proxion failed to start:{Environment.NewLine}{ex.Message}",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
