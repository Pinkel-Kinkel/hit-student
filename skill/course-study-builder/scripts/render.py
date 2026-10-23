"""Render every PDF under a folder to PNG pages (to read slides / handwriting visually) and
extract their text layer to one UTF-8 file.

usage: python -I render.py <folder_with_pdfs> <out_dir> [dpi=100]

Writes:
  <out_dir>/<pdfname>_pNN.png   one image per page
  <out_dir>/_index.txt          pdf name, page count, text-layer size (0 => scanned/handwritten)
  <out_dir>/_text.txt           raw text layer of all PDFs (formulas come out garbled - read images for math)
"""
import sys, os, glob
import pymupdf

root, out = sys.argv[1], sys.argv[2]
dpi = int(sys.argv[3]) if len(sys.argv) > 3 else 100
os.makedirs(out, exist_ok=True)
index = open(os.path.join(out, "_index.txt"), "w", encoding="utf-8")
text = open(os.path.join(out, "_text.txt"), "w", encoding="utf-8")
for p in sorted(glob.glob(os.path.join(root, "**", "*.pdf"), recursive=True)):
    if "עם מפנים" in p or " - מפנים" in p:  # skip our own annotated outputs
        continue
    d = pymupdf.open(p)
    base = os.path.splitext(os.path.basename(p))[0].replace(" ", "_")
    chars = 0
    print("=====", os.path.relpath(p, root), file=text)
    for i, pg in enumerate(d):
        t = pg.get_text()
        chars += len(t.strip())
        print(f"--- p{i + 1}\n{t}", file=text)
        pg.get_pixmap(dpi=dpi).save(os.path.join(out, f"{base}_p{i + 1:02d}.png"))
    print(f"{os.path.relpath(p, root)}\tpages={len(d)}\ttextchars={chars}", file=index)
print("done")
