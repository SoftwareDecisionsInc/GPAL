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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;
using System.Net.NetworkInformation;
using System.Net;
using System.Text.RegularExpressions;

namespace GenerallyPositive.Browser
{
    public partial class BrowserHelper
    {
        /// <summary>
        /// Starts the browser downloading <paramref name="url"/> by sending the tab to it, without waiting for a
        /// page, since a file never becomes one. Chrome and Edge take CDP Page.navigate, which returns at once.
        /// Firefox has no CDP, and its driver waits out the whole page-load limit on a download, so the limit is
        /// cut to <see cref="DownloadNavigationMs"/> for this one navigation and put back after.<br/>
        /// Whether the file arrived is for the download watcher to say.
        /// </summary>
        /// <param name="url">Absolute http or https url to download</param>
        /// <param name="browserSettings">BrowserSettings of the browser that downloads it</param>
        internal static void SeleniumStartDownload(string url, BrowserSettings browserSettings)
        {
            IWebDriver driver = browserSettings.BrowserDriver;

            if (BrowserType.FireFox != browserSettings.BrowserType)
            {
                object answer = ExecuteCdp(driver, "Page.navigate", new Dictionary<string, object> { { "url", url } });
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Sent the tab to [{url}] to download it, the browser answered [{Newtonsoft.Json.JsonConvert.SerializeObject(answer)}]", browserSettings.Browser, GPALObjectType.Browser);
            }
            else
            {
                try
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(DownloadNavigationMs);
                    driver.Navigate().GoToUrl(url);
                }
                catch (WebDriverTimeoutException)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No page came back for [{url}] within [{DownloadNavigationMs}] ms, as expected of a file", browserSettings.Browser, GPALObjectType.Browser);
                }
                finally
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(browserSettings.WaitForDocumentReadyTimeoutMs);
                }
            }
        }
        /// <summary>
        /// checking the extension of the url, but also
        /// calling the URL to see if it returns a pdf response header
        /// GPAL calls this if we don't detect a file downloaded but expect one
        /// </summary>
        /// <param name="browser">Browser whose user agent the header request is made with</param>
        /// <param name="url">the url of the potential pdf file</param>
        /// <returns>true or false</returns>
        public static bool TestUrlForPDF(Browser browser, string url)
        {
            if (null == url)
                return false;

            bool retVal = false;

            // a url containing .pdf is taken as a pdf; otherwise its headers are checked
            if (0 <= url.IndexOf(".pdf", 0, StringComparison.OrdinalIgnoreCase))
                retVal = true;
            else
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", GetUserAgent(browser));

                    try
                    {
                        // Use HttpMethod.Head to retrieve headers ONLY (minimal overhead)
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url))
                        {
                            HttpResponseMessage response = client.SendAsync(request).Result;

                            if (response.IsSuccessStatusCode)
                            {
                                // 1. Check Content-Type for application/pdf
                                string contentType = response.Content.Headers.ContentType?.MediaType;
                                if (contentType != null && contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                                {
                                    retVal = true;
                                }
                                // 2. Fallback: Check Content-Disposition for a .pdf filename
                                else if (response.Content.Headers.ContentDisposition != null)
                                {
                                    string fileName = response.Content.Headers.ContentDisposition.FileName;
                                    if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                    {
                                        retVal = true;
                                    }
                                }
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to verify URL [{url}] via HEAD request. Status code [{response.StatusCode}]");
                            }
                        }
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Exception during HEAD request for [{url}]", null, GPALObjectType.None, ex);
                    }
                }

            if (true == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GPAL detected PDF file opened in browser [{url}]");

            return retVal;
        }

        /// <summary>
        /// True if <paramref name="content"/> starts with %PDF.<br/>
        /// The url proves nothing: a redirect or consent page is served as HTML at the same .pdf url.
        /// </summary>
        /// <param name="content">The bytes that came back</param>
        /// <returns>True if the bytes begin with the pdf signature</returns>
        internal static bool IsPdf(byte[] content)
        {
            return 4 < content?.Length
                && '%' == content[0] && 'P' == content[1] && 'D' == content[2] && 'F' == content[3];
        }
        /// <summary>
        /// Ensures a PDF file ends up at <paramref name="savePath"/>.<br/>
        /// First checks if the browser already downloaded it to the target or default download directory
        /// (and moves it into place if so); otherwise fetches <paramref name="pdfUrl"/> from inside the page,
        /// and falls back to downloading it directly via HttpClient.
        /// </summary>
        /// <param name="pdfUrl">URL of the PDF file</param>
        /// <param name="savePath">Full path (directory + filename) to save the PDF to</param>
        /// <param name="browser">Browser used to determine the default download directory and user agent, and to fetch from</param>
        /// <returns>True if the file was moved or downloaded successfully, false otherwise</returns>
        public static bool DownloadPdfFile(string pdfUrl, string savePath, IBrowser browser)
        {
            // the click may already have downloaded the file, so look for it in the save folder
            string filePath = $@"{FileHelper.EnsureDirectoryEndsWithBackslash(Path.GetDirectoryName(savePath))}{System.IO.Path.GetFileName(pdfUrl)}";
            if (true == File.Exists(filePath))
            {
                FileHelper.EnsureFolderFor(savePath);
                File.Move(filePath, savePath);
                return true;
            }
            else
            {
                filePath = $@"{FileHelper.GetDefaultDownloadDirectory(browser)}{Path.GetFileName(pdfUrl)}";
                if (true == File.Exists(filePath))
                {
                    FileHelper.EnsureFolderFor(savePath);
                    File.Move(filePath, savePath);
                    return true;
                }
            }

            // Fetch from inside the page first, on the browser's connection, so a url signed for this session works;
            // HttpClient below would be refused. A redirect page can be served at this url until the browser
            // reaches the PDF, so write only PDF bytes. Otherwise wait PdfFetchWaitMs, read the browser's current
            // url, and fetch that.
            for (int attempt = 0; attempt < PdfFetchAttempts; attempt++)
            {
                byte[] fetched = FetchBytes((Browser)browser, pdfUrl, null, out string _);

                if (true == IsPdf(fetched))
                {
                    FileHelper.EnsureFolderFor(savePath);
                    File.WriteAllBytes(savePath, fetched);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"PDF file [{pdfUrl}] fetched from the page to [{savePath}]");
                    return true;
                }

                if (attempt < PdfFetchAttempts - 1)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{pdfUrl}] answered with [{fetched?.Length ?? 0}] bytes that are not a pdf, waiting for the file itself", browser, GPALObjectType.Browser);
                    Thread.Sleep(PdfFetchWaitMs);
                    pdfUrl = GetCurrentUrl(((Browser)browser).BrowserSettings) ?? pdfUrl;
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{pdfUrl}] never answered with a pdf, the page in front of the file did. Nothing written to [{savePath}]", browser, GPALObjectType.Browser);
            }

            // In-page fetch never got a PDF: use HttpClient. In-page fetch obeys cross-origin rules, so a PDF on
            // another domain can be refused there.
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", GetUserAgent(browser));

                try
                {
                    // Download the PDF file content synchronously
                    byte[] pdfContent = client.GetByteArrayAsync(pdfUrl).Result;

                    if (false == IsPdf(pdfContent))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{pdfUrl}] answered with [{pdfContent?.Length ?? 0}] bytes that are not a pdf. Nothing written to [{savePath}]", browser, GPALObjectType.Browser);
                        return false;
                    }

                    // Save the PDF file to disk
                    FileHelper.EnsureFolderFor(savePath);
                    File.WriteAllBytes(savePath, pdfContent);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"PDF file [{pdfUrl}] downloaded successfully to [{savePath}]");
                    return true;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"PDF file NOT downloaded from [{pdfUrl}]", ex);
                }
            }
            return false;
        }
    }
}
