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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GenerallyPositive;

namespace GenerallyPositive
{
    using System.Collections.Generic;
    using System.IO;
    using MailKit.Net.Imap;
    using MailKit.Net.Pop3;
    using MailKit;
    using MailKit.Net.Smtp;
    using MailKit.Search;
    using MimeKit;

    /// <summary>
    /// Provides a fluent API for configuring, sending, and receiving email via SMTP, IMAP, and POP.
    /// Supports specifying server connection settings, envelope details (from/to/cc/bcc, subject, body, attachments),
    /// sending mail through SMTP, retrieving messages from an IMAP inbox, and persisting/loading mail settings to/from a <see cref="GPALFile"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Mail
    ///     .WithSMTPServerName("smtp.example.com")
    ///     .WithSMTPPortNum(465)
    ///     .WithUserName("user@example.com")
    ///     .WithPassword("password")
    ///     .WithFromEmailAddress("user@example.com")
    ///     .WithToEmailAddress("recipient@example.com")
    ///     .WithSubject("Hello")
    ///     .WithBody("This is the message body.")
    ///     .Send();
    /// </code>
    /// </example>
    public class GPALMail : IGPALMail
    {
        private GPALMailSettings gPALMailSettings;
        private static string MailSettingsFilePath { get; set; } = "./GPALMail.yaml";
        /// <summary>
        /// Backing settings object that holds the server connection details, envelope addresses,
        /// subject/body content, attachments, and any received emails for this mail instance.
        /// </summary>
        private GPALMailSettings GPALMailSettings
        {
            get
            {
                return gPALMailSettings;
            }
            set
            {
                gPALMailSettings = value;
            }
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="GPALMail"/> class with empty settings,
        /// including initialized (empty) lists for to/cc/bcc addresses, attachments, and received emails.
        /// </summary>
        internal GPALMail()
        {
            GPALMailSettings = new GPALMailSettings();
            GPALMailSettings.ToEmailAddresses = new List<MailboxAddress>();
            GPALMailSettings.CcEmailAddresses = new List<MailboxAddress>();
            GPALMailSettings.BccEmailAddresses = new List<MailboxAddress>();
            GPALMailSettings.Attachments = new List<string>();
            GPALMailSettings.ReceivedEmails = new List<ReceivedEmail>();
            LoadSettings();
        }
        /// <summary>
        /// Returns this instance cast to <see cref="IGPALMail"/>, allowing the fluent chain to be
        /// converted back to the root mail interface.
        /// </summary>
        /// <returns>This instance as <see cref="IGPALMail"/>.</returns>
        public IGPALMail ToGPALObject()
        {
            return this;
        }
        /// <summary>
        /// Sets the IMAP server hostname used for receiving mail, and forgets any POP server,
        /// since <see cref="Receive"/> reads from one server or the other.
        /// </summary>
        /// <param name="serverName">The IMAP server hostname or address.</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithIMAPServerName(string serverName)
        {
            GPALMailSettings.IMAPServerName = serverName;
            GPALMailSettings.POPServerName = null;
            return this;
        }

        /// <summary>
        /// Sets the POP server hostname used for receiving mail, and forgets any IMAP server,
        /// since <see cref="Receive"/> reads from one server or the other.
        /// </summary>
        /// <param name="serverName">The POP server hostname or address.</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithPOPServerName(string serverName)
        {
            GPALMailSettings.POPServerName = serverName;
            GPALMailSettings.IMAPServerName = null;
            return this;
        }

        /// <summary>
        /// Sets the SMTP server hostname used for sending mail.
        /// </summary>
        /// <param name="serverName">The SMTP server hostname or address.</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithSMTPServerName(string serverName)
        {
            GPALMailSettings.SMTPServerName = serverName;
            return this;
        }

        /// <summary>
        /// Sets the port number used to connect to the IMAP server.
        /// </summary>
        /// <param name="portNum">The IMAP port number. Defaults to 993 (IMAP over SSL/TLS).</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithIMAPPortNum(int portNum = 993)
        {
            GPALMailSettings.IMAPPortNum = portNum;
            return this;
        }
        /// <summary>
        /// Sets the port number used to connect to the POP server.
        /// </summary>
        /// <param name="portNum">The POP port number. Defaults to 110 (plain POP3).</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithPOPPortNum(int portNum = 110)
        {
            GPALMailSettings.POPPortNum = portNum;
            return this;
        }
        /// <summary>
        /// Sets the port number used to connect to the SMTP server.
        /// </summary>
        /// <param name="portNum">The SMTP port number. Defaults to 465 (SMTP over SSL/TLS).</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithSMTPPortNum(int portNum = 465)
        {
            GPALMailSettings.SMTPPortNum = portNum;
            return this;
        }
        /// <summary>
        /// Sends even when the server's certificate cannot be validated.<br/>
        /// Off by default. The server's identity is not checked, so an impostor is accepted too. Only for an internal
        /// relay with an unsigned certificate.
        /// </summary>
        /// <param name="acceptAnyCertificate">True to send without checking the certificate</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithAcceptAnyCertificate(bool acceptAnyCertificate = true)
        {
            GPALMailSettings.AcceptAnyCertificate = acceptAnyCertificate;
            return this;
        }
        /// <summary>
        /// Sets the credential to log in with. Its username and password override <see cref="WithUserName"/> and
        /// <see cref="WithPassword"/>.
        /// </summary>
        /// <param name="credentials">The credential to log in with</param>
        /// <returns>Fluent interface to define more email settings.</returns>
        public IAllowEmailServerSettingsAndName WithCredentials(ICredentials credentials)
        {
            GPALMailSettings.Credentials = credentials;
            return this;
        }

        // Settings-file values, public so a form can show them. Message details belong to the envelope, the login to
        // the credential.
        /// <summary>When the last <see cref="Send"/> went out, null when it did not.</summary>
        public DateTime? Sent { get; private set; }

        /// <summary>
        /// Details of the last failure. <see cref="Sent"/> says whether the mail was sent; this says why not.
        /// </summary>
        public class MailError
        {
            /// <summary>Which way the call failed</summary>
            public Enums.MailFailure Failure { get; internal set; }
            /// <summary>What the failure said</summary>
            public string Message { get; internal set; }
            /// <summary>The server it was talking to</summary>
            public string Server { get; internal set; }
            /// <summary>The port on it</summary>
            public int Port { get; internal set; }
            /// <summary>The account it signed in as, null where it had none to sign in with</summary>
            public string UserName { get; internal set; }
            /// <summary>When it happened</summary>
            public DateTime Occurred { get; internal set; }
            /// <summary>The exception itself, for a caller that wants the stack</summary>
            public Exception Exception { get; internal set; }
        }

        /// <summary>
        /// The last failure, or null when the last call did not fail. Cleared at the start of <see cref="Send"/>
        /// and <see cref="Receive()"/>, so it always describes the most recent one.
        /// </summary>
        public MailError LastError { get; private set; }

        /// <summary>
        /// Records a failure in <see cref="LastError"/> and publishes it.
        /// </summary>
        /// <param name="failure">Which way it failed</param>
        /// <param name="message">What to publish, which is also what LastError carries</param>
        /// <param name="server">The server being talked to</param>
        /// <param name="port">The port on it</param>
        /// <param name="ex">The exception behind it, null where there was none</param>
        private void Failed(Enums.MailFailure failure, string message, string server, int port, Exception ex)
        {
            LastError = new MailError
            {
                Failure = failure,
                Message = message,
                Server = server,
                Port = port,
                UserName = ResolvedUserName,
                Occurred = DateTime.Now,
                Exception = ex
            };

            GPAL.PublishSimpleEvent(null == ex ? Enums.GPALEventType.ERROR : Enums.GPALEventType.EXCEPTION,
                message, this, Enums.GPALObjectType.Other, ex);
        }

        /// <summary>
        /// Connects the client and logs in if there is a username. The caller passes the client's own Connect call,
        /// keeping each protocol's options.
        /// </summary>
        /// <param name="connect">The client's own Connect call</param>
        /// <param name="authenticate">The client's own Authenticate call</param>
        /// <param name="server">The server being connected to, for the message</param>
        /// <param name="port">The port on it, for the message</param>
        /// <returns>True when connected and logged in</returns>
        private bool Reached(Action connect, Action authenticate, string server, int port)
        {
            bool retVal = false;

            try
            {
                connect();
                retVal = true;
            }
            catch (MailKit.Security.SslHandshakeException ex)
            {
                // Only a certificate failure is MailFailure.Certificate; it is the one WithAcceptAnyCertificate fixes.
                Failed(Enums.MailFailure.Certificate,
                    $"[{server}] on port [{port}] offered a certificate this machine will not accept. .WithAcceptAnyCertificate() connects anyway",
                    server, port, ex);
            }
            catch (Exception ex)
            {
                // Host not answering, nothing on the port, or connection refused. Report it; it does not end the run.
                Failed(Enums.MailFailure.Connection, $"Could not reach [{server}] on port [{port}]", server, port, ex);
            }

            // Log in only when there is a username; an open relay on a local port needs none.
            if (true == retVal && false == string.IsNullOrEmpty(ResolvedUserName))
                try
                {
                    authenticate();
                }
                catch (Exception ex)
                {
                    // Report a refused login; it does not end the run.
                    retVal = false;

                    Failed(Enums.MailFailure.Authentication, $"[{server}] refused the login for [{ResolvedUserName}]", server, port, ex);
                }

            return retVal;
        }
        /// <summary>The SMTP server mail is sent through, as set by <see cref="WithSMTPServerName"/> or read from a settings file.</summary>
        public string SMTPServerName => GPALMailSettings.SMTPServerName;
        /// <summary>The port used to reach <see cref="SMTPServerName"/>.</summary>
        public int SMTPPortNum => GPALMailSettings.SMTPPortNum;
        /// <summary>The IMAP server mail is read from, null when a POP server is the one named.</summary>
        public string IMAPServerName => GPALMailSettings.IMAPServerName;
        /// <summary>The port used to reach <see cref="IMAPServerName"/>.</summary>
        public int IMAPPortNum => GPALMailSettings.IMAPPortNum;
        /// <summary>The POP server mail is read from, null when an IMAP server is the one named.</summary>
        public string POPServerName => GPALMailSettings.POPServerName;
        /// <summary>The port used to reach <see cref="POPServerName"/>.</summary>
        public int POPPortNum => GPALMailSettings.POPPortNum;
        /// <summary>The address messages are sent from, as text, with the display name when there is one.</summary>
        public string FromAddress => GPALMailSettings.FromAddress;

        // the credential if there is one, otherwise whatever was set by hand
        private string ResolvedUserName
        {
            get
            {
                return null != GPALMailSettings.Credentials ? ((Credentials)GPALMailSettings.Credentials).Username : GPALMailSettings.UserName;
            }
        }

        private string ResolvedPassword
        {
            get
            {
                return null != GPALMailSettings.Credentials ? ((Credentials)GPALMailSettings.Credentials).Password : GPALMailSettings.Password;
            }
        }

        /// <summary>
        /// Sets the username used to authenticate with the mail server(s).
        /// </summary>
        /// <param name="userName">The account username.</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithUserName(string userName)
        {
            GPALMailSettings.UserName = userName;
            return this;
        }
        /// <summary>
        /// Sets the password used to authenticate with the mail server(s).
        /// </summary>
        /// <param name="password">The account password.</param>
        /// <returns>Fluent interface to continue configuring email server settings.</returns>
        public IAllowEmailServerSettingsAndName WithPassword(string password)
        {
            GPALMailSettings.Password = password;
            return this;
        }

        // The address added last, for .WithName.
        private MailboxAddress lastAddress = null;

        /// <summary>
        /// Sets the "From" email address for the message, parsing it from a string.
        /// </summary>
        /// <param name="mailAddress">The sender's email address as a string (e.g. "user@example.com" or "Name &lt;user@example.com&gt;").</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithFromEmailAddress(string mailAddress)
        {
            GPALMailSettings.FromEmailAddress = MailboxAddress.Parse(mailAddress);
            lastAddress = GPALMailSettings.FromEmailAddress;
            return this;
        }
        /// <summary>
        /// Adds one or more "To" recipients, parsed from a single string that may contain
        /// multiple addresses delimited by commas, semicolons, or pipes.
        /// </summary>
        /// <param name="mailAddress">A delimited string of one or more recipient email addresses.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithToEmailAddress(string mailAddress)
        {
            foreach (MailboxAddress mailboxAddress in ParseEmailAddresses(mailAddress))
            {
                GPALMailSettings.ToEmailAddresses.Add(mailboxAddress);
                lastAddress = mailboxAddress;
            }
            return this;
        }

        /// <summary>
        /// Adds one or more "Cc" recipients, parsed from a single string that may contain
        /// multiple addresses delimited by commas, semicolons, or pipes.
        /// </summary>
        /// <param name="mailAddress">A delimited string of one or more recipient email addresses.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithCcEmailAddress(string mailAddress)
        {
            foreach (MailboxAddress mailboxAddress in ParseEmailAddresses(mailAddress))
            {
                GPALMailSettings.CcEmailAddresses.Add(mailboxAddress);
                lastAddress = mailboxAddress;
            }
            return this;
        }

        /// <summary>
        /// Adds one or more "Bcc" recipients, parsed from a single string that may contain
        /// multiple addresses delimited by commas, semicolons, or pipes.
        /// </summary>
        /// <param name="mailAddress">A delimited string of one or more recipient email addresses.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithBccEmailAddress(string mailAddress)
        {
            foreach (MailboxAddress mailboxAddress in ParseEmailAddresses(mailAddress))
            {
                GPALMailSettings.BccEmailAddresses.Add(mailboxAddress);
                lastAddress = mailboxAddress;
            }
            return this;
        }
        /// <summary>
        /// Adds one or more "To" recipients from an array of email address strings.
        /// </summary>
        /// <param name="emailParts">An array of recipient email addresses.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithToEmailAddress(string[] emailParts)
        {
            foreach (MailboxAddress mailboxAddress in CreateEmailAddresses(emailParts))
            {
                GPALMailSettings.ToEmailAddresses.Add(mailboxAddress);
                lastAddress = mailboxAddress;
            }
            return this;
        }

        /// <summary>
        /// Adds one or more "Cc" recipients from an array of email address strings.
        /// </summary>
        /// <param name="emailParts">An array of recipient email addresses.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithCcEmailAddress(string[] emailParts)
        {
            foreach (MailboxAddress mailboxAddress in CreateEmailAddresses(emailParts))
            {
                GPALMailSettings.CcEmailAddresses.Add(mailboxAddress);
                lastAddress = mailboxAddress;
            }
            return this;
        }

        /// <summary>
        /// Adds one or more "Bcc" recipients from an array of email address strings.
        /// </summary>
        /// <param name="emailParts">An array of recipient email addresses.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowNameEnvelopeAndActions WithBccEmailAddress(string[] emailParts)
        {
            foreach (MailboxAddress mailboxAddress in CreateEmailAddresses(emailParts))
            {
                GPALMailSettings.BccEmailAddresses.Add(mailboxAddress);
                lastAddress = mailboxAddress;
            }
            return this;
        }
        /// <summary>
        /// Adds the file(s) referenced by the given <see cref="GPALFile"/> as email attachments.
        /// Each filename in <see cref="GPALFile.Filenames"/> is treated as a (possibly wildcarded) path;
        /// matching files in the resolved directory are added to the attachment list.
        /// Errors resolving or adding individual files are logged via <see cref="GPAL.PublishSimpleEvent"/>
        /// rather than thrown, allowing the remaining attachments to still be processed.
        /// </summary>
        /// <param name="attachmentFile">The <see cref="GPALFile"/> describing the file(s) to attach.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions WithAttachment(GPALFile attachmentFile)
        {
            foreach (string filename in attachmentFile.Filenames)
                try
                {
                    string directory = Path.GetDirectoryName(filename);
                    string filenamepart = Path.GetFileName(filename);
                    string[] matches = Directory.GetFiles(false == string.IsNullOrEmpty(directory) ? directory : @".\", filenamepart);

                    // Warn: a name that matches no file attaches nothing and the message still sends.
                    if (0 == matches.Length)
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Nothing to attach at [{filename}]", attachmentFile, Enums.GPALObjectType.Other);

                    foreach (string filename2 in matches)
                        try 
                        { 
                            GPALMailSettings.Attachments.Add(filename2);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to add file attachments [{filename2}].", attachmentFile, Enums.GPALObjectType.Other, ex);
                        }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to add file attachments [{attachmentFile.Filename}].", attachmentFile, Enums.GPALObjectType.Other, ex);
                }
            return this;
        }

        /// <summary>
        /// Sets the message body, automatically detecting whether it is HTML or plain text via <see cref="IsHtml(string)"/>
        /// and storing it as <see cref="GPALMailSettings.BodyHTML"/> or <see cref="GPALMailSettings.BodyText"/> accordingly.
        /// </summary>
        /// <param name="body">The message body, as plain text or HTML markup.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions WithBody(string body)
        {
            if (true == IsHtml(body))
                GPALMailSettings.BodyHTML = body;
            else
                GPALMailSettings.BodyText = body;
            return this;
        }

        /// <summary>
        /// Sets the subject line of the message.
        /// </summary>
        /// <param name="subject">The subject text.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions WithSubject(string subject)
        {
            GPALMailSettings.Subject = subject;
            return this;
        }

        /// <summary>
        /// Sets the display name on the address added just before this call, whether that was the from,
        /// a to, a cc or a bcc. Warns and does nothing when no address has been added yet.
        /// </summary>
        /// <param name="name">The display name to show alongside that address.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions WithName(string name)
        {
            // Name the address added just before this call (from, to, cc or bcc), as an alternative to "Dead Mike
            // <mike@x.com>".
            if (null == lastAddress)
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"No address to put the name [{name}] on. Name an address after adding it", this, Enums.GPALObjectType.Other);
            else
                lastAddress.Name = name;

            return this;
        }
        /// <summary>
        /// Builds the message from the configured envelope (from/to/cc/bcc, subject, body, attachments)
        /// and sends it via SMTP using <see cref="GPALMailSettings.SMTPServerName"/> and <see cref="GPALMailSettings.SMTPPortNum"/>.
        /// If the initial connection fails (typically due to a certificate issue), retries with certificate
        /// validation disabled and TLS 1.1/1.2 explicitly enabled.
        /// </summary>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        /// <example>
        /// <code>
        /// GPAL.Mail
        ///     .WithSMTPServerName("smtp.example.com")
        ///     .WithSMTPPortNum(465)
        ///     .WithFromEmailAddress("user@example.com")
        ///     .WithToEmailAddress("recipient@example.com")
        ///     .WithSubject("Hello")
        ///     .WithBody("This is the message body.")
        ///     .Send();
        /// </code>
        /// </example>
        public IAllowEmailEnvelopeAndActions Send()
        {
            // Clear Sent and LastError at the start of the send.
            Sent = null;
            LastError = null;

            if (null == GPALMailSettings.FromEmailAddress)
            {
                // Report a missing from address instead of MimeKit's null argument exception for "address".
                Failed(Enums.MailFailure.Configuration, $"No from address to send with", GPALMailSettings.SMTPServerName, GPALMailSettings.SMTPPortNum, null);

                return this;
            }

            var message = new MimeMessage();
            message.From.Add(GPALMailSettings.FromEmailAddress);

            if (null != GPALMailSettings.ToEmailAddresses && 0 < GPALMailSettings.ToEmailAddresses.Count)
                foreach (var to in GPALMailSettings.ToEmailAddresses)
                    message.To.Add(to);

            if (null != GPALMailSettings.CcEmailAddresses && 0 < GPALMailSettings.CcEmailAddresses.Count)
                foreach (var cc in GPALMailSettings.CcEmailAddresses)
                    message.Cc.Add(cc);

            if (null != GPALMailSettings.BccEmailAddresses && 0 < GPALMailSettings.BccEmailAddresses.Count)
                foreach (var bcc in GPALMailSettings.BccEmailAddresses)
                    message.Bcc.Add(bcc);

            message.Subject = GPALMailSettings.Subject;
            var bodyBuilder = new BodyBuilder();
            if (null != GPALMailSettings.BodyHTML)
                bodyBuilder.HtmlBody = GPALMailSettings.BodyHTML;
            else
                bodyBuilder.TextBody = GPALMailSettings.BodyText;
            if (null != GPALMailSettings.Attachments && 0 < GPALMailSettings.Attachments.Count)
            {
                foreach (var attachment in GPALMailSettings.Attachments)
                {
                    bodyBuilder.Attachments.Add(attachment);
                }
            }
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                // Apply the certificate setting before connecting.
                if (true == GPALMailSettings.AcceptAnyCertificate)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.CAUTION,
                        $"Connecting to [{GPALMailSettings.SMTPServerName}] without checking its certificate, because the workflow asked for that",
                        this, Enums.GPALObjectType.None);

                    client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                }

