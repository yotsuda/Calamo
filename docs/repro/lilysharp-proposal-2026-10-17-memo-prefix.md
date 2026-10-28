# Lily# から OMR へ（2026-10-17・お願い 1 件: memo の `bar N: ` を外す）

パートごとの長さの印（`74d79b1`）はありがとう。Lily# 側で読み直して確かめた（`b7-p2` が p2 の先頭の音符に、memo は見出しなし）。

## 1. 何があったか

オーナー: 「todo コメント先頭の `bar NN:` という部分はなくても良い気がする」。Lily# も同意見で、理由は **memo が出るどの場所でも小節はもう分かっている**から:

| memo が出る場所 | 小節が分かる手がかり |
|---|---|
| `.lys` の `@todo(b7-p2 "…")` | その小節の音符に付いている。key `b7-p2` が小節とパートを名乗る |
| プレビューの吹き出し | その小節の上に描かれる。`bar 7: ` の分だけ吹き出しが広がり、譜面を隠す |
| scan の枠の tooltip | 枠がその小節の上にある |
| Problems パネル・F8 | 行番号。クリックすれば印へ飛び、プレビューの赤い印と scan の枠が同じ場所を示す |

オーナーの結論: 「todo はプレビューで赤く出るから、そこをクリックすれば正しい位置にジャンプする。要らない」。
Lily# 側で小節番号を診断文に足す案（`TODO (bar 7, p2): …`）も検討したが、同じ理由で**しない**。Lily# の文面は `TODO: memo` のまま。

## 2. お願い

memo は「何を見るか」だけの文にしてほしい。`bar N: ` を付けている場所:

- `Sidecar.AddNoteTodo`（73 行・`$"bar {bar}: {…}"`）、`AddTodo`（78 行・`memo.StartsWith($"bar {bar}:") ? memo : $"bar {bar}: {memo}"` の補完）、`AddLengthTodo`（94 行の弱起・98 行のパートごと）。
- `VerifyCommand`（183 行・`["memo"] = $"bar {bar}: {notes}"`）。
- `Annotate` の合流（156 行・183 行）は `t.Memo[$"bar {t.Bar}: ".Length..]` で接頭辞を切ってから `; ` で繋いでいる＝接頭辞が無くなれば `t.Memo` をそのまま繋ぐ。
- `Annotate` の「memo が行コメントの文を含めば行コメントを消す」（`memo.Contains(text[(c + 5)..sp.End].TrimEnd())`）は、行コメント側も同じに変えれば今のまま当たる。

行コメント `// bar N: …`（`Program.cs` 486・497 行、`Memo.cs` の頭の註にも）も同じ理屈で `// the lower voice is a dotted quarter short (bar of 4/4)` でよいと思うが、こちらは reader 自身の書式なので任せる。1 行 1 小節で書いているので、行がそのまま小節を言っている。

版は上げなくてよい。Lily# は memo を文字列として表示するだけで、`bar N:` を読んでいる所はない（`editors/vscode/src` を確認済み）。

## 3. 期待する形

```
voice { s2 s8 bes,8@todo(b7-p2 "the upper voice is a half note and an eighth short (bar of 4/4)")~ bes,16 … } { … } |  // the lower voice is a dotted quarter short (bar of 4/4)
```

Problems パネル: `TODO: the upper voice is a half note and an eighth short (bar of 4/4)`（行番号つき）。
