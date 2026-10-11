using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TsuruLauncher.Services
{
    /// <summary>一个本机 AI 命令行工具的静态描述（和「桌面 HTA 版 AI 工具管理器」一一对应）。</summary>
    public sealed class AiToolDefinition
    {
        public AiToolDefinition(string key, string name, string description, string package,
                                string kind, string cmdName, string args)
        {
            Key = key;
            Name = name;
            Description = description;
            Package = package;
            Kind = kind;
            CmdName = cmdName;
            Args = args;
        }

        public string Key { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>npm 包名（dsh 类也留着，方便显示，但不参与 npm 查询）。</summary>
        public string Package { get; }

        /// <summary>"npm" 或 "dsh"。</summary>
        public string Kind { get; }

        /// <summary>npm 全局根下的入口文件名，例如 openclaw.cmd / claude.cmd。</summary>
        public string CmdName { get; }

        /// <summary>启动参数，例如 "gateway --port 18789"、""、"web"。</summary>
        public string Args { get; }

        public bool IsNpm => Kind == "npm";
    }

    /// <summary>
    /// 运行时状态，供 UI 直接绑定。实现 <see cref="INotifyPropertyChanged"/>：
    /// 检测 / 查更新 / 安装全部在后台线程完成，结果回填后必须能把界面刷新出来。
    /// </summary>
    public sealed class AiToolState : INotifyPropertyChanged
    {
        public AiToolState(AiToolDefinition definition)
        {
            Definition = definition;
            Key = definition.Key;
            Name = definition.Name;
            Description = definition.Description;
        }

        public AiToolDefinition Definition { get; }

        public string Key { get; }
        public string Name { get; }
        public string Description { get; }

        private string _version = "";
        private string _latest = "";
        private string _entryPath = "";
        private bool _installed;
        private bool _checked;
        private bool _busy;

        /// <summary>本机已安装的版本号，空 = 未知 / 未安装。</summary>
        public string Version
        {
            get => _version;
            set { if (SetProperty(ref _version, value)) { Notify(nameof(HasUpdate)); Notify(nameof(StatusLine)); Notify(nameof(StatusDot)); } }
        }

        /// <summary>npm 上的最新版本号，空 = 未查询 / 查询失败。</summary>
        public string Latest
        {
            get => _latest;
            set { if (SetProperty(ref _latest, value)) { Notify(nameof(HasUpdate)); Notify(nameof(StatusLine)); Notify(nameof(StatusDot)); } }
        }

        /// <summary>启动入口（.cmd 或 .js 的完整路径）。</summary>
        public string EntryPath
        {
            get => _entryPath;
            set { if (SetProperty(ref _entryPath, value)) Notify(nameof(CanLaunch)); }
        }

        public bool Installed
        {
            get => _installed;
            set { if (SetProperty(ref _installed, value)) { Notify(nameof(HasUpdate)); Notify(nameof(StatusLine)); Notify(nameof(StatusDot)); Notify(nameof(CanLaunch)); Notify(nameof(CanInstall)); } }
        }

        /// <summary>是否已经查过最新版（联网那一步）。</summary>
        public bool Checked
        {
            get => _checked;
            set { if (SetProperty(ref _checked, value)) { Notify(nameof(HasUpdate)); Notify(nameof(StatusLine)); Notify(nameof(StatusDot)); Notify(nameof(CanInstall)); } }
        }

        /// <summary>该项正在忙（检测 / 安装 / 更新），按钮组禁用、状态点转「检测中」。</summary>
        public bool Busy
        {
            get => _busy;
            set { if (SetProperty(ref _busy, value)) { Notify(nameof(StatusLine)); Notify(nameof(StatusDot)); Notify(nameof(CanLaunch)); Notify(nameof(CanInstall)); } }
        }

        /// <summary>已安装且有更新（只有 npm 类才可能为 true）。</summary>
        public bool HasUpdate =>
            Installed && Checked && !string.IsNullOrEmpty(Latest) &&
            !string.Equals(Latest, Version, StringComparison.Ordinal);

        /// <summary>能否点「启动」：已安装且不在忙。</summary>
        public bool CanLaunch => Installed && !Busy;

        /// <summary>能否点「安装」：未安装且已经检测过且不在忙。</summary>
        public bool CanInstall => !Installed && Checked && !Busy;

        /// <summary>状态点颜色键："ok" | "missing" | "warn" | "checking"。</summary>
        public string StatusDot
        {
            get
            {
                if (Busy) return "checking";
                if (!Checked) return Installed ? "ok" : "missing";
                if (!Installed) return "missing";
                if (HasUpdate) return "warn";
                return "ok";
            }
        }

        /// <summary>卡片第二行文案：安装状态 + 版本 + 更新提示。</summary>
        public string StatusLine
        {
            get
            {
                if (Busy) return "检测中 …";
                if (!Installed) return Checked ? "未安装" : "未安装（点「检测」确认）";

                string ver = string.IsNullOrEmpty(Version) ? "?" : Version;
                string line = "已安装 · v" + ver;
                if (!Checked) return line + "   （未查更新）";

                if (!string.IsNullOrEmpty(Latest))
                {
                    if (HasUpdate) return line + "   → 可更新至 " + Latest;
                    return line + "   ✓ 已是最新";
                }
                return line + "   （无法查询更新）";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Notify(name);
            return true;
        }

        private void Notify(string? name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }

    /// <summary>
    /// 「AI 工具管理器」实验室工具的本机逻辑：
    /// 探测 OpenClaw / Claude Code / DeepSeek Harness 的安装状态、查最新版、安装 / 更新、启动、建桌面快捷方式。
    ///
    /// <para>数据落点和桌面 HTA 版完全一致（npm 全局根 %APPDATA%\npm、dsh 的 .dsh\profiles），
    /// 但进程调用改用 <see cref="Process"/> + WaitForExit 超时，不再用 HTA 那套「临时文件轮询」。</para>
    ///
    /// <para>所有方法都自带 try/catch，异常只作为返回值 / out 参数给出，绝不冒到 UI。</para>
    /// </summary>
    public static class AiToolService
    {
        /// <summary>管理的三个工具。</summary>
        public static readonly IReadOnlyList<AiToolDefinition> Tools = new List<AiToolDefinition>
        {
            new("openclaw", "OpenClaw", "常驻网关服务 (:18789)",
                "openclaw", "npm", "openclaw.cmd", "gateway --port 18789"),
            new("claude", "Claude Code", "命令行 AI 助手",
                "@anthropic-ai/claude-code", "npm", "claude.cmd", ""),
            new("dsh", "DeepSeek Harness", "Web 界面 (dsh web)",
                "@deepseek-ai/dsh", "dsh", "", "web"),
        };

        // ─────────────── 路径 ───────────────

        /// <summary>
        /// npm 全局根：%APPDATA%\npm。
        ///
        /// <para>⚠ 不能只用 <see cref="Environment.ExpandEnvironmentVariables"/>：如果进程环境里
        /// APPDATA 缺失 / 为空（沙箱、以服务方式启动、某些精简 shell 都会这样），
        /// ExpandEnvironmentVariables("%APPDATA%") 会原样返回 "%APPDATA%" 或空串，
        /// 拼出来的路径就成了相对路径 "npm"，探测必然全部落空 —— 表现就是「明明装了却显示未安装」。
        /// 这里改成优先取 .NET 的特殊文件夹 API（它不依赖环境变量），环境变量只作为兜底。</para>
        /// </summary>
        public static string NpmRoot()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = Environment.ExpandEnvironmentVariables("%APPDATA%");
            if (string.IsNullOrWhiteSpace(appData) || appData.Contains('%'))
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
            return Path.Combine(appData, "npm");
        }

        private static string DshBaseDir()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                userProfile = Environment.ExpandEnvironmentVariables("%USERPROFILE%");
            return Path.Combine(userProfile, ".dsh", "profiles", "node_modules", "@deepseek-ai", "dsh");
        }

        /// <summary>在 PATH 里找 node.exe，找不到返回 null。</summary>
        public static string? FindNodeExe()
        {
            try
            {
                // 先直接试 PATH 目录
                string[] exts = { ".exe", ".cmd", ".bat", "" };
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                             .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        foreach (var ext in exts)
                        {
                            var candidate = Path.Combine(dir.Trim(), "node" + ext);
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                    catch { /* 该目录不可访问 */ }
                }
            }
            catch { /* PATH 读不到 */ }

            // 兜底：where node
            var located = Where("node");
            return string.IsNullOrEmpty(located) ? null : located;
        }

        /// <summary>用 where 查命令位置（同步、瞬间完成，带超时）。</summary>
        private static string Where(string command)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe")
                {
                    Arguments = "/c where " + command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(5000))
                {
                    try { p.Kill(true); } catch { }
                    return "";
                }
                var line = (outTask.Result ?? string.Empty)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim() ?? "";
                return line.Contains('\\') || line.Contains('/') ? line : "";
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "AiToolService.Where");
                return "";
            }
        }

        // ─────────────── 探测 ───────────────

        /// <summary>
        /// 本地探测（不联网、不起 npm 进程），和 HTA 的 probe() 同一套判定。
        /// </summary>
        public static AiToolState Probe(AiToolDefinition t)
        {
            var state = new AiToolState(t);
            try
            {
                if (t.IsNpm)
                {
                    string npmRoot = NpmRoot();
                    string cmdPath = Path.Combine(npmRoot, t.CmdName);
                    string dir = Path.Combine(npmRoot, "node_modules", t.Package.Replace('/', '\\'));

                    if (File.Exists(cmdPath))
                    {
                        state.Installed = true;
                        state.EntryPath = cmdPath;
                    }
                    else if (Directory.Exists(dir))
                    {
                        state.Installed = true;
                        state.EntryPath = Where(Path.GetFileNameWithoutExtension(t.CmdName));
                    }

                    state.Version = ReadVersion(Path.Combine(dir, "package.json"));
                }
                else if (t.Kind == "dsh")
                {
                    string baseDir = DshBaseDir();
                    if (Directory.Exists(baseDir))
                    {
                        state.Installed = true;
                        state.EntryPath = Path.Combine(baseDir, "lib", "bin.js");
                        state.Version = ReadVersion(Path.Combine(baseDir, "package.json"));
                    }
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "AiToolService.Probe " + t.Key);
            }
            return state;
        }

        /// <summary>从 package.json 里正则读 "version": "x.y.z"（和 HTA 的 readVersion 一致）。</summary>
        private static string ReadVersion(string packageJsonPath)
        {
            try
            {
                if (!File.Exists(packageJsonPath)) return "";
                string text = File.ReadAllText(packageJsonPath);
                var m = Regex.Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch
            {
                return "";
            }
        }

        // ─────────────── 查最新版 ───────────────

        /// <summary>
        /// 跑 <c>npm view &lt;pkg&gt; version --silent</c>，只对 npm 类有效。
        /// 返回清洗后的版本号，失败 / 非 npm 返回 null。
        /// </summary>
        public static string? QueryLatestVersion(AiToolDefinition t, int timeoutMs = 20000)
        {
            if (!t.IsNpm) return null;
            try
            {
                var (exit, stdout, _) = RunCaptured("npm", "view " + t.Package + " version --silent", timeoutMs);
                if (exit != 0) return null;

                // HTA: raw.replace(/[^0-9a-zA-Z.\-]/g, "")
                string cleaned = Regex.Replace(stdout ?? string.Empty, "[^0-9a-zA-Z.\\-]", "");
                return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "AiToolService.QueryLatestVersion " + t.Key);
                return null;
            }
        }

        // ─────────────── 安装 / 更新 ───────────────

        /// <summary>
        /// 安装或更新一个工具。耗时可能几分钟，调用方负责放到后台线程（Task.Run）。
        /// </summary>
        public static bool InstallOrUpdate(AiToolDefinition t, bool latest, out string log, int timeoutMs = 300000)
        {
            log = "";
            try
            {
                if (t.Kind == "dsh")
                {
                    log = "DeepSeek Harness 不是 npm 包，请用它自己的方式更新（HTA 版同样不支持自动更新）。";
                    return false;
                }

                string args = "install -g " + t.Package + (latest ? "@latest" : "") + " --no-fund --no-audit";
                var (exit, stdout, stderr) = RunCaptured("npm", args, timeoutMs);
                log = ((stdout ?? "") + "\n" + (stderr ?? "")).Trim();
                return exit == 0;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "AiToolService.InstallOrUpdate " + t.Key);
                log = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 统一的「起进程 + 重定向 + 超时 Kill」封装，照 PythonRunner.Run 的写法。
        /// 返回 (exitCode, stdout, stderr)；超时返回 exitCode = -1。
        /// </summary>
        private static (int Exit, string Stdout, string Stderr) RunCaptured(string exe, string arguments, int timeoutMs)
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                // 走 cmd /c 是为了让 npm.cmd 这类批处理能被解析到
                Arguments = "/c " + exe + " " + arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "启动进程失败");

            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, so.Result ?? "", "命令超时（>" + timeoutMs / 1000 + "s）");
            }
            return (p.ExitCode, so.Result ?? "", se.Result ?? "");
        }

        // ─────────────── 启动 ───────────────

        /// <summary>用 cmd /k 启动（窗口留着看输出），不重定向，否则看不到控制台。</summary>
        public static void Launch(AiToolDefinition t, AiToolState state)
        {
            try
            {
                string entry = state.EntryPath;
                if (string.IsNullOrEmpty(entry) && t.IsNpm)
                    entry = Path.Combine(NpmRoot(), t.CmdName);

                if (string.IsNullOrEmpty(entry))
                {
                    Utilities.Logger.LogWarning("[AiTools] 找不到 " + t.Name + " 的启动入口");
                    return;
                }

                string inner = t.Kind == "dsh"
                    ? "node \"" + entry + "\"" + (string.IsNullOrEmpty(t.Args) ? "" : " " + t.Args)
                    : "\"" + entry + "\"" + (string.IsNullOrEmpty(t.Args) ? "" : " " + t.Args);

                var psi = new ProcessStartInfo("cmd.exe")
                {
                    Arguments = "/k \"" + inner + "\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                };
                Process.Start(psi);
                Utilities.Logger.LogInfo("[AiTools] 已启动 " + t.Name);
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "AiToolService.Launch " + t.Key);
            }
        }

        // ─────────────── 桌面快捷方式 ───────────────

        /// <summary>
        /// 在桌面给每个已安装的工具建一个 .lnk。
        ///
        /// <para>⚠ 走过的两条弯路，都记在这里免得以后再踩：
        /// ① 用 <c>Type.InvokeMember(..., BindingFlags.SetProperty, ...)</c> 晚期绑定 WScript.Shell
        /// 的 CreateShortcut 对象设属性 → 抛 <c>DISP_E_MEMBERNOTFOUND (0x80020003)「找不到成员」</c>。
        /// 那个 shortcut 对象走自定义 IDispatch，.NET 反射拿不到它的属性 setter。
        /// ② 改成生成临时 VBScript 交给 cscript 执行 → 运行时策略把 cscript 当 LOLBin 拦掉，
        /// 不安全也不可移植。
        /// 最终用标准的 <see cref="IShellLink"/> COM 接口（shell32 自带，不依赖 WSH、不起子进程）。</para>
        /// </summary>
        public static string CreateDesktopShortcuts(IReadOnlyList<AiToolState> states)
        {
            int made = 0;
            var skipped = new List<string>();

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                return "创建失败：找不到桌面目录。";

            foreach (var s in states)
            {
                var t = s.Definition;
                if (!s.Installed) { skipped.Add(t.Name + "（未安装）"); continue; }

                string entry = s.EntryPath;
                if (string.IsNullOrEmpty(entry) && t.IsNpm)
                    entry = Path.Combine(NpmRoot(), t.CmdName);

                if (string.IsNullOrEmpty(entry) || !File.Exists(entry))
                {
                    skipped.Add(t.Name + "（找不到入口）");
                    continue;
                }

                string args = "/k \"" + entry + "\"" + (string.IsNullOrEmpty(t.Args) ? "" : " " + t.Args);

                try
                {
                    string lnkPath = Path.Combine(desktop, t.Name + ".lnk");
                    if (TryCreateShortcut(lnkPath, "cmd.exe", args, desktop, t.Name + " - " + t.Description))
                        made++;
                    else
                        skipped.Add(t.Name + "（写入失败）");
                }
                catch (Exception ex)
                {
                    Utilities.Logger.LogError(ex, "AiToolService.CreateDesktopShortcuts " + t.Key);
                    skipped.Add(t.Name + "（写入失败）");
                }
            }

            if (made == 0 && skipped.Count == 0) return "没有可创建快捷方式的工具";

            string msg = "已创建 " + made + " 个桌面快捷方式";
            if (skipped.Count > 0) msg += " · 跳过：" + string.Join("、", skipped);
            return msg;
        }

        /// <summary>用 shell32 的 IShellLink + IPersistFile 写一个 .lnk（标准做法，不依赖 WSH）。</summary>
        private static bool TryCreateShortcut(string lnkPath, string target, string arguments, string workingDir, string description)
        {
            object? linkObj = null;
            object? persistObj = null;
            try
            {
                var linkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")); // ShellLink coclass
                if (linkType == null) return false;
                linkObj = Activator.CreateInstance(linkType);
                if (linkObj is not ShellLink.IShellLinkW link) return false;

                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description);
                link.SetIconLocation("cmd.exe", 0);

                persistObj = linkObj;
                if (persistObj is not ShellLink.IPersistFile pf) return false;
                pf.Save(lnkPath, true);
                return File.Exists(lnkPath);
            }
            finally
            {
                if (persistObj != null && System.Runtime.InteropServices.Marshal.IsComObject(persistObj))
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(persistObj); } catch { }
                if (linkObj != null && System.Runtime.InteropServices.Marshal.IsComObject(linkObj))
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(linkObj); } catch { }
            }
        }
    }

    /// <summary>
    /// shell32 的 IShellLink / IPersistFile（最小子集），只为了写 .lnk 文件。
    /// 用 COM 接口而不是 WSH 晚期绑定：接口是编译期检查的，不会被运行时策略拦，也不起子进程。
    /// </summary>
    internal static class ShellLink
    {
        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("000214F9-0000-0000-C000-000000000046")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLinkW
        {
            void GetPath([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
            void SetDescription([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
            void SetWorkingDirectory([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
            void SetArguments([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([System.Runtime.InteropServices.Out, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszFile);
        }

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("0000010b-0000-0000-C000-000000000046")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string? pszFileName, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
