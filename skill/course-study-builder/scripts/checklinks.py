"""Sanity-check a study page before shipping it.

usage: python -I checklinks.py <page_or_body.html>

Reports: internal links whose target id is missing, lecturer formulas (ids f-*/t-*) that no hint
references, and the list of exercise hint ids (use-*) that pointer configs can target.
"""
import sys, re

s = open(sys.argv[1], encoding="utf-8").read()
ids = set(re.findall(r'id="([^"]+)"', s))
hrefs = re.findall(r'href="#([^"]+)"', s)
missing = sorted(set(h for h in hrefs if h not in ids))
formulas = sorted(i for i in ids if i.startswith(("f-", "t-")))
used = set(h for h in hrefs if h.startswith(("f-", "t-")))
print("ids:", len(ids), "| internal links:", len(hrefs))
print("MISSING link targets:", missing or "none")
print("formulas never used in a hint:", [f for f in formulas if f not in used] or "none")
print("exercise hints:", sorted(i for i in ids if i.startswith("use-")))
sys.exit(1 if missing else 0)
