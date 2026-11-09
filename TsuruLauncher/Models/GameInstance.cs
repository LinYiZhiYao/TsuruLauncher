namespace TsuruLauncher.Models
{
    public class GameInstance
    {
        public string Id { get; set; } = string.Empty; // e.g. "1.16.5"
        public string Type { get; set; } = "release"; // release, snapshot, old_beta...
        public string MainClass { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty; // The .minecraft folder (libraries/assets)
        public string GameDir { get; set; } = string.Empty; // The working directory (mods/saves)
        public int RequiredJavaVersion { get; set; } = 8; // Default to 8 for older versions
        public string MinecraftArguments { get; set; } = string.Empty; // For old versions (<1.13)
        public string AssetIndexId { get; set; } = string.Empty; // e.g. "1.16" or "legacy"
        public string ClientJarUrl { get; set; } = string.Empty;
        public string ClientJarSha1 { get; set; } = string.Empty;
        public long FileSize { get; set; } = 0;
        
        public LogConfig? LogConfig { get; set; }

        public List<Library> Libraries { get; set; } = new List<Library>();
        public List<string> InheritsFrom { get; set; } = new List<string>(); // For Forge/Fabric
        
        // For 1.13+ arguments
        public List<Argument> GameArguments { get; set; } = new List<Argument>();
        public List<Argument> JvmArguments { get; set; } = new List<Argument>();
        
        public bool UseGlobalSettings { get; set; } = true;
        public int CustomMemoryMb { get; set; } = 4096;
        public int CustomMinMemoryMb { get; set; } = 512;
        public string CustomJavaPath { get; set; } = string.Empty;
        public string CustomJvmArgs { get; set; } = string.Empty;

        // ── 版本设置页里那些「以前只有界面、没有行为」的项 ──────────────
        // ⚠ 这几个属性之前**根本不存在**：VersionSettings 模型里有同名字段，
        //   但版本设置页保存的是 tsuru_profile.json，而读取方（本类和 LaunchService）
        //   完全不认识这些键 —— 于是用户在界面上改了也不生效。
        //   现在补进模型 + 读写 + 启动时真正使用。

        /// <summary>游戏窗口标题（空 = 跟随全局）。启动时拼成 <c>--title "..."</c>。</summary>
        public string WindowTitle { get; set; } = string.Empty;

        /// <summary>自定义信息（展示用，例如「和朋友联机专用」）。</summary>
        public string CustomInfo { get; set; } = string.Empty;

        /// <summary>附加游戏参数（会拼在启动命令尾部，空 = 无）。</summary>
        public string GameArgs { get; set; } = string.Empty;

        /// <summary>内存优化策略：0=跟随全局 1=开启 2=关闭。</summary>
        public int MemoryOptimize { get; set; } = 0;

        /// <summary>禁止更新 Mod。</summary>
        public bool DisableModUpdate { get; set; }

        /// <summary>忽略 Java 兼容性警告。</summary>
        public bool IgnoreJavaCompatibility { get; set; }

        /// <summary>关闭文件校验（跳过启动前的完整性检查）。</summary>
        public bool DisableFileVerify { get; set; }

        /// <summary>禁用 Java Launch Wrapper。</summary>
        public bool DisableLaunchWrapper { get; set; }
        /// <summary>启动游戏前执行的命令（空 = 不执行）。</summary>
        public string PreLaunchCommand { get; set; } = string.Empty;
    }

    public class Library
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty; // Relative path
        public string Url { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public bool IsNative { get; set; } = false;
    }

    public class Argument
    {
        public string Value { get; set; } = string.Empty; // The actual argument string
        // Simple rule representation for now. Real logic is more complex.
        public bool IsAllowed { get; set; } = true; 
    }

    public class LogConfig
    {
        public string Argument { get; set; } = string.Empty;
        public LogFile File { get; set; } = new LogFile();
        public string Type { get; set; } = string.Empty;
    }

    public class LogFile
    {
        public string Id { get; set; } = string.Empty;
        public string Sha1 { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}