# IMSLP から手で落とすファイル（OpenScore Lieder の 15 曲、2026-10-21）

IMSLP はスクリプトからのダウンロードを JavaScript の確認ページで止める。ブラウザで下の URL を開いて PDF を落とし、
「置き場所」に `imslp.pdf` の名前で置く。曲集のファイルは丸ごとのままでよい（その曲のページはこちらで `songs.tsv` の
`pages` に書いて切り出す）。同じファイルを使う曲は 1 回だけ落とせばよい（`prep-eval.ps1` が同じ番号の曲に回す）。

どれも OpenScore がそのファイルから写したもの（`songs.tsv` の `imslp`）なので、正解（OpenScore の MusicXML）とスキャンの版は同じ。

| # | 曲 | IMSLP | 置き場所 |
|---|---|---|---|
| 1 | Kinkel《6 Lieder》Op.10-3「So wahr die Sonne scheinet」 | https://imslp.org/wiki/Special:ImagefromIndex/618113 | `kinkel10-3/scan/imslp.pdf` |
| 2 | Kinkel《6 Lieder》Op.19-3「Liebesmacht」 | https://imslp.org/wiki/Special:ImagefromIndex/171873 | `kinkel19-3/scan/imslp.pdf` |
| 3 | Kinkel《6 Lieder》Op.7-2「Wunsch」・7-4「Die Lorelei」 | https://imslp.org/wiki/Special:ImagefromIndex/617921 | `kinkel7-2/scan/imslp.pdf` |
| 4 | Faisst《5 Lieder》Op.4-5「Lied aus Bosnien」 | https://imslp.org/wiki/Special:ImagefromIndex/621606 | `faisst4-5/scan/imslp.pdf` |
| 5 | Lang《3 Lieder》Op.36-1・36-2 | https://imslp.org/wiki/Special:ImagefromIndex/617782 | `lang36-1/scan/imslp.pdf` |
| 6 | Lehmann《Songs of a "Flapper"》1・5 | https://imslp.org/wiki/Special:ImagefromIndex/627144 | `lehmann-flapper1/scan/imslp.pdf` |
| 7 | Holmès《20 Mélodies》16「La princesse sans cœur」 | https://imslp.org/wiki/Special:ImagefromIndex/295514 | `holmes-melodies16/scan/imslp.pdf` |
| 8 | Boulanger《Clairières dans le ciel》2「Elle est gravement gaie」 | https://imslp.org/wiki/Special:ImagefromIndex/25057 | `boulanger-clairieres02/scan/imslp.pdf` |
| 9 | Butterworth《6 Songs from A Shropshire Lad》1「Loveliest of trees」 | https://imslp.org/wiki/Special:ImagefromIndex/239744 | `butterworth-shropshire1/scan/imslp.pdf` |
| 10 | Chaminade「Au pays bleu」 | https://imslp.org/wiki/Special:ImagefromIndex/154300 | `chaminade-paysbleu/scan/imslp.pdf` |
| 11 | Elgar《2 Songs》Op.60-1「The Torch」 | https://imslp.org/wiki/Special:ImagefromIndex/555775 | `elgar60-1/scan/imslp.pdf` |
| 12 | Gonzaga「Ai que broma!」 | https://imslp.org/wiki/Special:ImagefromIndex/605868 | `gonzaga-broma/scan/imslp.pdf` |

置いたら知らせてほしい: こちらで各曲のページを確かめて `songs.tsv` に書き、`tools/prep-eval.ps1` で切り出して評価台に入れる。

2026-10-21: 12 個とも届いた（オーナーが手で落とした）。各曲のページは PDF の縮小一覧で見て `songs.tsv` の `pages` に書いた
（Holmès は曲集の目次の 88–93 ページ = PDF の 94–99）。Elgar（920×1187）と Kinkel Op.19（1500×1887）は IMSLP の画像そのものが粗いが、実物どおりそのまま使う。
