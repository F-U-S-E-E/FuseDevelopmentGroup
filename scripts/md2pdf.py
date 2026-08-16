"""Render a set of Markdown docs into a single paginated PDF manual."""
import html
import os
import re
import sys

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import LETTER
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (BaseDocTemplate, CondPageBreak, Frame,
                                KeepTogether, ListFlowable, ListItem,
                                PageBreak, PageTemplate, Paragraph, Preformatted,
                                Spacer, Table, TableStyle)

ACCENT = colors.HexColor("#8a3324")
INK = colors.HexColor("#1a1a1a")
MUTED = colors.HexColor("#5b5b5b")
RULE = colors.HexColor("#c9c4bd")
CODE_BG = colors.HexColor("#f4f2ef")

ss = getSampleStyleSheet()


def _st(name, **kw):
    base = dict(fontName="Helvetica", fontSize=9.5, leading=13.5, textColor=INK,
                alignment=TA_LEFT, spaceBefore=0, spaceAfter=0)
    base.update(kw)
    return ParagraphStyle(name, **base)


S = {
    "title":   _st("t", fontName="Helvetica-Bold", fontSize=26, leading=30, textColor=ACCENT),
    "subtitle": _st("st", fontSize=12, leading=16, textColor=MUTED),
    "h1":      _st("h1", fontName="Helvetica-Bold", fontSize=17, leading=21,
                   textColor=ACCENT, spaceBefore=16, spaceAfter=7),
    "h2":      _st("h2", fontName="Helvetica-Bold", fontSize=12.5, leading=16,
                   textColor=INK, spaceBefore=12, spaceAfter=5),
    "h3":      _st("h3", fontName="Helvetica-Bold", fontSize=10.5, leading=14,
                   textColor=INK, spaceBefore=9, spaceAfter=3),
    "body":    _st("body", spaceAfter=6),
    "li":      _st("li", spaceAfter=2.5),
    "code":    ParagraphStyle("code", fontName="Courier", fontSize=8.2, leading=11,
                              textColor=INK, leftIndent=7),
    "cell":    _st("cell", fontSize=8.6, leading=11.5),
    "cellh":   _st("cellh", fontName="Helvetica-Bold", fontSize=8.6, leading=11.5),
    "toc":     _st("toc", fontSize=10, leading=16),
}

INLINE = [
    (re.compile(r"`([^`]+)`"), r'<font face="Courier" size="8.6">\1</font>'),
    (re.compile(r"\*\*([^*]+)\*\*"), r"<b>\1</b>"),
    (re.compile(r"(?<![\w*])\*([^*\n]+)\*(?![\w*])"), r"<i>\1</i>"),
]


def inline(text):
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)   # links -> label
    text = html.escape(text, quote=False)
    text = text.replace("&lt;", "<").replace("&gt;", ">") if False else text
    for pat, rep in INLINE:
        text = pat.sub(rep, text)
    return text


def split_row(line):
    line = line.strip()
    if line.startswith("|"):
        line = line[1:]
    if line.endswith("|"):
        line = line[:-1]
    return [c.strip() for c in line.split("|")]


