namespace TsuruLauncher.Quiz.Views
{
    /// <summary>页面每次被切回来时刷新数据（统计、错题本这类需要实时更新的）</summary>
    public interface IRefreshable
    {
        void Refresh();
    }
}
