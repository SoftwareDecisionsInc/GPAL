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
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Sends a message to a chat channel through its webhook: Slack, Teams, Discord, Google Chat, Telegram, ntfy.sh, or
    /// any service that takes a POST of {"text": ...}. The webhook's address says which service it is, and the message
    /// is shaped for it.<br/>
    /// The address is the credential: anyone holding it can post to the channel. It is never written to a log; events
    /// name the service instead.
    /// </summary>
    /// <example>
    /// <code>
    ///     ICredentials slack = GPAL.CredentialsFor(CredentialServiceType.StaticKey)
    ///         .WithKeyFromEnv("SLACK_ALERTS_WEBHOOK")
    ///         .ToGPALObject();
    ///
    ///     GPAL.Notify
    ///         .WithWebhook(slack)
    ///         .Send("Nightly export finished: [412] rows");
    /// </code>
    /// </example>
    public class GPALNotify : IGPALNotify
    {
        /// <summary>
        /// Every webhook a Send posts to, in the order they were added.
        /// </summary>
        private readonly List<string> webhooks = new List<string>();

        /// <summary>
        /// Returns this <see cref="GPALNotify"/>, so it can be held and sent to again.
        /// </summary>
        /// <returns>This notifier</returns>
        public IGPALNotify ToGPALObject()
        {
            return this;
        }
        /// <summary>
        /// Adds a webhook held in GPAL.Credentials, as the key of a StaticKey credential: WithServiceKey, WithKeyFromEnv
        /// or WithKeyFromApi. Add several and Send posts to each.
        /// </summary>
        /// <param name="webhook">The credential holding the webhook's address</param>
        /// <returns>Fluent interface to add another webhook or send</returns>
        public IAllowNotifyActions WithWebhook(ICredentials webhook)
        {
            Add((webhook as Credentials)?.ServiceKey, "The credential given to WithWebhook");

            return this;
        }
        /// <summary>
        /// Adds a webhook by its address. Add several and Send posts to each.<br/>
        /// <b>NOTE:</b> the address is the credential. Keep it out of a workflow's source with the ICredentials overload
        /// and WithKeyFromEnv.
        /// </summary>
        /// <param name="webhookUrl">The webhook's address</param>
        /// <returns>Fluent interface to add another webhook or send</returns>
        public IAllowNotifyActions WithWebhook([URL] string webhookUrl)
        {
            Add(webhookUrl, "The address given to WithWebhook");

            return this;
        }
        /// <summary>
        /// Keeps a webhook to send to when it is an http or https address. One that is not is published without
        /// repeating it, since a webhook's address is a secret.
        /// </summary>
        /// <param name="address">The webhook's address</param>
        /// <param name="source">What gave it, for the message</param>
        private void Add(string address, string source)
        {
            string trimmed = address?.Trim() ?? string.Empty;
            bool usable = true == Uri.TryCreate(trimmed, UriKind.Absolute, out Uri parsed)
                       && (Uri.UriSchemeHttp == parsed.Scheme || Uri.UriSchemeHttps == parsed.Scheme);

            if (true == usable)
                webhooks.Add(trimmed);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"{source} is not an http or https address, so nothing will be sent to it.", this, GPALObjectType.Other);
        }
        /// <summary>
        /// Posts the message to every webhook added, shaped for the service each one belongs to. A service that refuses
        /// it is published with what the service answered, and the rest are still sent to.
        /// </summary>
        /// <param name="message">The text to post</param>
        /// <returns>Fluent interface to send again or add another webhook</returns>
        public IAllowNotifyActions Send(string message)
        {
            if (0 == webhooks.Count)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Send has no webhook to post to. Add one with WithWebhook first.", this, GPALObjectType.Other);

            foreach (string webhook in webhooks)
                Post(webhook, message ?? string.Empty);

            return this;
        }
        /// <summary>
        /// Posts one message to one webhook.
        /// </summary>
        /// <param name="webhook">The webhook's address</param>
        /// <param name="message">The text to post</param>
        private void Post(string webhook, string message)
        {
            string service = "a webhook";

            try
            {
                Uri address = new Uri(webhook);
                service = ServiceFor(address);

                // a bound on one webhook that does not answer, so the rest are not held up for long
                using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                using (HttpRequestMessage request = RequestFor(address, service, message))
                using (HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    if (true == response.IsSuccessStatusCode)
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Message sent to [{service}]", this, GPALObjectType.Other);
                    else
                    {
                        // What the service said, which names the problem (Slack's invalid_payload, Telegram's chat not found).
                        // A server can echo the address it was sent to, whole or in part, plain or escaped, so every
                        // segment and query value long enough to be a token is taken out in each form
                        string answer = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        IEnumerable<string> secrets = address.AbsolutePath.Split('/')
                            .Concat(address.Query.TrimStart('?').Split('&').Select(part => part.Substring(part.IndexOf('=') + 1)))
                            .Select(part => Uri.UnescapeDataString(part))
                            .Where(part => 8 <= part.Length)
                            .SelectMany(part => new[] { part, Uri.EscapeDataString(part), part.Replace("/", "\\/") })
                            .Distinct()
                            .OrderByDescending(part => part.Length);

                        foreach (string secret in secrets)
                            answer = answer.Replace(secret, "[webhook]");

                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{service}] refused the message: [{(int)response.StatusCode} {response.ReasonPhrase}] [{(answer.Length > 300 ? answer.Substring(0, 300) : answer)}]", this, GPALObjectType.Other);
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to send the message to [{service}].", this, GPALObjectType.Other, ex);
            }
        }
        /// <summary>
        /// Which service a webhook belongs to, from its address.
        /// </summary>
        /// <param name="address">The webhook's address</param>
        /// <returns>Slack, Teams, Teams connector, Discord, Google Chat, Telegram, ntfy.sh, or the host for any other
        /// webhook</returns>
        internal static string ServiceFor(Uri address)
        {
            string host = address.Host.ToLowerInvariant();

            // Teams has two kinds: a Workflows webhook, on Power Automate's hosts, and the older connector webhook; the
            // .us hosts are the government clouds. Discord also answers on its ptb and canary hosts, with or without an
            // api version in the path
            return "hooks.slack.com" == host ? "Slack"
                 : true == new[] { ".logic.azure.com", ".powerplatform.com", ".powerautomate.com", ".logic.azure.us", ".powerplatform.us", ".powerautomate.us" }.Any(suffix => true == host.EndsWith(suffix)) ? "Teams"
                 : true == host.EndsWith(".webhook.office.com") || true == host.EndsWith(".webhook.office365.us") || "outlook.office.com" == host ? "Teams connector"
                 : ("discord.com" == host || "discordapp.com" == host || true == host.EndsWith(".discord.com")) && true == System.Text.RegularExpressions.Regex.IsMatch(address.AbsolutePath, @"^/api(/v\d+)?/webhooks/") ? "Discord"
                 : "chat.googleapis.com" == host ? "Google Chat"
                 : "api.telegram.org" == host ? "Telegram"
                 : "ntfy.sh" == host ? "ntfy.sh"
                 : host;
        }
        /// <summary>
        /// The request a service expects for a message.
        /// </summary>
        /// <param name="address">The webhook's address</param>
        /// <param name="service">The service, from <see cref="ServiceFor"/></param>
        /// <param name="message">The text to post</param>
        /// <returns>The POST to send</returns>
        internal static HttpRequestMessage RequestFor(Uri address, string service, string message)
        {
            Uri target = address;
            HttpContent content;

            switch (service)
            {
                case "Teams":
                    // a Workflows webhook takes an Adaptive Card, wrapped as a message attachment
                    JObject card = new JObject(
                        new JProperty("$schema", "http://adaptivecards.io/schemas/adaptive-card.json"),
                        new JProperty("type", "AdaptiveCard"),
                        new JProperty("version", "1.4"),
                        new JProperty("body", new JArray(new JObject(
                            new JProperty("type", "TextBlock"),
                            new JProperty("text", message),
                            new JProperty("wrap", true)))));

                    content = Json(new JObject(
                        new JProperty("type", "message"),
                        new JProperty("attachments", new JArray(new JObject(
                            new JProperty("contentType", "application/vnd.microsoft.card.adaptive"),
                            new JProperty("content", card))))));
                    break;
                case "Slack":
                    // Slack reads < > and & as its own markup, so they are escaped the way Slack documents
                    content = Json(new JObject(new JProperty("text", message.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"))));
                    break;
                case "Discord":
                    // nothing in the message pings anyone, an @everyone included
                    content = Json(new JObject(
                        new JProperty("content", message),
                        new JProperty("allowed_mentions", new JObject(new JProperty("parse", new JArray())))));
                    break;
                case "Telegram":
                    // The bot's address carries the chat as ?chat_id=, and may carry more such as parse_mode. They go in the
                    // body with the text, and what is left is the bot's sendMessage address. A flag with no value, such as
                    // disable_notification, is sent as true
                    JObject telegram = new JObject();

                    foreach (string part in address.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int equals = part.IndexOf('=');
                        string name = Uri.UnescapeDataString(0 > equals ? part : part.Substring(0, equals));

                        telegram[name] = 0 > equals ? "true" : Uri.UnescapeDataString(part.Substring(equals + 1));
                    }

                    telegram["text"] = message;
                    target = new Uri(address.GetLeftPart(UriPartial.Path));
                    content = Json(telegram);
                    break;
                case "ntfy.sh":
                    content = new StringContent(message, Encoding.UTF8, "text/plain");
                    break;
                default:
                    // the Teams connector, Google Chat, Mattermost, Rocket.Chat and most others take {"text": ...}
                    content = Json(new JObject(new JProperty("text", message)));
                    break;
            }

            return new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
        }
        /// <summary>
        /// A JSON body.
        /// </summary>
        /// <param name="body">The JSON</param>
        /// <returns>The body, as application/json</returns>
        private static HttpContent Json(JObject body)
        {
            return new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        }
    }
}
