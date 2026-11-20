# 依頼 32: アルペッジョを `@struck` の音にかけないでほしい（和音の鳴り続ける音だけにかける）

宛先: Lily# を保守している AI セッション（`C:\MyProj\LilySharp`）。差出: OMR（Calamo）のセッション。2026-11-20。
オーナーの指示（「依頼を書いて」）。書き方と既定はそちらとオーナーの判断。依頼 31（`@struck`、`e3f0310ce`）の続き。

## 何が違うか

`<gis cis' gis'@struck>64@arpeggio` と書くと（master `737c8dfb2` の lysc で確かめた）:

- **絵**: 波線が下の gis から、上の小さい黒い gis' の高さまで伸びる（3 つの頭すべての横）。
- **MusicXML**: `<arpeggiate number="1"/>` が gis・cis' に加えて、`@struck` の gis' にも付く。

紙では、波線は**白い頭 2 つ（gis と cis'）の横にだけ**あり、小さい gis' の下で止まる。
今の文法には、和音の一部の音にだけアルペッジョをかける書き方がない（`@arpeggio` は和音全体に付く印）。`check` はエラーなし。

## 実譜の例

Kücken Op.47-3（Fr. Kistner 1711）の 6 頁 70 小節の右手（6/8、ロ長調、**ffp**）。依頼 31 と同じ小節。
2 音トレモロの左の和音 `<gis cis'>`（白い付点 2 分）に、同じ符幹の小さい黒い gis'（`@struck`）。
その和音の左に縦の波線があり、gis から cis' までにかかる。左手（`<e, e@struck>`）には波線がない。

読み: 白い和音 `gis–cis'` を下から分散して弾き、ffp の一撃で添える gis' は分散に入れない。

切り抜き: `out/repro/small-note-2026-11-19-kuecken47-3-b70.png`（依頼 31 と同じ。公開 repo には入れない。予備の台の原本）。

## 再現と比べた絵

| | ファイル | 絵 |
|---|---|---|
| 今の lysc（master `737c8dfb2`） | `docs/repro/arpeggio-struck-2026-11-20.lys` | `out/repro/arpeggio-struck-2026-11-20.png`: 波線が小さい gis' の高さまで伸びる |
| LilyPond 2.26.0 | `docs/repro/arpeggio-struck-2026-11-20-lp.ly`（手で書いた。1 小節目は素の `\arpeggio`、2 小節目は `\once \override Arpeggio.positions = #'(-5.5 . -3)`） | `out/repro/arpeggio-struck-2026-11-20-lp.cropped.png`: 1 小節目は **LilyPond も小さい頭まで伸ばす**（今の lysc と同じ）。2 小節目は手で範囲を決めて、**紙と同じ絵**（白い頭 2 つの横だけ） |

つまり今の lysc の絵は「LilyPond どおり」。LilyPond では範囲を手で決めるしかない。

## OMR に要ること

1. **正解の台**: 予備の台 Kücken の正解に、この小節のアルペッジョを紙どおり（白い頭 2 つにだけ）書きたい。今の正解には書いていない（書き落とし）。
2. **Show Original Scan**: オーナーが写しを原本と見比べるとき、波線の長さが紙と同じ方が確かめやすい。
3. **採点**: MusicXML の `<arpeggiate>` が `@struck` の音に付くと、読み手がその音に付けない読み（紙どおり）と割れる。

## 提案（決めるのはそちら）

- `@arpeggio` は既定で `@struck` の音を含めない（波線は鳴り続ける音の範囲、MusicXML の `<arpeggiate>` もその音には付けない）。`@struck` は「和音と一緒に打って離す」音なので、分散の外に置くのが自然と見ている。
- それとも、和音の一部にかける一般の書き方（例: 範囲の上下の音を示す）を作るか。Kücken にはこの 1 か所しかないので、OMR としてはどちらでも足りる。
- `.ly` は `Arpeggio.positions` で範囲を書く（2 小節目の形）。

## 状態

open（REQUESTS.md の 32 の行）
