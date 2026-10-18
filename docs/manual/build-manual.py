#!/usr/bin/env python3
"""Build the SharpPortico manual as a PDF.

Pipeline
--------
    docs/manual/src/*.md   (ordered by file name)
        -> python-markdown            (tables, attr_list, md_in_html)
        -> Pygments                   (every fenced block, language-labelled)
        -> one HTML document          (cover + contents + body, manual.css)
        -> Chrome --print-to-pdf      (A4, real page boxes, no browser chrome)
        -> pypdf                      (contents page numbers, running footer,
                                       outline/bookmarks, document metadata)

Two passes are made over Chrome. The first lays the document out with placeholder
page numbers in the contents; heading text is then located in the produced PDF by
extracting each page's text, so the numbers in the second pass are measured rather
than estimated. Page geometry is unchanged by the substitution (the numbers are
right-aligned in a fixed-width column), so the second layout has the same pagination
as the first; the result is verified and rebuilt once more if it does not.

Usage
-----
    python docs/manual/build-manual.py                 # build the PDF
    python docs/manual/build-manual.py --keep-html     # also keep build/manual.html
    python docs/manual/build-manual.py --no-stamp      # skip footer/bookmarks

Requires: python-markdown, pygments, pypdf, and Chrome or Edge (headless print).
"""

from __future__ import annotations

import argparse
import html
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
from pathlib import Path

try:
    import markdown
    from markdown.extensions.toc import slugify
    from pygments import highlight
    from pygments.formatters import HtmlFormatter
    from pygments.lexers import get_lexer_by_name
    from pygments.lexers.special import TextLexer
    from pygments.util import ClassNotFound
    from pypdf import PdfReader, PdfWriter, PageObject
    from pypdf.generic import (
        DecodedStreamObject,
        DictionaryObject,
        NameObject,
        NumberObject,
    )
except ImportError as exc:  # pragma: no cover - environment problem, not a build path
    sys.exit(f"missing dependency: {exc}. pip install markdown pygments pypdf")

ROOT = Path(__file__).resolve().parent            # docs/manual
DOCS = ROOT.parent                                # docs
REPO = DOCS.parent                                # repository root
SRC = ROOT / "src"
BUILD = ROOT / "build"
CSS = ROOT / "manual.css"
LOGO = DOCS / "assets" / "SharpPortico.jpg"

VERSION = "1.5.0"
PRODUCT = "SharpPortico"
MANUAL_TITLE = "SharpPortico 1.5.0 — The Manual"

LANGUAGE_LABELS = {
    "csharp": "C#",
    "cs": "C#",
    "protobuf": "proto",
    "proto": "proto",
    "yaml": "YAML",
    "yml": "YAML",
    "json": "JSON",
    "bash": "shell",
    "sh": "shell",
    "shell": "shell",
    "powershell": "PowerShell",
    "ps1": "PowerShell",
    "xml": "MSBuild / XML",
    "msbuild": "MSBuild / XML",
    "text": "text",
    "console": "console",
    "ini": "editorconfig",
    "editorconfig": "editorconfig",
}

# A block longer than this may break across pages; keeping it whole would leave a
# half-empty page and push the following prose further away from its heading.
WRAP_AFTER_LINES = 34

FENCE = re.compile(r"^```([A-Za-z0-9_+.-]*)[ \t]*\n(.*?)^```[ \t]*$", re.S | re.M)


def highlight_code(code: str, language: str) -> str:
    """One fenced block as the HTML the stylesheet expects."""
    language = (language or "text").lower()
    try:
        lexer = get_lexer_by_name(language)
    except ClassNotFound:
        lexer = TextLexer()

    body = highlight(code, lexer, HtmlFormatter(nowrap=True))
    label = LANGUAGE_LABELS.get(language, language)
    wrap = " wrap" if code.count("\n") > WRAP_AFTER_LINES else ""
    return (
        f'<div class="codeblock{wrap}" data-lang="{html.escape(label)}">'
        f'<span class="lang">{html.escape(label)}</span>'
        f"<pre><code>{body}</code></pre></div>\n"
    )


def expand_fences(text: str) -> str:
    """Replace every fenced block with highlighted HTML before markdown runs.

    The block is emitted as raw HTML, which python-markdown passes through
    verbatim; doing it before the markdown pass means the code inside is never
    seen as markdown and never needs escaping twice.
    """
    def repl(match: re.Match[str]) -> str:
        language, code = match.group(1), match.group(2)
        if not code.endswith("\n"):
            code += "\n"
        return highlight_code(code, language)

    return FENCE.sub(repl, text)


