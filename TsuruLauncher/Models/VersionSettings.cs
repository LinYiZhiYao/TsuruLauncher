namespace TsuruLauncher.Models
{
    /// <summary>
    /// 版本设置配置
    /// </summary>
    public class VersionSettings
    {
        // 基本信息
        public string VersionId { get; set; } = "";
        public string CustomName { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconPath { get; set; } = "";
        public string Category { get; set; } = "";

        // 启动选项
        public bool VersionIsolation { get; set; } = true;
        public string WindowTitle { get; set; } = "";
        public string CustomInfo { get; set; } = "";
        public string JavaPath { get; set; } = "";

        // 内存设置
        public MemoryAllocation MemoryMode { get; set; } = MemoryAllocation.FollowGlobal;
        public int MinMemoryMB { get; set; } = 512;
        public int MaxMemoryMB { get; set; } = 2048;

        // 命令
        public string GameArgs { get; set; } = "";
        public string PreLaunchCommand { get; set; } = "";

        // 高级选项（以前只有界面没有行为，现在会保存并被 GameService / LaunchService 读取）
        /// <summary>内存优化策略：0=跟随全局 1=开启 2=关闭。</summary>
        public int MemoryOptimizeIndex { get; set; } = 0;
        public bool DisableModUpdate { get; set; } = false;
        public bool IgnoreJavaCompatibility { get; set; } = false;
        public bool DisableFileVerify { get; set; } = false;
        public bool DisableLaunchWrapper { get; set; } = false;
        public string PostExitCommand { get; set; } = "";
    }

    public enum MemoryAllocation
    {
        FollowGlobal,   // 跟随全局设置
        Auto,           // 自动配置
        Custom          // 自定义
    }
}