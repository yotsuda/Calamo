# 実譜の評価台の候補（2026-10-14）

易しく短いピアノ曲。どれも **Mutopia に LilyPond のソースがある**ので、正解はそこから自動で作り
（`chopin28-4` と同じく LilyPond のイベント出力）、IMSLP の別の版はその正解を版の違いだけ直して使う
（《詩人の恋》の B と同じ）。読み取りの調整に使う組と評価だけの組に分ける（分け方は正解ができてから決める）。

## Mutopia（取得済み: `<曲>/src/`、.ly と A4 の PDF）

| フォルダ | 曲 | ページ | LilyPond | 元にした版 | ライセンス |
|---|---|---|---|---|---|
| `burgmuller100-01` | ブルグミュラー Op.100-1「素直な心」 | 1 | 2.16.0 | Litolff | Public Domain |
| `burgmuller100-02` | ブルグミュラー Op.100-2「アラベスク」 | 1 | 2.16.0 | Litolff | Public Domain |
| `clementi36-1` | クレメンティ ソナチネ Op.36-1（全楽章） | 3 | 2.18.2 | Schirmer 1893 | Public Domain |
| `schumann15-1` | シューマン《子供の情景》1「見知らぬ国と人々について」 | 1 | **2.6.0**（`convert-ly` が要る見込み） | Leichte Stücke 1900 | Public Domain |
| `schumann15-7` | シューマン《子供の情景》7「トロイメライ」 | 2 | **2.6.0**（同上） | 同上 | Public Domain |
| `tchaikovsky39-1` | チャイコフスキー《子供のアルバム》1「朝の祈り」 | 1 | 2.18.2 | Schirmer 1904 | Public Domain |
| `tchaikovsky39-5` | チャイコフスキー《子供のアルバム》5「木の兵隊の行進」 | 1 | 2.18.2 | Schirmer 1904 | Public Domain |

Mutopia の PDF は LilyPond の浄書（Lily# に近い見た目）なので、それ自体は「LilyPond の PDF」の台になる。

## IMSLP（**手で落とす**: スクリプトからは人間の確認（CAPTCHA）が出て取れない）

ブラウザで `https://imslp.org/wiki/Special:ImagefromIndex/<番号>` を開いて落とし、右の場所に置く。
曲集のファイルは全部のままでよい（使うページはこちらで切り出す）。

| 番号 | 曲 | 版 | 種類 | ページ | ライセンス | 置き場所 |
|---|---|---|---|---|---|---|
| 935040 | ブルグミュラー Op.100（全曲） | Ruiming Zhang 2024 | **新しい浄書** | 37 | CC BY 4.0 | `burgmuller100-01/imslp/zhang-2024.pdf` |
| 337046 | ブルグミュラー Op.100（全曲） | Schirmer 1903（Oesterle） | スキャン | 36 | PD | `burgmuller100-01/imslp/schirmer-1903.pdf` |
| 725302 | クレメンティ Op.36（全曲） | Nicholas Bosse 2021 | **新しい浄書** | 40 | CC BY 4.0 | `clementi36-1/imslp/bosse-2021.pdf` |
| 73600 | クレメンティ Op.36-1 | Schirmer c.1893（Köhler） | スキャン | 6 | PD | `clementi36-1/imslp/schirmer-1893.pdf` |
| 67290 | シューマン Op.15（全曲） | Richard Johnson Editions 2010 | **新しい浄書** | 17 | CC BY-NC 3.0 | `schumann15-1/imslp/rje-2010.pdf` |
| 289791 | シューマン Op.15（全曲） | Breitkopf 全集 1880（Clara Schumann） | スキャン | 10 | PD | `schumann15-1/imslp/breitkopf-1880.pdf` |
| 154626 | チャイコフスキー Op.39-1 | Hapebe 2011（abcm2ps） | **新しい浄書** | 1 | 未確認（WIMA） | `tchaikovsky39-1/imslp/hapebe-2011.pdf` |
| 154630 | チャイコフスキー Op.39-5 | Hapebe 2011（abcm2ps） | **新しい浄書** | 1 | 未確認（WIMA） | `tchaikovsky39-5/imslp/hapebe-2011.pdf` |
| 07996 | チャイコフスキー Op.39（全曲） | Schirmer 1904（Ruthardt） | スキャン | 31 | PD | `tchaikovsky39-1/imslp/schirmer-1904.pdf` |

ブルグミュラー Op.100-2・シューマン Op.15-7・チャイコフスキー Op.39-5 のスキャンは、上の全曲のファイルから切り出す（置き場所は -01・-1 の側だけ）。

「新しい浄書」の組版ソフトは IMSLP に書かれていない（Finale・Sibelius・MuseScore・Dorico のどれか。abcm2ps だけ明記）。
落としたら PDF の情報（`Producer`）で確かめる。

## 見たが外したもの

- クレメンティ Op.36 の Viktor Keil 2024（#926979）と Mutopia（#02564）: LilyPond の浄書なので Mutopia と重なる。
- シューマン Op.15-7 の Reccmo 2012（#201570、PMX）: 候補に足してもよい（MusiXTeX 系の別の組版）。
- チャイコフスキー Op.39 の Jurgenson 1878 初版（#1052567）: 古い版画で難しい。易しい曲がそろってから。