def convert(path: Path) -> str:
    """One chapter file to HTML."""
    text = path.read_text(encoding="utf-8")
    text = expand_fences(text)
    return markdown.markdown(
        text,
        extensions=["tables", "attr_list", "md_in_html", "sane_lists", "toc"],
        output_format="html5",
    )


def slug(text: str) -> str:
    """The id the TOC links to; matches what markdown's own slugify would do."""
    plain = re.sub(r"<[^>]+>", "", text)
    return slugify(plain, "-")


def collect_headings(body_html: str) -> list[tuple[int, str, str]]:
    """Every h1/h2 in document order, as (level, text, id)."""
    found: list[tuple[int, str, str]] = []
    for match in re.finditer(
        r"<h([123])(?:\s+id=\"([^\"]*)\")?[^>]*>(.*?)</h\1>", body_html, re.S
    ):
        level = int(match.group(1))
        existing = match.group(2)
        inner = match.group(3)
        plain = html.unescape(re.sub(r"<[^>]+>", "", inner)).strip()
        if level == 1 and plain == "Contents":
            continue
        found.append((level, plain, existing or slug(plain)))
    return found


def uniquify_heading_ids(body_html: str, used: set[str]) -> str:
    """Give every heading an id that is unique across the whole book.

    Markdown's toc extension makes ids unique within one file, not across chapters, so two chapters with
    a \"What is not generated\" section would otherwise share one id - and the contents would link both
    entries to whichever page was measured last.
    """
    heading = re.compile(r"<h([123])([^>]*)>")

    def replace(match: re.Match[str]) -> str:
        level, attributes = match.group(1), match.group(2)
        id_match = re.search(r'\bid="([^"]*)"', attributes)
        base = id_match.group(1) if id_match else f"section"
        candidate = base
        suffix = 1
        while candidate in used:
            candidate = f"{base}-{suffix}"
            suffix += 1
        used.add(candidate)

        if id_match:
            attributes = attributes[: id_match.start(1)] + candidate + attributes[id_match.end(1):]
        else:
            attributes = f' id="{candidate}"' + attributes
        return f"<h{level}{attributes}>"

    return heading.sub(replace, body_html)


def cover_html() -> str:
    logo = LOGO.as_uri() if LOGO.exists() else ""
    image = f'<img class="logo" src="{logo}" alt="SharpPortico" />' if logo else ""
    return f"""
<div class="cover">
  {image}
  <h1 class="title">The SharpPortico<br />Manual</h1>
  <div class="rule"></div>
  <div class="version">Version {VERSION} &middot; .NET 10</div>
  <p class="subtitle">OpenAPI contracts, turned into protobuf messages, gRPC services,
  generated C# clients and a REST gateway &mdash; at build time, with no reflection.</p>
  <ul class="promise">
    <li>Every option, every mapping rule, every diagnostic &mdash; with the code it produces</li>
    <li>A deep dive into the mapping: scalars, shapes, names, presence, operations</li>
    <li>Using the generated messages, servers, clients and the proxy gateway</li>
    <li>Complete worked example: one specification from YAML to running client</li>
  </ul>
  <div class="imprint">
    {PRODUCT} is published as <strong>SharpPortico.SourceGenerator</strong>,
    <strong>SharpPortico.Runtime</strong> and <strong>SharpPortico.Cli</strong> on nuget.org.<br />
    This manual describes version {VERSION} exactly. Code shown as generated output is
    copied from a build of this version.
  </div>
</div>
"""


def toc_html(headings: list[tuple[int, str, str]], pages: dict[str, int] | None) -> str:
    rows = ['<div class="toc-page">', '<h1 id="contents">Contents</h1>']
    for level, title, anchor in headings:
        if level > 2:
            continue
        number = f'{pages.get(anchor, "")}' if pages else "&middot;"
        rows.append(
            f'<div class="toc-entry lvl{level}">'
            f'<span class="title"><a href="#{anchor}">{html.escape(title)}</a></span>'
            f'<span class="dots"></span><span class="pg">{number}</span></div>'
        )
    rows.append(
        '<p class="toc-note">Page numbers are the physical pages of this PDF; the cover is page 1. '
        'Every chapter starts on a new page. The running footer is added after printing.</p>'
    )
    rows.append("</div>")
    return "\n".join(rows)


