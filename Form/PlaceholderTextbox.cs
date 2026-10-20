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

        public string PlaceholderText
        {
            get { return placeholderText; }
            set
            {
                placeholderText = value;
                SetPlaceholder();
            }
        }

        private bool _suppressPlaceholder = false;

        public PlaceholderTextBox()
        {
            TextChanged += OnTextChanged;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);

            // Clear on focus only when the text on screen is the placeholder. Trusting the flag let a click wipe real
            // text.
            if (isPlaceholder && this.Text == placeholderText)
            {
                _suppressPlaceholder = true;
                isPlaceholder = false;
                this.ForeColor = System.Drawing.SystemColors.WindowText;
                this.Text = "";
                _suppressPlaceholder = false;
            }
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (!isPlaceholder && string.IsNullOrEmpty(this.Text))
                SetPlaceholder();
        }

        private void SetPlaceholder()
        {
            // A box with no placeholder must not be flagged as showing one, or the next text looks like placeholder and
            // the next click wipes it. OnTextChanged returns early without a placeholder, so nothing would clear the
            // flag.
            if (!isPlaceholder && !string.IsNullOrEmpty(placeholderText))
            {
                this.Text = placeholderText;
                this.ForeColor = Color.Gray;
                isPlaceholder = true;
            }
        }

        private void RemovePlaceHolder()
        {
            if (isPlaceholder)
            {
                this.ForeColor = System.Drawing.SystemColors.WindowText;
                isPlaceholder = false;
            }
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            if (_suppressPlaceholder || string.IsNullOrEmpty(placeholderText))
                return;

            if (this.Text.Length == 0 && !this.Focused)
                SetPlaceholder();
            else
                RemovePlaceHolder();
        }
    }
}

