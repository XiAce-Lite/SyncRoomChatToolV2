using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncRoomChatToolV2
{
    public class Tools
    {
        /// <summary>
        /// 指定された文字列を含むウィンドウタイトルを持つプロセスを取得します。
        /// </summary>
        /// <param name="windowTitle">ウィンドウタイトルに含む文字列。</param>
        /// <returns>該当するプロセスの配列。</returns>
        public static Process[] GetProcessesByWindowTitle(string windowTitle)
        {
            System.Collections.ArrayList list = [];

            //すべてのプロセスを列挙する
            foreach (Process p in Process.GetProcesses())
            {
                //指定された文字列がメインウィンドウのタイトルに含まれているか調べる
                if (0 <= p.MainWindowTitle.IndexOf(windowTitle))
                {
                    //含まれていたら、コレクションに追加
                    list.Add(p);
                }
            }

            //コレクションを配列にして返す
            return (Process[])list.ToArray(typeof(Process));
        }

        public static void OpenUrl(string url)
        {
            ProcessStartInfo pi = new()
            {
                FileName = url,
                UseShellExecute = true,
            };

            Process.Start(pi);
        }
    }
}
