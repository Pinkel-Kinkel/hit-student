---
name: course-study-builder
description: Turn a university course's weekly lecture slides, exercise sheets, solutions and past exams (PDFs, incl. handwritten) into exam-focused study material - one interactive HTML study page per week built from the lecturer's own worked solutions (every formula he uses, explained inside its topic section, must-know vs skip, gap-filling explanations, question patterns, errata in official solutions, and a final 'use in exercises' section with a solving hint per exercise) plus copies of the exercise PDFs with red margin pointers next to each question linking to its hint and to the sections it needs (never answers). Use when the user uploads course material for a week ("שיעור N", "week N"), asks for a study page / summary sheet for a course, asks to annotate exercise PDFs with where-to-look hints, or uploads past exams to re-weight the material. Hebrew-first (RTL), works for any language.
---

# Course study builder

Goal: save the student time. Lecture decks are bloated **and** have gaps. Weight everything by what the
exercises and (later) the exams actually ask. Output per week:

1. `דפי לימוד/שבוע-N.html` - study page (from `templates/page_shell.html` + a body you write)
2. `שיעור N/תרגילים/עם מפנים/<file> - מפנים.pdf` - exercise PDFs with red pointers in a left margin
3. An updated progress log in the course folder (see below)

## 0. Always start from the course log

The course folder holds a log file (e.g. `physics_claude.md`; template: `templates/course_log_template.md`).
It is the source of truth across computers and sessions: read it fully first, and update it at the end of
every session (status table, per-week summary, decisions, errata, anchors used, session log line).
If it doesn't exist, create it from the template on the first session.

## 1. Read everything for the week

- Install once: `pip install pymupdf`. Run Python with `-I` on downloaded files; keep scripts out of the
  course folder unless they are the course's own `כלים/` tools.
- `python -I scripts/render.py "<week folder>" "<scratch>/rN"` - renders every page to PNG and dumps the text
  layer. **Read math from the images, not the text layer** (formulas come out garbled; Hebrew is often reversed).
  `_index.txt` shows `textchars=0` for scanned/handwritten files: read those pages visually (dpi 150 if dense)
  and flag any uncertain digit/subscript explicitly rather than guessing.
- Read **all** exercise files first (sheet `targ`, solutions `solut`, recitation `tirgul`, question-only
  copies, class-exercise slides), then the decks. Detect duplicate files (same text) and say so.
- Recompute every official answer. Collect typos/errors (wrong exponent, wrong mass, inconsistent sign or
  ħ vs ħ/2 conventions, mismatched numbers between question and solution).
- Points in a question (e.g. "9 נק'") mean it came from an exam - mark it as exam style.

## 1b. Lecturer's solutions first (the core of the method - do not skip)

The student's goal is to solve **this lecturer's** questions the way **he** solves them. So the page is
built *from his worked solutions*, not from the topic:

1. Go through every worked solution (solution files, recitation solutions, handwritten pages, worked
   examples inside the decks) **line by line**. For each step write down what he did and which
   formula / relation / constant / unit conversion / approximation / algebra move he used - including
   "small" ones (e.g. `hc = 1240 eV·nm`, the area fraction `πR²/4πD²`, `°C+273`, `eV/c -> kg·m/s`).
2. Note his notation and sign conventions, and where they are inconsistent (e.g. stopping voltage
   positive in the theory slide but written as `V0 = −4 V` in examples). Show both on the page.
3. Every item must appear on the page as an **explicit formula card** (`.fx`, id `f-*`; techniques `t-*`)
   **inside the explanation subsection it belongs to** - not in one big separate formula list (the student
   rejected that). Each card explains: the symbols, what it says in words, when to use it, the trap, the
   lecturer's notation, and which questions use it. Concepts the questions ask about a lot get a one-line
   `.big-idea` (e.g. "λ₀ is the MAXIMUM wavelength that still ejects electrons"). Mentioning a formula only
   inside prose or a solution is NOT enough.
4. Checklist before finishing: every formula from step 1 has a row, and `scripts/checklinks.py` reports
   no missing link targets and no formula unused by any hint.
