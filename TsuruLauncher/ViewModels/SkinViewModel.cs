using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TsuruLauncher.Models;
using TsuruLauncher.Services;

namespace TsuruLauncher.ViewModels
{
    /// <summary>皮肤库里的一张皮肤。</summary>
    public partial class SkinEntry : ObservableObject
    {
        public SkinEntry(string path, string? displayName = null)
        {
            Path = path;
            Name = displayName ?? SkinService.DisplayName(path);
            Preview = SkinService.Load(path);
            IsSlim = Preview != null && SkinService.DetectSlim(Preview);
        }

        /// <summary>账号当前皮肤那种「没有本地文件」的条目。</summary>
        public SkinEntry(string name, BitmapImage? preview, bool isSlim)
        {
            Path = "";
            Name = name;
            Preview = preview;
            IsSlim = isSlim;
        }

        /// <summary>
        /// 是不是「账号当前正在用」的那张。
        /// ⚠ 必须是可观察属性：换完皮肤要能重新标记，不能只在构造时定死。
        /// </summary>
        [ObservableProperty]
        private bool _isCurrent;

        /// <summary>
        /// 是不是那个「＋ 添加皮肤 / 拖放」占位卡片。
        /// 做成集合里的一个条目，是为了让它**和皮肤卡片一起排在 WrapPanel 里**（常驻第一位）。
        /// </summary>
        public bool IsAddCard { get; init; }

        /// <summary>给 XAML 用的取反（没有现成的反向 BoolToVis 转换器）。</summary>
        public bool IsNotAddCard => !IsAddCard;

        /// <summary>「＋ 添加皮肤 / 拖放」占位卡片。</summary>
        public static SkinEntry AddCard() => new("添加皮肤", null, false) { IsAddCard = true };

        /// <summary>账号当前皮肤那张卡片（没有本地文件）。</summary>
        public static SkinEntry FromAccount(string name, BitmapImage? preview, bool isSlim)
        {
            var e = new SkinEntry(name, preview, isSlim);
            e.IsCurrent = true;
            return e;
        }

        /// <summary>
        /// 能不能上传到账号 —— 只要有本地文件就行。
        /// ⚠ 包括「账号皮肤历史」那些卡：它们也要能重新上传，用来把皮肤切回去。
        /// </summary>
        public bool CanApply => !string.IsNullOrEmpty(Path);

        public string Path { get; }
        public string Name { get; }
        public BitmapImage? Preview { get; }

        /// <summary>纤细（Alex）模型 —— 手臂 3 格宽，渲染时要跟着变。</summary>
        public bool IsSlim { get; }

        /// <summary>卡片缩略图边长（px）。渲染一次就缓存，取大一点免得放大糊。</summary>
        public const int ThumbnailSize = 192;

        /// <summary>缩略图的偏转角 —— 比预览区转得多一点，卡片上更看得出是个 3D 模型。</summary>
        public const double ThumbnailYaw = -25;

        private BitmapSource? _thumbnail;
        private bool _thumbnailTried;

        /// <summary>
        /// 卡片用的 **3D 缩略图**。
        ///
        /// <para>⚠ 必须在**逐帧队列**里渲染，不能在数据绑定期同步渲。</para>
        ///
        /// <para>原因：一张缩略图要走 <c>Measure/Arrange/UpdateLayout</c> +
        /// <c>RenderTargetBitmap.Render</c>，而 WPF 3D 的这一步实测要 31~63ms
        /// （正好是 2~4 个 ~15.6ms 的帧间隔 —— 是在等渲染线程，不是算力不够）。
        /// 皮肤页一打开要 12 张（9 款内置默认皮肤 + 已保存 + 账号卡），
        /// 同步连渲 = <b>12 × 约 40ms ≈ 485ms 的 UI 线程冻结</b>，
        /// 表现就是「切到皮肤页明显卡一下」。</para>
        ///
        /// <para>现在的行为：绑定时只做一次字典查找（微秒级）；未命中的投进队列，
        /// 每帧渲一张，渲完通过 PropertyChanged 回填（<c>SkinEntry</c> 本来就是
        /// ObservableObject）。卡片刻之间不再有一整块阻塞。</para>
        /// </summary>
        public BitmapSource? Thumbnail
        {
            get
            {
                if (_thumbnail != null) return _thumbnail;
                if (_thumbnailTried) return null;
                _thumbnailTried = true;
                if (Preview == null) return null;

                // 命中缓存直接返回（字典查找，微秒级）
                if (TryGetCachedThumbnail(Path, IsSlim, Preview, out var hit))
                {
                    _thumbnail = hit;
                    return hit;
                }

                // 未命中：排进逐帧队列，先返回 null（卡片短暂空白，下一帧起逐张出现）
                _thumbQueue.Enqueue(this);
                PumpThumbQueue();
                return null;
            }
        }

        /// <summary>逐帧队列渲完之后回填，并通知绑定刷新。</summary>
        internal void ApplyThumbnail(BitmapSource? bmp)
        {
            _thumbnail = bmp;
            OnPropertyChanged(nameof(Thumbnail));
        }

        // ── 逐帧渲染队列 ───────────────────────────────────────────────
        // 每一轮 Dispatcher 只渲一张：渲完把控制权交还，输入和重绘都能插进来。
        // 这样 12 张的总耗时不变，但不再是一整块 485ms 的白屏。
        private static readonly Queue<SkinEntry> _thumbQueue = new();
        private static bool _thumbPumping;
        /// <summary>取消代次：离开皮肤页时 +1，让已排队的回调自己作废。</summary>
        private static int _thumbGen;

        /// <summary>
        /// 离开皮肤页时调用：清空待渲染队列，正在排队的那次回调直接作废。
        ///
        /// ⚠ 为什么必须有：队列是 <c>static</c> 的，切到别的页面后仍会一张张渲下去。
        /// 而单张渲染实测要 **~1.1 秒**（<c>RenderTargetBitmap.Render</c> 等渲染线程），
        /// 全部跑在 UI 线程上 —— 用户切到「下载」页时，转场动画只有 280ms，
        /// 被这 1.1 秒一卡，动画直接跳到终点，看起来就是「划入动画没了」。
        /// </summary>
        internal static void CancelThumbQueue()
        {
            _thumbGen++;
            _thumbQueue.Clear();
            _thumbPumping = false;
        }

