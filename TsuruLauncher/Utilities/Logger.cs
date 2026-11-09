using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace TsuruLauncher.Utilities
{
    /// <summary>
    /// 文件日志。
    ///
    /// <para><b>为什么是异步的</b>：原实现每次 <c>Log()</c> 都在**调用线程**（也就是 UI 线程）上
    /// <c>File.AppendAllText</c> —— 打开句柄 + 定位到末尾 + 写入 + 关闭，一次 0.3~2ms，
    /// 遇到杀软扫描/磁盘压力还会飙到 10~30ms。而一次「打开实验室工具」就要写 7~8 行日志，
    /// 落点正好散在点击帧与 240/260/320ms 那几个定时器上 —— 实测的慢帧位置与之完全吻合。
    /// 现在 <c>Log()</c> 只做入队，写盘交给一条后台线程（保持句柄常开 + AutoFlush）。</para>
    ///
    /// <para><b>可见性</b>：<c>AutoFlush = true</c> 让每行写完就 Flush 到操作系统，
    /// 所以即使进程被强杀（<c>taskkill /F</c>），已经入队并落盘的行依然能被外部读取 ——
    /// 自动化脚本读日志不受影响。最坏情况只丢最后 ~250ms 内还没被后台线程取走的行。</para>
    /// </summary>
    public static class Logger
    {
        /// <summary>
        /// 日志落点：优先程序目录（便携版）；**写不进去就退回 %APPDATA%\TsuruLauncher**。
        ///
        /// ⚠ 为什么必须做这个回退：安装包默认装到 <c>C:\Program Files\</c>，
        /// 普通用户对它**没有写权限**，原来写死程序目录的结果是——
        /// 安装版一个日志文件都不会产生，所有异常被静默吞掉，出问题完全没法查。
        /// </summary>
        private static readonly string LogPath = ResolveLogPath();

        private static string ResolveLogPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                // 探测可写：建一个马上删掉的空文件，不留下痕迹
                string probe = Path.Combine(exeDir, ".write_probe");
                using (new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                { }
                return Path.Combine(exeDir, "TsuruLauncher.log");
            }
            catch
            {
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsuruLauncher");
                    Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "TsuruLauncher.log");
                }
                catch
                {
                    return Path.Combine(Path.GetTempPath(), "TsuruLauncher.log");
                }
            }
        }

        /// <summary>日志所在目录（轮转清理用，不能再用 BaseDirectory —— 可能已回退到 %APPDATA%）。</summary>
        private static string LogDir => Path.GetDirectoryName(LogPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        private static readonly object _lock = new object();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024;

        /// <summary>待写入的行（含行尾换行符）。</summary>
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

        /// <summary>唤醒后台写线程。</summary>
        private static readonly AutoResetEvent _signal = new AutoResetEvent(false);

        private static Thread? _worker;
        private static volatile bool _stop;

        public static void Initialize()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath))
                    {
                        var info = new FileInfo(LogPath);
                        if (info.Length > MaxLogSizeBytes)
                        {
                            string archivePath = LogPath.Replace(".log", $".{DateTime.Now:yyyyMMdd_HHmmss}.log");
                            File.Move(LogPath, archivePath);

                            int maxArchives = 3;
                            var archives = Directory.GetFiles(LogDir, "TsuruLauncher.*.log")
                                .OrderByDescending(f => f).Skip(maxArchives).ToList();
                            foreach (var old in archives)
                            {
                                try { File.Delete(old); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            EnsureWorker();
        }

        /// <summary>启动后台写线程（幂等）。</summary>
        private static void EnsureWorker()
        {
            if (_worker != null) return;
            lock (_lock)
            {
                if (_worker != null) return;
                _stop = false;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "TsuruLogWriter",
                    Priority = ThreadPriority.BelowNormal
                };
                _worker.Start();
            }
        }

        /// <summary>运行期每隔这么多「写入字符数」才去 stat 一次文件大小（避免每行都 stat）。</summary>
        private const long RuntimeCheckBytes = 4 * 1024 * 1024;

        private static StreamWriter? TryOpenLog()
        {
            try
            {
                var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                return new StreamWriter(fs) { AutoFlush = true };
            }
            catch { return null; }
        }

        private static void PruneArchives()
        {
            try
            {
                const int maxArchives = 3;
                var archives = Directory.GetFiles(LogDir, "TsuruLauncher.*.log")
                    .OrderByDescending(f => f).Skip(maxArchives).ToList();
                foreach (var old in archives)
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// 运行期轮转：超过上限就把当前日志改名归档，然后重开一个。
        ///
        /// <para>⚠ 原来只有 <see cref="Initialize"/> 里检查一次 —— 一个不关的会话会无限增长。
        /// 实测：连开 3 天的日志 61.7 MB，历史归档里有 81 MB 和 95 MB 的，而常量写的是 5 MB。
        /// 一旦出现刷屏级别的异常（曾经有个 XAML bug 一下报了 6431 次），几百 MB 很快就写出来了。</para>
        /// </summary>
        private static bool RotateLogIfTooBig(ref StreamWriter? writer)
        {
            try
            {
                long len;
                try { len = writer?.BaseStream?.Length ?? 0; }
                catch { return false; }
                if (len < MaxLogSizeBytes) return false;

                try { writer?.Flush(); writer?.Dispose(); } catch { }
                writer = null;

                string archivePath = LogPath.Replace(".log", $".{DateTime.Now:yyyyMMdd_HHmmss}.log");
                try { File.Move(LogPath, archivePath); } catch { }
                PruneArchives();

                writer = TryOpenLog();
                return true;
            }
            catch
            {
                writer = TryOpenLog();
                return false;
            }
        }

        private static void WorkerLoop()
        {
            StreamWriter? writer = null;
            long writtenSinceCheck = 0;

            writer = TryOpenLog();

            while (!_stop)
            {
                try { _signal.WaitOne(250); } catch { }
                if (writer == null) writer = TryOpenLog();
                writtenSinceCheck += Drain(writer);

                if (writtenSinceCheck >= RuntimeCheckBytes)
                {
                    writtenSinceCheck = 0;
                    RotateLogIfTooBig(ref writer);
                }
            }

            Drain(writer);
            try { writer?.Flush(); writer?.Dispose(); } catch { }
        }

        private static long Drain(StreamWriter? writer)
        {
            long bytes = 0;
            while (_queue.TryDequeue(out var line))
            {
                try { writer?.Write(line); bytes += line.Length + 1; } catch { /* 尽力而为 */ }
            }
            return bytes;
        }

        public static void Log(string message, string type = "INFO")
        {
            try
            {
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] {message}{Environment.NewLine}";
                EnsureWorker();
                _queue.Enqueue(logLine);
                _signal.Set();
            }
            catch { /* Best effort logging */ }
        }

        /// <summary>把队列里剩下的行写出去（退出前调用；平时不需要）。</summary>
        public static void Flush()
        {
            try { _signal.Set(); } catch { }
        }

        /// <summary>停止后台写线程并做最后一次落盘。</summary>
        public static void Shutdown()
        {
            try
            {
                _stop = true;
                try { _signal.Set(); } catch { }
                try { _worker?.Join(TimeSpan.FromMilliseconds(800)); } catch { }
                _worker = null;
            }
            catch { }
        }

        public static void LogError(Exception ex, string context)
        {
            Log($"[{context}] {ex.Message}\nStackTrace: {ex.StackTrace}", "ERROR");
            if (ex.InnerException != null)
            {
                Log($"InnerException: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}", "ERROR-INNER");
            }
        }

        public static void LogInfo(string message)
        {
            Log(message, "INFO");
        }

        public static void LogWarning(string message)
        {
            Log(message, "WARNING");
        }

        public static void LogDebug(string message)
        {
            Log(message, "DEBUG");
        }

        public static void LogCritical(string message)
        {
            Log(message, "CRITICAL");
        }

        public static void LogGameOutput(string message)
        {
            Log(message, "GAME-OUTPUT");
        }

        public static void LogGameError(string message)
        {
            Log(message, "GAME-ERROR");
        }

        public static void ClearLog()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath))
                    {
                        File.WriteAllText(LogPath, $"--- Log Cleared at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\n");
                    }
                }
            }
            catch { /* Best effort */ }
        }

        public static string GetLogPath()
        {
            return LogPath;
        }
    }
}
