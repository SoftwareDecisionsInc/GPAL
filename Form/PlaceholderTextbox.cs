// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GenerallyPositive.GPALForm
{
    /// <summary>
    /// https://stackoverflow.com/questions/11873378/adding-placeholder-text-to-textbox
    /// </summary>
    public class PlaceholderTextBox : TextBox
    {
        private bool isPlaceholder = false;
        private string placeholderText;

        // the bullet a password box shows; the box shows the placeholder in plain text and the bullets once there is
        // text of its own
        private char passwordChar = '\0';

        private bool _suppressPlaceholder = false;

        public PlaceholderTextBox()
        {
            TextChanged += OnTextChanged;
        }

        public string PlaceholderText
        {
            get { return placeholderText; }
            set
            {
                placeholderText = value;

                // a placeholder already on screen is swapped for the new one, or taken away when there is none
                HidePlaceholder();
                SetPlaceholder();
            }
        }

        /// <summary>
        /// True while the box shows its placeholder rather than text of its own.
        /// </summary>
        public bool ShowingPlaceholder => isPlaceholder;

        /// <summary>
        /// The box's own text: empty while it shows its placeholder.
        /// </summary>
        public string RealText => true == isPlaceholder ? "" : base.Text;

        /// <summary>
        /// The text on screen, the placeholder included. Emptying a box that shows its placeholder changes nothing:
        /// GPAL writes every value it copies from the box back into it, and an empty value would otherwise take the
        /// placeholder down and put it back without end.
        /// </summary>
        public override string Text
        {
            get { return base.Text; }
            set
            {
                if (false == (isPlaceholder && string.IsNullOrEmpty(value)))
                    base.Text = value;
            }
        }

        /// <summary>
        /// The character that hides what is typed in a password box. The placeholder shows in plain text.
        /// </summary>
        public new char PasswordChar
        {
            get { return passwordChar; }
            set
            {
                passwordChar = value;
                base.PasswordChar = true == isPlaceholder ? '\0' : value;
            }
        }

        /// <summary>
        /// Takes the placeholder off the screen and leaves the box empty, for code about to add text to it.
        /// </summary>
        internal void HidePlaceholder()
        {
            if (isPlaceholder)
            {
                _suppressPlaceholder = true;
                isPlaceholder = false;
                this.ForeColor = SystemColors.WindowText;
                base.PasswordChar = passwordChar;
                base.Text = "";
                _suppressPlaceholder = false;
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);

            HidePlaceholder();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);

            SetPlaceholder();
        }

        // Shows the placeholder in an empty box that does not have the focus, so text of the box's own is never
        // replaced and typing never lands after it. The flag is set before the text, so a TextChanged handler reading
        // ShowingPlaceholder or RealText already sees the placeholder.
        private void SetPlaceholder()
        {
            if (!isPlaceholder && !string.IsNullOrEmpty(placeholderText) && string.IsNullOrEmpty(base.Text) && !this.Focused)
            {
                _suppressPlaceholder = true;
                isPlaceholder = true;
                base.PasswordChar = '\0';
                this.ForeColor = Color.Gray;
                base.Text = placeholderText;
                _suppressPlaceholder = false;

                // a TextChanged handler that put text of its own in the box while the placeholder went up keeps it,
                // and TextChanged is raised again so GPAL's copy of the text and the handlers see it
                if (placeholderText != base.Text)
                {
                    RemovePlaceHolder();
                    base.OnTextChanged(EventArgs.Empty);
                }
            }
        }

        // text of the box's own arrived while the placeholder showed
        private void RemovePlaceHolder()
        {
            if (isPlaceholder)
            {
                this.ForeColor = SystemColors.WindowText;
                base.PasswordChar = passwordChar;
                isPlaceholder = false;
            }
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (false == _suppressPlaceholder && false == string.IsNullOrEmpty(placeholderText))
            {
                // emptied in code while it does not have the focus: the placeholder goes back on screen
                if (0 == base.Text.Length)
                    SetPlaceholder();
                else
                    RemovePlaceHolder();
            }
        }
    }
}

