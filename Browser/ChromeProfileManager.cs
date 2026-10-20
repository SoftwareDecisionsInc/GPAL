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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

//using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Enums;
using static Org.BouncyCastle.Asn1.Cmp.Challenge;

namespace GenerallyPositive.Browser
{
    internal class ChromeProfileManager
    {
        private static readonly Random random = new Random();

        // Prefix on every temp profile directory, Chromium and Firefox. The sweep deletes only these.
        private const string TempProfilePrefix = "BrowserProfile_";

        // Suffix added by the rename just before deletion. If a sweep dies mid-delete, the next sweep deletes the
        // renamed directory without waiting for the minimum age.
        private const string SweepingSuffix = ".sweeping";

        // Minimum age before a temp profile counts as abandoned. Another GPAL process may have created it and not
        // launched yet, so it has no open files but is in use.
        private const int AbandonedAfterMinutes = 1;

        // Minimum seconds between sweeps. One is requested on every launch and close, far more often than needed.
        private const int SweepEverySeconds = 30;

        private static DateTime lastSweep = DateTime.MinValue;
        private static int sweeping = 0;

        /// <summary>
        /// The path for a new temp profile directory, under <see cref="GPAL.TempProfileDirectory"/> and named so
        /// the sweep will recognize it. The directory is not created here.
        /// </summary>
        /// <returns>The full path for a temp profile directory nothing else is using</returns>
        internal static string NewTempProfilePath()
        {
            return Path.Combine(GPAL.TempProfileDirectory, $"{TempProfilePrefix}{Guid.NewGuid():N}");
        }

        /// <summary>
        /// Deletes a temp profile directory, renaming it first.<br/>
        /// Windows will not rename a directory with a file open inside, so a successful rename means its browser has
        /// exited. A failed rename changes nothing, where a failed delete has already removed part of the profile.
        /// </summary>
        /// <param name="directory">The temp profile directory to remove</param>
        /// <returns>True when the directory is gone</returns>
        internal static bool DeleteWhenNothingIsUsingIt(string directory)
        {
            bool deleted = false;

            try
            {
                string renamedTo = directory;

                // A .sweeping directory was already confirmed free by the sweep that renamed it, so delete it
                // without renaming again.
                if (false == directory.EndsWith(SweepingSuffix))
                {
                    renamedTo = Path.Combine(Path.GetDirectoryName(directory), $"{TempProfilePrefix}{Guid.NewGuid():N}{SweepingSuffix}");
                    Directory.Move(directory, renamedTo);
                }

                Directory.Delete(renamedTo, true);
                deleted = true;
            }
            catch (Exception)
            {
                // Either the rename failed and a browser still has the profile, or the delete failed part way and
                // the next sweep finishes it.
            }

            return deleted;
        }

