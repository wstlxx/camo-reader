using System;
using System.Windows.Forms;

namespace CamoReader
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // ApplicationConfiguration.Initialize is available in .NET 6+ to set up
            // default high DPI/window settings for WinForms apps.
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
