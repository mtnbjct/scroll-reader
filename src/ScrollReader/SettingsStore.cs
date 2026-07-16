using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace ScrollReader;

/// <summary>
/// Owns settings.json: creates a commented template on first run, adds
/// missing keys (with comments and defaults) when the app gains new
/// settings, loads it, and hot-reloads (debounced) when the user saves the
/// file. Construct and use on the UI thread.
/// </summary>
internal sealed class SettingsStore : IDisposable
{
    /// <summary>
    /// Single source of truth for the settings file: key, default value
    /// (JSON), and comment lines. Values must match the defaults in
    /// <see cref="Settings"/> — a test enforces this.
    /// </summary>
    private static readonly (string Key, string ValueJson, string[] Comments)[] TemplateEntries =
    {
        ("hotkey", "\"Ctrl+Alt+R\"", new[]
        {
            "読書モード開始のホットキー。例: \"Ctrl+Alt+R\", \"Ctrl+Shift+Space\", \"F9\"",
            "マウス派は \"Ctrl+MiddleClick\"（Ctrl+ホイールクリック）も指定可。",
            "素の \"MiddleClick\" も可能だが、ブラウザの中クリック等と競合しやすいので注意",
        }),
        ("wheelMode", "\"cruise\"", new[]
        {
            "ホイール下回転の挙動:",
            "  \"cruise\": 自動送り。下に回すほど加速、上に回すと減速→停止→コマ送り巻き戻し",
            "  \"step\":   1ノッチ = 1文節",
        }),
        ("cruiseBaseMs", "350", new[]
        {
            "クルーズ最低速（レベル1）の1文節あたりの時間（ミリ秒）。minDisplayMs が上限速度",
        }),
        ("cruiseAccelPercent", "25", new[]
        {
            "クルーズで1段加速するごとに何%速くなるか（5〜50）",
        }),
        ("lengthWeight", "0.05", new[]
        {
            "文字数に応じた表示時間の増減（1文字あたりの割合）。",
            "基準4文字より長いほどゆっくり、短いほど速く。0 で無効、最大 0.3",
        }),
        ("maxSegmentLength", "7", new[]
        {
            "日本語の表示単位の最大文字数。これを超える結合をしない（トークン自体が長い場合は超えることあり）",
        }),
        ("segmenter", "\"mecab\"", new[]
        {
            "日本語の分割エンジン: \"mecab\"（同梱辞書・高精度）または \"os\"（Windows組み込み・軽量）",
        }),
        ("minDisplayMs", "120", new[]
        {
            "1文節（単語）の最短表示時間（ミリ秒）。大きいほどゆっくり流れる",
        }),
        ("maxPendingSteps", "5", new[]
        {
            "ホイールを勢いよく回したときに先読みされる最大ステップ数（stepモード・巻き戻し）",
        }),
        ("fontSize", "44", new[]
        {
            "拡大テキストのフォントサイズ",
        }),
        ("orpEnabled", "true", new[]
        {
            "英語テキストで ORP（最適認識点）を赤くハイライトし、常に同じ位置に揃える",
        }),
        ("showStats", "true", new[]
        {
            "セッション終了時に読んだ文字数と速度を一瞬表示する",
        }),
        ("blockedProcesses", "[]", new[]
        {
            "ホットキーを無効にするアプリ（プロセス名、.exe は省略可）例: [\"Photoshop\", \"game.exe\"]",
        }),
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

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
        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, BuildTemplate(), Utf8NoBom);
        }
        else if (AddMissingKeys(File.ReadAllText(FilePath)) is { } migrated)
        {
            File.WriteAllText(FilePath, migrated, Utf8NoBom);
        }
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

        try
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // Watching can fail on exotic setups (e.g. the folder reached
            // through filesystem virtualization + junctions). Degrade to
            // no hot reload instead of crashing; settings still load.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    internal static string BuildTemplate()
    {
        var sb = new StringBuilder("{\n");
        var first = true;
        foreach (var entry in TemplateEntries)
        {
            if (!first) sb.Append('\n');
            first = false;
            AppendEntry(sb, entry);
        }
        return sb.Append("}\n").ToString();
    }

    /// <summary>
    /// Returns the file content with entries the user's file lacks inserted
    /// after the opening brace (comments and defaults included), preserving
    /// everything else byte for byte. Null when nothing needs to change or
    /// the file is not parseable.
    /// </summary>
    internal static string? AddMissingKeys(string json)
    {
        HashSet<string> existing;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            existing = doc.RootElement.EnumerateObject()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return null; // broken file: leave it alone, Reload keeps last good settings
        }

        var missing = TemplateEntries.Where(e => !existing.Contains(e.Key)).ToList();
        if (missing.Count == 0) return null;

        var brace = json.IndexOf('{');
        var sb = new StringBuilder();
        sb.Append(json, 0, brace + 1).Append('\n');
        foreach (var entry in missing)
        {
            sb.Append('\n');
            AppendEntry(sb, entry);
        }
        sb.Append(json.AsSpan(brace + 1));
        return sb.ToString();
    }

    private static void AppendEntry(StringBuilder sb, (string Key, string ValueJson, string[] Comments) entry)
    {
        foreach (var line in entry.Comments) sb.Append("  // ").Append(line).Append('\n');
        sb.Append("  // デフォルト: ").Append(entry.ValueJson).Append('\n');
        sb.Append("  \"").Append(entry.Key).Append("\": ").Append(entry.ValueJson).Append(",\n");
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