        private static void PumpThumbQueue()
        {
            if (_thumbPumping || _thumbQueue.Count == 0) return;
            _thumbPumping = true;
            int gen = _thumbGen;

            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    if (gen != _thumbGen) return;   // 已被 CancelThumbQueue 作废
                    _thumbPumping = false;
                    if (_thumbQueue.Count == 0) return;

                    SkinEntry? e = null;
                    try
                    {
                        e = _thumbQueue.Dequeue();
                        e.ApplyThumbnail(e.RenderAndCacheThumbnail());
                    }
                    catch
                    {
                        try { e?.ApplyThumbnail(null); } catch { }
                    }

                    if (_thumbQueue.Count > 0) PumpThumbQueue();   // 交给下一帧
                }));
        }

        // ── 缩略图缓存 ────────────────────────────────────────────────
        // ⚠ 为什么必须有缓存：
        //   `RenderThumbnail` 每次都要 **new 一个 SkinPreview3D**、走 Measure/Arrange/
        //   UpdateLayout，再用 RenderTargetBitmap 离屏渲染 —— 单张就要几十毫秒。
        //   而 `Reload()`（**切页签 / 换账号 / 导入 / 删除**都会调）会把所有 SkinEntry
        //   **重建**一遍，没有缓存就是每次切页签把整个皮肤库重渲一次。
        //   实测：6 张皮肤时切一次页签 ≈ 200~400ms 的卡顿。
        //
        // 键 = 文件路径 + 最后修改时间 + 模型类型：
        //   · 带上 mtime → 用户改了同名文件会重新渲 ✓
        //   · 路径为空（账号那张临时卡）不缓存，它本来就只出现一次 ✓
        internal static int ThumbCacheCount { get { lock (_thumbLock) return _thumbCache.Count; } }

        /// <summary>命中 / 未命中计数（只在调试日志里用，用来证明缓存确实在起作用）。</summary>
        internal static int ThumbHit, ThumbMiss;

        private static readonly Dictionary<string, BitmapSource> _thumbCache = new();
        private static readonly object _thumbLock = new();
        private const int ThumbCacheLimit = 128;

        /// <summary>
        /// 只查缓存（内存 → 磁盘），**绝不渲染**。内存查找微秒级，磁盘约 1~3ms。
        /// 返回 false 表示未命中。
        /// </summary>
        private static bool TryGetCachedThumbnail(string path, bool slim, BitmapSource? preview, out BitmapSource? bmp)
        {
            bmp = null;

            string key = ThumbCacheKey(path, slim, preview);
            if (string.IsNullOrEmpty(key)) return false;

            lock (_thumbLock)
            {
                if (_thumbCache.TryGetValue(key, out var hit)) { ThumbHit++; bmp = hit; return true; }
            }

            // 内存没有 → 试磁盘。重启后第一次进皮肤页走的就是这条：
            // 直接读 PNG（约 1~3ms），省掉整轮 31~63ms/张 的 3D 离屏渲染。
            bmp = LoadThumbFromDisk(key);
            if (bmp == null) return false;

            ThumbDiskHit++;
            Utilities.Logger.LogInfo("[SkinThumb] 磁盘命中（省掉一次 3D 渲染）");
            lock (_thumbLock)
            {
                if (_thumbCache.Count >= ThumbCacheLimit) _thumbCache.Clear();
                _thumbCache[key] = bmp;
            }
            return true;
        }

        /// <summary>
        /// 缓存键。有文件路径就用「路径 + mtime + 模型」，换了皮肤文件自然失效；
        /// 没有路径的（账号当前那张）用**贴图内容指纹** —— 内容没变就复用，
        /// 不然它每次启动都要重渲一遍（它是不缓存列表里最后剩下的那一张）。
        /// </summary>
        private static string ThumbCacheKey(string path, bool slim, BitmapSource? preview)
        {
            try
            {
                if (!string.IsNullOrEmpty(path))
                    return $"{path}|{System.IO.File.GetLastWriteTimeUtc(path).Ticks}|{slim}";

                if (preview == null) return "";

                // ⚠ 不要用 FormatConvertedBitmap：它要求源位图可转换，遇到某些格式
                //   （或未冻结的自定义源）会抛异常 —— 异常被 catch 吞掉后 key 变成空串，
                //   于是这张永远不进缓存、每次启动都重渲（实测就是这样漏掉了账号卡）。
                //   直接按原始格式取像素，任何 BitmapSource 都支持。
                int stride = ((preview.Format.BitsPerPixel + 7) / 8) * preview.PixelWidth;
                var buf = new byte[stride * preview.PixelHeight];
                preview.CopyPixels(buf, stride, 0);
                byte[] h = System.Security.Cryptography.SHA1.HashData(buf);
                return $"acct|{Convert.ToHexString(h.AsSpan(0, 8))}|{slim}";
            }
            catch { return ""; }
        }

        // ── 缩略图磁盘缓存 ─────────────────────────────────────────────
        // 内存缓存只在进程内有效，一重启就全没了 —— 于是每次开皮肤页都要把
        // 缩略图重渲一遍。落盘之后第二次启动直接读文件，等于「提前加载好了」。
        //
        // 文件名用 key 的哈希：key 里含完整路径 + mtime，直接当文件名会撞长度限制
        // 和非法字符。mtime 进了 key，所以换了皮肤文件自然会生成新的，不会读旧图。
        internal static int ThumbDiskHit;

        // ⚠ 目录只算一次并缓存：ThumbDiskDir 会被绑定 getter 反复调用（每次缩略图查询
        //   都走 ThumbCacheKey → DiskPathFor），里面若每次都 Directory.CreateDirectory，
        //   就是在 UI 线程上反复做系统调用。实测前没感觉，但这是白烧的。
        private static string? _thumbDiskDir;
        private static bool _thumbDiskDirTried;

        private static string ThumbDiskDir()
        {
            if (_thumbDiskDirTried) return _thumbDiskDir ?? "";
            _thumbDiskDirTried = true;
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TsuruLauncher", ".thumbcache");
                System.IO.Directory.CreateDirectory(dir);
                _thumbDiskDir = dir;
                return dir;
            }
            catch { _thumbDiskDir = null; return ""; }
        }

        private static string DiskPathFor(string key)
        {
            try
            {
                string dir = ThumbDiskDir();
                if (string.IsNullOrEmpty(dir)) return "";
                byte[] hash = System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(key));
                string name = Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant() + ".png";
                return System.IO.Path.Combine(dir, name);
            }
            catch { return ""; }
        }

        private static BitmapSource? LoadThumbFromDisk(string key)
        {
            try
            {
                string p = DiskPathFor(key);
                if (string.IsNullOrEmpty(p) || !System.IO.File.Exists(p)) return null;

                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;   // 读进内存后立刻释放文件句柄
                img.UriSource = new Uri(p, UriKind.Absolute);
                img.EndInit();
                img.Freeze();                                  // 冻结后才可跨线程共享
                return img;
            }
            catch { return null; }
        }

        private static void SaveThumbToDisk(string key, BitmapSource bmp)
        {
            try
            {
                string p = DiskPathFor(key);
                if (string.IsNullOrEmpty(p)) return;
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = System.IO.File.Create(p);
                enc.Save(fs);
            }
            catch { /* 写不进磁盘不影响显示 */ }
        }

        /// <summary>
        /// 真正渲染并写入缓存。**只在逐帧队列里调用**，不要放回绑定 getter。
        /// </summary>
        internal BitmapSource? RenderAndCacheThumbnail()
        {
            if (Preview == null) return null;

            ThumbMiss++;
            var bmp = Controls.SkinPreview3D.RenderThumbnail(Preview, IsSlim, ThumbnailSize, ThumbnailYaw);
            if (bmp == null) return null;

            // 账号卡（没有文件路径）现在也能缓存 —— ThumbCacheKey 会退化成
            // 用贴图内容指纹当键。它是不缓存列表里最后剩下的那一张，
            // 不处理的话每次启动都还要重渲一次。
            string key = ThumbCacheKey(Path, IsSlim, Preview);
            if (string.IsNullOrEmpty(key)) return bmp;

            lock (_thumbLock)
            {
                if (_thumbCache.Count >= ThumbCacheLimit) _thumbCache.Clear();
                _thumbCache[key] = bmp;
            }
            SaveThumbToDisk(key, bmp);   // 落盘：下次启动就不用重渲了
            return bmp;
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    /// <summary>
    /// 皮肤选择器。
    /// 已保存皮肤 = 本地皮肤库（%APPDATA%\TsuruLauncher\skins）；
    /// 官方皮肤 = 当前账号的皮肤（暂时只读展示，上传要 Mojang 的皮肤 API）。
    /// </summary>
    public partial class SkinViewModel : ObservableObject
    {
        public ObservableCollection<SkinEntry> SavedSkins { get; } = new();

        /// <summary>
        /// 官方**默认皮肤**（Minecraft 自带的 9 款），随程序打包在
        /// <c>Assets/SkinPreview/defaults/</c>。
        ///
        /// ⚠ 这才是「官方皮肤」页签该有的内容 —— 参考（Axolotl）那一页是**默认皮肤库**，
        ///   让用户挑一款用，而不是只显示账号当前那张。
        /// </summary>
        public ObservableCollection<SkinEntry> DefaultSkins { get; } = new();

        /// <summary>
        /// 官方页签的**切换列表** = Minecraft 自带的 9 款默认皮肤。
        ///
        /// ⚠ 这里**只放官方皮肤**，不放账号当前那张 ——
        ///   账号皮肤是「当前正在用的」，显示在上面的 3D 预览和账号行里；
        ///   这个列表纯粹是「想换皮肤时来这里挑一张」，点哪张就切到哪张。
        /// </summary>
        public ObservableCollection<SkinEntry> OfficialSkins { get; } = new();

        /// <summary>当前预览的皮肤贴图。</summary>
        [ObservableProperty]
        private BitmapImage? _previewSkin;

        /// <summary>当前预览的皮肤名（预览区左上角那个小标签）。</summary>
        [ObservableProperty]
        private string _previewName = "未选择";

        /// <summary>true = 官方皮肤页签，false = 已保存皮肤页签。</summary>
        [ObservableProperty]
        private bool _isOfficialTab;

        [ObservableProperty]
        private bool _isLoading;

        public bool IsSavedTab => !IsOfficialTab;

        /// <summary>右栏标题跟着页签变。</summary>
        public string SectionTitle => IsOfficialTab ? "默认皮肤" : "已保存皮肤";

        /// <summary>皮肤库为空 → 显示「添加皮肤 / 拖放」大落点。</summary>
        public bool HasNoSkins => SavedSkins.Count == 0;

        // ── 官方皮肤（当前账号正在用的那张）────────────────────────

        /// <summary>
        /// 取当前账号的委托，由页面注入。
        /// （SkinPage 的 DataContext 是本 VM，拿不到 MainViewModel，所以让页面把取账号的方式传进来。）
        /// 用委托而不是快照 —— 这样每次切到「官方皮肤」都能拿到最新登录的账号。
        /// </summary>
        private Func<Account?>? _accountProvider;

        /// <summary>上次拉取账号档案的时间（用来做 10 秒 TTL，避免切页签就发请求）。</summary>
        private DateTime _lastOfficialFetchUtc = DateTime.MinValue;

        /// <summary>当前显示的是**哪个账号**的皮肤历史（离线账号不该看到正版的）。</summary>
        private string? _backupOwner;

        /// <summary>
        /// 预览区当前显示的是**哪个账号**的皮肤。
        /// ⚠ 换账号时必须靠它判断要不要立刻清空预览 —— 否则在新账号的皮肤拉回来之前，
        ///   预览区会一直显示上一个账号的皮肤（隐私问题：别人的号能看到你的皮肤）。
        /// </summary>
        private string? _previewOwner;

        /// <summary>
        /// 「我自己的皮肤」那张卡片 —— 账号当前正在用的那张。
        /// ⚠ 它指向**隐藏目录里的备份文件**（有 Path 才能重新应用），
        ///   但显示在列表第一位；用户自己加的文件排在它后面。
        /// </summary>
        private SkinEntry? _accountSkinCard;

        /// <summary>默认皮肤的像素指纹缓存（避免每次进页面都比 9 张）。</summary>
        private Dictionary<string, string>? _defaultHashes;

        private readonly AuthenticationService _auth = new();


        [ObservableProperty]
        private bool _isOfficialLoading;

        /// <summary>已经拿到官方皮肤 → 右栏显示账号信息卡，而不是占位文案。</summary>
        [ObservableProperty]
        private bool _hasOfficialSkin;

        /// <summary>拿不到时给用户看的原因（没登录 / 没买游戏 / 下载失败…）。</summary>
        [ObservableProperty]
        private string _officialMessage = "";

        /// <summary>皮肤模型（CLASSIC = 经典 / SLIM = 纤细）。</summary>
        [ObservableProperty]
        private string _officialVariant = "";

        /// <summary>当前预览的是不是纤细模型（官方皮肤才知道，本地皮肤一律按经典渲染）。</summary>
        [ObservableProperty]
        private bool _isSlimPreview;

        /// <summary>没拿到皮肤、也不在加载中 → 显示原因文案。</summary>
        public bool ShowOfficialMessage => !HasOfficialSkin && !IsOfficialLoading;

        partial void OnHasOfficialSkinChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowOfficialMessage));
            OnPropertyChanged(nameof(OfficialAccountLine));
        }

        partial void OnOfficialVariantChanged(string value) => OnPropertyChanged(nameof(OfficialAccountLine));
        partial void OnIsOfficialLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowOfficialMessage));

        partial void OnIsOfficialTabChanged(bool value)
        {
            OnPropertyChanged(nameof(IsSavedTab));
            OnPropertyChanged(nameof(SectionTitle));

            if (value)
            {
                // 首次切到官方页签才填充那 9 张默认皮肤 —— 在「已保存」页签时
                // 它们不可见，提前填充只会白付 9 张缩略图的 3D 渲染成本。
                EnsureOfficialList();

                // ⚠ 10 秒内已经拉过就不再拉。
                //   之前每次切到「官方皮肤」都发一次 /minecraft/profile 请求 ——
                //   来回切页签会连续打网络，既卡又撞 Mojang 的频率限制。
                //   真正需要强制刷新的地方（换完皮肤）会直接调 LoadOfficialAsync。
                if ((DateTime.UtcNow - _lastOfficialFetchUtc).TotalSeconds > 10)
                    _ = LoadOfficialAsync();
            }
            else
            {
                // 切回「已保存」时把预览恢复成选中的本地皮肤
                var cur = SavedSkins.FirstOrDefault(x => x.IsSelected) ?? SavedSkins.FirstOrDefault();
                if (cur != null) Select(cur);
                else { PreviewSkin = null; PreviewName = "未选择"; IsSlimPreview = false; }
            }
        }

        /// <summary>页面注入「取当前账号」的委托。</summary>
        public void SetAccountProvider(Func<Account?> provider)
        {
            _accountProvider = provider;
            _lastAccount = provider();   // 记下来，避免 OnPageShown 再拉一次

            // ⚠ 进页面就拉账号皮肤 ——
            //   参考里「已保存皮肤」第一张就是**我自己的皮肤**，
            //   不能等用户切到「官方皮肤」页签才加载（那样已保存列表里会缺这张）。
            _ = LoadOfficialAsync();
        }

        /// <summary>
        /// 拉取并预览当前账号的官方皮肤。
        ///
        /// 微软账号：GET /minecraft/profile 的 skins[] → 下载 textures.minecraft.net 的 PNG。
        /// 外置登录（Yggdrasil / LittleSkin）：GET {server}/sessionserver/session/minecraft/profile/{uuid}，
        /// 里面 properties[name=textures] 的 value 是 base64 的 JSON，里面再带皮肤 URL。
        /// </summary>
        public async Task LoadOfficialAsync()
        {
            _lastOfficialFetchUtc = DateTime.UtcNow;

            // ⚠⚠ 先**彻底重置** —— 换账号时不能留着上一个账号的皮肤。
            //   之前切到离线账号，预览和标签还显示上一个正版账号的皮肤。
            HasOfficialSkin = false;
            OfficialSkin = null;
            OfficialThumbnail = null;
            OfficialCape = null;
            CapeName = "";
            OfficialVariant = "";
            OfficialMessage = "";
            ApplyStatus = "";
            _accountSkinCard = null;
            // ⚠ 这里**不要**清空 AccountChipText ——
            //   清空会让标签短暂退回 PreviewName（显示成裸的「Steve」而不是「Steve（账号）」），
            //   用户切页签时会看到标签闪一下、少掉「（账号）」。
            //   新值拉回来后会被覆盖，所以留着旧值更稳。
            RebuildOfficialList();

            var account = _accountProvider?.Invoke();
            Utilities.Logger.LogInfo(
                $"[Skin] LoadOfficial 开始：account={(account == null ? "<null>" : account.Username)} " +
                $"type={(account == null ? "-" : account.Type.ToString())} " +
                $"providerSet={_accountProvider != null}");

            // ⚠⚠ 换账号：**立刻**把预览清成默认皮肤，不等网络。
            //   上面那段重置没有动 PreviewSkin —— 不在这里清的话，
            //   从「登录 A」切到「登录 B」时，B 会在 A 的皮肤上停住好几百毫秒；
            //   如果 B 的皮肤拉取失败且没有本地备份，就会一直显示 A 的皮肤。
            if (account != null &&
                !string.Equals(_previewOwner, account.Username, StringComparison.OrdinalIgnoreCase))
            {
                Utilities.Logger.LogInfo(
                    $"[Skin] 账号切换：{_previewOwner ?? "<无>"} → {account.Username}，先清空预览");
                ShowDefaultPreview();
                _previewOwner = account.Username;
            }

            if (account == null)
            {
                OfficialMessage = "还没有登录账号。到「设置」里登录微软账号或外置登录后再回来。";
                Utilities.Logger.LogInfo("[Skin] LoadOfficial：拿不到账号（provider 为 null 或未登录），退回默认皮肤");
                _backupOwner = null;
                _previewOwner = null;   // 没有账号 = 预览里不该有任何人的皮肤
                Reload();               // ⚠ 必须先 Reload —— 它会把预览重置成「未选择」
                ShowDefaultPreview();   // 再盖上默认皮肤
                return;
            }

            if (account.Type == AccountType.Offline)
            {
                // 离线账号游戏里用的是默认皮肤（Steve），预览也照这个来
                OfficialMessage = "离线账号没有官方皮肤，游戏里用的是默认皮肤。";
                AccountChipText = $"{account.Username}（离线）";
                _backupOwner = null;   // ⚠ 离线账号不该显示别的账号的皮肤历史
                Reload();               // ⚠ 必须先 Reload（会重置预览），再盖默认皮肤
                ShowDefaultPreview();
                return;
            }

            IsOfficialLoading = true;
            try
            {
                // 令牌过期就先用 refresh_token 续一次
                if (account.Type == AccountType.Microsoft &&
                    account.ExpiryTime > 0 &&
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() > account.ExpiryTime - 60)
                {
                    try { await _auth.RefreshSessionAsync(account); } catch { /* 续失败就照原样试 */ }
                }

                List<AuthenticationService.MinecraftSkin> skins;
                List<AuthenticationService.MinecraftCape> capes = new();

                if (account.Type == AccountType.Microsoft)
                {
                    var profile = await _auth.GetProfileAsync(account.MinecraftAccessToken);
                    skins = profile.Skins;
                    capes = profile.Capes;
                }
                else
                {
                    skins = await GetYggdrasilSkinsAsync(account);
                }

                var active = skins.FirstOrDefault(x => x.IsActive) ?? skins.FirstOrDefault();
                if (active == null)
                {
                    OfficialMessage = "这个账号还没有设置皮肤，游戏里用的是默认皮肤。";
                    Utilities.Logger.LogInfo("[Skin] LoadOfficial：账号没有设置皮肤（skins 为空）");
                    return;
                }

                var skinBytes = await _auth.DownloadSkinAsync(active.Url);
                if (skinBytes == null)
                {
                    OfficialMessage = "皮肤图片下载失败，检查一下网络再重试。";
                    Utilities.Logger.LogInfo("[Skin] LoadOfficial：皮肤图片下载失败 url=" + active.Url);
                    return;
                }

                OfficialSkin = DecodeSkin(skinBytes);
                if (OfficialSkin == null)
                {
                    OfficialMessage = "下载到的皮肤图片无法解析。";
                    return;
                }

                bool slim = active.Variant.Equals("SLIM", StringComparison.OrdinalIgnoreCase);
                OfficialVariant = slim ? "纤细（Alex）" : "经典（Steve）";

                OfficialThumbnail = Controls.SkinPreview3D.RenderThumbnail(
                    OfficialSkin, slim, SkinEntry.ThumbnailSize, SkinEntry.ThumbnailYaw);

                HasOfficialSkin = true;

                // 披风：正在穿的那件
                var cape = capes.FirstOrDefault(c => c.IsActive) ?? capes.FirstOrDefault();
                CapeName = cape?.Alias ?? "";
                // 先算出「这张皮肤叫什么」（能对上某款默认皮肤就用它的名字）
                string chipName = DescribeAccountSkin(account.Username, OfficialSkin);
                string preferred = chipName.EndsWith("（账号）")
                    ? chipName[..^"（账号）".Length]
                    : account.Username;

                // ⚠⚠ 把账号当前皮肤**备份到本地**（只增不改）。
                //   换皮肤会直接覆盖账号上的，Mojang 不保留旧的 —— 不备份的话
                //   用户一点列表里的默认皮肤，他自己那张就永久没了。
                string? skinBackup = SkinService.BackupAccountSkin(account.Username, skinBytes, preferred);

                var capeBytes = cape == null ? null : await _auth.DownloadSkinAsync(cape.Url);
                OfficialCape = DecodeSkin(capeBytes ?? Array.Empty<byte>());

                if (Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1" && capeBytes != null)
                {
                    string dump = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "_cape_dump.png");
                    try { System.IO.File.WriteAllBytes(dump, capeBytes); } catch { }
                    Utilities.Logger.LogInfo($"[Skin] 披风 {cape!.Alias} url={cape.Url} " +
                        $"bytes={capeBytes.Length} 已存到 {dump}");
                }

                RebuildOfficialList();

                // 「已保存皮肤」里放一张「我自己的皮肤」卡片（账号当前那张），
                // 这样用户在本地皮肤之间切换时也能切回账号皮肤。
                // 备份进皮肤库后重新扫一遍 —— 这样「已保存皮肤」里就会出现
                // 账号当前这张（以及以前换过的每一张），随时能点回去。
                // 「我自己的皮肤」卡片 = 账号当前那张（指向隐藏备份，能重新应用）
                // ⚠ 标签用**皮肤名**（"Steve（账号）"）而不是用户名 ——
                //   账号皮肤是 Steve 时显示 "Player（账号）" 会让人以为那是原皮肤。
                _backupOwner = account.Username;
                string cardLabel = $"{preferred}（账号）";
                _accountSkinCard = skinBackup != null
                    ? new SkinEntry(skinBackup, cardLabel)
                    : SkinEntry.FromAccount(cardLabel, OfficialSkin, slim);
                _accountSkinCard.IsCurrent = true;

                Reload();

                // ⚠ MarkCurrent 必须放在 Reload **之后**（条目是 Reload 建出来的），
                //   而且标记完还要**重新选中** —— Reload 里选的时候 IsCurrent 还没标上，
                //   结果高亮会落在错误的那张卡上。
                MarkCurrentAccountSkin(_accountSkinCard);

                // ⚠ 官方页签进来先显示**账号当前那张**（含披风）——
                //   预览区显示的是"我自己的皮肤"，下面的列表是拿来切换的。
                // ⚠ PreviewName **不要**在这里覆盖成用户名 ——
                //   MarkCurrentAccountSkin 里已经按卡片名设过了（比如 "Steve"），
                //   在它之后再赋值会把名字冲掉。
                Utilities.Logger.LogInfo($"[Skin] LoadOfficial 成功：slim={slim} 贴图={OfficialSkin?.PixelWidth}x{OfficialSkin?.PixelHeight}");
                IsSlimPreview = slim;
                PreviewSkin = OfficialSkin;
                if (string.IsNullOrEmpty(PreviewName) || PreviewName == "未选择")
                    PreviewName = account.Username;

                // 预览上方的标签 = 账号当前皮肤状态（能换成默认皮肤的名字）
                AccountChipText = chipName;
            }
            catch (Exception ex)
            {
                OfficialMessage = "拉取账号皮肤失败：" + ex.Message;
                Utilities.Logger.LogError(ex, "Skin.LoadOfficial");

                // ⚠⚠ 失败时**退回本地备份**，不能让界面空掉。
                //   网络/接口挂了不代表皮肤丢了 —— 备份一直在 skins/.account/<用户名>/ 里。
                //   之前这里只写了错误信息，用户看到的是"我皮肤全没了"，实际上文件都在。
                try
                {
                    var acct = _accountProvider?.Invoke();
                    var backups = SkinService.ListAccountBackups(acct?.Username);

                    if (backups.Count > 0)
                    {
                        // 优先用上次确认过的「当前」；没有才退回"最近修改"这个近似值
                        string newest = SkinService.ReadCurrentAccountSkin(acct!.Username)
                                        ?? backups.OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f)).First();

                        _backupOwner = acct!.Username;
                        _accountSkinCard = new SkinEntry(newest, $"{acct.Username}（账号）");
                        _accountSkinCard.IsCurrent = true;

                        Reload();
                        MarkCurrentAccountSkin(_accountSkinCard);
                        AccountChipText = $"{acct.Username}（离线缓存）";
                    }
                    else
                    {
                        Reload();
                        ShowDefaultPreview();
                    }
                }
                catch { }
            }
            finally
            {
                IsOfficialLoading = false;
            }
        }

        /// <summary>官方皮肤贴图（右栏卡片用）。</summary>
        [ObservableProperty]
        private BitmapImage? _officialSkin;

        /// <summary>官方皮肤的 3D 缩略图。</summary>
        [ObservableProperty]
        private BitmapSource? _officialThumbnail;

        /// <summary>账号正在穿的披风贴图（没有就是 null）。</summary>
        [ObservableProperty]
        private BitmapImage? _officialCape;

        /// <summary>披风名（Mojang 给的可读名，比如 "Migrator"）。</summary>
        [ObservableProperty]
        private string _capeName = "";

        public bool HasCape => OfficialCape != null;
        partial void OnOfficialCapeChanged(BitmapImage? value) => OnPropertyChanged(nameof(HasCape));

        /// <summary>正在上传皮肤到账号。</summary>
        [ObservableProperty]
        private bool _isApplying;

        /// <summary>上传结果 / 报错（显示在列表下方）。</summary>
        [ObservableProperty]
        private string _applyStatus = "";

        private System.Threading.CancellationTokenSource? _statusClearCts;

        /// <summary>
        /// 状态文案显示几秒后自动消失 —— 不然「✅ 已应用到账号」会一直挂在那里，
        /// 用户下次进来还以为是新提示。
        /// </summary>
        partial void OnApplyStatusChanged(string value)
        {
            _statusClearCts?.Cancel();
            if (string.IsNullOrEmpty(value)) return;

            var cts = new System.Threading.CancellationTokenSource();
            _statusClearCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(6000, cts.Token);
                    if (!cts.IsCancellationRequested)
                    {
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            if (ReferenceEquals(_statusClearCts, cts)) ApplyStatus = "";
                        });
                    }
                }
                catch { }
            });
        }

        /// <summary>
        /// 3D 预览上方那个小标签的文案 —— **永远反映账号当前正在用的皮肤**。
        /// 能对上某款默认皮肤就显示它的名字（「Steve（账号）」），
        /// 对不上就显示「<用户名>（账号）」。换完皮肤会自动重算。
        /// </summary>
        [ObservableProperty]
        private string _accountChipText = "";

        /// <summary>标签实际显示的内容：有账号皮肤就显示账号状态，否则退回预览名。</summary>
        public string ChipText => string.IsNullOrEmpty(AccountChipText) ? PreviewName : AccountChipText;

        partial void OnAccountChipTextChanged(string value) => OnPropertyChanged(nameof(ChipText));
        partial void OnPreviewNameChanged(string value) => OnPropertyChanged(nameof(ChipText));

        /// <summary>页签顶部那行「账号当前皮肤：xxx · 纤细」。</summary>
        public string OfficialAccountLine =>
            HasOfficialSkin && _accountProvider?.Invoke() is { } a
                ? $"账号当前：{a.Username} · {OfficialVariant}"
                : "";

        private static BitmapImage? DecodeSkin(byte[] bytes)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                using (var ms = new System.IO.MemoryStream(bytes))
                {
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                }
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>外置登录（Yggdrasil）取皮肤。</summary>
        private static async Task<List<AuthenticationService.MinecraftSkin>> GetYggdrasilSkinsAsync(Account account)
        {
            var result = new List<AuthenticationService.MinecraftSkin>();
            if (string.IsNullOrWhiteSpace(account.YggdrasilServer) || string.IsNullOrWhiteSpace(account.Uuid))
                return result;

            string url = account.YggdrasilServer.TrimEnd('/')
                         + "/sessionserver/session/minecraft/profile/" + account.Uuid.Replace("-", "");

            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            string body = await http.GetStringAsync(url);

            var json = Newtonsoft.Json.Linq.JObject.Parse(body);
            if (json["properties"] is not Newtonsoft.Json.Linq.JArray props) return result;

            foreach (var p in props)
            {
                if (!string.Equals(p["name"]?.ToString(), "textures", StringComparison.OrdinalIgnoreCase)) continue;

                string b64 = p["value"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(b64)) continue;

                string decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                var tex = Newtonsoft.Json.Linq.JObject.Parse(decoded);

                string skinUrl = tex["textures"]?["SKIN"]?["url"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(skinUrl)) continue;

                string model = tex["textures"]?["SKIN"]?["metadata"]?["model"]?.ToString() ?? "";
                result.Add(new AuthenticationService.MinecraftSkin(
                    skinUrl.Replace("http://", "https://"),
                    model.Equals("slim", StringComparison.OrdinalIgnoreCase) ? "SLIM" : "CLASSIC",
                    true));
            }

            return result;
        }

        public SkinViewModel()
        {
            // ⚠ 顺序不能反：Reload() 在库为空时会退回默认皮肤，
            //   而默认皮肤是 LoadDefaultSkins() 才装进来的 —— 反了就会退成 null，
            //   界面显示「还没有皮肤」（安装版实测踩过）。
            LoadDefaultSkins();
            Reload();
        }

        /// <summary>加载随程序打包的官方默认皮肤（9 款）。</summary>
        private void LoadDefaultSkins()
        {
            DefaultSkins.Clear();

            string dir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "SkinPreview", "defaults");
            if (!System.IO.Directory.Exists(dir)) return;

            // 按官方展示顺序
            string[] order = { "Steve", "Alex", "Ari", "Efe", "Kai", "Makena", "Noor", "Sunny", "Zuri" };
            foreach (var name in order)
            {
                string p = System.IO.Path.Combine(dir, name + ".png");
                if (System.IO.File.Exists(p)) DefaultSkins.Add(new SkinEntry(p));
            }
        }

        /// <summary>重新扫描皮肤库。</summary>
        [RelayCommand]
        public void Reload()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int thumbs = 0;
            try
            {
                string? keep = SavedSkins.FirstOrDefault(s => s.IsSelected)?.Path;

                SavedSkins.Clear();

                // 第一位永远是「＋ 添加皮肤 / 拖放」占位卡（参考里它常驻，不是只在空列表时才出现）
                SavedSkins.Add(SkinEntry.AddCard());

                // 接下来 = 账号皮肤的历史（**当前那张排第一**）。
                // ⚠ 历史必须都留着 —— 用户换完皮肤想换回去时靠的就是这些卡片。
                if (_accountSkinCard != null) SavedSkins.Add(_accountSkinCard);

                foreach (var path in SkinService.ListAccountBackups(_backupOwner))
                {
                    if (_accountSkinCard != null &&
                        string.Equals(path, _accountSkinCard.Path, StringComparison.OrdinalIgnoreCase))
                        continue;   // 当前那张已经加过了

                    SavedSkins.Add(new SkinEntry(path));
                }

                // 最后 = 用户自己加的文件（顶层目录）
                foreach (var path in SkinService.ListSkins())
                    SavedSkins.Add(new SkinEntry(path));

                OnPropertyChanged(nameof(HasNoSkins));

                // 优先保留用户原来选中的那张；没有就选「我自己的皮肤」；再没有就随便一张
                var pick = SavedSkins.FirstOrDefault(s => !s.IsAddCard && s.Path == keep)
                           ?? _accountSkinCard
                           ?? SavedSkins.FirstOrDefault(s => !s.IsAddCard);

                if (pick != null) Select(pick);
                // ⚠ 库为空时**不能**把预览清成 null —— 那会让整个预览区显示「还没有皮肤」。
                //   退回随程序打包的默认皮肤（Steve），至少预览区是正常的。
                //   账号皮肤拉回来之后会覆盖掉。
                else ShowDefaultPreview();
            }
            catch { }
            finally
            {
                // 只在调试开关打开时打，用来量化「缩略图缓存」的效果
                if (Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1")
                {
                    sw.Stop();
                    Utilities.Logger.LogInfo(
                        $"[Skin] Reload 耗时 {sw.Elapsed.TotalMilliseconds:F1}ms（{SavedSkins.Count} 项，" +
                        $"缩略图 命中 {SkinEntry.ThumbHit} / 重渲 {SkinEntry.ThumbMiss}）");
                }
            }
        }

        /// <summary>选中某张皮肤 → 预览区换成它。</summary>
        /// <summary>选中一张「已保存皮肤」。（占位卡片不参与选中）</summary>
        public void Select(SkinEntry entry)
        {
            if (entry.IsAddCard) return;
            ClearSelection();
            entry.IsSelected = true;
            ApplyPreview(entry);
        }

        /// <summary>
        /// 把「跟账号当前皮肤内容一致」的那张卡标记为「当前」。
        /// 换完皮肤重新加载时，标记会自动落到新皮肤那张上。
        /// </summary>
        private void MarkCurrentAccountSkin(SkinEntry? card)
        {
            foreach (var e in SavedSkins) e.IsCurrent = false;
            if (card == null || !SavedSkins.Contains(card)) return;

            card.IsCurrent = true;

            // 重新选中「我自己的皮肤」那张（Reload 时还没标记，会选错）
            ClearSelection();
            card.IsSelected = true;
            ApplyPreview(card);
        }

        /// <summary>没有账号皮肤时，预览显示默认皮肤（Steve）。</summary>
        private void ShowDefaultPreview()
        {
            var steve = DefaultSkins.FirstOrDefault(d => d.Name == "Steve") ?? DefaultSkins.FirstOrDefault();
            if (steve?.Preview == null)
            {
                PreviewSkin = null;
                PreviewName = "未选择";
                IsSlimPreview = false;
                return;
            }

            IsSlimPreview = steve.IsSlim;   // 先设模型类型再换贴图
            PreviewSkin = steve.Preview;
            PreviewName = steve.Name;
        }

        /// <summary>
        /// 页面每次显示时调用。
        /// ⚠ 账号换了（比如从正版切到离线）必须把皮肤状态整个重置 ——
        ///   否则会继续显示上一个账号的皮肤。
        /// </summary>
        public void OnPageShown()
        {
            var account = _accountProvider?.Invoke();
            if (ReferenceEquals(account, _lastAccount)) return;

            _lastAccount = account;
            _ = LoadOfficialAsync();
        }

        /// <summary>上一次见到的账号，用来判断有没有换过。</summary>
        private Account? _lastAccount;

        /// <summary>
        /// 描述账号当前皮肤：能跟某款默认皮肤对上就返回它的名字，否则返回用户名。
        /// 这样用户换完皮肤，预览上方的标签会立刻变成新皮肤的名字。
        /// </summary>
        private string DescribeAccountSkin(string username, BitmapImage? skin)
        {
            try
            {
                string hash = SkinService.PixelHash(skin);
                if (!string.IsNullOrEmpty(hash))
                {
                    _defaultHashes ??= DefaultSkins
                        .Where(d => d.Preview != null)
                        .ToDictionary(d => SkinService.PixelHash(d.Preview), d => d.Name);

                    if (_defaultHashes.TryGetValue(hash, out var name))
                        return $"{name}（账号）";
                }
            }
            catch { }

            return $"{username}（账号）";
        }

        /// <summary>切换列表是否已填充过（见 <see cref="EnsureOfficialList"/>）。</summary>
        private bool _officialFilled;

        /// <summary>
        /// 重建切换列表 = 9 款官方默认皮肤。
        ///
        /// <para>⚠ 这里**不无条件填充**：9 张默认皮肤的缩略图每张要 31~63ms 的 WPF 3D
        /// 离屏渲染。皮肤页默认开在「已保存皮肤」页签，那 9 张根本看不见 ——
        /// 无条件填充等于每次进皮肤页都白渲 9 张（约 300ms）。
        /// 所以只在官方页签真正可见时才填（<see cref="EnsureOfficialList"/>）。</para>
        /// </summary>
        private void RebuildOfficialList()
        {
            OfficialSkins.Clear();
            _officialFilled = false;
            if (IsOfficialTab) EnsureOfficialList();
        }

        /// <summary>
        /// 首次切到「官方皮肤」页签时才真正填充默认皮肤列表。
        /// 观感不变（切过去就看到一样的 9 张），只是不再为看不见的页签提前付渲染成本。
        /// </summary>
        private void EnsureOfficialList()
        {
            if (_officialFilled) return;
            _officialFilled = true;
            OfficialSkins.Clear();
            foreach (var d in DefaultSkins) OfficialSkins.Add(d);
        }

        /// <summary>
        /// 点官方页签里的一张皮肤：先切预览；如果不是账号当前那张，**再上传到账号**。
        ///
        /// 上传走 <c>PUT /minecraft/profile/skins</c>（multipart）。
        /// ⚠ Mojang 限制大约一分钟一次，429 会给出提示。
        /// </summary>
        [RelayCommand]
        public async Task SelectDefaultAsync(SkinEntry? entry)
        {
            if (entry == null) return;

            // ⚠ 上传中就别再点了 —— 并发上传会互相覆盖，而且 Mojang 有频率限制
            if (IsApplying) return;

            ApplyStatus = "";

            // 账号当前那张：只是选中，不用上传
            if (entry.IsCurrent || !entry.CanApply)
            {
                ClearSelection();
                entry.IsSelected = true;
                ApplyPreview(entry);
                return;
            }

            var account = _accountProvider?.Invoke();
            if (account == null || account.Type != AccountType.Microsoft)
            {
                ApplyStatus = "只有登录微软正版账号才能把皮肤应用到账号。";
                return;
            }

            // ⚠⚠ 先确认、**再切预览**。
            //   之前是先切预览再确认，用户点「取消」后预览停在候选皮肤上，
            //   看起来像已经换了（实际账号没变）—— 状态不一致。
            //   上传会直接覆盖账号皮肤，Mojang 不保留旧的，必须让用户确认。
            var confirm = Controls.iOS26Dialog.Show(
                $"要把「{entry.Name}」应用到账号吗？\n\n" +
                "账号当前的皮肤已经备份到本地皮肤库，随时可以切回来。",
                "更换皮肤", Controls.DialogIcon.Info, Controls.DialogButtons.YesNo);

            if (confirm != true)
            {
                ApplyStatus = "已取消，账号皮肤没变。";
                return;   // 预览保持不动 = 还是账号当前那张
            }

            ClearSelection();
            entry.IsSelected = true;
            ApplyPreview(entry);

            IsApplying = true;
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(entry.Path);
                await _auth.UploadSkinAsync(account.MinecraftAccessToken, bytes, entry.IsSlim);
                ApplyStatus = "✅ 已应用到账号，进游戏就能看到（可能需要重新进入世界）。";

                // ⚠ 上传成功后**先用本地结果更新 UI**，不要立刻拉档案 ——
                //   Mojang 的 /minecraft/profile 有延迟，马上查还是旧数据，
                //   界面会闪回旧皮肤，看起来像没换成功。
                _backupOwner = account.Username;
                _accountSkinCard = entry;
                entry.IsCurrent = true;
                MarkCurrentAccountSkin(entry);

                // 过一会儿再拉一次，跟服务端对齐
                await Task.Delay(2500);
                await LoadOfficialAsync();
            }
            catch (Exception ex)
            {
                ApplyStatus = ex.Message;
            }
            finally
            {
                IsApplying = false;
            }
        }

        /// <summary>两个列表的选中态互斥（切页签时不会两边都亮）。</summary>
        private void ClearSelection()
        {
            foreach (var s in SavedSkins) s.IsSelected = false;
            foreach (var s in DefaultSkins) s.IsSelected = false;
        }

        /// <summary>
        /// 把某张皮肤推给 3D 预览。
        /// ⚠ 顺序有讲究：**先设模型类型，再换贴图** ——
        ///   反过来会先用旧的模型类型渲染一次（首次加载时 IsSlimPreview 还是 false），
        ///   纤细皮肤的手臂会先花一下。
        /// </summary>
        private void ApplyPreview(SkinEntry entry)
        {
            IsSlimPreview = entry.IsSlim;
            PreviewSkin = entry.Preview;
            PreviewName = entry.Name;
        }

        /// <summary>导入一张皮肤（文件选择器 / 拖放都走这里）。</summary>
        public async Task<bool> ImportAsync(string sourcePath)
        {
            IsLoading = true;
            try
            {
                string? imported = await Task.Run(() => SkinService.Import(sourcePath)).ConfigureAwait(true);
                if (imported == null) return false;

                Reload();
                var entry = SavedSkins.FirstOrDefault(s => s.Path == imported);
                if (entry != null) Select(entry);
                return true;
            }
            finally { IsLoading = false; }
        }

        /// <summary>删除一张皮肤。</summary>
        public void Remove(SkinEntry entry)
        {
            if (entry.IsAddCard) return;

            // ⚠ 删掉就找不回来了 —— 必须先确认（用户自己找的皮肤不能无声消失）
            var ok = Controls.iOS26Dialog.Show(
                $"要从皮肤库里移除「{entry.Name}」吗？\n\n" +
                "文件会被放进回收站，误删还能捞回来。",
                "移除皮肤", Controls.DialogIcon.Info, Controls.DialogButtons.YesNo);
            if (ok != true) return;

            if (!SkinService.DeleteToRecycleBin(entry.Path)) return;

            bool wasSelected = entry.IsSelected;
            SavedSkins.Remove(entry);

            // 如果删的是账号皮肤那张，卡片还在（它由账号驱动），只取消选中即可
            if (ReferenceEquals(entry, _accountSkinCard)) _accountSkinCard = null;

            OnPropertyChanged(nameof(HasNoSkins));

            // ⚠ 不能判断 Count == 0 —— 列表里永远有「添加卡」，Count 至少是 1。
            //   要判断「除添加卡外还有没有东西」。
            var next = SavedSkins.FirstOrDefault(x => !x.IsAddCard);
            if (wasSelected)
            {
                if (next != null) Select(next);
                else { PreviewSkin = null; PreviewName = "未选择"; }
            }
        }
    }
}
