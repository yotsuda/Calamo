# Licenses of the data

The code of this repository is under the Apache License 2.0 ([LICENSE](../LICENSE)). The files
under `data/` keep the licenses of their sources, as below.

| Files | Source | License |
|---|---|---|
| `**/event-listener*.ly` | [LilyPond](https://lilypond.org) (`ly/event-listener.ly`, copied or adapted) | GNU GPL v3 or later |
| `print/`, `detector.onnx`, `glyph-train.tsv`, the other `.tsv` tables, every `.score`, `.notes`, `.lys` and `check/` file | made by this project (generated with Lily# / LilyPond, or written by hand) | Apache-2.0 |
| `bwv610/` (Bach BWV 610), `wtk1/` (Bach BWV 846) | [Mutopia Project](https://www.mutopiaproject.org) | Public domain |
| `eval/*/src/` (Burgmüller Op.100-1, -2; Chopin Op.28-4; Clementi Op.36-1; Schumann Op.15-1, -7; Tchaikovsky Op.39-1, -5) and the `truth*/page.ly` made from them | Mutopia Project | Public domain |
| `eval/dichterliebe/src/`, `eval/lieder/*/ms/`, `dev/lieder/*/src/`, `dev/lieder/*/ms/` | [OpenScore Lieder](https://github.com/OpenScore/Lieder) | CC0 1.0 |
| `eval/dichterliebe/scan/breitkopf.pdf`, `eval/chopin28-4/scan/peters.pdf`, `eval/lieder/*/scan/imslp.pdf`, `dev/lieder/*/scan/imslp.pdf` | scans of 19th- and early 20th-century prints, from [IMSLP](https://imslp.org) (the number of each is in `songs.tsv` and the `.score` headers) | Public domain |
| `eval/burgmuller100-01/imslp/schirmer-1903.pdf` (IMSLP #337046), `eval/clementi36-1/imslp/schirmer-1893.pdf` (#73600), `eval/schumann15-1/imslp/breitkopf-1880.pdf` (#289791), `eval/tchaikovsky39-1/imslp/schirmer-1904.pdf` (#07996) | IMSLP | Public domain |
| `eval/burgmuller100-01/imslp/zhang-2024.pdf` (IMSLP #935040) | engraved by Ruiming Zhang, 2024 | CC BY 4.0 |
| `eval/clementi36-1/imslp/bosse-2021.pdf` (IMSLP #725302) | engraved by Nicholas Bosse, 2021 | CC BY 4.0 |
| `fair/*/*/imslp.pdf` (S59's fair set; the IMSLP number of each is in `tools/prep-fair.ps1` and the `.score` headers): Lassen Op.48-4 (Bosworth #679347, Hainauer #710192), Sommer Op.9 (Litolff 1886, #777922), Hüe 6 Mélodies (Leduc c.1886, #994220), Bargiel Op.41-1 (Breitkopf 1877, #16043; Schirmer 1879, #07475), Lacombe Op.28 (Hamelle 1878, #958979) | IMSLP | Public domain |

## Not included: download by hand

These editions are used by evaluation sets but are not redistributed here (a non-commercial
license, or one not stated). Download each from IMSLP in a browser
(`https://imslp.org/wiki/Special:ImagefromIndex/<number>`) and put it at the path shown; the sets
that need it (`docs/eval-sets.tsv`) cannot run without it.

| IMSLP | Edition | License | Put it at |
|---|---|---|---|
| #67290 | Schumann Op.15, Richard Johnson Editions 2010 | CC BY-NC 3.0 | `eval/schumann15-1/imslp/rje-2010.pdf` |
| #154626 | Tchaikovsky Op.39-1, Hapebe 2011 | not stated | `eval/tchaikovsky39-1/imslp/hapebe-2011.pdf` |
| #154630 | Tchaikovsky Op.39-5, Hapebe 2011 | not stated | `eval/tchaikovsky39-5/imslp/hapebe-2011.pdf` |
| #510037 (PMLUS00461) | M. R. Lang, An Irish Love Song Op.22, A. P. Schmidt 1895 | public domain in the US only (Lang died 1972: not in life+70 countries, Japan among them) | `fair/lang22/schmidt/imslp.pdf`; the truth written from it (`fair/lang22/truth/lang22.lys`, a transcription) is kept out too |

Band scores scanned from commercial books, used during development, are not in this repository
and never will be.