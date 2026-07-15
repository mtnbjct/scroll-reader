# Scroll Reader

視線を動かさずに読む、Windows 用の RSVP スタイル読書ツール。

テキストを選択してホットキー（既定: `Ctrl+Alt+R`）を押すと「読書モード」に入り、マウスホイールを回すたびに、文節（日本語）／単語（英語）単位に区切られた拡大テキストがカーソル位置に 1 つずつ表示されます。下に回せば読み進み、上に回せば戻れます。

> Scroll Reader is an OS-wide RSVP-style reading tool for Windows. Select any text, press the hotkey (default `Ctrl+Alt+R`), and scroll the mouse wheel to step through the text one Japanese bunsetsu / English word at a time, displayed enlarged at the cursor position.

## 使い方

1. `ScrollReader.exe` を起動する（タスクトレイに常駐します）
2. 任意のアプリでテキストを選択する
3. `Ctrl+Alt+R` を押して読書モードに入る
4. マウスホイール下回転で読み進む、上回転で戻る
5. 終了: 最後の文節で一呼吸おいてもう一度下回転 / `Esc` / マウスクリック / 任意のキー入力 / もう一度 `Ctrl+Alt+R`

読書モード中、ホイールイベントは Scroll Reader が受け取るため、下のアプリはスクロールしません。

### 読み飛ばし防止

ホイール入力はそのまま反映せず、バッファして一定のテンポで再生します。

- 1 文節あたり最短 80ms の表示時間を保証（勢いよく回しても、するするっと流れるだけで飛ばない）
- 保留ステップは ±5 でクランプ（大量に回した分は破棄され、暴走しない）
- 逆回転は保留ステップを相殺するブレーキとして働く
- 最後の文節では一旦停止し、0.3 秒以上おいてもう一度下に回したときだけ終了

### 巻き戻し表示

到達済みの最前線より手前に戻って表示しているときは、テキストがうっすらグレーになります。白に戻れば最前線（未読の先頭）です。

## 仕組み

| 要素 | 実装 |
|---|---|
| 選択テキスト取得 | UI Automation の TextPattern（第一候補）→ 失敗時は Ctrl+C 送信＋クリップボード読み取り（元の内容は復元） |
| ホイール捕捉 | 低レベルマウスフック（WH_MOUSE_LL）。読書モード中のみインストール |
| 表示 | 最前面・クリック透過・非アクティブ化のオーバーレイウィンドウ（WPF） |
| 日本語分節 | Windows 組み込みの `Windows.Data.Text.WordsSegmenter` ＋ 助詞・助動詞を前の語へ結合する文節化ヒューリスティック |
| 英語分節 | 空白区切り |

## ビルド

.NET 8 SDK 以降（Windows 10 19041 SDK ターゲット）が必要です。

```
dotnet build
dotnet test
dotnet run --project src/ScrollReader
```

## 既知の制約

- 管理者権限で動作しているアプリのテキストは、Scroll Reader も管理者権限で起動しないと取得・フックできません（Windows の仕様）
- クリップボードフォールバック時、テキスト以外のクリップボード内容（画像など）は復元されません
- 文節分割はヒューリスティックであり、完全ではありません

## ロードマップ

- [ ] 設定 UI（ホットキー・フォントサイズ・表示位置・速度カーブ）
- [ ] 禁止アプリリスト（プロセス名ベース）
- [ ] クルーズモード（勢いよく回すと自動送り、逆回転で停止）
- [ ] 英語向け ORP（最適認識点）アライメント
- [ ] macOS / Linux 対応

## ライセンス

[MIT](LICENSE)
