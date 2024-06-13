using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace SyncRoomChatToolV2
{
    public partial class UIAutomationLib
    {
        //readonly string ModuleName = "UIAutomationLib";

        [LibraryImport("user32.dll", EntryPoint = "FindWindowEx", StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        static extern bool SendMessage(IntPtr hWnd, uint Msg, int wParam, StringBuilder lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessage(int hWnd, int Msg, int wparam, int lparam);

        [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, string lParam);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(IntPtr hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const int WM_GETTEXT = 0x000D;
        private const int WM_GETTEXTLENGTH = 0x000E;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_RETURN = 0x0D;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        public static void SendReturn(IntPtr hWnd)
        {
            PostMessage(hWnd, WM_KEYDOWN, VK_RETURN, 0);
        }

        public static void SendMinimized(IntPtr hWnd)
        {
            PostMessage(hWnd, WM_SYSCOMMAND, SC_MINIMIZE, 0);
        }

        public static string GetControlText(IntPtr hWnd)
        {
            StringBuilder controlText = new();
            try
            {
                Int32 size = SendMessage(checked((int)hWnd), WM_GETTEXTLENGTH, 0, 0).ToInt32();
                if (size > 0)
                {
                    controlText = new StringBuilder(size + 1);
                    SendMessage(hWnd, (int)WM_GETTEXT, controlText.Capacity, controlText);
                }
            }
            catch (Exception ex)
            {
                string errMsg = $"エラーが発生しています in GetControlText {ex.Message}";
                MessageBox.Show(errMsg);
            }
            return controlText.ToString();
        }

        public static IntPtr FindHWndByCaptionAndProcessID(string windowTitle, int ProcessID)
        {
            IntPtr retIntPtr = IntPtr.Zero;
            int maxCount = 9999;
            int ct = 0;
            IntPtr prevChild = IntPtr.Zero;
            while (true && ct < maxCount)
            {
                IntPtr currChild = FindWindowEx(IntPtr.Zero, prevChild, null, null);
                if (currChild == IntPtr.Zero) break;
                if (IsWindowVisible(currChild))
                {
                    if (GetControlText(currChild).Contains(windowTitle))
                    {
                        _ = GetWindowThreadProcessId(currChild, out uint procID);
                        if (procID == ProcessID)
                        {
                            retIntPtr = currChild;
                            break;
                        }
                    }
                }
                prevChild = currChild;
                ++ct;
            }
            return retIntPtr;
        }

        //指定されたプロセスのMainFramに関するAutomationElementを取得
        public static AutomationElement GetMainFrameElement(Process p)
        {
            return AutomationElement.FromHandle(p.MainWindowHandle);
        }

        // Editエレメントを探して返す。
        public static AutomationElement GetEditElement(AutomationElement rootElement, string elementName)
        {
            AutomationElementCollection allElements = rootElement.FindAll(TreeScope.Element | TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            //見つからなかったら、渡されたルートエレメントを返す。
            AutomationElement returnElement = rootElement;

            foreach (AutomationElement el in allElements)
            {
                if (el.Current.Name.Contains(elementName))
                {
                    returnElement = el;
                    break;
                }
            }
            return returnElement;
        }

        // Buttonエレメントを探して返す。
        public static AutomationElement GetButtonElement(AutomationElement rootElement, string elementName)
        {
            AutomationElementCollection allElements = rootElement.FindAll(TreeScope.Element | TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            //見つからなかったら、渡されたルートエレメントを返す。
            AutomationElement returnElement = rootElement;

            foreach (AutomationElement el in allElements)
            {
                if (el.Current.Name.Contains(elementName))
                {
                    returnElement = el;
                    break;
                }
            }
            return returnElement;
        }

        public static InvokePattern? GetInvokePattern(AutomationElement targetControl)
        {
            InvokePattern? invokePattern;
            try
            {
                invokePattern = targetControl.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
            }
            // Object doesn't support the ValuePattern control pattern
            catch (InvalidOperationException)
            {
                return null;
            }

            return invokePattern;
        }

        public static ValuePattern? GetValuePattern(AutomationElement targetControl)
        {
            ValuePattern? valuePattern;
            try
            {
                valuePattern =
                    targetControl.GetCurrentPattern(
                    ValuePattern.Pattern)
                    as ValuePattern;
            }
            // Object doesn't support the ValuePattern control pattern
            catch (InvalidOperationException)
            {
                return null;
            }

            return valuePattern;
        }
    }
}