def assemble(headings, bodies: list[str], pages=None) -> str:
    body = "\n".join(bodies)
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>{html.escape(MANUAL_TITLE)}</title>
<meta name="author" content="MPCoreDeveloper" />
<meta name="description" content="The complete SharpPortico manual: OpenAPI to gRPC mapping, generated C#, proxy gateway, CLI and diagnostics." />
<link rel="stylesheet" href="{CSS.as_uri()}" />
</head>
<body>
{cover_html()}
{toc_html(headings, pages)}
<main class="manual-body">
{body}
</main>
</body>
</html>
"""


CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]


def find_chrome() -> str:
    for candidate in CHROME_CANDIDATES:
        if candidate and Path(candidate).exists():
            return candidate
    for name in ("chrome", "chrome.exe", "msedge", "msedge.exe"):
        found = shutil.which(name)
        if found:
            return found
    sys.exit("no Chrome or Edge found: the PDF is printed by a browser engine")


def render(html_text: str, out: Path) -> None:
    """Print one HTML document to PDF with a headless browser."""
    page = BUILD / "manual.html"
    page.write_text(html_text, encoding="utf-8")
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()

    profile = tempfile.mkdtemp(prefix="sharpportico-manual-")
    try:
        command = [
            find_chrome(),
            "--headless=new",
            "--disable-gpu",
            "--no-sandbox",
            "--no-first-run",
            "--disable-extensions",
            "--no-pdf-header-footer",
            "--print-to-pdf-no-header",
            "--run-all-compositor-stages-before-draw",
            "--virtual-time-budget=20000",
            f"--user-data-dir={profile}",
            f"--print-to-pdf={out}",
            page.as_uri(),
        ]
        result = subprocess.run(command, capture_output=True, text=True, timeout=300)
    finally:
        shutil.rmtree(profile, ignore_errors=True)

    if not out.exists():
        sys.stderr.write(result.stdout + "\n" + result.stderr + "\n")
        sys.exit("the browser did not produce a PDF")


def normalise(line: str) -> str:
    text = unicodedata.normalize("NFKC", line)
    return re.sub(r"\s+", " ", text).strip()


TOC_MARKER = "cover is page 1"


def body_start(pdf: Path) -> int:
    """The first page of the body: everything before it is cover and contents."""
    pages = [normalise(p.extract_text() or "") for p in PdfReader(str(pdf)).pages]
    for index, text in enumerate(pages):
        if TOC_MARKER in text:
            return index + 1
    return 1


def locate_pages(pdf: Path, headings, start: int = 0) -> tuple[dict[str, int], int]:
    """Find the page each heading starts on, by reading the produced PDF's text."""
    reader = PdfReader(str(pdf))
    pages = [normalise(page.extract_text() or "") for page in reader.pages]
    hits: dict[str, int] = {}
    cursor = start
    for _level, title, anchor in headings:
        wanted = normalise(title)
        found = None
        for index in range(cursor, len(pages)):
            if wanted and wanted in pages[index].splitlines():
                found = index
                break
        if found is None:                      # a heading broken over two lines
            for index in range(cursor, len(pages)):
                if wanted and wanted in pages[index]:
                    found = index
                    break
        if found is None:
            hits[anchor] = cursor + 1          # keep the order and move on
        else:
            hits[anchor] = found + 1
            cursor = found
    return hits, len(pages)


def pdf_string(text: str) -> str:
    """A PDF literal string: only WinAnsi-representable characters survive."""
    safe = text.encode("latin-1", "replace").decode("latin-1")
    return safe.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")


def footer_overlay(writer: PdfWriter, width: float, height: float, left: str, right: str):
    """A transparent page carrying the running footer, ready to be merged."""
    overlay = PageObject.create_blank_page(width=width, height=height)

    font = DictionaryObject()
    font[NameObject("/Type")] = NameObject("/Font")
    font[NameObject("/Subtype")] = NameObject("/Type1")
    font[NameObject("/BaseFont")] = NameObject("/Helvetica")
    font[NameObject("/Encoding")] = NameObject("/WinAnsiEncoding")
    font_ref = writer._add_object(font)

    resources = DictionaryObject()
    resources[NameObject("/Font")] = DictionaryObject({NameObject("/F1"): font_ref})
    overlay[NameObject("/Resources")] = resources

    margin = 48.2                       # 17mm, the same inset as the stylesheet
    baseline = 30.0
    size = 8.0
    rule_y = baseline + 12.0

    # Helvetica averages a little under half an em per glyph; the estimate only has
    # to place the right-hand footer, and a millimetre of drift is invisible.
    right_width = 0.5 * size * len(right)
    right_x = width - margin - right_width

    operations = [
        "q",
        "0.85 0.82 0.90 RG",
        "0.4 w",
        f"{margin:.2f} {rule_y:.2f} m {width - margin:.2f} {rule_y:.2f} l S",
        "0.42 0.34 0.55 rg",
        f"BT /F1 {size:.2f} Tf 1 0 0 1 {margin:.2f} {baseline:.2f} Tm ({pdf_string(left)}) Tj ET",
        f"BT /F1 {size:.2f} Tf 1 0 0 1 {right_x:.2f} {baseline:.2f} Tm ({pdf_string(right)}) Tj ET",
        "Q",
    ]

    stream = DecodedStreamObject()
    stream.set_data("\n".join(operations).encode("latin-1", "replace"))
    overlay[NameObject("/Contents")] = writer._add_object(stream)
    return overlay


