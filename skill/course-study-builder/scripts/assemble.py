"""Build a study page from the shared shell (templates/page_shell.html: CSS, KaTeX, progress checkboxes)
and a body fragment you wrote.

usage: python -I assemble.py <body.html> <out.html> "<page title>" <storage_key> [lang=he] [dir=rtl]

The body must start right after <div class="wrap"> and end with the closing </div> of .wrap
(see templates/body_skeleton.html).
"""
import sys, os

body, out, title, key = sys.argv[1:5]
lang = sys.argv[5] if len(sys.argv) > 5 else "he"
direction = sys.argv[6] if len(sys.argv) > 6 else ("rtl" if lang in ("he", "ar") else "ltr")
shell_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "templates", "page_shell.html")
shell = open(shell_path, encoding="utf-8").read()
page = (shell.replace("{{TITLE}}", title).replace("{{LANG}}", lang).replace("{{DIR}}", direction)
        .replace("{{STORAGE_KEY}}", key).replace("{{BODY}}", open(body, encoding="utf-8").read()))
open(out, "w", encoding="utf-8").write(page)
print("ok", out)
