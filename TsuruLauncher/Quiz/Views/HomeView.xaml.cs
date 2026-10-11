using System.Windows;
using System.Windows.Controls;

namespace TsuruLauncher.Quiz.Views
{
    public partial class HomeView : UserControl, IRefreshable
    {
        public HomeView()
        {
            InitializeComponent();
            Refresh();
        }

        public void Refresh()
        {
            StatDone.Text = Services.ProgressService.DoneCount.ToString();
            StatAcc.Text = Services.ProgressService.Accuracy.ToString("0.0") + "%";
            StatWrong.Text = Services.ProgressService.WrongCount.ToString();
            BankInfo.Text =
                $"{Services.BankService.StatusMessage}\n" +
                $"其中真题（官方样题 + 真题汇编）：{Services.BankService.RealChoiceCount} 道选择题 / " +
                $"{Services.BankService.RealCodeCount} 道操作题，其余为个人精编题，刷题页可勾「仅真题」过滤。";
        }

        private void GoChoice(object sender, RoutedEventArgs e) => GoTo(1);
        private void GoCode(object sender, RoutedEventArgs e) => GoTo(2);
        private void GoExam(object sender, RoutedEventArgs e) => GoTo(3);
        private void GoWrong(object sender, RoutedEventArgs e) => GoTo(4);

        private static void GoTo(int index) => QuizHost.Navigate(index);
    }
}
