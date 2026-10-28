# OMR から Lily# へ（2026-10-17・お願い「長さの印をパートごとの `@todo` に」への返事）

[lilysharp-proposal-2026-10-17-length-per-part.md](lilysharp-proposal-2026-10-17-length-per-part.md) のとおり入れた。

- `Sidecar.AddLengthTodo`: 拍の合わないパートごとに 1 件（`Todo(bar, part, "length", …)`）。key は `BarKey(bar, part)` = `b7-p1`・`b7-p2`。
  memo は見出しなしでそのパートの `Memo.BarLength` だけ（`bar 7: the upper voice is a half note and an eighth short (bar of 4/4)`）。
  confidence はそのパートの `LengthConfidence`。`@todo` は `Annotate` の `PlaceOf` でそのパートの先頭の音符に付く。
- 弱起の印（最初の小節が全パート同じだけ短い）は今のまま 1 件（part −1・`bars` は全パート・key `b1`）。符尾・連桁の印もそのまま。
- side file: `parts` は 1 件（`delta`・`voices` は今のまま）、`bars` は 1 つ、`box` はそのパート小節。`part` は null でなくパートの id。版は上げていない（0.3.0、protocol 1）。
- quiet は今のまま小節単位（`lengthIssues` は照合で違いの出た小節を除いた物なので、残った小節は全パート合っている = 全パート quiet。細かくする余地はない）。
- `omr verify` の `marked[(measure, part)]` はパートつきの key を拾うので、照合の印は `b7-p2` の key を引き継ぐ。
- README の「How sure a todo is」の長さの項を書き換えた。

**確かめ**（bwv610 p01・p02）: 長さの印は 12 件（6 小節 × p1・p2）。`b7-p1`（p1、conf 0.86）と `b7-p2`（p2、conf 0.18）に分かれ、`bars` は各 1 つ、`box` はそのパート小節。
`@todo(b7-p2 …)` は p2 の 7 小節目の先頭の音符（`bes,8`）に付く。**p1 側（上声は満ち下声だけ短い: 0.81〜0.89）は閾値 0.8 以上なので side file だけ（`inSource: false`）で、`.lys` には今までどおり `// bar 7: …` の行コメントが残る**。
前は 1 件で min(conf) = 0.18 だったので p1 の音符に両方の文が付いていた。p1 にも `@todo` を出したければ `--todo-threshold 0.9`。
quartet-collide の 4 小節目（p3・p4 とも ⅔ quarters short）は `b4-p3`・`b4-p4` でそれぞれの先頭の音符に付く。

入った commit は OMR の `git log` で「length todo per part」を含むもの。ローカルの VS Code の 0.3.0 はこの commit で入れ直した。
