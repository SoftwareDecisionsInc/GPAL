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

using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerallyPositive
{
    /// <summary>
    /// Backing storage for <see cref="GPALMail"/>: server connection settings (IMAP/POP/SMTP host, port, credentials),
    /// the email envelope (from/to/cc/bcc, subject, body, attachments), and any messages retrieved via <see cref="GPALMail.Receive"/>.
    /// </summary>
    internal class GPALMailSettings
    {
        public string IMAPServerName { get; set; }
        public string POPServerName { get; set; }
        public string SMTPServerName { get; set; }
        public int IMAPPortNum { get; set; }
        public int POPPortNum { get; set; }
        public int SMTPPortNum { get; set; }

        // Whether a send may accept a certificate that fails validation. Off by default. See
        // GPALMail.WithAcceptAnyCertificate.
        public bool AcceptAnyCertificate { get; set; } = false;
        // Internal: as a public MailboxAddress it would be written to the settings file as a whole MimeKit object that
        // cannot be read back.
        internal MailboxAddress FromEmailAddress { get; set; }

        /// <summary>
        /// The address messages are sent from, as text, which is the form a settings file can hold and read back.
        /// Round trips the display name too: "Dead Mike &lt;mike@deadmike.com&gt;" comes back as both.
        /// </summary>
        public string FromAddress
        {
            get
            {
                return FromEmailAddress?.ToString();
            }
            set
            {
                FromEmailAddress = true == string.IsNullOrWhiteSpace(value) ? null : MailboxAddress.Parse(value);
            }
        }
        internal List<MailboxAddress> ToEmailAddresses { get; set; } = new List<MailboxAddress>();
        internal List<MailboxAddress> CcEmailAddresses { get; set; } = new List<MailboxAddress>();
        internal List<MailboxAddress> BccEmailAddresses { get; set; } = new List<MailboxAddress>();
        internal List<string> Attachments { get; set; } = new List<string>();
        internal string BodyText { get; set; }
        internal string BodyHTML { get; set; }
        internal string Subject { get; set; }
        internal List<ReceivedEmail> ReceivedEmails { get; set; } = new List<ReceivedEmail>();

        // Limits on what Receive fetches. Default is the whole mailbox.
        internal bool UnreadOnly { get; set; }
        internal int MaxMessages { get; set; } = int.MaxValue;
        // Internal, so the settings file never holds the username or password; those belong in a credential.
        internal string UserName { get; set; }
        internal string Password { get; set; }
        internal ICredentials Credentials { get; set; }
    }
}

