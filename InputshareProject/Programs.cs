using System;
using InputshareLib;
using System.Windows.Forms;
using System.IO;

namespace Inputshare
{
    class Program
    {

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                if (!Directory.Exists(Environment.CurrentDirectory + "\\logs"))
                {
                    Directory.CreateDirectory(Environment.CurrentDirectory + "\\logs");
                }
            }
            catch (Exception ex) { }

            ISLogger.SetLogFileName(@".\logs\Inputshare.log");
            ISLogger.EnableConsole = false;
            ISLogger.EnableLogFile = true;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private void ReadWinCopyFile(string[] args)
        {

        }
    }
}
