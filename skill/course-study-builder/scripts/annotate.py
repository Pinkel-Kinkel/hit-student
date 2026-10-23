"""Add a red 'pointer' margin column to exercise PDFs: next to each question, which sections of the
weekly study page explain how to solve it (pointers only, never answers). Each line links to the section.

usage: python -I annotate.py <config.py>

The config file (see templates/pointers_config_example.py) defines:
  HTML   - absolute path of the weekly study page
  LABEL  - {anchor_id: "section name as shown in red"}; an anchor may also be "other-page.html#id"
  Q      - {question_key: [(anchor_id, "which part / hint"), ...]}
  JOBS   - [(src_pdf, dst_pdf, [(page_index, y_of_question_top_pt, question_key), ...]), ...]
Optional: WEEK, HEADER, SUBHEADER, WHERE (UI strings), RTL (default True).
"""
import sys, os, runpy, pymupdf
from urllib.parse import quote

cfg = runpy.run_path(sys.argv[1])
HTML, LABEL, Q, JOBS = cfg["HTML"], cfg["LABEL"], cfg["Q"], cfg["JOBS"]
WEEK = cfg.get("WEEK", "")
HEADER = cfg.get("HEADER", f"מפנים לדף הלימוד של שבוע {WEEK}")
SUBHEADER = cfg.get("SUBHEADER", "לחיצה על שורה פותחת את הסעיף")
WHERE = cfg.get("WHERE", "◄ איפה לחפש:")
RTL = cfg.get("RTL", True)


def find_fonts():
    """A font folder with a regular+bold TTF that has Hebrew glyphs (Arial on Windows/mac, DejaVu on Linux)."""
    candidates = [("C:/Windows/Fonts", "arial.ttf", "arialbd.ttf"),
                  ("/Library/Fonts", "Arial.ttf", "Arial Bold.ttf"),
                  ("/System/Library/Fonts/Supplemental", "Arial.ttf", "Arial Bold.ttf"),
                  ("/usr/share/fonts/truetype/dejavu", "DejaVuSans.ttf", "DejaVuSans-Bold.ttf")]
    for d, reg, bold in candidates:
        if os.path.exists(os.path.join(d, reg)):
            return d, reg, bold if os.path.exists(os.path.join(d, bold)) else reg
    sys.exit("No suitable font found; add your font folder to find_fonts() in annotate.py")


FONT_DIR, FONT_REG, FONT_BOLD = find_fonts()
COL = 200  # width of the added margin column (left side), in pt
RED = "#c8102e"
CSS = f"""
@font-face {{ font-family: heb; src: url("{FONT_REG}"); }}
@font-face {{ font-family: heb; font-weight: bold; src: url("{FONT_BOLD}"); }}
* {{ font-family: heb; direction: {"rtl" if RTL else "ltr"}; text-align: {"right" if RTL else "left"}; color: {RED}; margin: 0; padding: 0; }}
.h {{ font-size: 8pt; font-weight: bold; }}
.i {{ font-size: 8.5pt; line-height: 1.25; }}
.n {{ font-size: 7pt; color: #7a1020; }}
.top {{ font-size: 8.5pt; font-weight: bold; }}
"""
ARCH = pymupdf.Archive(FONT_DIR)
BASE = "file:///" + quote(HTML.replace("\\", "/"), safe="/:")
DIR = BASE.rsplit("/", 1)[0]


def url(anchor):
    """'g1' -> this week's page; 'שבוע-4.html#un-3' -> another page in the same folder."""
    if ".html#" in anchor:
        page, frag = anchor.split("#", 1)
        return f"{DIR}/{quote(page)}#{frag}"
    return f"{BASE}#{anchor}"


def annotate(src_path, dst_path, marks):
    src = pymupdf.open(src_path)
    out = pymupdf.open()
    by_page = {}
    for pi, y, key in marks:
        by_page.setdefault(pi, []).append((y, key))
    for pi, sp in enumerate(src):
        w, h = sp.rect.width, sp.rect.height
        page = out.new_page(width=w + COL, height=h)
        page.show_pdf_page(pymupdf.Rect(COL, 0, COL + w, h), src, pi)
        page.draw_line((COL - 4, 20), (COL - 4, h - 20), color=(0.85, 0.75, 0.77), width=0.6)
        if pi == 0:
            page.insert_htmlbox(pymupdf.Rect(8, 18, COL - 12, 60),
                                f'<p class="top">{HEADER}</p><p class="n">{SUBHEADER}</p>', css=CSS, archive=ARCH)
        cursor = 0
        for y, key in sorted(by_page.get(pi, [])):
            y = max(y, cursor + 6)
            top = y
            page.insert_htmlbox(pymupdf.Rect(8, y, COL - 12, y + 14), f'<p class="h">{WHERE}</p>', css=CSS, archive=ARCH)
            y += 12
            for anchor, note in Q[key]:
                html = f'<p class="i">• {LABEL[anchor]}' + (f' <span class="n">({note})</span>' if note else "") + "</p>"
                r = pymupdf.Rect(8, y, COL - 12, y + 40)
                page.insert_htmlbox(r, html, css=CSS, archive=ARCH)
                ys = [b[3] for b in page.get_text("blocks", clip=r)]
                used = (max(ys) - y) if ys else 12
                page.insert_link({"kind": pymupdf.LINK_URI, "from": pymupdf.Rect(8, y, COL - 12, y + used),
                                  "uri": url(anchor)})
                y += used + 3
            page.draw_line((COL - 8, top + 2), (COL - 8, y - 2), color=(0.78, 0.06, 0.18), width=1.2)
            cursor = y
    # MuPDF stores file:// URIs as Launch actions with the #anchor glued to the filename; rewrite as real URIs.
    for page in out:
        for ln in page.get_links():
            f = ln.get("file") or ln.get("uri") or ""
            if "#" in f:
                target = f.lstrip("/")
                if not target.startswith("file:"):
                    target = "file:///" + target
                out.xref_set_key(ln["xref"], "A", f"<</S/URI/URI({target})>>")
    out.save(dst_path, garbage=3, deflate=True)
    return len(out)


for src_path, dst_path, marks in JOBS:
    unknown = [k for _, _, k in marks if k not in Q] + [a for k in Q for a, _ in Q[k] if a not in LABEL]
    assert not unknown, unknown
    annotate(src_path, dst_path, marks)
print("done", len(JOBS))
