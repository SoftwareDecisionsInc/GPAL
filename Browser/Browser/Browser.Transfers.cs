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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// ElementAssistant sets these fields to hand its own element and selector to the download and upload workflows
        bool useEAvars = false;
        GPALElement EAelement = null;
        Selector EAselector = null;
        /// <summary>
        /// Entry point used by ElementAssistant.LeftClickAndDownload to download from a specific element/selector
        /// rather than the current unit of work's selector list.
        /// </summary>
        /// <param name="gPalFile">GPALFile to download to</param>
        /// <param name="webElement">Element to click to start the download</param>
        /// <param name="selector">Selector associated with the element</param>
        internal void LeftClickAndDownload(GPALFile gPalFile, GPALElement webElement, Selector selector)
        {
            useEAvars = true;
            EAelement = webElement;
            EAselector = selector;

            LeftClickAndDownload(gPalFile);

        }
        // Seconds to wait for a Save As dialog that may not open.
        internal const int UnexpectedSaveAsWaitSeconds = 2;
        internal const int ProfileRestoreWaitMs = 5000;      // how long to give the browser to shut down before the profile goes back

        /// <summary>
        /// Left clicks the element(s) found for the current selector(s) and downloads the resulting file to
        /// <paramref name="gPalFileToDownloadTo"/>.
        /// </summary>
        /// <param name="gPalFileToDownloadTo">GPALFile to download to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClickAndDownload(GPALFile gPalFileToDownloadTo)
        {
            string fullFilePathToSave;
            HWND windowOpened = IntPtr.Zero;
            List<string> returnFilename = new List<string>();
            bool noSaveAsDialog = false;
            FileDownloaded = false;
            int rowCount = 0;

            if (true == useEAvars)
            {
                DownloadWorkflow(EAelement, EAselector);
                return this;
            }

            int DownloadWorkflow(GPALElement webElement, Selector selector)
            {
                GPALFile fileToSave = gPalFileToDownloadTo.Next;
                fullFilePathToSave = FileHelper.CleanUpFileDestination(fileToSave.Filename, out string newFilename, BrowserSettings.DeleteFileBeforeDownload);
                returnFilename.Add(fullFilePathToSave);  // return the filenames generated

                string currentUrl = null, currentUrl2 = null;

                currentUrl = BrowserHelper.GetCurrentUrl(BrowserSettings);

                FileDownloaded = null;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloading to [{fullFilePathToSave}]", this, GPALObjectType.Browser);

                if (true == BrowserSettings.UseDirectDownload)
                {
                    // Download from inside the page first, with the browser's cookies and clearance, then in a tab.
                    // Not HttpClient: without cookies a file behind a login returns 403.
                    string directUrl = FileHelper.GetDownloadUrl(webElement);

                    if (true == string.IsNullOrWhiteSpace(directUrl))
                    {
                        // No download url on the element, usually a script-driven button; it has to be clicked.
                        FileDownloaded = false;
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{webElement?.TagName}] carries no url to download, so WithUseDirectDownload has nothing to ask for. Leave it off and the click is used instead", this, GPALObjectType.Browser);
                    }
                    else
                    {
                        FileDownloaded = FileHelper.FetchOrDownloadInTab(directUrl, fullFilePathToSave, this);

                        if (false == FileDownloaded)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{directUrl}] did not download to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                    }
                }
                // No prompt (or headless) means no Save As dialog; watch the download directory. OpenPDFExternally is
                // a profile preference neither CDP nor the extension can change, so it is not checked.
                else if (true == BrowserSettings.UseHeadless || false == BrowserSettings.PromptForDownload)
                {
                    string watchedDirectory = FileHelper.SetupDownloadWatcher(fullFilePathToSave, BrowserSettings);

                    // Puppeteer and OttoMagic set the download path and click; the loop below waits for the file
                    if (true == BrowserSettings.UsePuppeteer)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][Left] clicked with modifiers [NONE].");
                        PuppeteerClient.LeftClickAndDownload(webElement.ElementHandle).DownloadTo(fileToSave).Execute();
                    }
                    else if (true == BrowserSettings.UseOttoMagic)
                    {
                        // These branches click through their own client, not ElementHelper.Click, so they publish the
                        // click event themselves; otherwise a missing file cannot be told from a missed click.
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][Left] clicked with modifiers [NONE].");
                        MagicHelper.LeftClickAndDownload(webElement.Css, fileToSave);
                    }
                    else   // selenium
                    {
                        BrowserHelper.SetHeadlessDownload(BrowserSettings, fullFilePathToSave);
                        ElementHelper.Click(this, selector, webElement, ClickType.LeftClick, ModifierKeys.NONE);
                    }

                    Thread.Sleep(250);
                    // a modal that opens before the download starts is dismissed by the persistent selectors
                    string urlAfterClick = BrowserHelper.GetCurrentUrl(BrowserSettings);

                    // Persistent selectors apply only if the click stayed on the page.
                    if (true == UrlHelper.AreEquivalent(currentUrl, urlAfterClick))
                        CheckPersistentSelectors();

                    if (false == UrlHelper.AreEquivalent(currentUrl, urlAfterClick)
                        && true == fileToSave.Filename.EndsWith(".pdf")
                        && true == BrowserHelper.TestUrlForPDF(this, urlAfterClick))
                    {
                        //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{urlAfterClick}] is the pdf itself, fetching it rather than waiting on a Save As dialog.", this, GPALObjectType.Browser);
                        FileDownloaded = BrowserHelper.DownloadPdfFile(urlAfterClick, fullFilePathToSave, this);
                    }

                    // In case PromptForDownload is not honored or missing or somehow not working
                    /*
                    if (false == BrowserSettings.UseHeadless)
                    {
                        HWND unexpectedDialog = WaitForCommonDialog(UnexpectedSaveAsWaitSeconds);

                        if (IntPtr.Zero != unexpectedDialog)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] asked where to save the file even though prompting was turned off. Answering the dialog with [{newFilename}].", this, GPALObjectType.Browser);

                            HardwareHelper.SendString(newFilename);
                            HardwareHelper.SendKey(GPAL.VK_RETURN);
                        }
                    }
                    */

                    // The timeout counts time without progress. It restarts whenever the file size changes, so a
                    // moving download is never cut off and a stalled or unstarted one ends.
                    Stopwatch sinceProgress = Stopwatch.StartNew();
                    long lastSeen = long.MinValue;

                    // A false FileDownloaded is not final: the watcher fires many times, and an early event can be
                    // false before a later one finds the file. The loop ends on download or on the allowance.
                    while (null == FileDownloaded || false == FileDownloaded)
                    {
                        // The watcher stays up until after the Save As recovery, so a file that arrives after this
                        // loop can still set FileDownloaded.
                        long progress = FileHelper.DownloadProgress(watchedDirectory, fullFilePathToSave);

                        if (progress != lastSeen)
                        {
                            lastSeen = progress;
                            sinceProgress.Restart();
                        }
                        else if (sinceProgress.Elapsed.TotalSeconds > FileHelper.AllowanceFor(watchedDirectory, BrowserSettings))
                            break;

                        Thread.Sleep(100); // Prevents 100% CPU usage
                    }

                    currentUrl2 = BrowserHelper.GetCurrentUrl(BrowserSettings);

                    // no file: the click may have opened the pdf in the browser, so test the url for a pdf and download it
                    if (false == (bool)(FileDownloaded ?? false) && false == BrowserSettings.OpenPDFExternally && true == fileToSave.Filename.EndsWith(".pdf"))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Testing [{currentUrl2}] for PDF file", this, GPALObjectType.Browser);
                        if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2) && true == BrowserHelper.TestUrlForPDF(this, currentUrl2))
                        {
                            FileDownloaded = BrowserHelper.DownloadPdfFile(currentUrl2, fullFilePathToSave, this);
                            noSaveAsDialog = !(bool)FileDownloaded;
                        }
                    }

                    // there is no Save As on this path, so the file can only be in the download directory
                    if (false == (bool)(FileDownloaded ?? false))
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File not downloaded to [{fullFilePathToSave}].", this, GPALObjectType.Browser);
                        FileHelper.DiscardAbandonedDownload(BrowserSettings);

                }
                else if (false == BrowserSettings.UseHeadless)    // if we are not headless, we can handle the dialog or direct download
                {
                    ElementHelper.Click(this, selector, webElement, ClickType.LeftClick, ModifierKeys.NONE);

                    string urlAfterClick = BrowserHelper.GetCurrentUrl(BrowserSettings);

                    // a modal that opens before the download starts is dismissed by the persistent selectors
                    if (true == UrlHelper.AreEquivalent(currentUrl, urlAfterClick))
                        CheckPersistentSelectors();

                    // The click navigated to the pdf, so no Save As will open. Download it from its url and skip the
                    // dialog wait. OpenPDFExternally is not checked: had the browser honored it, it would not have
                    // navigated.
                    if (false == UrlHelper.AreEquivalent(currentUrl, urlAfterClick)
                        && true == fileToSave.Filename.EndsWith(".pdf")
                        && true == BrowserHelper.TestUrlForPDF(this, urlAfterClick))
                    {
                        //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{urlAfterClick}] is the pdf itself, fetching it rather than waiting on a Save As dialog.", this, GPALObjectType.Browser);
                        FileDownloaded = BrowserHelper.DownloadPdfFile(urlAfterClick, fullFilePathToSave, this);
                    }

                    if (false == (bool)(FileDownloaded ?? false) && true == BrowserSettings.PromptForDownload)
                        windowOpened = WaitForCommonDialog(BrowserSettings.WaitForWindowTimeoutInSeconds);

                    // The pdf is already downloaded; skip the Save As recovery below, which would report a false
                    // failure and use an element gone with the navigation.
                    if (true == (bool)(FileDownloaded ?? false))
                        noSaveAsDialog = false;
                    // the Save As dialog opened: type the filename and press Enter
                    else if (IntPtr.Zero != windowOpened)
                    {
                        HardwareHelper.SendString(newFilename);
                        HardwareHelper.SendKey(GPAL.VK_RETURN);

                        // send Enter again if the Save As dialog is still open
                        windowOpened = WaitForCommonDialog(1);

                        if (IntPtr.Zero != windowOpened)
                            HardwareHelper.SendKey(GPAL.VK_RETURN);

                        string actualDownloadedFilename = null;

                        actualDownloadedFilename = FileHelper.WaitForDownloadToFinish(newFilename, BrowserSettings.DownloadTimeoutInSec);

                        FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename);

                        if (false == FileDownloaded)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File not downloaded to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                            FileHelper.DiscardAbandonedDownload(BrowserSettings);

                    }
                    else
                    {
                        if (true == BrowserSettings.PromptForDownload)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to handle Save As... dialog [{fullFilePathToSave}][{selector.Name}]", BrowserSettings, GPALObjectType.Other);

                        noSaveAsDialog = true;

                        // the click may have opened the pdf in the browser; read the url to test it
                        currentUrl2 = BrowserHelper.GetCurrentUrl(BrowserSettings);

                        // OpenPDFExternally is not checked here either; no Save As appeared, so the browser ignored it.
                        if (false == (bool)(FileDownloaded ?? false) && true == fileToSave.Filename.EndsWith(".pdf"))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Testing [{currentUrl2}] for PDF file", this, GPALObjectType.Browser);
                            if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2) && true == BrowserHelper.TestUrlForPDF(this, currentUrl2))
                            {
                                FileDownloaded = BrowserHelper.DownloadPdfFile(currentUrl2, fullFilePathToSave, this);
                                noSaveAsDialog = !(bool)FileDownloaded;
                            }
                        }
                    }
                }

                // No Save As dialog within WaitForWindowTimeoutInSeconds (30 by default): the site may have downloaded
                // straight to the browser's download directory, so look there for a file with the GPALFile's extension
                if (true == noSaveAsDialog)
                {
                    string downloadDirectory = FileHelper.GetDefaultDownloadDirectory(this);
                    string downloadUrl = FileHelper.GetDownloadUrl(webElement);
                    string filename = Path.GetFileName(downloadUrl) ?? fullFilePathToSave;

                    string wildcardFilename = "*" + Path.GetExtension(filename);
                    string systemDownloadedTo = Path.Combine(downloadDirectory, wildcardFilename);
                    string actualDownloadedFilename = null;

                    actualDownloadedFilename = FileHelper.WaitForDownloadToFinish(systemDownloadedTo, BrowserSettings.DownloadTimeoutInSec);

                    FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename);

                    if (true == FileDownloaded)
                    {
                        if (false == fullFilePathToSave.Equals(actualDownloadedFilename))
                        {
                            FileHelper.EnsureFolderFor(fullFilePathToSave);
                            File.Move(actualDownloadedFilename, fullFilePathToSave);
                            if (false == actualDownloadedFilename.Equals(fullFilePathToSave) && true == File.Exists(actualDownloadedFilename))
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloaded [{actualDownloadedFilename}] unable to move to [{fullFilePathToSave}]", null, GPALObjectType.None);
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{actualDownloadedFilename}] moved to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                        }
                    }
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{filename}] not found in [{downloadDirectory}]", this, GPALObjectType.Browser);
                }

                if (null != FileHelper.watcher)    // we might not have disposed of watcher
                {
                    try
                    {
                        FileHelper.watcher.Dispose();
                        FileHelper.watcher = null;
                    }
                    catch { }
                }

                // dismiss the 'downloads' popup which can steal keys, clicks or get in the way; the real mouse cannot
                // reach a hidden desktop, so not there
                if (false == BrowserSettings.UseHeadless && false == BrowserSettings.HiddenDesktop && BrowserType.Edge == BrowserSettings.BrowserType)
                {
                    // KLUDGE: a hardware top clicks the browser chrome, which dismisses the 'downloads' popup
                    BrowserHelper.TopBrowser(this, false, true);
                }

                if (int.MaxValue != CurrentUOW.WithAllThatMatch && ++rowCount == CurrentUOW.WithAllThatMatch)
                    return -1;
                else 
                    return 0;
            }

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    ReadOnlyCollection<GPALElement> webelements = ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                    if (null != webelements)
                    {
                        // Save the elements' page url, to return to if a download click navigates away.
                        string pageOfTheElements = BrowserHelper.GetCurrentUrl(BrowserSettings);
                        int matchCount = matchedElements.Count;

                        for (int elementIdx = 0; elementIdx < matchCount; elementIdx++)
                        {
                            string urlNow = BrowserHelper.GetCurrentUrl(BrowserSettings);

                            // The click navigated away: go back and find the elements again. The old references throw
                            // an unknown reference error.
                            if (false == UrlHelper.AreEquivalent(pageOfTheElements, urlNow))
                            {
                                GoTo(pageOfTheElements);
                                ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool _, out matchedElements);

                                if (matchedElements.Count != matchCount)
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{selector.Name}] matched [{matchedElements.Count}] on the way back rather than [{matchCount}], so the remaining downloads may not be the ones first found.", this, GPALObjectType.Browser);
                            }

                            if (elementIdx >= matchedElements.Count)
                                break;

                            // -1 ends the downloads
                            if (-1 == DownloadWorkflow(matchedElements[elementIdx], selector))
                                break;
                        }
                    }
                }

                foreach (string filepath in returnFilename)
                {
                    gPalFileToDownloadTo.ReturnFilenames.Add(filepath); // return the filenames we generated
//                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File saved to [{filepath}]");
                }
            }

            GetSetCurrentUrl();

            return this;
        }
        /// <summary>
        /// Left click the input[type=file] or custom control button and upload what is on the clipboard. FileList
        /// uploads the copied files. Every other type is written to a file first, named by WithFilename or
        /// clipboard plus the type's extension, in GPAL's temp folder.
        /// </summary>
        /// <example>
        /// <code>
        ///     myBrowser
        ///         .WithSelector((Selector)"#attach")
        ///         .LeftClickAndUpload(GPAL.Clipboard.GetClipboard(ClipboardType.Image).WithFilename("receipt.png"));
        /// </code>
        /// </example>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClickAndUpload(IAllowClipboardInto clipboard)
        {
            GPALFile file = ((GPALClipboard)clipboard).FileToUpload();

            // with nothing to upload, InAction still closes the unit of work, so the next WithSelector starts a new one
            if (null != file)
                LeftClickAndUpload(file);
            else
                InAction(WaitTime.Never < CurrentUOW.WaitForInMs);

            return this;
        }
        /// <summary>
        /// Entry point used by ElementAssistant.LeftClickAndUpload to upload from a specific element/selector
        /// rather than the current unit of work's selector list.
        /// </summary>
        /// <param name="gPalFile">GPALFile to upload from</param>
        /// <param name="webElement">Element to click to start the upload</param>
        /// <param name="selector">Selector associated with the element</param>
        internal void LeftClickAndUpload(GPALFile gPalFile, GPALElement webElement, Selector selector)
        {
            useEAvars = true;
            EAelement = webElement;
            EAselector = selector;

            LeftClickAndUpload(gPalFile);

        }
        /// <summary>
        /// Left click the input[type=file] or custom control button
        /// Headful - enter filename in open file dialog
        /// Headless - selenium and puppeteer can use CDP top populate files, OttoMagic requires the native app to stream the file to the browser...
        /// </summary>
        /// <param name="gPalFileToUpload"></param>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector LeftClickAndUpload(GPALFile gPalFileToUpload)
        {
            HWND windowOpened = IntPtr.Zero;

            if (true == useEAvars)
            {
                UploadWorkflow(EAelement, EAselector, false);   // the element passed in gets the first file
                return this;
            }

            // A selector that finds one element uploads every file to it; with more than one element, each
            // element gets the first file.
            int UploadWorkflow(GPALElement webElement, Selector selector, bool uploadAllFiles)
            {
                string fileToUpload = null;

                // Check .multiple before the loop; the first SendKeys makes the element stale. Read the property
                // with JavaScript: GetAttribute capitalises the name and misses the driver's special case for this
                // valueless boolean attribute.
                bool sendAllAtOnce = true == UseSelenium
                                     && 1 < gPalFileToUpload.Count
                                     && true == Convert.ToBoolean(BrowserHelper.ExecuteJavaScript(this, "return arguments[0].multiple;", webElement.WebElement) ?? false);

                for (int fileCount = 0; fileCount < gPalFileToUpload.Count; fileCount++)
                {
                    fileToUpload = gPalFileToUpload.Filenames[fileCount];

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Uploading [{fileToUpload}]", this, GPALObjectType.Browser);

                    // hardware input types into the file dialog; otherwise the file is set on the input directly
                    bool hardwareAsked = true == BrowserSettings.UseHardware || InteractionType.Hardware == selector.InteractionType;

                    // headless opens no file dialog for hardware to type into, so the file is set on the input directly
                    if (false == hardwareAsked || true == BrowserSettings.UseHeadless)
                    {
                        if (true == hardwareAsked)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Headless has no file dialog, so [{fileToUpload}] is set on the input directly", this, GPALObjectType.Browser);

                        if (true == string.IsNullOrEmpty(fileToUpload))
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, "For some reason we have an empty filename", gPalFileToUpload, GPALObjectType.Other);

                        // Puppeteer and OttoMagic set the file on the input; nothing checks that the upload finished
                        if (true == BrowserSettings.UsePuppeteer)
                        {
                            if (true == uploadAllFiles)
                            {
                                PuppeteerClient.LeftClickAndUpload(webElement.ElementHandle).UploadFrom(gPalFileToUpload).Execute();
                                break;
                            }
                            PuppeteerClient.LeftClickAndUpload(webElement.ElementHandle).UploadFrom(fileToUpload).Execute();
                        }
                        else if (true == BrowserSettings.UseOttoMagic)
                            MagicHelper.LeftClickAndUpload(webElement.Css, fileToUpload);
                        else   // selenium
                        {
                            // chromedriver adds a file per SendKeys; geckodriver throws on the second. Both accept all
                            // paths newline-separated in one SendKeys.
                            string filesToSend = true == sendAllAtOnce ? string.Join("\n", gPalFileToUpload.Filenames) : fileToUpload;

                            try
                            {
                                ((IWebElement)webElement.iWebElement).SendKeys(filesToSend);   // SendKeys on an input[type=file] adds the path as a file
                            }
                            catch    // stale element
                            {
                                ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool _, out List<GPALElement> matchedElements);
                                webElement = 0 < matchedElements.Count ? matchedElements[0] : null;
                                webElement?.SendKeys(filesToSend.Replace("\\", "/"));
                            }

                            if (true == sendAllAtOnce)
                                break;
                        }

                        Thread.Sleep(250);
                    }
                    else    // hardware asked for, with a window to type into
                    {
                        ElementHelper.Click(this, selector, webElement, ClickType.LeftClick, ModifierKeys.NONE);    // click the input[type=file] button

                        windowOpened = WaitForCommonDialog(BrowserSettings.WaitForWindowTimeoutInSeconds);

                        // the file dialog opened: type the filename(s) and press Enter
                        if (IntPtr.Zero != windowOpened)
                        {
                            int filesToSend = 1;

                            if (true == webElement.GetAttribute("multiple")?.ToLower().Equals("true"))
                                filesToSend = gPalFileToUpload.Count;

                            if (true == uploadAllFiles)
                                for (int idx = 0; idx < gPalFileToUpload.Count; idx++)
                                {
                                    fileToUpload = gPalFileToUpload.Filenames[idx];
                                    HardwareHelper.SendString($@"""{fileToUpload}"" ");
                                }
                            else
                                HardwareHelper.SendString($@"""{fileToUpload}"" ");

                            HardwareHelper.SendKey(GPAL.VK_RETURN);

                            // send Enter again if the file dialog is still open
                            windowOpened = WaitForCommonDialog(1);

                            if (IntPtr.Zero != windowOpened)
                                HardwareHelper.SendKey(GPAL.VK_RETURN);

                            if (true == uploadAllFiles)
                                break;  // we send all the filenames, so long as they are not too long
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to handle Open File dialog [{fileToUpload}][{selector.Name}] not upoloaded.", BrowserSettings, GPALObjectType.Other);
                            return -1;
                        }
                    }

                    //if (false == BrowserSettings.UseHeadless && BrowserType.Edge == BrowserSettings.BrowserType)    // dismiss the 'downloads' popup which can steal keys, clicks or get in the way
                    //{
                    //    // KLUDGE: a hardware top clicks the browser chrome, which dismisses the 'downloads' popup
                    //    bool tmpTop = GPAL.SeleniumTopsBrowser;
                    //    GPAL.WithSeleniumToppingBrowser(false);
                    //    BrowserHelper.TopBrowser(this);
                    //    GPAL.WithSeleniumToppingBrowser(tmpTop);
                    //}

                    // stop after the WithAllThatMatch count of files
                    if (int.MaxValue != CurrentUOW.WithAllThatMatch && fileCount == CurrentUOW.WithAllThatMatch && false == uploadAllFiles)
                        return -1;
                    else if (false == uploadAllFiles)
                        return 0;
                }

                return -1;
            }

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    ReadOnlyCollection<GPALElement> webelements = ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                    if (null != webelements)
                    {
                        foreach (GPALElement webElement in matchedElements)
                        {
                            // -1 ends the uploads: a failure, all files sent, or the WithAllThatMatch count reached
                            if (-1 == UploadWorkflow(webElement, selector, 1 == matchedElements.Count))
                                break;
                        }
                    }
                }
            }

            GetSetCurrentUrl();

            return this;
        }

        /// <summary>
        /// Right click the element(s) found for Selector(s) defined in the UOW, select 'Save as...', wait for the SAVE AS dialog, enter the filename and save the file.<br/>
        /// <b>The saved filename is then added to the end of the filename list.</b><br/>
        /// Useful if files are not overwritten.<br/>
        /// One right click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a right click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <param name="filenames">GPALFile the downloaded file is saved to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RightClickAndDownload(GPALFile filenames)
        {
            string fullFilePathToSave;
            HWND windowOpened = IntPtr.Zero;
            List<string> returnFilename = new List<string>();
            FileDownloaded = false;

            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    ReadOnlyCollection<GPALElement> webelements = ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                    if (null != webelements)
                    {
                        foreach (GPALElement webElement in matchedElements)
                        {
                            int rowCount = 0;

                            fullFilePathToSave = FileHelper.CleanUpFileDestination(filenames.Next, out string newFilename, BrowserSettings.DeleteFileBeforeDownload);
                            returnFilename.Add(fullFilePathToSave);  // return the filenames generated

                            // each matched element of each selector is right clicked and downloaded
                            // CAVEAT: this is untested with multiple selector elements

                            // KLUDGE: Selenium cannot open a context menu, so a Selenium selector right clicks through JavaScript and other engines use hardware when headful; the interaction type is put back after the click
                            Enums.InteractionType interactionType = selector.InteractionType;
                            if (Enums.InteractionType.Selenium == selector.InteractionType && "GPALElement" != webElement.TagName)
                                selector.InteractionType = InteractionType.JavaScript;
                            else if ("GPALElement" != webElement.TagName && false == BrowserSettings.UseHeadless)
                                selector.InteractionType = InteractionType.Hardware;

                            if (false == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware))    // if we are not headless, we will handle the dialog, otherwise set using selenium and click
                            {
                                bool noSaveAsDialog = false;
                                string currentUrl = null, currentUrl2 = null;

                                if (true == BrowserSettings.UseOttoMagic)
                                    currentUrl = MagicHelper.GetCurrentUrl();
                                else if (true == BrowserSettings.UsePuppeteer)
                                    currentUrl = PuppeteerClient.GetCurrentUrl().Execute();
                                else
                                    currentUrl = BrowserDriver.Url;

                                ElementHelper.Click(this, selector, webElement, ClickType.RightClick, ModifierKeys.NONE);

                                // Edge under driver control resets its context menu to the top while it settles, so wait 5 seconds
                                // and press Home before moving down the menu
                                Thread.Sleep(5000);

                                // Down to Save As: three presses on Chrome, four on Firefox and Edge, then Enter. One press too
                                // many copies the link instead.
                                HardwareHelper.SendKey(GPAL.VK_HOME);   // home key press is first element
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);

                                if (BrowserType.FireFox == BrowserSettings.BrowserType || BrowserType.Edge == BrowserSettings.BrowserType)
                                    HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_RETURN);

                                // put back the selector's interaction type
                                selector.InteractionType = interactionType;

                                windowOpened = WaitForCommonDialog(BrowserSettings.WaitForWindowTimeoutInSeconds);

                                // Firefox titles: "Enter name of file to save to" for a link, "Save As" for the
                                // page. The dialog is found by class, so any language works; on an English system a
                                // "Save As" title means the wrong menu item was clicked, so warn.
                                if (IntPtr.Zero != windowOpened
                                    && BrowserType.FireFox == BrowserSettings.BrowserType
                                    && "Save As" == FormHelper.TitleOf(windowOpened))
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"GPAL thinks a misclick happened and the wrong item is saved. Please verify [{fullFilePathToSave}].", this, GPALObjectType.Browser);

                                if (IntPtr.Zero != windowOpened)
                                {
                                    HardwareHelper.SendString(fullFilePathToSave);
                                    HardwareHelper.SendKey(GPAL.VK_RETURN);

                                    // send Enter again if the Save As dialog is still open
                                    windowOpened = WaitForCommonDialog(5);

                                    if (IntPtr.Zero != windowOpened)
                                        HardwareHelper.SendKey(GPAL.VK_RETURN);

                                    // wait for file to download
                                    string actualDownloadedFilename2 = null;

                                    actualDownloadedFilename2 = FileHelper.WaitForDownloadToFinish(fullFilePathToSave, BrowserSettings.DownloadTimeoutInSec);

                                    FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename2);

                                    if (BrowserType.Edge == BrowserSettings.BrowserType)    // dismiss the 'downloads' popup which can steal keys, clicks or get in the way
                                    {
                                        // KLUDGE: a hardware top clicks the browser chrome, which dismisses the 'downloads' popup
                                        BrowserHelper.TopBrowser(this, false, true);
                                    }
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to handle Save As... dialog [{fullFilePathToSave}][{selector.Name}]", BrowserSettings, GPALObjectType.Other);
                                    noSaveAsDialog = true;

                                    // the click may have opened the pdf in the browser; read the url to test it
                                    if (true == BrowserSettings.UseOttoMagic)
                                        currentUrl2 = MagicHelper.GetCurrentUrl();
                                    else if (true == BrowserSettings.UsePuppeteer)
                                        currentUrl2 = PuppeteerClient.GetCurrentUrl().Execute();
                                    else
                                        currentUrl2 = BrowserDriver.Url;

                                    // test the url for a pdf and download it
                                    if (false == BrowserSettings.OpenPDFExternally && true == fullFilePathToSave.EndsWith(".pdf"))
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Testing [{currentUrl2}] for PDF file", this, GPALObjectType.Browser);
                                        if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2) && true == BrowserHelper.TestUrlForPDF(this, currentUrl2))
                                        {
                                            FileDownloaded = BrowserHelper.DownloadPdfFile(currentUrl2, fullFilePathToSave, this);
                                            noSaveAsDialog = !(bool)FileDownloaded;
                                        }
                                    }
                                }

                                // No Save As dialog within WaitForWindowTimeoutInSeconds (30 by default): the site may have
                                // downloaded straight to the browser's download directory, so look there for a file with the
                                // GPALFile's extension
                                if (true == noSaveAsDialog)
                                {
                                    string downloadDirectory = FileHelper.GetDefaultDownloadDirectory(this);
                                    string downloadUrl = FileHelper.GetDownloadUrl(webElement);
                                    string filename = Path.GetFileName(downloadUrl) ?? fullFilePathToSave;

                                    string wildcardFilename = "*" + Path.GetExtension(filename);
                                    string systemDownloadedTo = Path.Combine(downloadDirectory, wildcardFilename);
                                    string actualDownloadedFilename2 = null;

                                    actualDownloadedFilename2 = FileHelper.WaitForDownloadToFinish(systemDownloadedTo, BrowserSettings.DownloadTimeoutInSec);

                                    FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename2);

                                    if (true == FileDownloaded)
                                    {
                                        if (false == fullFilePathToSave.Equals(actualDownloadedFilename2))
                                        {
                                            FileHelper.EnsureFolderFor(fullFilePathToSave);
                                            File.Move(actualDownloadedFilename2, fullFilePathToSave);
                                            if (false == actualDownloadedFilename2.Equals(fullFilePathToSave) && true == File.Exists(actualDownloadedFilename2))
                                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloaded file [{actualDownloadedFilename2}] unable to move to [{fullFilePathToSave}]", null, GPALObjectType.None);
                                            else
                                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{actualDownloadedFilename2}] moved to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Replaces right click and Save As: download over the browser's connection, from the
                                // page first, then in a tab. FileDownloaded reflects the result.
                                string rightClickUrl = FileHelper.GetDownloadUrl(webElement);

                                if (true == string.IsNullOrWhiteSpace(rightClickUrl))
                                {
                                    FileDownloaded = false;
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{webElement?.TagName}] carries no url to download, so there is nothing to save", this, GPALObjectType.Browser);
                                }
                                else
                                {
                                    FileDownloaded = FileHelper.FetchOrDownloadInTab(rightClickUrl, fullFilePathToSave, this);

                                    if (false == FileDownloaded)
                                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{rightClickUrl}] did not download to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                                }
                            }

                            // The interaction was switched for the right click only. Put it back on every path, or the
                            // selector's next action on this or another browser runs with it.
                            selector.InteractionType = interactionType;
                            /* selenium navigate the context menu, untested
                            else
                            {
                                SetDownloadFilename(fullFilePath);

                                // Create an Actions object
                                Actions actions = new Actions(browserSettings.BrowserDriver);
                                actions.SendKeys(Keys.Home);

                                // Simulate pressing the down arrow key multiple times
                                actions.SendKeys(Keys.ArrowDown);
                                actions.SendKeys(Keys.ArrowDown);
                                actions.SendKeys(Keys.ArrowDown);

                                // If using Firefox, simulate pressing the down arrow key one more time to get to FF command
                                if (BrowserType.FireFox == BrowserSettings.BrowserType)
                                    actions.SendKeys(Keys.ArrowDown);

                                // Simulate pressing the return key
                                actions.SendKeys(Keys.Enter);

                                // Simulate typing the filename
                                if (BrowserType.FireFox == browserSettings.BrowserType || BrowserType.Edge == browserSettings.BrowserType)
                                    actions.SendKeys(Keys.ArrowDown);

                                // Simulate pressing the return key
                                actions.SendKeys(Keys.Enter).Build().Perform();
                            }
                            */
                            
                            // stop after the WithAllThatMatch count of elements
                            if (int.MaxValue != CurrentUOW.WithAllThatMatch && ++rowCount == CurrentUOW.WithAllThatMatch)
                                break;
                        }
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to detect Save As open window.", this, GPALObjectType.Browser);
                    }
                }

                foreach (string filepath in returnFilename)
                {
                    filenames.ReturnFilenames.Add(filepath); // return the filenames we generated

                    // Log "saved" only if the file exists; otherwise warn.
                    if (true == File.Exists(filepath))
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File saved to [{filepath}]", this, GPALObjectType.Browser);
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Nothing was saved to [{filepath}]", this, GPALObjectType.Browser);
                }
            }

            return this;
        }
    }
}
