"""Find the y-coordinate (pt) of every question header in an exercise PDF, for placing pointers.

usage: python -I locate.py <exercise.pdf> <out.txt> [regex]

Default regex matches Hebrew "שאלה" headers and numbered questions like 1.6 / 2.17.
Output lines: <page_index> <y_top_pt> <word>. Also dumps all text blocks with coordinates
(useful when headers are split oddly; Hebrew text is often extracted reversed).
For scanned pages (no text layer) compute y from a rendered image: y_pt = y_px * 72 / dpi.
"""
import sys, re
import pymupdf

pdf, outp = sys.argv[1], sys.argv[2]
rx = re.compile(sys.argv[3] if len(sys.argv) > 3 else r"שאלה|^\d\.\d{1,2}$")
d = pymupdf.open(pdf)
with open(outp, "w", encoding="utf-8") as out:
    print("# headers: page y word", file=out)
    for i, pg in enumerate(d):
        for w in pg.get_text("words"):
            if rx.search(w[4]):
                print(i, round(w[1]), w[4], file=out)
    print("\n# blocks", file=out)
    for i, pg in enumerate(d):
        print(f"=== page {i} size={pg.rect}", file=out)
        for b in pg.get_text("blocks"):
            t = b[4].replace("\n", " | ")[:90]
            print(f"p{i} [{b[0]:.0f},{b[1]:.0f},{b[2]:.0f},{b[3]:.0f}] {t}", file=out)
print("done")