        /// <summary>
        /// Deletes unused temp profile directories on a background thread, no more often than
        /// <see cref="SweepEverySeconds"/>.<br/>
        /// A temp profile is removed when its browser closes, so this clears the ones left by a run that crashed
        /// or was killed, and the firefox profiles that are not removed on close.
        /// </summary>
        internal static void SweepAbandonedTempProfiles()
        {
            // One sweep at a time, not too often, and off the thread that is launching a browser
            if (DateTime.Now < lastSweep.AddSeconds(SweepEverySeconds))
                return;

            if (0 != Interlocked.Exchange(ref sweeping, 1))
                return;

            lastSweep = DateTime.Now;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string root = GPAL.TempProfileDirectory;

                    if (false == string.IsNullOrEmpty(root) && true == Directory.Exists(root))
                    {
                        DateTime olderThan = DateTime.Now.AddMinutes(-AbandonedAfterMinutes);
                        int swept = 0;

                        foreach (string directory in Directory.GetDirectories(root, TempProfilePrefix + "*"))
                        {
                            // Skip profiles younger than the minimum age; their browser may not have opened its
                            // files yet. An already renamed directory is deleted regardless of age.
                            if (false == directory.EndsWith(SweepingSuffix) && Directory.GetCreationTime(directory) > olderThan)
                                continue;

                            if (true == DeleteWhenNothingIsUsingIt(directory))
                                swept++;
                        }

                        if (0 < swept)
                            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Cleared [{swept}] temporary browser profile(s) left behind by earlier runs from [{root}]");
                    }
                }
                catch (Exception ex)
                {
                    // A sweep that cannot run leaves the directories in place
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Could not clear old temporary browser profiles", null, GPALObjectType.None, ex);
                }
                finally
                {
                    Interlocked.Exchange(ref sweeping, 0);
                }
            });
        }

        internal static string CreateTempUserProfile(
            string downloadDirectory,
            bool promptForDownload,
            bool openPDFExternally,
            bool loadImages,
            bool useOttoMagic,
            Dictionary<string, object> additionalPrefs = null)
        {
            try
            {
                if (!Directory.Exists(downloadDirectory))
                    Directory.CreateDirectory(downloadDirectory);

                // Create brand new empty profile root
                string tempProfileDir = NewTempProfilePath();

                string defaultDir = Path.Combine(tempProfileDir, "Default");

                Directory.CreateDirectory(defaultDir);

                Base64Helper base64Helper = new Base64Helper();
                base64Helper.WithInput(GPAL.GPALSettings.Preferences).SaveTo(Path.Combine(defaultDir, "Preferences"));
                base64Helper.WithInput(GPAL.GPALSettings.SecurePreferences).SaveTo(Path.Combine(defaultDir, "Secure Preferences"));
                base64Helper.WithInput(GPAL.GPALSettings.LocalState).SaveTo(Path.Combine(tempProfileDir, "Local State"));

                // The canned Preferences blob holds whatever it was captured with, so write the workflow's settings
                // over it as for a supplied profile: download prompt, pdf viewer, image loading and
                // .WithAdditionalPrefs.
                ApplyDownloadPreferences(tempProfileDir, downloadDirectory, promptForDownload, openPDFExternally);

                ApplyImagePreference(tempProfileDir, loadImages);

                if (null != additionalPrefs && 0 < additionalPrefs.Count)
                    ApplyAdditionalPreferences(tempProfileDir, additionalPrefs);

                return tempProfileDir;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    Enums.GPALEventType.EXCEPTION,
                    $"Failed creating temp Chrome profile folder",
                    null,
                    Enums.GPALObjectType.None,
                    ex);

                return null;
            }
        }


        // reddit consent banner - test to see if injecting this works
        // "Your request has been blocked by network security. Please try to login with your Reddit account."
        // https://www.reddit.com/r/firefox/comments/1mqmsv5/your_request_has_been_blocked_by_network_security/

        // Preferences GPAL sets on a supplied profile, by Chrome name, shared by apply and restore.
        // Do not add session.restore_on_startup: Chromium guards it with an HMAC in Secure Preferences, and writing
        // it from outside makes Chromium reset it and much of the file. Session restore is stopped by
        // profile.exit_type instead, since it comes from crash recovery.
        // Written into a supplied profile while GPAL runs on it, holding the download preferences as found, and deleted
        // once they are put back.
        private const string RestoreFileName = "gpal.restore.json";

        /// <summary>
        /// Reads a restore file written by <see cref="ApplyDownloadPreferences"/>. A preference saved as null was not
        /// in the profile, so it comes back as null and is removed on restore.
        /// </summary>
        /// <param name="restorePath">The restore file</param>
        /// <returns>The download preferences as they were before that run</returns>
        private static Dictionary<string, JToken> ReadRestoreFile(string restorePath)
        {
            Dictionary<string, JToken> previous = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(File.ReadAllText(restorePath)) ?? new Dictionary<string, JToken>();

            foreach (string key in downloadPreferenceKeys)
                if (false == previous.ContainsKey(key) || JTokenType.Null == previous[key]?.Type)
                    previous[key] = null;

            return previous;
        }

        private static readonly string[] downloadPreferenceKeys =
        {
            "download.prompt_for_download",
            "download.default_directory",
            "plugins.always_open_pdf_externally",
            "browser.show_hub_popup_on_download_start",
        };


        /// <summary>
        /// Sets a dotted preference path, making the objects along the way when they are not there. SelectToken
        /// reads a path like this but Newtonsoft has nothing that writes one.
        /// </summary>
        /// <param name="root">The Preferences document</param>
        /// <param name="path">Dotted path, e.g. download.prompt_for_download</param>
        /// <param name="value">What to set it to</param>
        private static void SetByPath(JObject root, string path, JToken value)
        {
            string[] parts = path.Split('.');
            JObject node = root;

            for (int idx = 0; idx < parts.Length - 1; idx++)
            {
                if (false == (node[parts[idx]] is JObject child))
                {
                    child = new JObject();
                    node[parts[idx]] = child;
                }

                node = child;
            }

            node[parts[parts.Length - 1]] = value;
        }
        /// <summary>
        /// Finds the Preferences file for a chrome profile directory, whether the directory given is the user data
        /// root holding Default, or a profile directory in its own right.
        /// </summary>
        /// <param name="profileDirectory">The profile or user data directory</param>
        /// <returns>The path to the Preferences file, or null when there is not one</returns>
        private static string PreferencesFileFor(string profileDirectory)
        {
            string preferencesPath = null;

            if (false == string.IsNullOrEmpty(profileDirectory))
            {
                string inDefault = Path.Combine(profileDirectory, "Default", "Preferences");
                string inProfile = Path.Combine(profileDirectory, "Preferences");

                preferencesPath = File.Exists(inDefault) ? inDefault : File.Exists(inProfile) ? inProfile : null;
            }

            return preferencesPath;
        }

        /// <summary>
        /// Writes the download preferences into an existing Chrome profile and returns the previous values.<br/>
        /// The download prompt and pdf viewer cannot be set over CDP or the extension, only in the profile. Chrome
        /// rewrites this file on exit, so it must not be running on this profile.
        /// </summary>
        /// <param name="profileDirectory">The profile to write to</param>
        /// <param name="downloadDirectory">Where downloads should be saved, or null to leave it alone</param>
        /// <param name="promptForDownload">Whether chrome should ask where to save each file</param>
        /// <param name="openPDFExternally">Whether chrome should download pdfs rather than display them</param>
        /// <returns>The values that were there before, to hand back to <see cref="RestoreDownloadPreferences"/></returns>
        internal static Dictionary<string, JToken> ApplyDownloadPreferences(string profileDirectory, string downloadDirectory, bool promptForDownload, bool openPDFExternally)
        {
            Dictionary<string, JToken> previous = null;
            string preferencesPath = PreferencesFileFor(profileDirectory);
            string restorePath = null == profileDirectory ? null : Path.Combine(profileDirectory, RestoreFileName);

            // A restore file left behind means a run on this profile ended without its Close, so its values were
            // never put back. Put them back first, so they are what this run saves and later restores.
            if (null != restorePath && true == File.Exists(restorePath))
            {
                RestoreDownloadPreferences(profileDirectory, ReadRestoreFile(restorePath));

                GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Put back the download preferences an earlier run left in [{profileDirectory}]", null, GPALObjectType.None);
            }

            if (null == preferencesPath)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No chrome Preferences file under [{profileDirectory}], so prompt for download and open pdf externally cannot be set for this profile.", null, GPALObjectType.None);
            else
                try
                {
                    JObject preferences = JObject.Parse(File.ReadAllText(preferencesPath));

                    previous = new Dictionary<string, JToken>();
                    foreach (string key in downloadPreferenceKeys)
                        previous[key] = preferences.SelectToken(key);

                    SetByPath(preferences, "download.prompt_for_download", promptForDownload);
                    SetByPath(preferences, "plugins.always_open_pdf_externally", openPDFExternally);

                    // Turn off the download flyout Edge opens, which takes focus and covers the top right of the page.
                    SetByPath(preferences, "browser.show_hub_popup_on_download_start", false);

                    // profile.exit_type is not a setting and is not restored; a killed browser leaves it Crashed.
                    // After a crash Chromium reopens the previous session regardless of session.restore_on_startup,
                    // so set it to Normal.
                    SetByPath(preferences, "profile.exit_type", "Normal");

                    // Written without a trailing separator. Chrome and Edge open Save As for every download when the
                    // folder ends in one, whatever download.prompt_for_download says. A drive root keeps its
                    // separator.
                    if (false == string.IsNullOrEmpty(downloadDirectory))
                        SetByPath(preferences, "download.default_directory",
                            downloadDirectory == Path.GetPathRoot(downloadDirectory) ? downloadDirectory : downloadDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                    File.WriteAllText(preferencesPath, JsonConvert.SerializeObject(preferences));   // not JToken.ToString(Formatting), whose overload binds against whichever Newtonsoft the host app loaded

                    // The values as found, saved in the profile until Close puts them back. A run that ends without
                    // its Close leaves the file, and the next run on this profile restores from it.
                    File.WriteAllText(restorePath, JsonConvert.SerializeObject(previous));

                    // Publish the file path and each setting's old and new value, since a profile's contents cannot
                    // be seen from outside.
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Profile [{preferencesPath}] prompt for download [{previous["download.prompt_for_download"]?.ToString() ?? "unset"}] -> [{promptForDownload}], open pdf externally [{previous["plugins.always_open_pdf_externally"]?.ToString() ?? "unset"}] -> [{openPDFExternally}], download directory [{previous["download.default_directory"]?.ToString() ?? "unset"}] -> [{downloadDirectory ?? "left alone"}]", null, GPALObjectType.None);
                }
                catch (Exception ex)
                {
                    previous = null;
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not write download preferences into [{preferencesPath}]", null, GPALObjectType.None, ex);
                }

            return previous;
        }

        /// <summary>
        /// Writes the image loading setting into a Chrome profile, like <see cref="ApplyDownloadPreferences"/>.<br/>
        /// Selenium uses --blink-settings on the command line; OttoMagic and Puppeteer have no equivalent, so it is a
        /// profile preference. 1 loads images, 2 blocks them.
        /// </summary>
        /// <param name="profileDirectory">The profile to write to</param>
        /// <param name="loadImages">Whether images should load</param>
        internal static void ApplyImagePreference(string profileDirectory, bool loadImages)
        {
            string preferencesPath = PreferencesFileFor(profileDirectory);

            if (null == preferencesPath)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No chrome Preferences file under [{profileDirectory}], so load images cannot be set for this profile.", null, GPALObjectType.None);
            else
                try
                {
                    JObject preferences = JObject.Parse(File.ReadAllText(preferencesPath));

                    SetByPath(preferences, "profile.default_content_setting_values.images", true == loadImages ? 1 : 2);

                    File.WriteAllText(preferencesPath, JsonConvert.SerializeObject(preferences));

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Profile [{preferencesPath}] load images [{loadImages}]", null, GPALObjectType.None);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not write the load images preference into [{preferencesPath}]", null, GPALObjectType.None, ex);
                }
        }

        /// <summary>
        /// Writes preferences a workflow named itself into a chrome profile, each key a dotted path the same way
        /// the settings above are written.
        /// </summary>
        /// <param name="profileDirectory">The profile to write to</param>
        /// <param name="additionalPrefs">The preferences to write, keyed by dotted path</param>
        internal static void ApplyAdditionalPreferences(string profileDirectory, Dictionary<string, object> additionalPrefs)
        {
            string preferencesPath = PreferencesFileFor(profileDirectory);

            if (null == preferencesPath)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No chrome Preferences file under [{profileDirectory}], so the additional preferences cannot be set for this profile.", null, GPALObjectType.None);
            else
                try
                {
                    JObject preferences = JObject.Parse(File.ReadAllText(preferencesPath));

                    foreach (KeyValuePair<string, object> pref in additionalPrefs)
                        SetByPath(preferences, pref.Key, null == pref.Value ? JValue.CreateNull() : JToken.FromObject(pref.Value));

                    File.WriteAllText(preferencesPath, JsonConvert.SerializeObject(preferences));

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Profile [{preferencesPath}] took [{additionalPrefs.Count}] additional preference(s): [{string.Join(", ", additionalPrefs.Keys)}]", null, GPALObjectType.None);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not write the additional preferences into [{preferencesPath}]", null, GPALObjectType.None, ex);
                }
        }

        /// <summary>
        /// Puts the download preferences back the way <see cref="ApplyDownloadPreferences"/> found them, so a
        /// profile the workflow borrowed is not left pointed at the workflow's download directory.
        /// </summary>
        /// <param name="profileDirectory">The profile that was written to</param>
        /// <param name="previous">What that method answered</param>
        internal static void RestoreDownloadPreferences(string profileDirectory, Dictionary<string, JToken> previous)
        {
            string preferencesPath = null == previous ? null : PreferencesFileFor(profileDirectory);

            if (null == preferencesPath)
                return;

            try
            {
                JObject preferences = JObject.Parse(File.ReadAllText(preferencesPath));

                foreach (string key in downloadPreferenceKeys)
                    if (null == previous[key])
                        preferences.SelectToken(key)?.Parent?.Remove();   // it was not there before, so it goes back to not being there
                    else
                        SetByPath(preferences, key, previous[key]);

                File.WriteAllText(preferencesPath, JsonConvert.SerializeObject(preferences));   // not JToken.ToString(Formatting), whose overload binds against whichever Newtonsoft the host app loaded

                File.Delete(Path.Combine(profileDirectory, RestoreFileName));

                // Publish what was restored, pairing with ApplyDownloadPreferences' message. The browser also writes
                // this file on shutdown, and the timestamps show which write was last.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Profile [{preferencesPath}] put back, prompt for download [{previous["download.prompt_for_download"]?.ToString() ?? "unset"}], download directory [{previous["download.default_directory"]?.ToString() ?? "unset"}]", null, GPALObjectType.None);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not put the download preferences back in [{preferencesPath}]", null, GPALObjectType.None, ex);
            }
        }

        internal static void RemoveTempUserProfile(string profileDirectory)
        {
            Exception innerEx = null;
            if (!Directory.Exists(profileDirectory))
                return;

            // The browser is still shutting down, so early attempts find files open. Retrying is safe because
            // DeleteWhenNothingIsUsingIt renames first, so an early attempt leaves the profile intact.
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (true == DeleteWhenNothingIsUsingIt(profileDirectory))
                    {
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Deleted temporary user profile at [{profileDirectory}]");
                        return;
                    }

                    System.Threading.Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    innerEx = ex;
                    System.Threading.Thread.Sleep(500);
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to delete temporary user profile at [{profileDirectory}][{innerEx.Message}]", null, GPALObjectType.None, innerEx);
                }
            }

            // The profile is left intact with the BrowserProfile_ prefix, so the next sweep deletes it.
            GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Still in use after 10 tries, leaving temporary user profile at [{profileDirectory}] for the next run to clear");
        }
    }
}
