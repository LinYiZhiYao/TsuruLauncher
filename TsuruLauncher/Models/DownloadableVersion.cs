using System;

namespace TsuruLauncher.Models
{
    public class DownloadableVersion
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "release";
        public string Url { get; set; } = string.Empty;
        public DateTime ReleaseTime { get; set; }
        public DateTime Time { get; set; }
        
        // Helper property for UI display
        public string DisplayType => Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照版",
            "old_beta" => "远古测试版",
            "old_alpha" => "远古Alpha",
            _ => Type
        };

        public bool IsRelease => Type == "release";

        /// <summary>
        /// ⚠ 必须重写 ToString()。
        ///
        /// <para>版本下拉框用的是自绘 ComboBox 模板（ContentPresenter 绑 SelectionBoxItem）。
        /// 只要那条链路有一处没把 ContentTemplate 传下去，WPF 就退回 ToString()，
        /// 选择框里就会显示 <c>TsuruLauncher.Models.DownloadableVersion</c> —— 实测就是这样。
        /// 光靠 XAML 的 DisplayMemberPath 兜不住，重写 ToString 才是稳的。</para>
        /// </summary>
        public override string ToString() => Id;
    }

    public class VersionManifest
    {
        public DownloadableVersion? Latest { get; set; }
        public System.Collections.Generic.List<DownloadableVersion> Versions { get; set; } = new System.Collections.Generic.List<DownloadableVersion>();
    }
}