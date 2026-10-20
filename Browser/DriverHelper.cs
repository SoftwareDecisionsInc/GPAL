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

using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    internal static class DriverHelper
    {
        /// <summary>
        /// The driver's download page, for the message published when the automatic update fails.
        /// </summary>
        /// <param name="browserType">The browser the driver is for</param>
        /// <returns>The download page</returns>
        internal static string ManualDownloadPage(BrowserType browserType)
        {
            string retVal;

            switch (browserType)
            {
                case BrowserType.Edge:
                    retVal = "https://developer.microsoft.com/en-us/microsoft-edge/tools/webdriver";
                    break;
                case BrowserType.FireFox:
                    retVal = "https://github.com/mozilla/geckodriver/releases";
                    break;
                default:
                    retVal = "https://googlechromelabs.github.io/chrome-for-testing/";
                    break;
            }

            return retVal;
        }

        /// <summary>
        /// Updates the browser's driver from the published release data. Chrome and Edge drivers are looked up by the
        /// browser's major version, so a browser held behind stable still gets a match. Geckodriver is not tied to
        /// Firefox versions, so Firefox gets the latest.
        /// </summary>
        /// <param name="browserSettings">Settings holding the browser type and the driver location to update</param>
        public static void UpdateDriver(BrowserSettings browserSettings)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Driver Auto Update running for [{browserSettings.BrowserType}]", browserSettings, GPALObjectType.Other);

            // Do not stop if the update fails. The existing driver may still work, and if not, starting the browser
            // reports it.
            try
            {
                if (BrowserType.FireFox == browserSettings.BrowserType)
                    UpdateFirefoxDriver(browserSettings);
                else if (BrowserType.Edge == browserSettings.BrowserType)
                    UpdateEdgeDriver(browserSettings);
                else
                    UpdateChromeDriver(browserSettings);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to update the [{browserSettings.BrowserType}] driver. Update it by hand from [{ManualDownloadPage(browserSettings.BrowserType)}]", browserSettings, GPALObjectType.Browser, ex);
            }
        }

        /// <summary>
        /// Chromedriver from Chrome for Testing's milestone list, which holds the newest driver for every Chrome
        /// major from 113 on, with a download per platform. The major of the installed Chrome picks the entry, so
        /// Chrome 153 gets the 153 driver whether or not stable has since moved on to 154.
        /// </summary>
        /// <param name="browserSettings">Settings holding the driver location to update</param>
        private static void UpdateChromeDriver(BrowserSettings browserSettings)
        {
            string path = FileHelper.EnsureDirectoryEndsWithBackslash(browserSettings.DriverLocation);
            string driver = GPAL.GPALSettings.ChromeDriverFilename;
            string platform = GPALSettings.DriverPlatform;
            string url = GPAL.GPALSettings.ChromeDriverUpdateURL;
            GPALFile versionFile = Path.Combine(path, $"{BrowserType.Chrome}.version.yaml");

            GetChromeVersion(out string browserVersion);

            string major = browserVersion.Split('.')[0];
            string recorded = RecordedVersion(versionFile, $"{path}{driver}");

            if (true == string.Equals(recorded, browserVersion, StringComparison.OrdinalIgnoreCase))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Web driver already current for Chrome [{browserVersion}]", browserSettings, GPALObjectType.Browser);
            else
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", MagicHelper.GetUserAgentString(BrowserType.Chrome));

                    JToken milestone = JObject.Parse(httpClient.GetStringAsync(url).Result)["milestones"]?[major];
                    JToken download = milestone?["downloads"]?["chromedriver"]?.FirstOrDefault(d => platform == d["platform"]?.ToString());

                    if (null == download)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Chrome for Testing publishes no [{platform}] chromedriver for Chrome [{major}]", browserSettings, GPALObjectType.Browser);
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Please update driver manually at [{ManualDownloadPage(BrowserType.Chrome)}]", browserSettings, GPALObjectType.Browser);
                    }
                    else
                        DownloadAndExtractDriver(BrowserType.Chrome, download["url"].ToString(), path, GPAL.GPALSettings.ChromeDriverZipFilename, driver, browserVersion, versionFile);
                }
            }
        }

        /// <summary>
        /// Msedgedriver from Microsoft's driver host, which answers LATEST_RELEASE_&lt;major&gt;_WINDOWS with the
        /// newest driver build for that major, and serves it at &lt;version&gt;/edgedriver_&lt;platform&gt;.zip. The
        /// answer is UTF-16 text with a byte order mark, so it is read as bytes and decoded by its mark.
        /// </summary>
        /// <param name="browserSettings">Settings holding the driver location to update</param>
        private static void UpdateEdgeDriver(BrowserSettings browserSettings)
        {
            string path = FileHelper.EnsureDirectoryEndsWithBackslash(browserSettings.DriverLocation);
            string driver = GPAL.GPALSettings.EdgeDriverFilename;
            string zipFilename = GPAL.GPALSettings.EdgeDriverZipFilename;
            string host = GPAL.GPALSettings.EdgeDriverUpdateURL;
            GPALFile versionFile = Path.Combine(path, $"{BrowserType.Edge}.version.yaml");

            GetEdgeVersion(out string browserVersion);

            string major = browserVersion.Split('.')[0];
            string recorded = RecordedVersion(versionFile, $"{path}{driver}");

            if (true == string.Equals(recorded, browserVersion, StringComparison.OrdinalIgnoreCase))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Web driver already current for Edge [{browserVersion}]", browserSettings, GPALObjectType.Browser);
            else
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", MagicHelper.GetUserAgentString(BrowserType.Edge));

                    byte[] answer = httpClient.GetByteArrayAsync($"{host}LATEST_RELEASE_{major}_WINDOWS").Result;
                    string latest;

                    using (StreamReader reader = new StreamReader(new MemoryStream(answer), Encoding.UTF8, true))
                        latest = reader.ReadToEnd().Trim().Trim('\0');

                    if (false == latest.StartsWith($"{major}."))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Microsoft names no msedgedriver for Edge [{major}], it answered [{latest}]", browserSettings, GPALObjectType.Browser);
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Please update driver manually at [{ManualDownloadPage(BrowserType.Edge)}]", browserSettings, GPALObjectType.Browser);
                    }
                    else
                        DownloadAndExtractDriver(BrowserType.Edge, $"{host}{latest}/{zipFilename}", path, zipFilename, driver, browserVersion, versionFile);
                }
            }
        }

        /// <summary>
        /// Updates geckodriver from the release JSON at FirefoxDriverUpdateURL, not the GitHub releases page, whose
        /// asset links load through include-fragment and are not in the HTML. The file has no version resource and is
        /// not versioned with Firefox; tag_name is the version and assets has the download.<br/>
        /// FirefoxDriverZipFilename is a glob against asset names to pick this machine's build.
        /// </summary>
        /// <param name="browserSettings">Settings holding the driver location to update</param>
        private static void UpdateFirefoxDriver(BrowserSettings browserSettings)
        {
            string path = FileHelper.EnsureDirectoryEndsWithBackslash(browserSettings.DriverLocation);
            string driver = GPAL.GPALSettings.FirefoxDriverFilename;
            string zipFilename = GPAL.GPALSettings.FirefoxDriverZipFilename;
            string[] nameParts = zipFilename.Split('*');
            string installed = GetGeckodriverVersion($"{path}{driver}");
            string url = GPAL.GPALSettings.FirefoxDriverUpdateURL;
            GPALFile versionFile = Path.Combine(path, $"{BrowserType.FireFox}.version.yaml");

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", MagicHelper.GetUserAgentString(BrowserType.FireFox));

                JObject release = JObject.Parse(httpClient.GetStringAsync(url).Result);
                string latest = release["tag_name"]?.ToString().TrimStart('v');

                JToken asset = release["assets"]?.FirstOrDefault(a =>
                    true == a["name"].ToString().StartsWith(nameParts[0], StringComparison.OrdinalIgnoreCase) &&
                    true == a["name"].ToString().EndsWith(nameParts[nameParts.Length - 1], StringComparison.OrdinalIgnoreCase));

                if (true == string.Equals(installed, latest, StringComparison.OrdinalIgnoreCase))
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Web driver already current for geckodriver [{installed}]", browserSettings, GPALObjectType.Browser);
                else if (null == asset)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Geckodriver [{latest}] publishes no asset matching [{zipFilename}]", browserSettings, GPALObjectType.Browser);
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Please update driver manually at [{ManualDownloadPage(BrowserType.FireFox)}]", browserSettings, GPALObjectType.Browser);
                }
                else
                    DownloadAndExtractDriver(BrowserType.FireFox, asset["browser_download_url"].ToString(), path, zipFilename.Replace("*", ""), driver, latest, versionFile);
            }
        }
        /// <summary>
        /// Version of the installed geckodriver. The file has no version resource, so run geckodriver --version and
        /// read "geckodriver 0.37.0 (hash date)".
        /// </summary>
        /// <param name="driverPath">Full path to geckodriver</param>
        /// <returns>The version, null when there is no driver yet or it would not say</returns>
        private static string GetGeckodriverVersion(string driverPath)
        {
            string installed = null;

            if (true == File.Exists(driverPath))
            {
                Process process = Process.Start(new ProcessStartInfo
                {
                    FileName = driverPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                string[] parts = (process.StandardOutput.ReadLine() ?? string.Empty).Split(' ');
                process.WaitForExit();

                if (1 < parts.Length)
                    installed = parts[1];
            }

            return installed;
        }
        /// <summary>
        /// The browser version recorded when its driver was last downloaded. Null if the driver file is missing, so
        /// a deleted driver is downloaded again.
        /// </summary>
        /// <param name="versionFile">The browser's version file</param>
        /// <param name="driverPath">Full path to the driver the record describes</param>
        /// <returns>The recorded version, null when there is no driver or no record</returns>
        private static string RecordedVersion(GPALFile versionFile, string driverPath)
        {
            string retVal = null;

            if (true == File.Exists(driverPath) && true == File.Exists(versionFile.Filenames[0]))
            {
                IGPALGrid<string> recorded = GPAL.Grid.ToGPALObject();

                GPAL.Converter.WithInput(versionFile).SaveTo(ref recorded);

                // the record is the browser name and a version, so the version is the cell that starts with a digit
                foreach (List<string> row in recorded)
                    foreach (string cell in row)
                        if (false == string.IsNullOrEmpty(cell) && true == char.IsDigit(cell[0]))
                            retVal = cell;
            }

            return retVal;
        }
        /// <summary>
        /// Downloads a driver zip, extracts the driver and records its version. A running driver cannot be replaced,
        /// so with a GPAL browser still open the update fails and the next start retries.
        /// </summary>
        /// <param name="browserType">The browser the driver is for</param>
        /// <param name="href">Url of the zip to download</param>
        /// <param name="path">Driver directory, ending in a backslash</param>
        /// <param name="zipFilename">Name to save the zip as while we work with it</param>
        /// <param name="driver">Name of the driver file inside the zip</param>
        /// <param name="recordedVersion">Version to write to the version file: the browser's for Chrome and Edge, the driver's for Firefox</param>
        /// <param name="versionFile">The browser's version file</param>
        private static void DownloadAndExtractDriver(BrowserType browserType, string href, string path, string zipFilename, string driver, string recordedVersion, GPALFile versionFile)
        {
            File.Delete($"{path}{zipFilename}");
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Trying to download driver [{href}] to [{$"{path}{zipFilename}"}]");

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", MagicHelper.GetUserAgentString(browserType));
                FileHelper.EnsureFolderFor($"{path}{zipFilename}");
                File.WriteAllBytes($"{path}{zipFilename}", httpClient.GetByteArrayAsync(href).Result);
            }

            using (ZipArchive archive = ZipFile.OpenRead($"{path}{zipFilename}"))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Extracting [{driver}] from zip file");

                ZipArchiveEntry entryToExtract = archive.Entries.FirstOrDefault(e => e.Name.Equals(driver, StringComparison.OrdinalIgnoreCase));

                if (null != entryToExtract)
                {
                    string destinationFilePath = Path.Combine(path, entryToExtract.Name);

                    if (true == File.Exists(destinationFilePath))
                        File.Delete(destinationFilePath);

                    entryToExtract.ExtractToFile(destinationFilePath);
                }
            }

            File.Delete($"{path}{zipFilename}");

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{browserType}] web driver updated for [{recordedVersion}]");

            IGPALGrid<string> gPALGrid = GPAL.Grid.ToGPALObject();
            gPALGrid.AddRow(new List<string> { browserType.ToString(), recordedVersion });

            GPAL.Converter.WithInput(gPALGrid).SaveTo(versionFile);
        }
        internal static string GetChromeVersion(out string fullVersion)
        {
            string version = null;
            RegistryKey regKey = null;

            try
            {
                regKey = Registry.CurrentUser.OpenSubKey(GPAL.GPALSettings.ChromeVersionRegistry);
                version = regKey.GetValue("version").ToString();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to get Chrome version from registry", null, Enums.GPALObjectType.None, ex);
            }
            finally
            {
                regKey?.Close();
            }
            fullVersion = version;

            return string.Join(".", version.Split('.').Take(3));    // NOTEL CAVEAT: 144.0.7559.96 - we are taking 3 but that doesn't always work, but anything less and we will update every time
        }
        internal static string GetEdgeVersion(out string fullVersion)
        {
            string version = null;
            RegistryKey regKey = null;

            try
            {
                regKey = Registry.CurrentUser.OpenSubKey(GPAL.GPALSettings.EdgeVersionRegistry);
                version = regKey.GetValue("version").ToString();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to get Edge version from registry", null, Enums.GPALObjectType.None, ex);
            }
            finally
            {
               regKey?.Close();
            }

            fullVersion = version;
            return version.Split('.')[0];
        }
        internal static string GetFirefoxVersion(out string fullVersion)
        {
            string version = null;
            RegistryKey regKey = null;

            try
            {
                regKey = Registry.CurrentUser.OpenSubKey(GPAL.GPALSettings.FirefoxVersionRegistry);
                version = regKey.GetValue("CurrentVersion")?.ToString();
                if (true == string.IsNullOrEmpty(version))
                {
                    regKey = Registry.CurrentUser.OpenSubKey(GPAL.GPALSettings.FirefoxVersionRegistry2);
                    version = regKey.GetValue("CurrentVersion")?.ToString();
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to get Firefox version from registry", null, Enums.GPALObjectType.None, ex);
            }
            finally
            {
                regKey?.Close();
            }
            fullVersion = version;

            return version.Split('.')[0];
        }
    }
}

