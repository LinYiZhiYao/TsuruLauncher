using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TsuruLauncher.Quiz.Services
{
    public class RunResult
    {
        public bool Success { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";
        public string Error { get; set; } = "";
        public bool TimedOut { get; set; }
    }

    /// <summary>
    /// 调用本机 Python 解释器执行代码，用来给操作题判题。
    /// </summary>
    public static class PythonRunner
    {
        public static string? InterpreterPath { get; set; }

        /// <summary>按优先级探测可用的 Python 解释器</summary>
        public static string? Detect()
        {
            if (!string.IsNullOrWhiteSpace(InterpreterPath) && File.Exists(InterpreterPath))
                return InterpreterPath;

            var candidates = new[]
            {
                "py",
                "python",
                "python3",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Python"),
            };

            foreach (var c in candidates)
            {
                var found = Probe(c);
                if (found != null) return found;
            }

            // 扫一遍常见安装目录，取版本号最高的
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } la
                    ? Path.Combine(la, "Programs", "Python") : null,
                @"C:\Python313", @"C:\Python312", @"C:\Python311",
            };
            string? best = null;
            foreach (var root in roots)
            {
                if (root == null || !Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var exe = Path.Combine(dir, "python.exe");
                    if (File.Exists(exe)) best = exe;
                }
            }
            return best;
        }

        private static string? Probe(string command)
        {
            try
            {
                var psi = new ProcessStartInfo(command)
                {
                    Arguments = "-c \"import sys;print(sys.version_info[0])\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                var outp = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                if (p.ExitCode == 0 && outp.StartsWith("3")) return command;
            }
            catch { /* 该候选不可用 */ }
            return null;
        }

        public static RunResult Run(string code, string? stdin = null, int timeoutMs = 8000)
        {
            var exe = Detect();
            if (exe == null)
                return new RunResult { Error = "没找到 Python 解释器，请在设置里指定 python.exe 路径" };

            var dir = Path.Combine(Path.GetTempPath(), "NcreQuizRun");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "answer_" + Guid.NewGuid().ToString("N")[..8] + ".py");
            // 统一按 UTF-8 写，并在文件头声明编码，避免中文注释炸掉
            File.WriteAllText(file, code, new UTF8Encoding(false));

            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = "-X utf8 \"" + file + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = stdin != null,
                    CreateNoWindow = true,
                    WorkingDirectory = dir,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                if (p == null) return new RunResult { Error = "启动 Python 失败" };

                if (stdin != null)
                {
                    p.StandardInput.Write(stdin);
                    p.StandardInput.Close();
                }

                var so = p.StandardOutput.ReadToEndAsync();
                var se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    return new RunResult { TimedOut = true, Error = "运行超时（可能是死循环或等待输入）" };
                }

                var r = new RunResult
                {
                    Stdout = so.Result,
                    Stderr = se.Result,
                    Success = p.ExitCode == 0,
                };
                if (!r.Success && string.IsNullOrWhiteSpace(r.Error))
                    r.Error = "退出码 " + p.ExitCode;
                return r;
            }
            catch (Exception ex)
            {
                return new RunResult { Error = ex.Message };
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        /// <summary>比对输出：忽略行尾空格与末尾空行，忽略大小写差异之外的格式噪声</summary>
        public static bool OutputMatches(string actual, string expected)
        {
            static string Norm(string s)
            {
                var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var sb = new StringBuilder();
                foreach (var l in lines) sb.Append(l.TrimEnd()).Append('\n');
                return sb.ToString().Trim();
            }
            return string.Equals(Norm(actual), Norm(expected), StringComparison.Ordinal);
        }
    }
}
