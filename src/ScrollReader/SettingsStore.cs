using System.IO;
using System.Text;
using System.Windows.Threading;

namespace ScrollReader;

/// <summary>
/// Owns settings.json: creates a commented template on first run, loads it,
/// and hot-reloads (debounced) when the user saves the file. Construct and
/// use on the UI thread.
/// </summary>
internal sealed class SettingsStore : IDisposable
{
    private const string DefaultTemplate = """
        {
          // 読書モード開始のホットキー。例: "Ctrl+Alt+R", "Ctrl+Shift+Space", "F9"
          // マウス派は "Ctrl+MiddleClick"（Ctrl+ホイールクリック）も指定可。
          // 素の "MiddleClick" も可能だが、ブラウザの中クリック等と競合しやすいので注意
          "hotkey": "Ctrl+Alt+R",

          // ホイール下回転の挙動:
          //   "cruise": 自動送り。下に回すほど加速、上に回すと減速→停止→コマ送り巻き戻し
          //   "step":   1ノッチ = 1文節
          "wheelMode": "cruise",

          // クルーズ最低速（レベル1）の1文節あたりの時間（ミリ秒）。
          // 1段加速するごとに25%速くなり、minDisplayMs が上限速度
          "cruiseBaseMs": 350,

          // 日本語の表示単位の最大文字数。これを超える結合をしない（トークン自体が長い場合は超えることあり）
          "maxSegmentLength": 7,

          // 日本語の分割エンジン: "mecab"（同梱辞書・高精度）または "os"（Windows組み込み・軽量）
          "segmenter": "mecab",

          // 1文節（単語）の最短表示時間（ミリ秒）。大きいほどゆっくり流れる
          "minDisplayMs": 120,

          // ホイールを勢いよく回したときに先読みされる最大ステップ数（stepモード・巻き戻し）
          "maxPendingSteps": 5,

          // 拡大テキストのフォントサイズ
          "fontSize": 44,

          // 英語テキストで ORP（最適認識点）を赤くハイライトし、常に同じ位置に揃える
          "orpEnabled": true,

          // ホットキーを無効にするアプリ（プロセス名、.exe は省略可）例: ["Photoshop", "game.exe"]
          "blockedProcesses": []
        }
        """;

    private readonly Dispatcher _dispatcher;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private string _lastJson = "";

    public Settings Current { get; private set; } = new();

    public string FilePath { get; }

    /// <summary>Raised on the UI thread after the file changed and reloaded successfully.</summary>
    public event Action? Changed;

    public SettingsStore(string? filePath = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScrollReader", "settings.json");
    }

    public void Initialize()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        if (!File.Exists(FilePath)) File.WriteAllText(FilePath, DefaultTemplate, new UTF8Encoding(false));
        Reload(raiseChanged: false);

        _debounce = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Reload(raiseChanged: true);
        };

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            // Editors fire several events per save; restart the debounce window.
            _debounce!.Stop();
            _debounce.Start();
        });

    private void Reload(bool raiseChanged)
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            if (json == _lastJson) return;
            Current = Settings.Load(json);
            _lastJson = json;
            if (raiseChanged) Changed?.Invoke();
        }
        catch
        {
            // Unparseable or mid-write file: keep the last good settings.
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Stop();
    }
}