                // Auto lets the port decide, as POP does: STARTTLS on 587, implicit TLS on 465.
                bool loggedIn = Reached(
                    () => client.Connect(GPALMailSettings.SMTPServerName, GPALMailSettings.SMTPPortNum, MailKit.Security.SecureSocketOptions.Auto),
                    () => client.Authenticate(ResolvedUserName, ResolvedPassword),
                    GPALMailSettings.SMTPServerName, GPALMailSettings.SMTPPortNum);

                if (true == loggedIn)
                    try
                    {
                        client.Send(message);
                        client.Disconnect(true);

                        Sent = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        // Connected and logged in, but the message was rejected, e.g. a recipient or a mailbox over
                        // quota.
                        Failed(Enums.MailFailure.Transfer,
                            $"[{GPALMailSettings.SMTPServerName}] accepted the login and then would not take the message",
                            GPALMailSettings.SMTPServerName, GPALMailSettings.SMTPPortNum, ex);
                    }
            }

            return this;
        }

        /// <summary>
        /// Connects to the configured IMAP server, authenticates with <see cref="GPALMailSettings.UserName"/> and
        /// <see cref="GPALMailSettings.Password"/>, opens the inbox read-only, and copies every message into
        /// <see cref="GPALMailSettings.ReceivedEmails"/> as a <see cref="ReceivedEmail"/> (subject, from, and text body).
        /// </summary>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions Receive()
        {
            LastError = null;

            // POP if a POP server is set, otherwise IMAP.
            if (false == string.IsNullOrWhiteSpace(GPALMailSettings.POPServerName))
                ReceiveOverPOP();
            else if (false == string.IsNullOrWhiteSpace(GPALMailSettings.IMAPServerName))
                ReceiveOverIMAP();
            else
                Failed(Enums.MailFailure.Configuration, $"No pop or imap server to read mail from", null, 0, null);

            return this;
        }

        /// <summary>
        /// Reads the mail as <see cref="Receive"/> does and hands the messages over in one call.
        /// </summary>
        /// <param name="receivedEmails">Receives the messages, empty when nothing was fetched</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions Receive(out List<ReceivedEmail> receivedEmails)
        {
            Receive();

            receivedEmails = new List<ReceivedEmail>(GPALMailSettings.ReceivedEmails);

            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Handing back [{receivedEmails.Count}] received emails.", this, Enums.GPALObjectType.Other);

            return this;
        }

        /// <summary>
        /// Limits <see cref="Receive()"/> to messages unread on the server. Off by default.
        /// </summary>
        /// <param name="unreadOnly">True to fetch only unread messages.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions WithUnreadOnly(bool unreadOnly)
        {
            GPALMailSettings.UnreadOnly = unreadOnly;
            return this;
        }

        /// <summary>
        /// Caps how many messages one <see cref="Receive()"/> fetches, taking the newest first. Every message
        /// by default, which on a mailbox of any size is a great deal of downloading.
        /// </summary>
        /// <param name="maxMessages">The most messages to fetch.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions WithMaxMessages(int maxMessages)
        {
            GPALMailSettings.MaxMessages = maxMessages;
            return this;
        }

        private void ReceiveOverPOP()
        {
            using (var client = new Pop3Client())
                if (true == Reached(
                        () => client.Connect(GPALMailSettings.POPServerName, GPALMailSettings.POPPortNum, MailKit.Security.SecureSocketOptions.Auto),
                        () => client.Authenticate(ResolvedUserName, ResolvedPassword),
                        GPALMailSettings.POPServerName, GPALMailSettings.POPPortNum))
                {
                    // POP has no read/unread state; unread-only is ignored.
                    if (true == GPALMailSettings.UnreadOnly)
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"POP cannot tell read from unread, so every message is being fetched from [{GPALMailSettings.POPServerName}]", this, Enums.GPALObjectType.Other);

                    try
                    {
                        int first = Math.Max(0, client.Count - GPALMailSettings.MaxMessages);

                        for (int i = first; i < client.Count; i++)
                            Collect(client.GetMessage(i), null);

                        client.Disconnect(true);
                    }
                    catch (Exception ex)
                    {
                        // Logged in but the mailbox could not be read. Keep the messages collected so far.
                        Failed(Enums.MailFailure.Transfer,
                            $"[{GPALMailSettings.POPServerName}] stopped while the mailbox was being read",
                            GPALMailSettings.POPServerName, GPALMailSettings.POPPortNum, ex);
                    }
                }
        }

        private void ReceiveOverIMAP()
        {
            using (var client = new ImapClient())
                if (true == Reached(
                        () => client.Connect(GPALMailSettings.IMAPServerName, GPALMailSettings.IMAPPortNum, true),
                        () => client.Authenticate(ResolvedUserName, ResolvedPassword),
                        GPALMailSettings.IMAPServerName, GPALMailSettings.IMAPPortNum))
                    try
                    {
                        var inbox = client.Inbox;
                        inbox.Open(MailKit.FolderAccess.ReadOnly);

                        // Ask the server which messages match instead of downloading the mailbox.
                        IList<UniqueId> ids = inbox.Search(true == GPALMailSettings.UnreadOnly
                            ? SearchQuery.NotSeen
                            : SearchQuery.All);

                        int first = Math.Max(0, ids.Count - GPALMailSettings.MaxMessages);

                        for (int i = first; i < ids.Count; i++)
                            Collect(inbox.GetMessage(ids[i]), ids[i]);

                        client.Disconnect(true);
                    }
                    catch (Exception ex)
                    {
                        // Logged in but the inbox could not be read. Keep the messages collected so far.
                        Failed(Enums.MailFailure.Transfer,
                            $"[{GPALMailSettings.IMAPServerName}] stopped while the inbox was being read",
                            GPALMailSettings.IMAPServerName, GPALMailSettings.IMAPPortNum, ex);
                    }
        }

        /// <summary>
        /// Marks every message <see cref="Receive()"/> collected as read on the server, in one connection. Call it
        /// after handling them. IMAP only.
        /// </summary>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions MarkAllAsRead()
        {
            ApplyFlag(GPALMailSettings.ReceivedEmails, MessageFlags.Seen, false);
            return this;
        }

        /// <summary>
        /// Deletes every message <see cref="Receive()"/> collected, flagging and expunging in one connection. The rest
        /// of the mailbox is untouched. IMAP only.
        /// </summary>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions DeleteAll()
        {
            ApplyFlag(GPALMailSettings.ReceivedEmails, MessageFlags.Deleted, true);
            return this;
        }

        /// <summary>
        /// Flags messages on the IMAP server in one connection, and expunges if asked. Messages not from an IMAP
        /// Receive have no server id and are skipped.
        /// </summary>
        /// <param name="messages">The messages to flag.</param>
        /// <param name="flag">The flag to add.</param>
        /// <param name="expunge">True to expunge the flagged messages before disconnecting.</param>
        internal void ApplyFlag(IEnumerable<ReceivedEmail> messages, MessageFlags flag, bool expunge)
        {
            List<UniqueId> uids = messages.Where(m => null != m?.Uid).Select(m => m.Uid.Value).ToList();

            if (0 == uids.Count)
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Nothing to flag: no message came from an IMAP Receive", this, Enums.GPALObjectType.Other);
            else
                using (var client = new ImapClient())
                {
                    client.Connect(GPALMailSettings.IMAPServerName, GPALMailSettings.IMAPPortNum, true);
                    client.Authenticate(ResolvedUserName, ResolvedPassword);

                    var inbox = client.Inbox;
                    inbox.Open(MailKit.FolderAccess.ReadWrite);
                    inbox.AddFlags(uids, flag, true);

                    if (true == expunge)
                        inbox.Expunge(uids);

                    client.Disconnect(true);

                    GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Flagged [{uids.Count}] message(s) as [{flag}].", this, Enums.GPALObjectType.Other);
                }
        }

        private void Collect(MimeMessage message, UniqueId? uid)
        {
            ReceivedEmail received = new ReceivedEmail
            {
                Subject = message.Subject,
                From = message.From.ToString(),
                // Use the HTML body when there is no text body; transactional mail often has none.
                Body = message.TextBody ?? message.HtmlBody,
                HtmlBody = message.HtmlBody,
                Received = message.Date.LocalDateTime,
                MessageId = message.MessageId,
                Uid = uid,
                Mailbox = this
            };

            // Keep each attachment's name and part so it can be saved on request. Reading mail writes nothing to disk.
            foreach (MimeEntity attachment in message.Attachments)
            {
                received.Attachments.Add(attachment.ContentDisposition?.FileName ?? attachment.ContentType?.Name);
                received.AttachmentParts.Add(attachment);
            }

            GPALMailSettings.ReceivedEmails.Add(received);
        }
        /// <summary>
        /// Writes every attachment on every message <see cref="Receive"/> collected into the given directory,
        /// creating it when it is not there. Use <see cref="ReceivedEmail.SaveTo(GPALFile)"/> for one message's.
        /// </summary>
        /// <param name="directory">The directory to write the attachments into</param>
        /// <returns>Fluent interface to continue building the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions SaveTo(GPALFile directory)
        {
            int written = 0;

            foreach (ReceivedEmail received in GPALMailSettings.ReceivedEmails)
                written += received.SaveTo(directory).Count;

            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Wrote [{written}] attachments to [{directory.Filename}].", this, Enums.GPALObjectType.Other);

            return this;
        }

        /// <summary>
        /// Resets the email envelope to a blank state: clears all recipient lists, subject, body text,
        /// attachments, and any received emails. The from address and the server connection settings
        /// (host/port/credentials) are left unchanged.
        /// </summary>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions Clear()
        {
            // Keep the from address; it is the sender and is saved with the server settings.
            GPALMailSettings.ToEmailAddresses.Clear();
            GPALMailSettings.CcEmailAddresses.Clear();
            GPALMailSettings.BccEmailAddresses.Clear();
            GPALMailSettings.Subject = null;
            GPALMailSettings.BodyText = null;
            GPALMailSettings.BodyHTML = null;   // Send prefers this one, so leaving it made every later message re-send the last html body
            GPALMailSettings.Attachments.Clear();
            GPALMailSettings.ReceivedEmails.Clear();
            return this;
        }
        /// <summary>
        /// Loads this instance's <see cref="GPALMailSettings"/> from the given <see cref="GPALFile"/> via
        /// <see cref="GPAL.Converter"/>, replacing the current server connection and envelope settings.
        /// </summary>
        /// <param name="mailSettingsFile">The file to load mail settings from.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions LoadSettings(GPALFile mailSettingsFile = null)
        {
            if (mailSettingsFile != null) 
                MailSettingsFilePath = mailSettingsFile.Filename;

            if (true == System.IO.File.Exists(MailSettingsFilePath))
                GPAL.Converter.WithInput((GPALFile)MailSettingsFilePath).SaveTo(ref gPALMailSettings);
            else
                // Nothing to load, so write a settings file with the fields to fill in.
                SaveSettings();

            return this;
        }
        /// <summary>
        /// Saves this instance's <see cref="GPALMailSettings"/> (server connection and envelope settings)
        /// to the given <see cref="GPALFile"/> via <see cref="GPAL.Converter"/>.
        /// </summary>
        /// <param name="mailSettingsFile">The file to save mail settings to.</param>
        /// <returns>Fluent interface to continue configuring the email envelope and actions.</returns>
        public IAllowEmailEnvelopeAndActions SaveSettings(GPALFile mailSettingsFile = null)
        {
            if (mailSettingsFile != null) MailSettingsFilePath = mailSettingsFile.Filenames[0];
            GPAL.Converter.WithInput(gPALMailSettings).SaveTo((GPALFile)MailSettingsFilePath);
            return this;
        }
        #region HELPERS
        /// <summary>
        /// Splits a single string containing one or more email addresses delimited by commas, semicolons,
        /// or pipes, and parses each into a <see cref="MailboxAddress"/>.
        /// </summary>
        /// <param name="input">A delimited string of one or more email addresses.</param>
        /// <returns>The parsed list of <see cref="MailboxAddress"/> values.</returns>
        public static List<MailboxAddress> ParseEmailAddresses(string input)
        {

            // Split the input string based on delimiters
            string[] emailParts = input.Split(new char[] { ',', ';', '|' });
            return CreateEmailAddresses(emailParts);
        }
        /// <summary>
        /// Parses each non-empty, trimmed entry in <paramref name="emailParts"/> into a <see cref="MailboxAddress"/>.
        /// </summary>
        /// <param name="emailParts">An array of email address strings.</param>
        /// <returns>The parsed list of <see cref="MailboxAddress"/> values.</returns>
        internal static List<MailboxAddress> CreateEmailAddresses(string[] emailParts)
        {
            List<MailboxAddress> addresses = new List<MailboxAddress>();
            foreach (string emailPart in emailParts)
            {
                string email = emailPart.Trim();
                if (!string.IsNullOrEmpty(email))
                {
                    // Create a MailboxAddress object
                    MailboxAddress address = MailboxAddress.Parse(email);
                    addresses.Add(address);
                }
            }
            return addresses;
        }
        /// <summary>
        /// Heuristically determines whether <paramref name="text"/> is HTML markup rather than plain text.
        /// A leading "&lt;!DOCTYPE html&gt;" is treated as HTML, a leading XML declaration is treated as not HTML,
        /// and otherwise the text is scanned for a matching pair of HTML tags.
        /// </summary>
        /// <param name="text">The text to inspect.</param>
        /// <returns>True if <paramref name="text"/> appears to be HTML; otherwise false.</returns>
        static bool IsHtml(string text)
        {
            // 
            // Check if the text starts with an XML declaration
            if (text.TrimStart().StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase))
                return true;
            else if (text.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                return false;

            // Define a regular expression pattern to match HTML tags
            string pattern = @"<\s*([^ >]+)[^>]*>.*?<\s*/\s*\1\s*>";

            // Use Regex to search for HTML tags in the text
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
        }

        #endregion HELPERS
    }
    /// <summary>
    /// A simplified representation of a single email message retrieved by <see cref="GPALMail.Receive"/>.
    /// </summary>
    public class ReceivedEmail
    {
        /// <summary>The message's subject line.</summary>
        public string Subject { get; set; }
        /// <summary>The message's "From" address, as a display string.</summary>
        public string From { get; set; }
        /// <summary>The plain-text body of the message, falling back to the html body when the sender wrote no text part.</summary>
        public string Body { get; set; }
        /// <summary>The html body of the message, null when the sender wrote none.</summary>
        public string HtmlBody { get; set; }
        /// <summary>When the message was sent, in local time.</summary>
        public DateTime Received { get; set; }
        /// <summary>The message id the sender stamped on it, which is how the same message is recognised across runs.</summary>
        public string MessageId { get; set; }

        // the imap identity of the message, so MarkAsRead knows which one to flag. null over pop
        internal UniqueId? Uid { get; set; }

        // The GPALMail that read this message; it holds the server and login.
        internal GPALMail Mailbox { get; set; }

        /// <summary>
        /// Marks this message as read on the server. Call it after handling the message, so a failed run has not marked
        /// mail it never handled. IMAP only.
        /// </summary>
        /// <returns>This message, so calls can be chained.</returns>
        public ReceivedEmail MarkAsRead()
        {
            Mailbox.ApplyFlag(new[] { this }, MessageFlags.Seen, false);
            return this;
        }

        /// <summary>
        /// Deletes this message from the server, flagging and expunging it in one call. IMAP only.
        /// </summary>
        /// <returns>This message, so calls can be chained.</returns>
        public ReceivedEmail Delete()
        {
            Mailbox.ApplyFlag(new[] { this }, MessageFlags.Deleted, true);
            return this;
        }
        /// <summary>The filename of each attachment on the message, so a workflow can tell what came with it. Empty when nothing was attached.</summary>
        public List<string> Attachments { get; set; } = new List<string>();

        // The attachments, in memory until saved.
        internal List<MimeEntity> AttachmentParts { get; set; } = new List<MimeEntity>();

        /// <summary>
        /// Writes this message's attachments into the given directory, creating it when it is not there,
        /// and hands back a <see cref="GPALFile"/> for each one written. A file already there is replaced.
        /// </summary>
        /// <param name="directory">The directory to write this message's attachments into</param>
        /// <returns>The files written, empty when the message had no attachments.</returns>
        public List<GPALFile> SaveTo(GPALFile directory)
        {
            List<GPALFile> retVal = new List<GPALFile>();

            if (0 < AttachmentParts.Count)
                Directory.CreateDirectory(directory.Filename);

            foreach (MimeEntity attachment in AttachmentParts)
            {
                // Use only the filename part of the attachment name, so ..\..\startup.cmd cannot escape the directory.
                string filename = Path.GetFileName(attachment.ContentDisposition?.FileName ?? attachment.ContentType?.Name);
                string path = Path.Combine(directory.Filename, filename);

                FileHelper.EnsureFolderFor(path);
                using (FileStream stream = File.Create(path))
                    if (attachment is MimePart part)
                        part.Content.DecodeTo(stream);
                    else
                        attachment.WriteTo(stream);

                retVal.Add(path);
            }

            return retVal;
        }
    }
}

