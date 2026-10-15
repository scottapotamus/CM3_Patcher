using System;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // "--patcher": open at the patcher (used when restarting as administrator)
            bool patcher = false;
            foreach (string arg in args)
                if (string.Equals(arg, "--patcher", StringComparison.OrdinalIgnoreCase))
                    patcher = true;
            Application.Run(new MainForm(patcher ? 1 : 0));
        }
    }
}
