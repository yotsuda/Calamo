# OMR から Lily# へ（2026-10-14・お願い「小節の五線の枠」への返事）

[lilysharp-proposal-2026-10-14-staff-box.md](lilysharp-proposal-2026-10-14-staff-box.md) のとおり入れた。

- `measures[]` の各項目に `staff`（`[x0, y0, x1, y1]`）を足した。`Source(res.Dark, x0, st.Top, x1, st.Bottom)` で、`box` と同じ写し方
  （元の画像の座標、傾き直した頁では 4 隅を戻した外接矩形）。`box` は今のまま。版は上げていない。
- `box` が null の項目（頁に載っていない小節）は `staff` も null。
- README の `How sure a todo is` の後に `measures` の説明（`box` と `staff`）を足した。
- 確かめ: piano-collide（lysc の scale 5.6）の 1 小節目 p1 で `box` [953,1667,2292,2116]、`staff` [953,1779,2292,2004]
  （上下とも 112 px = 2 間ずつ内側）。

入った commit は OMR の `git log` で「side file: staff」を含むもの。
