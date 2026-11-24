// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.GPALForm
{
    /// <summary>
    /// A read-only display control for rendering simple Markdown - headings (#, ##, ###), **bold**,
    /// *italic*, __underline__, ~~strikethrough~~, `inline code`, {colorname}text{} color spans,
    /// --- horizontal rules, and bullet (-, *) or numbered (1.) lists with nesting via leading spaces.
    /// Intended for displaying formatted AI responses or other structured text.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALRichTextBox gPalOutput = (GPALRichTextBox)GPAL.RichTextBox
    ///     .WithName("AIOutput")
    ///     .ToGPALObject();
    ///
    /// gPalOutput.Text = "# Summary\n\n**Key points:**\n- *First* point\n- __Second__ point\n- {red}Warning{}\n  - Nested detail";
    /// </code>
    /// </example>
    public class GPALRichTextBox : GPALControl, IAllowRichTextBoxControlSettings
    {
        private string _text = string.Empty;

        internal GPALRichTextBox()
        {
            ControlType = FormControlType.RichTextBox;
            // A rich text box shows rendered markdown, so it is read-only by default.
            IsReadOnly = true;
        }

        /// <summary>
        /// The Markdown source text. Setting this re-renders headings, **bold** spans, and bullet/numbered
        /// lists (with nesting via leading spaces) into the live control (if realized), marshalling to the
        /// UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public new string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                if (WindowsControl is RichTextBox rtb && true == Alive(rtb))
                {
                    PostToUi(rtb, () => RenderMarkdown(rtb, _text));
                }
            }
        }

        /// <summary>
        /// Appends <paramref name="text"/> as a new line, thread-safe.<br/>
        /// The line is appended verbatim rather than rendered as Markdown, so a line beginning with # or * is
        /// not treated as a heading or a bullet. <see cref="Text"/> is updated to match.
        /// </summary>
        /// <param name="text">The line to append.</param>
        /// <param name="maxLines">Rolling cap; 0 means unlimited.</param>
        public void AppendLine(string text, int maxLines = 0)
        {
            _text = 0 == _text.Length ? text : _text + Environment.NewLine + text;

            AppendLineCore(text, maxLines);
        }

        /// <summary>
        /// Empties the control and anything queued for it, thread-safe.
        /// </summary>
        public void Clear()
        {
            _text = string.Empty;

            ClearLinesCore();
        }

        /// <summary>
        /// Scrolls to the last line, thread-safe.
        /// </summary>
        public void ScrollToEnd() => ScrollToEndCore();

        /// <summary>
        /// Sets the initial Markdown source text.
        /// </summary>
        /// <param name="text">Markdown text to render.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public new IAllowRichTextBoxControlSettings WithText(string text)
        {
            this.Text = text;
            return this;
        }


        public override IAllowControlSettings WithReadOnly(bool readOnly = true)
        {
            IsReadOnly = readOnly;
            if (WindowsControl is RichTextBox rtb && true == Alive(rtb))
                rtb.ReadOnly = readOnly;
            return this;
        }

        // matches **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, {colorname}text{}; bold before italic so ** is not read as two italics
        private static readonly Regex _inlinePattern = new Regex(
            @"\*\*(?<b>.+?)\*\*|\*(?<i>.+?)\*|__(?<u>.+?)__|~~(?<s>.+?)~~|`(?<code>.+?)`|{(?<c>[^}]+)}(?<ct>.+?)\{\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Renders Markdown source text into a <see cref="RichTextBox"/>:
        /// lines starting with #, ##, or ### become progressively larger bold headings;
        /// a line containing only --- becomes a gray horizontal rule;
        /// inline **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, and {colorname}text{} spans are supported;
        /// lines starting with "- ", "* ", or "1. " (optionally indented in steps of two spaces)
        /// become bulleted/numbered paragraphs with indentation proportional to nesting depth.
        /// </summary>
        // Fonts used by the renderer, one per family, size and style; SelectionFont copies from it. Each font holds a
        // GDI handle, and re-rendering on every append would run out of handles with new fonts per render.
        private static readonly Dictionary<string, Font> renderFonts = new Dictionary<string, Font>();
        private static readonly object renderFontGate = new object();

        /// <summary>
        /// The font for one styled run of rendered Markdown, made once and kept.
        /// </summary>
        /// <param name="family">The font family name</param>
        /// <param name="size">The size in points</param>
        /// <param name="style">Bold, italic and the rest</param>
        /// <returns>The font, which belongs to the cache and must not be disposed by the caller</returns>
        private static Font RenderFont(string family, float size, FontStyle style)
        {
            string key = $"{family}|{size}|{(int)style}";
            Font retVal;

            lock (renderFontGate)
            {
                if (false == renderFonts.TryGetValue(key, out retVal))
                {
                    retVal = new Font(family, size, style);
                    renderFonts[key] = retVal;
                }
            }

            return retVal;
        }

        internal static void RenderMarkdown(RichTextBox rtb, string markdown)
        {
            rtb.Clear();

            Font baseFont = rtb.Font;
            Color baseColor = rtb.ForeColor;
            string codeFamily = "Courier New";
            string[] lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine;

                int headingLevel = 0;
                while (headingLevel < line.Length && line[headingLevel] == '#')
                    headingLevel++;
                if (headingLevel > 0 && headingLevel <= 3 && headingLevel < line.Length && line[headingLevel] == ' ')
                    line = line.Substring(headingLevel + 1);
                else
                    headingLevel = 0;

                int leadingSpaces = 0;
                while (leadingSpaces < line.Length && line[leadingSpaces] == ' ')
                    leadingSpaces++;
                int indentLevel = leadingSpaces / 2;
                string trimmed = line.Substring(leadingSpaces);

                // horizontal rule
                if (trimmed == "---")
                {
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.SelectionLength = 0;
                    rtb.SelectionFont = RenderFont(baseFont.FontFamily.Name, baseFont.Size, FontStyle.Regular);
                    rtb.SelectionColor = Color.Gray;
                    rtb.AppendText(new string('-', 48) + "\n");
                    continue;
                }

                bool isBulletItem = false;
                bool isListItem = false;
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    isBulletItem = true;
                    isListItem = true;
                    trimmed = trimmed.Substring(2);
                }
                else
                {
                    Match numberedMatch = Regex.Match(trimmed, @"^(\d+\.\s+)(.*)$");
                    if (numberedMatch.Success)
                    {
                        isListItem = true;
                        trimmed = numberedMatch.Groups[1].Value + numberedMatch.Groups[2].Value;
                    }
                }

                int lineStart = rtb.TextLength;

                FontStyle headingBase = headingLevel > 0 ? FontStyle.Bold : FontStyle.Regular;
                float headingSize = headingLevel > 0 ? baseFont.Size + (4 - headingLevel) * 2 : baseFont.Size;

                foreach ((string segmentText, FontStyle style, Color? color, bool isCode) in SplitInline(trimmed, headingBase))
                {
                    rtb.SelectionStart = rtb.TextLength;
                    rtb.SelectionLength = 0;
                    rtb.SelectionFont = isCode
                        ? RenderFont(codeFamily, baseFont.Size, FontStyle.Regular)
                        : RenderFont(baseFont.FontFamily.Name, headingSize, style);
                    rtb.SelectionColor = isCode ? Color.SteelBlue : (color ?? baseColor);
                    rtb.AppendText(segmentText);
                }
                rtb.AppendText("\n");

                if (isListItem)
                {
                    rtb.SelectionStart = lineStart;
                    rtb.SelectionLength = rtb.TextLength - lineStart;
                    rtb.SelectionBullet = isBulletItem;
                    rtb.SelectionIndent = 20 * (indentLevel + 1);
                }
            }

            rtb.SelectionStart = 0;
            rtb.SelectionLength = 0;
        }

        /// <summary>
        /// Splits a line into inline spans supporting **bold**, *italic*, __underline__, ~~strikethrough~~, `code`, and {colorname}text{}.
        /// <paramref name="inheritBase"/> is OR'd into every span's style so heading bold propagates into children.
        /// Color names are resolved via <see cref="Color.FromName"/>; unrecognised names fall back to null (default color).
        /// Code spans use monospace rendering regardless of style.
        /// </summary>
        private static List<(string Text, FontStyle Style, Color? Color, bool IsCode)> SplitInline(string line, FontStyle inheritBase = FontStyle.Regular)
        {
            var result = new List<(string Text, FontStyle Style, Color? Color, bool IsCode)>();
            int pos = 0;

            foreach (Match m in _inlinePattern.Matches(line))
            {
                if (m.Index > pos)
                    result.Add((line.Substring(pos, m.Index - pos), inheritBase, null, false));

                if (m.Groups["b"].Success)
                    result.Add((m.Groups["b"].Value, inheritBase | FontStyle.Bold, null, false));
                else if (m.Groups["i"].Success)
                    result.Add((m.Groups["i"].Value, inheritBase | FontStyle.Italic, null, false));
                else if (m.Groups["u"].Success)
                    result.Add((m.Groups["u"].Value, inheritBase | FontStyle.Underline, null, false));
                else if (m.Groups["s"].Success)
                    result.Add((m.Groups["s"].Value, inheritBase | FontStyle.Strikeout, null, false));
                else if (m.Groups["code"].Success)
                    result.Add((m.Groups["code"].Value, FontStyle.Regular, null, true));
                else if (m.Groups["c"].Success)
                {
                    Color named = Color.FromName(m.Groups["c"].Value);
                    Color? resolved = named.IsKnownColor || named.A > 0 ? (Color?)named : null;
                    // recurse so bold/italic/underline inside a color span still render correctly
                    foreach (var inner in SplitInline(m.Groups["ct"].Value, inheritBase))
                        result.Add((inner.Item1, inner.Item2, inner.Item3 ?? resolved, inner.Item4));
                }

                pos = m.Index + m.Length;
            }

            if (pos < line.Length)
                result.Add((line.Substring(pos), inheritBase, null, false));
            if (result.Count == 0)
                result.Add((string.Empty, inheritBase, null, false));

            return result;
        }
    }
}
