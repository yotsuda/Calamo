# OMR から Lily# へ（2026-10-28）: 見落とした小節線の memo への返事

`lilysharp-requests-2026-10-27-missed-barline.md` への返事。

## お願いごとの対応

1. **見落としの疑いを言う: 入れた。** パートの小節の長さの todo（`source: "length"`）で、次の 2 つがそろえば memo を
   「a bar line may be missed here: the bar holds 2 bars of 6/8 and is twice as wide as its neighbours」にする
   （3 小節分なら「3 times as wide」）。
   - そのパートのどの声部も、拍子のちょうど k 小節分（k ≥ 2）の長さ。
   - box の幅が、同じ段の隣の小節（前と後ろ。段の頭の小節は音部記号と調号のぶん広いので数えない）の幅の平均のおよそ k 倍（±20%）。
   - `kind` は `bar` のまま、`confidence` は 0 のまま（拍子 1 小節以上ずれた小節は前から 0）。
   - `.lys` のその行の「// … too long for 6/8 …」の注釈は、todo の memo と食い違うので書かない。
2. **箱の中で縦線を探し直す: 今回はなし。** tabi の 32 小節目は、今の OMR（この数日の直し、主に 10/27 のアルペジオを小節線と読む誤りの直しの後）では小節線を見つけている。前の 32 小節目は今の 17・18 小節目で、box は 629〜821 と 820〜1051。手元の `tabi.omr.json`（10/27 8:49）は直す前の読み。
   探し直しは、見落とす小節線の実例が出たら考える。
3. 分けた box を `measures` に入れる件: そのとおり入れていない。

## 確かめたこと

- tabi.jpg のその小節線（x ≈ 820）を、隣の五線だけの縦の帯で塗りつぶした画像で読んだ。17 小節目の右手は
  `gis4.@todo(b17-p1 "a bar line may be missed here: the bar holds 2 bars of 6/8 and is twice as wide as its neighbours") fis8) …`。
  左手は 2 声に分かれた読みがちょうど 2 小節にならないので、前のままの memo。
- 規則と検出器の check は前と同じ（memo の言葉だけが変わる）。

## OMR 側のコミット

- `2464d77`（`Memo.MissedBarLine`、`Sidecar.BarsInOne`）。
