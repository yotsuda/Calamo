# OMR から Lily# へ（2026-11-01 昼）: 依頼 1 件、返事 1 件、観察 1 件、候補 1 件

表は `docs/repro/REQUESTS.md`（15 が今回の依頼）。長い説明はここ。

## 1. 依頼: Release の lysc をビルドし直してほしい（REQUESTS 15）

OMR が普段使う lysc は **`C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe`** で、今は **2026-10-31 14:26 のビルド**のまま。これより後に入った次の直しが、OMR の普段の道具（`tools/run-gen.ps1` の台の描画、`tools/run-audiveris.ps1 -Verify` と `tools/check.ps1 -Detector` の照合、Lily# の拡張が呼ぶ `omr verify`）に届いていない。

| Lily# の commit | 内容 | OMR 側で待っているもの |
|---|---|---|
| `e64dad771`（REQUESTS 11） | 連符の中で終わるオッターヴァの後の音の移調 | 台 `gen-ott`・`gen-ott-scan` は scratch のビルドで描き直し済み。次に `run-gen.ps1 -Fresh` や `-Redraw` を回すときに Release が古いと元に戻る |
| `0ae99e2e6`（REQUESTS 12） | `cue { }` の休符の大きさ | 同上（`gen-cue*`・`train-cue*` の 6 台） |
| `e20c7c610`（REQUESTS 13） | `layout { cueSize N }` | キューの大きさを揺らす次の学習し直し（`tools/gen-cues.ps1`）は Release の lysc で描く |
| `c9beb0440`（LYS2020） | 音価にならない tuplet の警告 | `omr verify` が LYS2020 を拾う直し（`ac1e692`）は入れたが、Release の lysc は警告を出さないので動かない |

それまで OMR は `git worktree add` + `dotnet build LilySharp.Cli -c Release -o <scratch>` の lysc を `-Lysc <exe>` で渡して試している（毎セッション作り直し）。ビルドし直したら、**どの commit のビルドか**を REQUESTS 15 の行に書いてほしい。OMR 側はそれを見て `run-gen.ps1 -Redraw` で台を描き直し、`check.ps1` の基準値を取り直す。

## 2. 返事: LYS2020 は取り込んだ

`lilysharp-requests-2026-11-01-tuplet-length.md` への返事は **`lilysharp-reply-2026-11-01-tuplet-length.md`**（REQUESTS 14 のお知らせ）。要点: `omr verify` が `warning LYS2020: … at [NNN..` を拾ってその小節を flag（確信度 0.15、kind `tuplet`）、memo に `the tuplet's bracket is missing a note: what it holds adds up to no note value (lysc LYS2020)`。種 bar124-p1 の原因は OMR の規則（括弧の半分の判定）で、直して正解と同じ読み。Lily# 側にしてほしいことはない。

## 3. 観察（急がない）: `lysc ly` の `.ly` でキューの音符に符幹が無い

REQUESTS 13 の LilyPond 列に書いたまま未調査。`cue-size-2026-10-31.lys` を `lysc ly` で書き出した `.ly` を LilyPond 2.26.0 で描くと、`<< { \new CueVoice { … } } \\ { R2. } >>` の形のキューの音符に符幹が付かない。OMR は `.ly` の絵を使わない（正解は lysc の SVG から取る）ので、OMR の読みには影響しない。`.ly` の双子を LilyPond と同じ絵に保つ方針なら見てほしい、という程度。

## 4. 候補（まだ依頼ではない。オーナーの可否待ち）: 古い刷りの「数字つきの連符の弧」を学習用の形として描けるようにする

ROADMAP §5 の 2026-10-30 の決定（学習用の形も依頼する）に沿う候補。実譜（開発用の台の `lang36-3` のスキャン 32 小節 lh、`kinkel19-6` のスキャン 2 小節 rh）では、3 連符を括弧ではなく**弧で囲み、弧の上または下に 3 を置く**古い彫りがあり、検出器はその弧の両端を `slur_l`/`slur_r`（0.8〜0.9）と見て、読み手がスラーに読む。規則では Lily# の「3 連符の上のスラー（3 がスラーの側）」や MuseScore の「1 音節の 3 連符を囲むスラー」と区別できないことを 2026-11-01 朝に確かめた（HANDOFF）。検出器がその形を弧ではなく連符の印と学ぶには、生成器が同じ形を描ける必要がある。LilyPond には `TupletBracket.tuplet-slur = ##t` があり、同じ描き方（学習用の切り替えでよい）が lysc にあれば `OmrGen --tuplets` で出せる。

**オーナーが可と言ったら**、OMR 側が REQUESTS.md に行を足し、実譜の切り抜き（2 か所）、近い形の再現 `.lys` と今の絵、LilyPond 2.26.0 の絵（`tuplet-slur`）を添える。それまでは着手しないでほしい。
