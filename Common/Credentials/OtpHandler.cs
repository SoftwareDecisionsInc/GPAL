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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenerallyPositive;
using GenerallyPositive.Application;
using GenerallyPositive.Browser;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Makes RFC 6238 time-based one-time codes (TOTP), the 6 digits an authenticator app shows, from the secret a
    /// site hands out when two-factor sign-in is turned on.
    /// </summary>
    internal static class OtpHandler
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Makes the code for the given moment. The secret is either the bare base32 key or the whole otpauth://totp/
        /// URI, whose digits, period and algorithm then replace the defaults of 6, 30 seconds and SHA1.
        /// </summary>
        /// <param name="otpSecret">Base32 key, or an otpauth://totp/ URI holding it</param>
        /// <param name="utcNow">The moment to make the code for</param>
        /// <param name="validUntil">Receives the UTC time the code stops being current</param>
        /// <returns>The code, or an empty string if the secret could not be read</returns>
        internal static string GenerateCode(string otpSecret, DateTime utcNow, out DateTime validUntil)
        {
            string code = "";
            validUntil = utcNow;
            string secret = otpSecret?.Trim() ?? "";
            int digits = 6;
            int period = 30;
            string algorithm = "SHA1";
            string uriType = "totp";

            if (secret.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase))
            {
                Uri.TryCreate(secret, UriKind.Absolute, out Uri uri);
                uriType = uri?.Host ?? "";
                secret = "";
                foreach (string pair in (uri?.Query ?? "").TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = pair.Split(new[] { '=' }, 2);
                    string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                    switch (parts[0].ToLowerInvariant())
                    {
                        case "secret": secret = value; break;
                        case "digits": int.TryParse(value, out digits); break;
                        case "period": int.TryParse(value, out period); break;
                        case "algorithm": algorithm = value.ToUpperInvariant(); break;
                    }
                }
            }

            byte[] key = Base32Decode(secret);
            if (false == string.Equals(uriType, "totp", StringComparison.OrdinalIgnoreCase))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Only otpauth://totp/ URIs make time-based codes; this one is [{uriType}].", null, GPALObjectType.Other);
            }
            else if (null == key || 0 == key.Length)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "The one-time password secret is empty or is not base32.", null, GPALObjectType.Other);
            }
            else if (digits < 6 || digits > 8 || period < 1)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The one-time password URI asks for [{digits}] digits every [{period}] seconds; 6 to 8 digits and a period of 1 second or more are supported.", null, GPALObjectType.Other);
            }
            else
            {
                using (HMAC hmac = "SHA256" == algorithm ? (HMAC)new HMACSHA256(key) : "SHA512" == algorithm ? (HMAC)new HMACSHA512(key) : new HMACSHA1(key))
                {
                    long step = new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() / period;
                    byte[] counter = BitConverter.GetBytes(step);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(counter);
                    byte[] hash = hmac.ComputeHash(counter);

                    // RFC 4226 dynamic truncation: the low 4 bits of the last byte pick where 4 bytes are read from
                    int offset = hash[hash.Length - 1] & 0x0F;
                    int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
                    code = (binary % (int)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');
                    validUntil = DateTimeOffset.FromUnixTimeSeconds((step + 1) * period).UtcDateTime;
                }
            }
            return code;
        }

        /// <summary>
        /// Lists the accounts a secret holds. A bare base32 key or an otpauth://totp/ URI is one account; a Google
        /// Authenticator export (otpauth-migration://offline?data=...) packs every exported account into one QR code
        /// and becomes one otpauth://totp/ URI per account. Counter-based (HOTP) accounts in an export are skipped.
        /// </summary>
        /// <param name="otpSecret">Base32 key, otpauth://totp/ URI, or otpauth-migration:// URI</param>
        /// <returns>The accounts, empty when the secret is empty or the export cannot be read</returns>
        internal static List<OtpAccount> ReadAccounts(string otpSecret)
        {
            List<OtpAccount> accounts = new List<OtpAccount>();
            string secret = otpSecret?.Trim() ?? "";

            if (secret.StartsWith("otpauth-migration:", StringComparison.OrdinalIgnoreCase))
            {
                string data = "";
                int at = secret.IndexOf("data=", StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                    data = Uri.UnescapeDataString(secret.Substring(at + 5).Split('&')[0]).Replace(' ', '+');
                try
                {
                    byte[] payload = Convert.FromBase64String(data);
                    int skipped = 0;
                    // MigrationPayload field 1 is each exported account (OtpParameters)
                    foreach (byte[] parameters in ProtobufFields(payload, 1))
                    {
                        byte[] key = ProtobufFields(parameters, 1).FirstOrDefault() ?? new byte[0];
                        string name = Encoding.UTF8.GetString(ProtobufFields(parameters, 2).FirstOrDefault() ?? new byte[0]);
                        string issuer = Encoding.UTF8.GetString(ProtobufFields(parameters, 3).FirstOrDefault() ?? new byte[0]);
                        long algorithm = ProtobufVarint(parameters, 4);
                        long digits = ProtobufVarint(parameters, 5);
                        long type = ProtobufVarint(parameters, 6);
                        if (1 == type)
                        {
                            skipped++;
                        }
                        else
                        {
                            string label = string.IsNullOrEmpty(issuer) || name.StartsWith(issuer + ":") ? name : $"{issuer}:{name}";
                            accounts.Add(new OtpAccount
                            {
                                Issuer = issuer,
                                Name = name,
                                Uri = $"otpauth://totp/{Uri.EscapeDataString(label)}?secret={Base32Encode(key)}&issuer={Uri.EscapeDataString(issuer)}" +
                                      $"&algorithm={(2 == algorithm ? "SHA256" : 3 == algorithm ? "SHA512" : "SHA1")}&digits={(2 == digits ? 8 : 6)}&period=30"
                            });
                        }
                    }
                    if (skipped > 0)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The export holds [{skipped}] counter-based accounts, which make no time-based codes and were skipped.", null, GPALObjectType.Other);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "The otpauth-migration export could not be read", null, GPALObjectType.Other, ex);
                }
            }
            else if (secret.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                Uri.TryCreate(secret, UriKind.Absolute, out Uri uri);
                string label = Uri.UnescapeDataString(uri?.AbsolutePath.TrimStart('/') ?? "");
                string issuer = "";
                foreach (string pair in (uri?.Query ?? "").TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (pair.StartsWith("issuer=", StringComparison.OrdinalIgnoreCase))
                        issuer = Uri.UnescapeDataString(pair.Substring(7));
                }
                int colon = label.IndexOf(':');
                if (string.IsNullOrEmpty(issuer) && colon > 0)
                    issuer = label.Substring(0, colon);
                accounts.Add(new OtpAccount { Issuer = issuer, Name = colon > 0 ? label.Substring(colon + 1) : label, Uri = secret });
            }
            else if (secret.Length > 0)
            {
                accounts.Add(new OtpAccount { Issuer = "", Name = "", Uri = secret });
            }
            return accounts;
        }

        /// <summary>
        /// Picks the account to make a code for: the only one there is, or, when an export holds several, the one
        /// whose issuer or account name contains the GetCredentialsFor target.
        /// </summary>
        /// <param name="accounts">The accounts ReadAccounts listed</param>
        /// <param name="target">Issuer or account name to look for, or null</param>
        /// <returns>The account, or null when none or more than one fits</returns>
        internal static OtpAccount ChooseAccount(List<OtpAccount> accounts, string target)
        {
            OtpAccount account = null;
            List<OtpAccount> matches = string.IsNullOrEmpty(target)
                ? accounts
                : accounts.Where(a => a.Issuer.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 || a.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (0 == accounts.Count)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No one-time password secret was set; give it with WithOtpSecret or WithOtpQRCode.", null, GPALObjectType.Other);
            else if (1 == accounts.Count)
                account = accounts[0];
            else if (1 == matches.Count)
                account = matches[0];
            else if (0 == matches.Count)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"None of the [{accounts.Count}] accounts in the export matches [{target}]; they are [{string.Join(", ", accounts.Select(a => a.Issuer))}].", null, GPALObjectType.Other);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{matches.Count}] of the accounts in the export match [{target}]; name one with GetCredentialsFor. They are [{string.Join(", ", matches.Select(a => $"{a.Issuer}:{a.Name}"))}].", null, GPALObjectType.Other);
            return account;
        }

        /// <summary>
        /// The length-delimited values of one field number in a protobuf message, which is how the export stores its
        /// accounts and their secrets, names and issuers.
        /// </summary>
        private static List<byte[]> ProtobufFields(byte[] message, int fieldNumber)
        {
            List<byte[]> values = new List<byte[]>();
            int position = 0;
            while (position < message.Length)
            {
                long tag = ReadVarint(message, ref position);
                int wireType = (int)(tag & 7);
                long length = 0 == wireType ? ReadVarint(message, ref position) : 2 == wireType ? ReadVarint(message, ref position) : 1 == wireType ? 8 : 4;
                if (2 == wireType && fieldNumber == (tag >> 3))
                {
                    byte[] value = new byte[length];
                    Array.Copy(message, position, value, 0, length);
                    values.Add(value);
                }
                position += 0 == wireType ? 0 : (int)length;
            }
            return values;
        }

        /// <summary>
        /// The value of one varint field in a protobuf message, or 0 when the field is absent.
        /// </summary>
        private static long ProtobufVarint(byte[] message, int fieldNumber)
        {
            long value = 0;
            int position = 0;
            while (position < message.Length)
            {
                long tag = ReadVarint(message, ref position);
                int wireType = (int)(tag & 7);
                if (0 == wireType)
                {
                    long read = ReadVarint(message, ref position);
                    if (fieldNumber == (tag >> 3))
                        value = read;
                }
                else
                {
                    // the length is read first, as it moves position past itself
                    int length = 2 == wireType ? (int)ReadVarint(message, ref position) : 1 == wireType ? 8 : 4;
                    position += length;
                }
            }
            return value;
        }

        /// <summary>
        /// Reads a protobuf base-128 varint and moves past it.
        /// </summary>
        private static long ReadVarint(byte[] message, ref int position)
        {
            long value = 0;
            int shift = 0;
            byte b = 0x80;
            while (0 != (b & 0x80) && position < message.Length)
            {
                b = message[position++];
                value |= (long)(b & 0x7F) << shift;
                shift += 7;
            }
            return value;
        }

        /// <summary>
        /// Encodes bytes as RFC 4648 base32 without padding, the form otpauth:// URIs carry the secret in.
        /// </summary>
        internal static string Base32Encode(byte[] data)
        {
            StringBuilder text = new StringBuilder();
            int buffer = 0;
            int bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    text.Append(Base32Alphabet[(buffer >> bits) & 31]);
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0)
                text.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
            return text.ToString();
        }

        /// <summary>
        /// Decodes an RFC 4648 base32 string. Case, spaces, hyphens and '=' padding are ignored, as sites show the key
        /// in groups like "jbsw y3dp ehpk 3pxp".
        /// </summary>
        /// <param name="value">The base32 text</param>
        /// <returns>The decoded bytes, or null if the text holds a character outside the base32 alphabet</returns>
        internal static byte[] Base32Decode(string value)
        {
            List<byte> bytes = new List<byte>();
            int buffer = 0;
            int bits = 0;
            bool valid = true;
            foreach (char c in (value ?? "").ToUpperInvariant())
            {
                int index = Base32Alphabet.IndexOf(c);
                if (index >= 0)
                {
                    buffer = (buffer << 5) | index;
                    bits += 5;
                    if (bits >= 8)
                    {
                        bits -= 8;
                        bytes.Add((byte)(buffer >> bits));
                        buffer &= (1 << bits) - 1;
                    }
                }
                else if (' ' != c && '-' != c && '=' != c)
                {
                    valid = false;
                }
            }
            return valid ? bytes.ToArray() : null;
        }
    }
}
