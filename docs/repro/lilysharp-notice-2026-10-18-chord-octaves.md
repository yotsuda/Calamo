# Lily# から OMR へ（2026-10-18・お知らせ 1 件: relative の和音のオクターブ規則を変えた）

お願いではなく、知っておいてほしい文法の変更。**OMR が書く `.lys` は `octave absolute` なので影響は無い**（下の §3 で測った）。手で relative の本を書くときの読みが変わる。

## 1. 何が変わったか

relative モードの和音 `< >`（と arpeggio `<< >>`）のオクターブの規則。オーナー決定 2026-10-18・Lily# の commit `ece474ae6`（master・**未 release**＝v0.13.0 の後。Marketplace の拡張 0.13.0 と Release の `lysc` 0.13.0 は旧規則のまま）。

**新しい規則＝和音は先頭の音として読む**（2 行）:
- 先頭の音は単音とまったく同じに読む: 文字を直前の音から nearest に置き、その音の `'` `,` を足す。そこが和音の anchor。
- 残りの音は anchor の同じかすぐ上のオクターブに積む（書く順序は問わない）。後続の音の `'` `,` はその音だけを積んだ位置から動かす。和音の後の音は anchor から数える。

**旧規則**（2026-09-27〜10-18）: anchor は先頭の*裸の文字*（先頭の `'` `,` はその音だけに効く）で、残りはその裸の anchor の上に積んでいた。オーナーの報告 `g2 <d, f>`（D1 F2）に対し `g2 <c,, f>` が C1 **F3**＝低音を下げたら上の音が上がる、が廃止の理由。

| 書き方 | 新（今の master） | 旧（0.13.0） | 次の裸の `c`（新） |
|---|---|---|---|
| `<c e g>` = `<c g e>` | C4 E4 G4 | 同じ | C4 |
| `<c' e g>` | C5 E5 G5 | C5 E4 G4 | C5（旧は C4） |
| `<c e g>'` | C5 E5 G5 | 同じ | C5 |
| `<c' e' g'>` | C5 **E6 G6**（開いた和音） | C5 E5 G5 | C5 |
| `<c g,>` | C4 G3 | 同じ | C4 |
| `<c, e g> <c, e g>` | 2 つ目は 1 オクターブ下（`c, c,` と同じ） | 同じ和音 2 回 | — |
| `<< g' e c >>` | G5 E6 C6 | G5 E5 C5 | — |

罠になりやすいのは 2 つ: **全部の音に印を付ける書き方**（`<c' e' g'>`）は開いた和音になる＝1 オクターブ上の密集は `<c' e g>`。**上から降りる arpeggio** は `<< g' e, c, >>`（または度数 `<< 8 5 3 1 >>` の形）。

変わらないもの: `octave absolute`（各音は固定・積まない・基準を持たない）、度数和音 `<1 3 5>`（主音の床）、`'` `,` を `>` の後に書く全体の移動。

規則の全文は Lily# の `docs/GRAMMAR.md` の Chord の注（候補と経緯も）・`docs/SYNTAX_REFERENCE.md`・`CHANGELOG.md` の `## Unreleased`「A chord is read as its first note」。

## 2. OMR への影響

- **`OmrGen`・`OmrProto`・`Band` が書く `.lys` は `octave absolute`**（`Program.cs:422`・`OmrGen/Program.cs:262`・`Band.cs:89`）＝absolute は積まず基準も持たないので、生成した本・読んだ本の音は 1 つも動かない。
- `omr verify` や truth との突き合わせが Lily# の読み（`lysc check --pitches`・MIDI・MusicXML）を通るなら、absolute の本は前後で同じ答え。
- 手書きの truth（`data/eval/*/check/*/truth-*.lys`・`data/bwv610/*.lys`・`docs/repro/*.lys`）に relative の本が 7 冊あるが、どれも印の付いた和音を使っていないので変わらない（§3）。
- これから relative で書く本は §1 の表の読みになる。absolute で書き続けるなら何もしなくてよい。

## 3. 測ったこと

OMR の `.lys` のうち `out\`（生成物 32,790 冊・全部 `octave absolute`）を除いた **47 冊**（`data` と `docs/repro`）を、10-17 の Release `lysc`（旧規則）と master の Debug `lysc`（新規則）の `lysc check --pitches` で比べた: **変わった本 0**。うち `octave absolute` の無い（relative の）本は 7 冊で、これも同じ。

Lily# 側のコーパス（repo 679 冊＋Lab corpora 515 冊）では 15 冊が変わり、repo の 10 冊は音が変わらないように綴り直した（旧 `lysc octave --absolute` → 新 `lysc octave --relative`・鳴る音の列で検証）。relative の本を新規則に合わせて綴り直すときはこの手順が使える。

## 4. 連絡

- 次の release（v0.13.0 の次）に入る。Marketplace の拡張と `lysc` を上げるまで、エディタのプレビューは旧規則で鳴る。
- 返事は不要。質問があれば `docs/repro/lilysharp-reply-2026-10-18-chord-octaves.md` に。
