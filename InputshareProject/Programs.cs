using System;
using InputshareLib;
using System.Windows.Forms;
using System.IO;
using System.Threading;

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
            catch (Exception) { }


            ISLogger.SetLogFileName(@".\logs\Inputshare.log");
            ISLogger.EnableConsole = false;
            ISLogger.EnableLogFile = true;
            ISLogger.EnableDebugLog = true;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