def stamp(source: Path, target: Path, headings) -> None:
    """Add the running footer, the outline and the document metadata."""
    writer = PdfWriter()
    writer.append(PdfReader(str(source)))

    total = len(writer.pages)
    for index, page in enumerate(writer.pages):
        if index == 0:                  # the cover carries no footer
            continue
        overlay = footer_overlay(
            writer,
            float(page.mediabox.width),
            float(page.mediabox.height),
            f"SharpPortico {VERSION}  |  The Manual",
            f"Page {index + 1} of {total}",
        )
        page.merge_page(overlay)

    # Outline: parts (h1) with their sections (h2) and sub-sections (h3) beneath.
    page_text = [normalise(p.extract_text() or "") for p in PdfReader(str(source)).pages]
    cursor = body_start(source)     # skip the cover and contents; every heading is in the body
    parents: dict[int, object] = {}
    for level, title, _anchor in headings:
        wanted = normalise(title)
        found = cursor
        for index in range(cursor, len(page_text)):
            if wanted and wanted in page_text[index]:
                found = index
                break
        cursor = found
        parents[level] = writer.add_outline_item(title, found, parent=parents.get(level - 1))

    writer.add_metadata(
        {
            "/Title": MANUAL_TITLE,
            "/Author": "MPCoreDeveloper",
            "/Subject": "OpenAPI to gRPC: mapping rules, generated code, proxy gateway, CLI, diagnostics",
            "/Keywords": f"SharpPortico, gRPC, OpenAPI, protobuf, C#, source generator, manual, {VERSION}",
            "/Creator": "SharpPortico manual builder (markdown + Chrome + pypdf)",
            "/Producer": "Chrome print-to-pdf, stamped with pypdf",
        }
    )

    with open(target, "wb") as handle:
        writer.write(handle)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Build the SharpPortico manual PDF.")
    parser.add_argument("--out", default=str(DOCS / f"SharpPortico-Manual-{VERSION}.pdf"))
    parser.add_argument("--keep-html", action="store_true", help="keep build/manual.html")
    parser.add_argument("--no-stamp", action="store_true", help="skip footer, outline and metadata")
    args = parser.parse_args(argv)

    BUILD.mkdir(parents=True, exist_ok=True)
    out = Path(args.out).resolve()

    sources = sorted(SRC.glob("*.md"))
    if not sources:
        print(f"no chapters in {SRC}")
        return 1

    bodies = [convert(path) for path in sources]
    used_anchors: set[str] = set()
    bodies = [uniquify_heading_ids(body, used_anchors) for body in bodies]
    headings: list[tuple[int, str, str]] = []
    for body in bodies:
        headings.extend(collect_headings(body))

    # Pass 1: layout with placeholder numbers, so the body's pagination is final.
    first = BUILD / "pass1.pdf"
    render(assemble(headings, bodies), first)
    start = body_start(first)
    pages, _total = locate_pages(first, headings, start)

    # Pass 2: the measured numbers. Layout must not move; verify, and settle if it did.
    second = BUILD / "pass2.pdf"
    render(assemble(headings, bodies, pages), second)
    for _ in range(2):
        measured, _total = locate_pages(second, headings, start)
        if measured == pages:
            break
        pages = measured
        render(assemble(headings, bodies, pages), second)

    if args.no_stamp:
        shutil.copyfile(second, out)
    else:
        stamp(second, out, headings)

    if not args.keep_html:
        (BUILD / "manual.html").unlink(missing_ok=True)

    # Verify what was written: it must reopen, keep its pages, and carry the footer.
    check = PdfReader(str(out))
    text = check.pages[min(1, len(check.pages) - 1)].extract_text() or ""
    footer_ok = "Page 2 of" in text or len(check.pages) < 2

    print(f"chapters : {len(sources)}")
    print(f"headings : {len(headings)} (contents lists {sum(1 for h in headings if h[0] <= 2)})")
    print(f"pages    : {len(check.pages)}")
    print(f"footer   : {'ok' if footer_ok else 'MISSING'}")
    print(f"output   : {out} ({out.stat().st_size / 1024:.0f} KB)")
    return 0 if footer_ok else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
