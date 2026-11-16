# 公平な台を増やす: IMSLP から手で落とすファイル（S66、2026-11-16）

ROADMAP §0「本線の順番」3・S66: 公平な台を 1 台の揺れで結論が変わらない大きさ（15 台・12,000 音以上）にする。
今は 8 台・5,927 音。下の 10 ファイルで 18 台・約 13,000 音（見積もり）になる。読めない刷りや、正解を起こせない曲が出ても足りるように、少し多めに挙げた。

## 落とし方

IMSLP はスクリプトからのダウンロードを JavaScript の確認ページで止めるので、ブラウザで開いて落としてほしい。

1. 下の表の URL を開き、確認を済ませて PDF を落とす。
2. 落とした PDF は**名前を変えずに**（IMSLP の名前は `IMSLP<番号>-…pdf` になる）、1 つのフォルダーにまとめる（例: `C:\Users\ytsud\Downloads\fair2`）。
3. そのフォルダーの場所を知らせてほしい。こちらで各 PDF の曲のページを確かめて `tools/prep-fair.ps1` に足し、
   `data/fair/<曲>/<版>/` に切り出してから正解を起こす（S59 と同じ手順）。

曲集のファイルは丸ごとのままでよい。使う曲・ページはこちらで選ぶ。

## 選び方（S59 と同じ条件）

- **homr の学習の外**: OpenScore Lieder の作曲家（`OpenScore/Lieder` の `data/composers.yaml`、154 人）に入っていない。GrandStaff の作曲家（Bach・Beethoven・Chopin・Hummel・Joplin・Mozart・Scarlatti）でもない。
  PDMX（MuseScore）は musescore.com の検索で曲が出てこないことを見ただけなので、完全な証明ではない。Shelley「Love's Sorrow」と Kjerulf の歌は MuseScore に曲があったので外した。
- **古い刷りの、ページ単位のスキャン**（1849〜1905 年の版）。現代の組版の版は入れていない。
- **版元と国をばらした**: ドイツ（Kistner・Heinrichshofen・Bote & Bock・Senff）、フランス（Hamelle・Heugel）、アメリカ（Schirmer・Ditson）、ロシア（Gutheil、今の台には無い）。
- 今の公平な台の 6 人とも、開発用の台・評価台の作曲家とも重ならない。
- 歌 6 台、ピアノ 4 台。著作権はどれも切れている（作曲家の没年は 1924 年以前）。

## ファイル

| # | 曲 | 版 | 種類 | IMSLP | 頁 |
|---|---|---|---|---|---|
| 1 | Kücken《4 Lieder》Op.47-3「Die stille Wasserrose」（低声用） | Leipzig: Kistner, 1849 頃 | 歌 | https://imslp.org/wiki/Special:ImagefromIndex/574387 | 7 |
| 2 | Hildach《3 Lieder》Op.9-1「Auf dem Dorf in den Spinnstuben」（高声用） | Magdeburg: Heinrichshofen, 1891 | 歌 | https://imslp.org/wiki/Special:ImagefromIndex/706241 | 6 |
| 3 | Taubert《6 Lieder》Op.76-2 | New York: G. Schirmer, 1870 | 歌 | https://imslp.org/wiki/Special:ImagefromIndex/110522 | 5 |
| 4 | Wüerst《4 Lieder》Op.52-3「Wer's nur verstände」 | Berlin: Bote & Bock, 1870 頃 | 歌 | https://imslp.org/wiki/Special:ImagefromIndex/413040 | 5 |
| 5 | Puget「L'heure tendre」（曲集『Les frissons』から） | Paris: J. Hamelle, 1905 | 歌 | https://imslp.org/wiki/Special:ImagefromIndex/94633 | 5 |
| 6 | Buck《5 Songs》Op.67-5「When the Heart is Young」（高声、変ホ長調） | Boston: Ditson, 1902 | 歌 | https://imslp.org/wiki/Special:ImagefromIndex/185781 | 8 |
| 7 | Taubert《6 Kinderstücke》Op.136（2〜3 曲を使う） | Leipzig: Senff, 1862 頃 | ピアノ | https://imslp.org/wiki/Special:ImagefromIndex/104910 | 15 |
| 8 | A. Krause《2 Sonatinen》Op.10（第 1 番の第 1 楽章を使う） | Moscow: Gutheil, 年なし | ピアノ | https://imslp.org/wiki/Special:ImagefromIndex/295293 | 16 |
| 9 | Lack「Myosotis」Op.148 | Paris: Heugel, 1890 頃 | ピアノ | https://imslp.org/wiki/Special:ImagefromIndex/45177 | 4 |
| ~~10~~ | ~~Volkmann《Musikalisches Bilderbuch》Op.11 第 1 巻~~ **外した: 連弾（Secondo と Primo が見開きで別の頁）で、2 手のピアノの台にならない** | Leipzig: Kistner, 1853 頃 | 連弾 | https://imslp.org/wiki/Special:ImagefromIndex/1026370 | 15 |
| **11** | **Hiller《8 Leichte Klavierstücke》Op.79 第 1〜4 曲**（10 の代わり。**まだ落としていない**） | Mainz: B. Schott's Söhne, 1858 頃 | ピアノ | https://imslp.org/wiki/Special:ImagefromIndex/115197 | 14 |

