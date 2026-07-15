# Scroll Reader

視線を動かさずに読む、Windows 用の RSVP スタイル読書ツール。

テキストを選択してホットキーを押すと、文節（日本語）／単語（英語）単位の拡大テキストがカーソル位置に 1 つずつ表示されます。マウスホイールで読み進み、戻ることもできます。

> An OS-wide RSVP-style reading tool for Windows. Select any text, press the hotkey, and read it one Japanese bunsetsu / English word at a time with the mouse wheel — without moving your eyes.

## 使い方

1. `ScrollReader.exe` を起動（タスクトレイに常駐）
2. 任意のアプリでテキストを選択し、`Ctrl+Alt+R`
3. ホイール下: 自動送り開始、さらに下で加速（`▶3` 表示）。上: 減速 → 停止 → コマ送り巻き戻し
4. 終了: 最後の文節で一呼吸おいてもう一度下 / `Esc` / クリック / 任意のキー

- 読書モード中のホイールは Scroll Reader が受け取り、下のアプリはスクロールしません
- 読み返し中（未読の最前線より手前）はテキストがグレーになります
- 英語テキストでは ORP（最適認識点）が赤くハイライトされ、常に同じ位置に揃います
- `"wheelMode": "step"` で 1 ノッチ = 1 文節のコマ送りに変更可

## ビルドと実行

.NET 8 SDK 以降が必要です。

```
dotnet build                            # ビルド
dotnet test                             # テスト
dotnet run --project src/ScrollReader   # 実行
dotnet publish src/ScrollReader -c Release -o publish   # 配布用ビルド
```

## 設定

タスクトレイ右クリック →「設定を開く」（実体: `%AppData%\ScrollReader\settings.json`）。保存すると即反映されます。各項目の説明はファイル内のコメントを参照してください。

```jsonc
{
  "hotkey": "Ctrl+Alt+R",     // "Ctrl+MiddleClick"（Ctrl+ホイールクリック）等も可
  "wheelMode": "cruise",      // "cruise" または "step"
  "cruiseBaseMs": 350,        // クルーズ最低速（1文節あたりms）
  "maxSegmentLength": 7,      // 日本語の表示単位の最大文字数
  "minDisplayMs": 120,        // 最短表示時間 = 最高速度
  "maxPendingSteps": 5,       // 先読みバッファ上限
  "fontSize": 44,
  "orpEnabled": true,         // 英語のORPハイライト
  "blockedProcesses": []      // 無効にするアプリ（プロセス名）
}
```

素の `"MiddleClick"` はブラウザの中クリック操作等と競合するため、修飾キー付きを推奨します。

## 既知の制約

- 管理者権限のアプリに対しては、Scroll Reader も管理者権限で起動する必要があります
- クリップボードフォールバック時、テキスト以外のクリップボード内容は復元されません
- 文節分割はヒューリスティックであり、完全ではありません

## ロードマップ

- [ ] 設定 GUI
- [ ] macOS / Linux 対応

## ライセンス

[MIT](LICENSE)
