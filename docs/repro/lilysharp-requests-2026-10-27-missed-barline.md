# Lily# から OMR へ（2026-10-27・お願い: 見落とした小節線を「長さの合わない小節」と言わないでほしい）

オーナーの観察: `C:\Users\ytsud\Downloads\tabi.png` を読んだ `tabi.lys` の 32 小節目（L45）で Edit Bars Beside the Scan を開くと、スキャンが 2 小節分切り抜かれた。

## 何が起きていたか

OMR が 32 小節目と 33 小節目の間の小節線を見落とし、2 小節を 1 小節として書いた。

| 小節 | box（頁 1） | 幅 |
|---|---|---|
| 30 | 68,984,421,1087 | 353（頭の小節・音部記号と調号つき） |
| 31 | 420,984,631,1086 | 211 |
| **32** | **629,984,1051,1087** | **422** |
| 33 | 1050,985,1268,1087 | 218 |
| 34 | 1267,985,1478,1088 | 211 |

- 32 小節目の中身は 6/8 の 2 小節ちょうど（`gis,4. fis,8 cis8( dis8 | <gis, cis eis>16 fis16 eis4~ eis8) cis8( dis8`＝付点 2 分 2 つ分）。
- todo の memo は「a dotted half note too long for 6/8: an extra note, or a duration misread?」で、余分な音か音価の読み違いを疑わせる。本当は小節線が 1 本足りないだけ。
- Lily# は長さが拍子のちょうど整数倍のとき LYS の MissingBarLine（「about 2 bars of 6/8: a bar line looks to be missing」）を出し、絵も再生もそこで 2 小節に分ける。プレビューの赤い枠が 2 つなのはそのため。

## お願い（どれも任意・判断はそちら）

1. **見落としの疑いを言う**: 小節の長さが拍子の整数倍（2 倍以上）で、**かつ** box の幅が隣の小節のおよそ整数倍（今回は 422 ÷ 約 214 ≒ 2.0）のときは、memo を「a bar line may be missed here: the bar holds 2 bars of 6/8 and is twice as wide as its neighbours」のように書く。`kind` は今の `bar` のままでよい（Lily# は `kind` を読まない）。
2. **（できれば）その箱の中で小節線を探し直す**: 長さが割り切れる位置（今回は `dis8` の後）の x のあたりで、閾値を下げて縦線を見る。見つかれば 2 小節として書く。
3. 見つからないとき、2 小節を分けた box を `measures` に入れる必要は**ない**。Lily# は側の box を見て、その段にある複数の枠を合わせて切り抜くようにした（下）。

## Lily# 側で入れたもの（2026-10-27・第996・未 push）

- **クイックフィックス「Write the missing bar line」**（エディタの電球）: MissingBarLine の小節で、拍子の切れ目ごとに ` |` を書く。切れ目をまたいで伸ばす音があれば出さない。
- **Edit Bars Beside the Scan**: 行のプレビューに、その行が持つ小節を全部出す（前は最初の 1 つだけ）。行にボタン「write the missing bar line」（LSP の新しい要求 `lilysharp/missingBarLines`）。スキャンもプレビューも **Ctrl+ホイールで拡大縮小、ドラッグでスクロール**。
- AI に直させるときのプロンプトは、切り抜きが数小節にまたがることがあると言うようにした（前の便り以降）。
