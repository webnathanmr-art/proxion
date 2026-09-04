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

        using var disclaimer = new DisclaimerForm();
        if (disclaimer.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        try
        {
            Application.Run(new PersonalTrayContext());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Proxion failed to start:{Environment.NewLine}{ex.Message}",
                "Proxion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
