using System.Windows;

namespace MetroPlanner
{
    public partial class StationDialog : Window
    {
        public string NameCn => NameCnBox.Text.Trim();
        public string NameEn => NameEnBox.Text.Trim();
        public string NameThird => NameThirdBox.Text.Trim();
        public string ThirdLabel => ThirdLabelBox.Text.Trim();
        public bool Transfer => TransferChk.IsChecked == true;
        public bool Saved { get; private set; }

        public StationDialog(string cn, string en, string third, string thirdLabel, bool transfer)
        {
            InitializeComponent();
            NameCnBox.Text = cn;
            NameEnBox.Text = en;
            NameThirdBox.Text = third;
            ThirdLabelBox.Text = thirdLabel;
            TransferChk.IsChecked = transfer;
            NameCnBox.Focus();
            NameCnBox.SelectAll();
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            Saved = true;
            DialogResult = true;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void AutoEn_Click(object sender, RoutedEventArgs e)
        {
            var cn = NameCnBox.Text.Trim();
            if (cn.Length == 0) return;
            NameEnBox.Text = PinyinHelper.ToEnglish(cn);
        }

        private void AutoZhuyin_Click(object sender, RoutedEventArgs e)
        {
            var cn = NameCnBox.Text.Trim();
            if (cn.Length == 0) return;
            NameThirdBox.Text = PinyinHelper.ToZhuyin(cn);
            if (string.IsNullOrEmpty(ThirdLabelBox.Text.Trim())) ThirdLabelBox.Text = "拼音";
        }

        // 手动为光标前的字母加声调
        private void ApplyToneToLastVowel(int tone)
        {
            var box = NameThirdBox;
            int caret = box.CaretIndex;
            var text = box.Text;
            if (caret <= 0 || caret > text.Length) { caret = text.Length; }
            // 找到光标前最近的元音字母
            int idx = caret - 1;
            while (idx >= 0)
            {
                char c = text[idx];
                if ("aeiouüAEIOUÜ".IndexOf(c) >= 0) break;
                // 跳过已带声调的字符
                if ("āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜ".IndexOf(c) >= 0) break;
                idx--;
            }
            if (idx < 0) return;
            char ch = text[idx];
            char marked = PinyinHelper.MarkVowelPublic(ch, tone);
            if (marked == ch) return;
            box.Text = text.Substring(0, idx) + marked + text.Substring(idx + 1);
            box.CaretIndex = idx + 1;
        }
        private void Tone1_Click(object sender, RoutedEventArgs e) => ApplyToneToLastVowel(1);
        private void Tone2_Click(object sender, RoutedEventArgs e) => ApplyToneToLastVowel(2);
        private void Tone3_Click(object sender, RoutedEventArgs e) => ApplyToneToLastVowel(3);
        private void Tone4_Click(object sender, RoutedEventArgs e) => ApplyToneToLastVowel(4);
        private void Umlaut_Click(object sender, RoutedEventArgs e)
        {
            var box = NameThirdBox;
            int caret = box.CaretIndex;
            var text = box.Text;
            if (caret < 0 || caret >= text.Length) { caret = text.Length; }
            // 把光标前的 u 替换为 ü
            int idx = caret - 1;
            while (idx >= 0 && (text[idx] == 'u' || text[idx] == 'U') == false) idx--;
            if (idx < 0) return;
            char c = text[idx];
            char rep = c == 'U' ? 'Ü' : 'ü';
            box.Text = text.Substring(0, idx) + rep + text.Substring(idx + 1);
            box.CaretIndex = idx + 1;
        }
    }
}
