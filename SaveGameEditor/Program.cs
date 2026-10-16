using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyVersion("2.30.0.0")]
[assembly: AssemblyFileVersion("2.30.0.0")]

namespace CM3SaveGameEditor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // "--patcher" / "--competitions": open at the patcher or the Competition Editor (used when restarting as administrator)
            int mode = 0;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--patcher", StringComparison.OrdinalIgnoreCase))
                    mode = 1;
                else if (string.Equals(arg, "--competitions", StringComparison.OrdinalIgnoreCase))
                    mode = 4;
            }
            Application.Run(new MainForm(mode));
        }
    }
}
