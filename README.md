# Scroll Reader

Read without moving your eyes — an OS-wide RSVP reading tool for Windows.

Select text in any app, press the hotkey, and Scroll Reader shows it one unit at a time — Japanese bunsetsu or English words — enlarged at your cursor. Scroll the wheel to read on, speed up, slow down, or step back. Because the words come to your eyes instead of your eyes chasing the words, you read faster with less fatigue.

## Usage

1. Run `ScrollReader.exe` (it lives in the system tray)
2. Select text in any app and press `Ctrl+Alt+R`
3. Wheel down: start auto-advance; further down: speed up (shown as `▶3`). Wheel up: slow down → stop → step backwards
4. Exit: pause on the last unit and scroll down once more / `Esc` / click / any key

- While reading, wheel events go to Scroll Reader — the app underneath does not scroll
- Hold **Ctrl** to temporarily switch to the other wheel mode (cruise ⇄ step); the cruise speed is remembered
- Hold **Alt** and move the mouse to reposition the overlay
- Press the hotkey with nothing selected to **resume** the previous text where you left off
- Re-reading (behind the furthest point you've reached) renders in gray
- For English text, the optimal recognition point of each word is highlighted red and pinned to a fixed position
- A session ending after real reading briefly shows how much you read and how fast
- Set `"wheelMode": "step"` for one-notch-one-unit stepping instead of auto-advance

## Build and run

Requires the .NET 8 SDK or later. Windows only (for now).

```
dotnet build                            # build
dotnet test                             # run tests
dotnet run --project src/ScrollReader   # run
dotnet publish src/ScrollReader -c Release -o publish   # distributable build
```

## Settings

Right-click the tray icon → "設定を開く" (open settings), or edit `%AppData%\ScrollReader\settings.json` directly. Changes apply on save — no restart needed. Each key is documented by comments inside the generated file.

```jsonc
{
  "hotkey": "Ctrl+Alt+R",     // also e.g. "Ctrl+MiddleClick"
  "wheelMode": "cruise",      // "cruise" or "step"
  "cruiseBaseMs": 350,        // slowest cruise speed (ms per unit)
  "cruiseAccelPercent": 25,   // speed-up per cruise level (5-50)
  "lengthWeight": 0.05,       // cruise time scaling per character (0 = off)
  "sentencePauseFactor": 1.7, // cruise dwell on sentence ends (。！？…)
  "clausePauseFactor": 1.35,  // cruise dwell on clause ends (、)
  "maxSegmentLength": 7,      // max characters per Japanese unit
  "minSegmentLength": 3,      // shorter units merge with a neighbour
  "segmenter": "mecab",       // "mecab" (bundled dic) or "os" (lightweight)
  "minDisplayMs": 120,        // minimum display time = top speed
  "maxPendingSteps": 5,       // wheel input buffer cap
  "fontSize": 44,
  "orpEnabled": true,         // red ORP highlight for English
  "showStats": true,          // reading stats when a session ends
  "blockedProcesses": []      // apps where the hotkey is ignored
}
```

A bare `"MiddleClick"` hotkey conflicts with browser middle-click features; prefer a modified chord like `"Ctrl+MiddleClick"`.

## How it works

Selected text is captured via UI Automation (clipboard fallback with restore), a low-level mouse hook takes over the wheel during a session, and a click-through topmost overlay renders the units. Japanese is tokenized by NMeCab with the bundled IPA dictionary (POS-driven bunsetsu assembly), then length-balanced so most units land in 3–7 characters.

## Known limitations

- Apps running elevated require Scroll Reader to run elevated too
- The clipboard fallback restores text content only (not images etc.)
- Segmentation is heuristic and occasionally imperfect

## Roadmap

- [ ] macOS / Linux support

## License

[MIT](LICENSE)

---

## 日本語

視線を動かさずに読む、Windows 用の RSVP スタイル読書ツールです。任意のアプリでテキストを選択して `Ctrl+Alt+R` を押すと、文節／単語単位の拡大テキストがカーソル位置に 1 つずつ表示されます。

- **ホイール下**: 自動送り開始、さらに下で加速（`▶3` 表示）。**上**: 減速 → 停止 → コマ送り巻き戻し
- **Ctrl押下中**: もう一方のホイールモードに一時切替（クルーズ ⇄ ステップ、速度は記憶）
- **Alt+マウス移動**: 表示位置を移動。**未選択でホットキー**: 前回の続きから再開
- **終了**: 最後の文節で一呼吸おいてもう一度下 / `Esc` / クリック / 任意のキー
- 読み返し中はグレー表示。英語では ORP（最適認識点）が赤くハイライトされ同じ位置に揃います
- 設定はトレイ右クリック →「設定を開く」（`%AppData%\ScrollReader\settings.json`）。保存で即反映。各項目はファイル内コメント参照
- ビルドは .NET 8 SDK 以降で `dotnet build` / `dotnet test` / `dotnet run --project src/ScrollReader`
