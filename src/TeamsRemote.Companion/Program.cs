using System.Windows.Forms;
using TeamsRemote.Companion.UI;

namespace TeamsRemote.Companion;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new CompanionMainForm());
    }
}
