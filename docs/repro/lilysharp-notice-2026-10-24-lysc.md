# Lily# から OMR へ（2026-10-24・お知らせ: 利用者の機械では照合に使う lysc が見つからない）

Lily# 側で「Fix OMR Marks with AI…」を、使い捨ての VS Code で通しで動かした（偽の模型を登録し、オーナーの Largo を Scan Import 0.3.1 で読み直したもので）。照合の門（`omr verify --candidate --bars --no-write`）は最初、走らなかった。

```
bar 15 (p2): verify did not run (exit 2): no lysc found (--lysc, beside the OMR, or on the PATH)
```

## 何が起きているか

- `omr` は lysc を「`--lysc` の引数 → `omr.exe` の隣 → PATH」の順に探す（`ReadCommand.FindLysc`）。
- Lily# は拡張の `server\lysc.exe` があれば `--lysc` で渡す作り（`bundledLysc`）。しかし **Marketplace の Lily# の VSIX には lysc が入っていない**（`server` は言語サーバーだけ。lysc は GitHub Release の `lysc-*.zip` で別に配っている）。
- **Scan Import の拡張の `bin` にも lysc は無い**（0.3.1 で確かめた）。
- したがって利用者が lysc を自分で PATH に入れていない限り、次の 3 つは照合なしで動く。
  1. 取り込みの `omr read --verify`（`read: no lysc found, no redraw check` の警告だけ出る）
  2. Lily# の `lilysharp.omr.verifyOnSave`
  3. AI 修正の門 3（「verify —」のまま）

開発の機械では lysc がどこかにあるので気づかなかった。オーナーの B7 の確認で「取り込み直後に照合の `@todo` が付く」が通ったのも、`LILYSHARP_OMR_PATH` で開発の reader を指していたときかもしれない。

テストでは、使い捨ての VS Code の PATH に開発用の lysc を足したら、門 3 は正しく働いた（食い違う提案に「the scan says: bar 15: in the text, not on the scan: a ♮」・1 回 3 秒）。

## 直し方の候補（どれにするかはオーナーの判断）

| 案 | 中身 | 重さ |
|---|---|---|
| A | Lily# の VSIX の `server` に lysc を同梱する（言語サーバーと同じ .NET の上に置けば数 MB 増える程度の見込み） | Lily# の出荷の手順を直す。拡張が全部の照合に lysc を渡せる |
| B | Scan Import の `bin` に lysc を同梱する（`omr.exe` の隣） | OMR の出荷の手順を直す。lysc の版が Lily# とずれうる |
| C | 言語サーバー自身に lysc の役（`lysc svg` 相当）を持たせ、`omr` にそれを呼ばせる | 両側の取り決めの変更。いちばん重い |

Lily# 側の見立ては A（lysc の版が描画の正本である Lily# と常に揃う）。決まったら、どちら側が何をするかを REQUESTS.md に書く。

返事は要らない（オーナーの判断を待つ）。
