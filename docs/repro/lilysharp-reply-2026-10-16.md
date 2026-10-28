# Lily# から OMR へ（2026-10-16・`lilysharp-feedback-2026-10-16.md` の 4 件への返事）

| # | 結果 | Lily# 側 |
|---|---|---|
| 1 | **直した** | `R<dur>*N` は dur × N の長さを拍子の整数小節として読む（`BarRest.WholeBars`） |
| 2 | **直した** | 休みの数字を五線の輪郭（skyline）に入れた。題名との間が空く |
| 3 | **直した** | 下の五線の松葉は自分の五線の列から始まる（`HairpinEngraver.CalculateStartX/EndX`） |
| 4 | **直した** | 括弧の輪郭を両端で半ペン（0.05）広げた（LP の `Line_interface::make_line` と同じ）＝松葉の先と重なって衝突回避が働く |

入った commit は Lily# の `git log` で「whole bars of the meter」を含むもの。確かめは `lysc png --scale 2 docs/repro/*.lys`（Lily# の Lab `sessions/p907/png/` に絵がある）。

## 1. 5/4 などの全休符・複数小節の休み

- `R4*5` は 5/4 の 1 小節の休み（全休符 1 つ・小節の中央）、`R4*40` は 8 小節（教会式の休符と数字の 8）、`R8*20` は 2 小節。`R1*3` を 3/4 に書けば 4 小節（LilyPond と同じ）。
- 整数小節にならないもの（`R4*3` を 5/4 に）は今までどおり dur の休符 N 個で、小節の検査（LYS2001）がそう言う。
- 規則は 1 か所 `Music.BarRest.WholeBars` に置き、譜面（collector）・小節数の数え（`MeasureModel`・`CountBarsInScope`）・検証器・MusicXML が全部そこを読む。**空の小節が後ろに 32 個付く**のは、小節数を数える側が N 小節と数えていたため。
- **`R1*5/4` は Lily# の文法にない**（`*` の後ろは整数だけ）。`R1*5` ＋ スラッシュ音符 `/4` と読まれて「4」の休みと斜線が描かれる（返事の時点では LYS2002 も出る）。`R1*5/4*2` も同じく。OMR の書き出しは `R4*5N` のままでよい（`FullRest` がそう書くなら変更不要）。文法に `*N/M` を足すかはオーナー判断（今回は足していない）。
- `R*N`（音価なし）は今までどおり拍子に関係なく N 小節。

## 2. 最初の段の休みの数字が題名にぶつかる

- 数字（`MultiMeasureRestNumber`）は LilyPond では outside-staff-priority を持たない「五線の内側の墨」なので、五線自身の輪郭（inside skyline）と段の輪郭（system skyline）に数字の箱を足した（`MultiMeasureRestEngraver.SeedCountNumbers`）。題名→段のばねの床と、段の間隔がそれを見る。
- `mmrest-count-under-title-2026-10-16.lys` で「4」と「ties003」の間が空いた。`AddMultiRests` の `rng.Next(6, bars)` は `rng.Next(1, bars)` に戻せるはず。

## 3. 大譜表で松葉が別の列から描かれる

- 原因: 松葉の両端の列を、段の MeasureLayout の `Items`（＝**最初の五線**の item）に松葉の item 番号で引いていた。左手の item 番号が右手の item 数を超えると「見つからない」＝小節の頭から、超えないと右手の同じ番号の列から描かれていた。
- 多段では `LayoutUtilities.GetItemXOffset`（自分の五線の小節の item の時刻 → 列）で引くようにした。`hairpin-start-grandstaff-2026-10-14.lys` の 2 小節目の crescendo は `fis,,16` から始まる。1 段のときは変わらない。

## 4. ペダルの括弧の終わりのかぎと松葉の先

- `hairpin-over-pedal-bracket-2026-10-14.lys` を LilyPond 2.26.0 で描き、SVG の `<line>` を数値で読んだ（`lysc ly --pin-fonts` → `lilypond -dbackend=svg`）。3 小節目（五線の下端から）: 松葉の線は LP 2.213 間・Lily# 2.21 間で同じ。括弧の線は LP 3.776 間・Lily# 3.25 間＝**LP はかぎの上端を松葉の線の 0.563 下に置いている**（松葉の半ペン 0.05 ＋ outside-staff-padding 0.46 ＋ かぎの半ペン 0.05）＝衝突回避で押している。
- 原因: LP の線の stencil の箱は両軸にペンの半分だけ広い（`lily/line-interface.cc` `make_line`）ので、同じ音符から始まる松葉と括弧の端は「接する」のではなく 0.05 重なる。Lily# の括弧の箱は端で止まっていて、skyline の歩きは接点の対を訪ねない。括弧の箱（`PedalEngraver.BracketStencilBox`／`BracketUpProfile`）を x に ±0.05 広げた → 括弧は 3.61 間、かぎの上端は松葉の線の 0.4 下。
- 残り 0.16 間は、Lily# が段を組む前の「部屋」で松葉を `@p` との組み付け前の高さで予約する既知の差（別件）。

## 返事の時点のその他

- 第 1 項の修正で `docs/repro/multibar-rests-2026-10-14.lys` の `title ""` も戻せるはず（#2）。
