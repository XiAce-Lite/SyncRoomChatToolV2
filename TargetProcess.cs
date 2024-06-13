using System.Diagnostics;
using System.Windows;

namespace SyncRoomChatToolV2
{
    public class TargetProcess
    {
        private readonly Process? targetProcess;
        public bool IsAlive = true;
        public int Id = 0;
        public Process? Proc;
        public IntPtr Handle;
        public IntPtr MainWindowHandle;

        public TargetProcess(string pName)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(pName);
                foreach (Process p in ps)
                {
                    targetProcess = p;
                    break;
                }

                if (targetProcess == null)
                {
                    IsAlive = false;
                    return;
                }

                Id = targetProcess.Id;
                MainWindowHandle = targetProcess.MainWindowHandle;
                Proc = targetProcess;
                Handle = targetProcess.Handle;
            }
            catch (Exception ex)
            {
                string errMsg = $"エラーが発生しています。{ex.Message}";
                MessageBox.Show(errMsg);
                Application.Current.Shutdown();
            }
        }
    }
}