def parse(md, story, width):
    lines = md.split("\n")
    i = 0
    while i < len(lines):
        ln = lines[i]
        st = ln.strip()

        if not st:
            i += 1
            continue

        # fenced code
        if st.startswith("```"):
            i += 1
            buf = []
            while i < len(lines) and not lines[i].strip().startswith("```"):
                buf.append(lines[i])
                i += 1
            i += 1
            if buf:
                body = "\n".join(buf)
                tbl = Table([[Preformatted(body, S["code"])]], colWidths=[width])
                tbl.setStyle(TableStyle([
                    ("BACKGROUND", (0, 0), (-1, -1), CODE_BG),
                    ("BOX", (0, 0), (-1, -1), 0.4, RULE),
                    ("LEFTPADDING", (0, 0), (-1, -1), 7),
                    ("RIGHTPADDING", (0, 0), (-1, -1), 7),
                    ("TOPPADDING", (0, 0), (-1, -1), 6),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
                ]))
                story += [Spacer(1, 3), tbl, Spacer(1, 8)]
            continue

        # table
        if st.startswith("|") and i + 1 < len(lines) and re.match(r"^\|[\s:|-]+\|?\s*$", lines[i + 1].strip()):
            head = split_row(st)
            i += 2
            rows = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                rows.append(split_row(lines[i]))
                i += 1
            ncol = len(head)
            data = [[Paragraph(inline(c), S["cellh"]) for c in head]]
            for r in rows:
                r = (r + [""] * ncol)[:ncol]
                data.append([Paragraph(inline(c), S["cell"]) for c in r])
            first = min(2.35 * inch, width * 0.40) if ncol > 1 else width
            rest = (width - first) / max(1, ncol - 1) if ncol > 1 else 0
            cw = [first] + [rest] * (ncol - 1)
            tbl = Table(data, colWidths=cw, repeatRows=1, hAlign="LEFT")
            tbl.setStyle(TableStyle([
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#ece8e2")),
                ("LINEBELOW", (0, 0), (-1, 0), 0.7, ACCENT),
                ("GRID", (0, 0), (-1, -1), 0.25, RULE),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 5),
                ("RIGHTPADDING", (0, 0), (-1, -1), 5),
                ("TOPPADDING", (0, 0), (-1, -1), 3.5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 3.5),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#faf9f7")]),
            ]))
            story += [Spacer(1, 3), tbl, Spacer(1, 9)]
            continue

        # headings
        m = re.match(r"^(#{1,4})\s+(.*)", st)
        if m:
            lvl, txt = len(m.group(1)), m.group(2)
            key = "h1" if lvl <= 1 else ("h2" if lvl == 2 else "h3")
            story.append(CondPageBreak(0.85 * inch) if key != "h1" else Spacer(1, 2))
            story.append(Paragraph(inline(txt), S[key]))
            if key == "h1":
                story.append(Spacer(1, 1))
            i += 1
            continue

        # horizontal rule
        if re.match(r"^(-{3,}|\*{3,}|_{3,})$", st):
            story += [Spacer(1, 5),
                      Table([[""]], colWidths=[width],
                            style=TableStyle([("LINEABOVE", (0, 0), (-1, -1), 0.5, RULE)])),
                      Spacer(1, 5)]
            i += 1
            continue

        # lists
        if re.match(r"^([-*+]|\d+\.)\s+", st):
            items, ordered = [], bool(re.match(r"^\d+\.", st))
            while i < len(lines) and re.match(r"^\s*([-*+]|\d+\.)\s+", lines[i]):
                txt = re.sub(r"^\s*([-*+]|\d+\.)\s+", "", lines[i])
                i += 1
                while i < len(lines) and lines[i].startswith("  ") and lines[i].strip() \
                        and not re.match(r"^\s*([-*+]|\d+\.)\s+", lines[i]):
                    txt += " " + lines[i].strip()
                    i += 1
                items.append(ListItem(Paragraph(inline(txt), S["li"]), leftIndent=15))
            story.append(ListFlowable(items, bulletType="1" if ordered else "bullet",
                                      bulletFontSize=7, leftIndent=15, start="1" if ordered else None))
            story.append(Spacer(1, 6))
            continue

        # paragraph
        buf = []
        while i < len(lines) and lines[i].strip() and not re.match(
                r"^(#{1,4}\s|\||```|[-*+]\s|\d+\.\s|(-{3,}|\*{3,}|_{3,})$)", lines[i].strip()):
            buf.append(lines[i].strip())
            i += 1
        if buf:
            story.append(Paragraph(inline(" ".join(buf)), S["body"]))


def build(out_path, title, subtitle, sections):
    """sections: list of (heading, markdown_path)"""
    doc = BaseDocTemplate(out_path, pagesize=LETTER,
                          leftMargin=0.85 * inch, rightMargin=0.85 * inch,
                          topMargin=0.8 * inch, bottomMargin=0.75 * inch,
                          title=title, author="Hunter Rogers")
    width = doc.width

    def decorate(canv, d):
        canv.saveState()
        canv.setFont("Helvetica", 7.5)
        canv.setFillColor(MUTED)
        canv.drawString(d.leftMargin, 0.45 * inch, title)
        canv.drawRightString(d.leftMargin + width, 0.45 * inch, "%d" % canv.getPageNumber())
        canv.setStrokeColor(RULE)
        canv.setLineWidth(0.4)
        canv.line(d.leftMargin, 0.62 * inch, d.leftMargin + width, 0.62 * inch)
        canv.restoreState()

    frame = Frame(doc.leftMargin, doc.bottomMargin, width, doc.height, id="n")
    doc.addPageTemplates([PageTemplate(id="main", frames=[frame], onPage=decorate)])

    story = [Spacer(1, 1.7 * inch), Paragraph(html.escape(title), S["title"]), Spacer(1, 7)]
    if subtitle:
        story += [Paragraph(html.escape(subtitle), S["subtitle"])]
    story += [Spacer(1, 14),
              Table([[""]], colWidths=[width],
                    style=TableStyle([("LINEABOVE", (0, 0), (-1, -1), 1.1, ACCENT)]))]

    present = [(h, p) for h, p in sections if os.path.isfile(p)]
    story += [Spacer(1, 20), Paragraph("Contents", S["h2"])]
    for n, (h, _) in enumerate(present, 1):
        story.append(Paragraph("%d.&nbsp;&nbsp;%s" % (n, html.escape(h)), S["toc"]))
    story.append(PageBreak())

    for h, p in present:
        story.append(Paragraph(html.escape(h), S["h1"]))
        with open(p, encoding="utf-8") as fh:
            md = fh.read()
        md = re.sub(r"^#\s+.*\n", "", md, count=1)      # drop duplicate H1
        parse(md, story, width)
        story.append(PageBreak())

    if story and isinstance(story[-1], PageBreak):
        story.pop()
    doc.build(story)
    return out_path, len(present)
