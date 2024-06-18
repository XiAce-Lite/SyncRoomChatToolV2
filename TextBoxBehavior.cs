using System.Windows;
using System.Windows.Controls;

namespace SyncRoomChatToolV2
{
    /// <summary>
    /// TextBox 添付ビヘイビア
    /// </summary>
    public class TextBoxBehaviors
    {
        /// <summary>
        /// 複数行のテキストを扱う
        /// テキスト追加時に最終行が表示されるようにする
        /// </summary>
        public static readonly DependencyProperty AutoScrollToEndProperty =
                    DependencyProperty.RegisterAttached(
                        "AutoScrollToEnd", typeof(bool),
                        typeof(TextBoxBehaviors),
                        new UIPropertyMetadata(false, IsTextChanged)
                    );

        [AttachedPropertyBrowsableForType(typeof(TextBox))]
        public static bool GetAutoScrollToEnd(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoScrollToEndProperty);
        }

        public static void SetAutoScrollToEnd(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoScrollToEndProperty, value);
        }

        private static void IsTextChanged
            (DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // イベントを登録・削除 
            textBox.TextChanged -= OnTextChanged;

            Console.Write("-");

            var newValue = (bool)e.NewValue;
            if (newValue)
            {
                textBox.TextChanged += OnTextChanged;
            }
        }

        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (string.IsNullOrEmpty(textBox.Text))
            {
                return;
            }

            textBox.ScrollToEnd();
            Console.Write("*");
        }
    }
}
