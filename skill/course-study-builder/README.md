# course-study-builder

A [Claude Code](https://claude.com/claude-code) skill that turns a course's weekly material (lecture decks,
exercise sheets, solutions, past exams; PDFs, including handwritten ones) into **exam-focused study material**:

- **A study page per week** (`דפי לימוד/שבוע-N.html`), built *from the lecturer's own worked solutions*: every formula he uses explained inside its topic section (with the questions that use it), a "must know / skip" list based on
  what the exercises actually use, explanations that fill the gaps the slides leave, a question-pattern table
  (how to recognise a question → where to start → the trap), errata found in the official solutions, and
  a final "use in exercises" section with a solving hint per exercise (full solution hidden behind it), with per-question "solved" checkboxes.
- **Exercise PDFs with red pointers**: next to every question, a margin note saying *where the hint for it is* and
  which of the lecturer's formulas it needs. Pointers only, never answers. Each line links to that section.
- **A course log** in the course folder, so work continues across computers and sessions.

Hebrew-first (RTL), but works for any language.

## Install

```bash
git clone https://github.com/ariel853/studenting_with_ai ~/.claude/skills/course-study-builder
pip install pymupdf
```

Then, in Claude Code, inside your course folder: *"Here's week 3, build the study page and the pointer PDFs"*.

## Layout

```
SKILL.md                         workflow and rules Claude follows
scripts/render.py                PDF pages → PNG + text dump (to read slides and handwriting)
scripts/locate.py                y-coordinates of question headers in an exercise PDF
scripts/assemble.py              page shell + body → study page
scripts/annotate.py              red pointer margin + links on exercise PDFs (config-driven)
scripts/checklinks.py            verify page links and that every lecturer formula is used by a hint
templates/page_shell.html        shared CSS, KaTeX, dark mode, progress checkboxes
templates/body_skeleton.html     section structure for a weekly page
templates/pointers_config_example.py
templates/course_log_template.md
```

## Expected course folder

```
<Course>/
├── <course>_claude.md        log (created on first run)
├── שיעור 1/ …                lecture decks
│   └── תרגילים/              exercise sheets / solutions / recitations
├── מבחנים/                   past exams (optional, later)
├── דפי לימוד/                generated study pages
└── כלים/                     generated per-week pointer configs
```
