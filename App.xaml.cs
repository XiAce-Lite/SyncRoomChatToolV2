using System.Windows;
using SyncRoomChatToolV2.Properties;

namespace SyncRoomChatToolV2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Length > 0)
            {
                foreach (var item in e.Args) {
                    if (item != null) { 
                        if (item == "/demo")
                        {
                            Settings.Default.DemoMode = true;
                            Settings.Default.Save();
                            break;
                        }
                    }
                }
            }
        }
    }
}
