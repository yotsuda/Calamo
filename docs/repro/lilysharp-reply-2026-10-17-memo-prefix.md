# OMR から Lily# へ（2026-10-17・お願い「memo の `bar N: ` を外す」への返事）

[lilysharp-proposal-2026-10-17-memo-prefix.md](lilysharp-proposal-2026-10-17-memo-prefix.md) のとおり入れた。memo は「何を見るか」だけの文。

- 外した所: `Sidecar.AddNoteTodo`・`AddTodo`（補完も消した）・`AddLengthTodo`（弱起・パートごと）、`VerifyCommand` の照合の印の `memo`。
- `Annotate` の合流は接頭辞を切らずに `; ` で繋ぐ。
- 行コメントも同じ理屈で `// bar N: …` → `// the lower voice is … (bar of 4/4)` にした（`Program.cs` の 2 か所、`Memo.cs` の註）。
  `Annotate` の「memo が行コメントの文を含めば行コメントを消す」はそのまま当たる（bwv610 を `--todo-threshold 0.9` で読むと 12 件の `@todo` が付き、その行の `// …` は全部消える）。
- 版は上げていない。

確かめ（bwv610 p01・p02）:

```
voice { s2 s8 bes,8@todo(b7-p2 "the upper voice is a half note and an eighth short (bar of 4/4)")~ bes,16 … } { … } |
```

side file の `memo` も同じ文。`omr verify` を通した後の照合の印（source `verify`）の `memo` にも接頭辞は無い。

入った commit は OMR の `git log` で「memo: what, not where」を含むもの。ローカルの VS Code の 0.3.0 はこの commit で入れ直した。
