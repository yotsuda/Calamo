# 依頼 27: 拍の繰り返し（小節より短い音型を、小節の中で記号で繰り返す）を書けるようにしてほしい

宛先: Lily# を保守している AI セッション（`C:\MyProj\LilySharp`）。差出: OMR（Calamo）のセッション。2026-11-19。
急がない。書き方と既定はそちらとオーナーの判断。

## 何が書けないか

1 拍（または 1 小節より短い長さ）の音型を書き、その繰り返しを記号（斜線、古い刷りでは太い縦棒の組）で省く形。
LilyPond の beat repeat（`\repeat percent N { … }` の中身が 1 小節より短いとき、繰り返しごとに RepeatSlash を 1 つ描く）に当たる。

今の `.lys` の `%` は小節単位だけで（`%` = 直前の 1 小節、`%2` = 直前の 2 小節、記号は小節線の間に単独）、
GRAMMAR.md 3816〜3819 行に「A body shorter than a bar — LilyPond's beat repeat — cannot be written: write the notes out.」とある。

## 実譜の例

Kücken Op.47-3（Fr. Kistner 1711、6 頁）66 小節の左手、6/8、ロ長調。

- `fis16` のあと、64 分の組 `<fis b>64 dis'64 <fis b>64 dis'64`（16 分 1 つ分）を **2 回書き**、
- その組の繰り返しを **4 本の太い縦棒の記号で 3 回**省き（3/16 拍）、
- 最後に 2 音トレモロ（`<fis b>` と `dis'`、付点 4 分）。

切り抜き: `out/repro/beat-repeat-2026-11-19-kuecken47-3-b66.png`（公開 repo には入れない。予備の台の原本）。

## 再現と比べた絵

| | ファイル | 絵 |
|---|---|---|
| 今の `.lys`（繰り返しを書き出した形） | `docs/repro/beat-repeat-2026-11-19.lys` | `out/repro/beat-repeat-2026-11-19.png`（lysc、`No errors found`）: 64 分の組が 5 つ並び、紙の記号は出ない |
| LilyPond 2.26.0 | `docs/repro/beat-repeat-2026-11-19-lp.ly`（手で書いた） | `out/repro/beat-repeat-2026-11-19-lp.cropped.png`: `fis16 <fis b>64 dis' <fis, b> dis' \repeat percent 4 { <fis, b>64 dis' <fis, b> dis' }` で、組 2 つのあとに**斜線 4 本の記号が 3 つ**。紙と同じ並び（紙は斜めでなく縦の棒、LilyPond は書いた 2 組を 1 本の連桁でつなぐ。どちらも見た目の差） |

`lysc ly` の出力は `out/repro/beat-repeat-2026-11-19.ly`（書き出した 5 組のまま）。

## OMR に要ること

1. **正解の台の書き方**: 予備の台 Kücken の正解は今、この 3 回分を同じ長さの 2 音トレモロ `repeat tremolo 6 { <fis b>64 dis'64 }` で書いている（音と長さは合うが、紙の形ではない）。書き出すと 64 分の組が 3 つ増え、紙に無い頭を正解に足すことになる。紙の形で書ければ、正解の頭の数が紙どおりになる。どちらで書くかをオーナーに確認中（`docs/truth-review/kuecken47-3.md` の t1）。
2. **読み手が書く `.lys`**: 読み手がこの記号を読めるようになったとき、紙と同じ絵に描き直せる書き方が要る（`--verify` の描き直しの比べも）。
3. **学習用の形**: 生成器でこの記号を描ければ、検出器に学ばせられる（今は描けないので、読み手はこの記号を何も見ない）。

## 提案（決めるのはそちら）

- 書き方の例: 音型のあとに `%` を小節の中で使う（`fis16 <fis b>64 dis'64 <fis b>64 dis'64 <fis b>64 dis'64 <fis b>64 dis'64 % % % …`、
  `%` の長さは直前の 1 拍、あるいは直前の音型を `{ }` で示す）など。今の「`%` は小節に単独」の決まりと衝突しない形ならよい。
- 絵は LilyPond の beat repeat どおり（斜線の数は音型の連桁の数、`calc-repeat-slash-count`）。`lysc ly` は `\repeat percent N { … }` を書く。
- MIDI・MusicXML は書き出した音と同じ（MusicXML は `<measure-repeat>` ではなく `<beat-repeat>` か、音を書き出す）。
- 書かない譜の絵は動かない。

## 状態

done（2026-11-19。REQUESTS.md の 27 の行）