作品のページ（版の情報や、ほかの版を見るとき）:
[Kücken Op.47](https://imslp.org/wiki/4_Lieder,_Op.47_(K%C3%BCcken,_Friedrich_Wilhelm))・
[Hildach Op.9](https://imslp.org/wiki/3_Lieder,_Op.9_(Hildach,_Eugen))・
[Taubert Op.76](https://imslp.org/wiki/6_Lieder,_Op.76_(Taubert,_Wilhelm))・
[Wüerst Op.52](https://imslp.org/wiki/4_Lieder,_Op.52_(W%C3%BCerst,_Richard))・
[Puget](https://imslp.org/wiki/L'heure_tendre_(Puget,_Paul))・
[Buck Op.67](https://imslp.org/wiki/5_Songs,_Op.67_(Buck,_Dudley))・
[Taubert Op.136](https://imslp.org/wiki/6_Kinderst%C3%BCcke,_Op.136_(Taubert,_Wilhelm))・
[Krause Op.10](https://imslp.org/wiki/2_Sonatinen,_Op.10_(Krause,_Anton))・
[Lack Op.148](https://imslp.org/wiki/Myosotis,_Op.148_(Lack,_Th%C3%A9odore))・
[Volkmann Op.11](https://imslp.org/wiki/Musikalisches_Bilderbuch,_Op.11_(Volkmann,_Robert))

## 届いたもの（2026-11-16）

オーナーが 1〜10 を `C:\Users\ytsud\Downloads\fair2` に落とした。縮小一覧で中身を見て、9 ファイルから 10 台を `tools/prep-fair.ps1` で `data/fair/` に切り出した:

| 台 | 使った頁（PDF の頁） | 見て分かったこと |
|---|---|---|
| `kuecken47-3/kistner` | 2–7 | 1 頁目は表紙。頁が混んでいる（6 頁で多くの音） |
| `hildach9-1/heinrichshofen` | 2–5 | 頁が細切れの画像なので 300 dpi で描き直した（Lassen の Hainauer と同じ）。6 頁目は広告 |
| `taubert76-2/schirmer` | 2–5 | 英独 2 か国語の歌詞 |
| `wuerst52-3/bote` | 2–5 | IMSLP の画像が 1500 px 幅で粗い（Kinkel Op.19 と同じくらい）。実物どおり使う |
| `puget-heure/hamelle` | 1–5 | 曲集の 18 番 |
| `buck67-5/ditson` | 3–7 | 2・8 頁目は広告 |
| `taubert136-1/senff` | 3–5 | 第 1 曲。灰色の地 |
| `taubert136-2/senff` | 6–7 | 第 2 曲 |
| `krause10-1/gutheil` | 3–8 | 第 1 楽章だけ。運指の数字が多い |
| `lack148/heugel` | 2–4 | 1 頁目は表紙 |

Volkmann（10）は連弾だったので外した（選んだときに編成を確かめていなかった）。代わりに 11（Hiller Op.79）を落としてほしい。
Hiller は MuseScore に別の曲の写しがある（Tagebuch、Op.15 の練習曲、Op.35）。Op.79 は検索に出てこなかった。

## 予備（上のどれかが使えないとき）

| 曲 | 版 | IMSLP | 頁 |
|---|---|---|---|
| Taubert「Mélancolie」Op.85（ピアノ） | Berlin: Trautwein, 1850 頃 | https://imslp.org/wiki/Special:ImagefromIndex/104900 | 13 |
| Lack「Musette」Op.22（ピアノ） | Paris: Lemoine, 1877 頃 | https://imslp.org/wiki/Special:ImagefromIndex/408608 | 7 |
| Hildach《5 Lieder》Op.19-1「Der öde Garten」（歌） | Magdeburg: Heinrichshofen, 1894 | https://imslp.org/wiki/Special:ImagefromIndex/478601 | 4 |

## 正直に書いておくこと

1. スキャンの画質はまだ見ていない（IMSLP の API で番号・頁数・版元を取っただけ）。届いたら最初に見て、読めないほど粗いもの、現代の組み直し、写譜の手書きだったら外して予備に替える。
2. 音の数は頁数からの見積もり（歌 1 頁 ≈ 120〜150 音、ピアノ 1 頁 ≈ 200 音）。
3. PDMX に無いことは musescore.com の検索で見ただけ。GrandStaff・OpenScore Lieder の作曲家の一覧とは照らし合わせ済み。