5. Formulas that appear only in the slides and in no solution: list briefly under "not used in any solution".

## 2. Decide what goes where

- **A weekly page follows the topic of that week's exercises**, not the deck file names. A deck with no
  exercises yet is deferred to the week whose exercises use it (note this on both pages); if a later week
  covers a topic that had a temporary section earlier, replace the earlier section with a link.
- Must vs skip: count which concepts the questions use. History, derivations, admin slides -> "skip"
  (one line each). Keep qualitative "why does classical physics fail" points if they could be asked.
- Gaps: list every tool the solutions use that the slides don't teach (unit shortcuts like
  hc = 1240 eV·nm, sign conventions, ratio reasoning, vector decomposition, energy bookkeeping, terminology
  traps). These are the most valuable part of the page.

## 3. Write the page

- Copy `templates/body_skeleton.html`, fill it, then
  `python -I scripts/assemble.py body.html "<course>/דפי לימוד/שבוע-N.html" "<Course> – שבוע N" <course>-wN-done`
- Sections, in order: compact constants/units table (`#cheat`) · must/skip · core explanation
  (numbered subsections with the formula cards from step 1b) · gaps (`g1..`) · question patterns table (`pat-A..`: recognise -> start from ->
  trap) · errata · **🎯 שימוש בתרגילים** (`#use`, always last).
- "שימוש בתרגילים": one collapsed card per exercise, `id="use-<key>"`. The visible part is a **hint**:
  numbered steps per sub-question - where to start, which formulas (links to `#f-*`) in which order, the
  trap. No numbers, no answer. A nested `<details>` holds the full worked solution. Keep the small
  script from the skeleton that auto-opens the card when the page is opened at `#use-...`.
- Every subsection that a pointer may target gets a stable `id`. Record the ids in the log.
- Explanations in the user's language, short, concrete; show the step the official solution skipped.
- KaTeX pitfalls: no `·` inside `\text{}`; no `\vec p\,'`. Verify in a browser: count `.katex-error`
  elements (must be 0) and that the progress text says "0 מתוך N".
- Do not use `sed` to insert LaTeX (`\c`, `\t` become control chars) - use the Edit tool or Python.

## 4. Pointer PDFs (the student's favourite)

Rule: next to each question, in red: first line (bold) **"רמז: שימוש בתרגילים › <question>"** linking to
`#use-<key>`, then one line per explanation subsection whose formulas the question needs
("פוטואלקטרי › 2. הסף λ₀"), each linking there. **Never the answer.** See
`templates/pointers_config_example.py` (LABEL / HINT).

1. `python -I scripts/locate.py exercise.pdf out.txt` -> y of each question header (page index, y in pt).
   Scanned page: y_pt = y_px · 72 / dpi from the rendered image.
2. Write `<course>/כלים/pointers_weekN.py` from `templates/pointers_config_example.py`
   (`HTML`, `LABEL`, `Q`, `JOBS`; an anchor like `"שבוע-4.html#un-3"` links to another week's page).
3. `python -I scripts/annotate.py <course>/כלים/pointers_weekN.py`
   It adds a 200pt left margin column (original page untouched), red RTL text aligned with each question,
   each line a clickable `file:///...#anchor` link. Originals are never modified.
4. Render one page per output to PNG and look at it before reporting.
- Links are absolute paths: on another computer with a different path, re-run the config (text still works).
- Skip files that already contain full solutions (they don't need pointers).

## 5. When exams arrive

Tally topics/patterns per exam question, re-weight must/skip on every weekly page, fill the
`exam-style` callouts, and build a "לקראת המבחן" page: formula sheet, the lecturer's recurring question
templates (e.g. "two measurements, subtract to eliminate the unknown"), and practice in their style.

## Reporting to the user

Short, in their language: what the week's exercises focus on, what was deferred/moved and why, the main
gaps filled, errata found (only the ones that change a number matter most), file links. Mention that
progress checkboxes are per-browser and that formulas need internet (KaTeX CDN).
