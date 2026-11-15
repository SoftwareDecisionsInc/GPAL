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
        /// <summary>
        /// Sets the page orientation (Portrait/Landscape) used by <see cref="PrintToPDF"/>.
        /// </summary>
        /// <param name="pageOrientation">Desired page orientation</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithPageOrientation(PageOrientation pageOrientation)
        {
            BrowserSettings.PageOrientation = pageOrientation;
            return this;
        }
        // GPAL.Browser.GoTo("ebay.com").WithSelector(selectorToPrint).PrintToPDF("filename.pdf"); // print element and contents
        // GPAL.Browser.GoTo("ebay.com").WithSelector(first).WithSelector(second).PrintToPDF("filename.pdf"); // one pdf each
        // GPAL.Browser.GoTo("ebay.com").PrintToPDF("filename.pdf"); // print whole page
        /// <summary>
        /// Prints each selector in the unit of work to its own PDF, or the whole page with no selector.<br/>
        /// An action: honors .WaitFor and ends the unit of work.<br/>
        /// File names come from <see cref="GPALFile.Next"/>: your names in order, then names built from the last.
        /// Written files are in ReturnFilenames.
        /// </summary>
        /// <param name="gpalFile">GPALFile to save the PDFs to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector PrintToPDF(GPALFile gpalFile)
        {
            List<string> selectorPaths = new List<string>();
            bool named = false;

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                    if (SelectorType.Selector == selector.SelectorType   // we don't look up data literals
                        && 0 < (selector.SelectorPaths?.Count ?? 0)
                        && false == string.IsNullOrEmpty(selector.SelectorPaths[0].SelectorPath))
                    {
                        named = true;

                        // Printing needs a css path. For an image selector, use the css of the element under the
                        // image; for XPath, the css of the matched element.
                        string css = SelectorPathType.Image == selector.SelectorPaths[0].SelectorPathType
                            ? ElementHelper.CssUnderImage(this, selector)
                            : SelectorPathType.Xpath == selector.SelectorPaths[0].SelectorPathType
                                ? ElementHelper.CssForXPath(this, selector)
                                : selector.SelectorPaths[0].SelectorPath;

                        if (null != css)
                            selectorPaths.Add(css);
                    }

                // No selector: print the whole page. A selector whose element was not found prints nothing.
                if (false == named)
                    selectorPaths.Add("html");

                foreach (string selectorPath in selectorPaths)
                    PrintOneToPDF(selectorPath, gpalFile.Next);
            }

            // No GetSetCurrentUrl: a separate browser renders the pdf, so this one stays put.

            return this;
        }
        /// <summary>
        /// Prints one element, named by its css path, to one PDF file.
        /// </summary>
        /// <param name="selectorPath">Css path of the element to print, "html" for the whole page</param>
        /// <param name="outputFile">The file to write, already named by .Next</param>
        private void PrintOneToPDF(string selectorPath, GPALFile outputFile)
        {
            string fullHtml = null;

            if (true == BrowserSettings.UseOttoMagic)
                fullHtml = MagicHelper.GetContentAndCss(selectorPath);
            else
            {
                // ExecuteJavaScript, not ExecuteJavaScriptObj. These scripts use a top level return, which Puppeteer
                // rejects; ExecuteJavaScript retries inside a function, while ExecuteJavaScriptObj returns null.
                string elementContent = BrowserHelper.ExecuteJavaScript(BrowserSettings.Browser,
                    $@"var div = document.querySelector('{selectorPath}'); return div.outerHTML;");

                // Inject the div content & CSS used
                string cssContent = BrowserHelper.ExecuteJavaScript(BrowserSettings.Browser, $@"
                function getAllCss() {{
                        var css = '';
                        var stylesheets = document.styleSheets;
                        for (var i = 0; i < stylesheets.length; i++) {{
                            try {{
                                var rules = stylesheets[i].cssRules;
                                for (var j = 0; j < rules.length; j++) {{
                                    css += rules[j].cssText + ' ';
                                }}
                            }} catch (e) {{ /* Ignore cross-origin stylesheet errors */ }}
                        }}
                        return css;
                    }}

                    function getElementCss(element) {{
                        var css = '';
                        var style = window.getComputedStyle(element);
                        for (var i = 0; i < style.length; i++) {{
                            css += style[i] + ': ' + style.getPropertyValue(style[i]) + '; ';
                        }}
                        return css;
                    }}

                    var allCss = getAllCss();
                    var div = document.querySelector('{selectorPath}');
                    var elementCss = getElementCss(div);

                    return '<style>' + allCss + ' ' + elementCss + '</style>';
                ");
                fullHtml = $@"<html><head>{cssContent}</head><body>{elementContent}</body></html>";
            }

            // The page cannot read cross-domain (CDN) stylesheet rules, so add a <link> for each stylesheet for the
            // printing browser to load, and a <base> of the page url for relative urls. On OttoMagic this needs Allow
            // user scripts; on failure nothing is added. One expression, since a top level return is a syntax error
            // on OttoMagic and Puppeteer; only Selenium gets "return ".
            string linksScript = @"(function () {
                var head = '<base href=""' + location.href.replace(/""/g, '&quot;') + '"">';
                var links = document.querySelectorAll('link[rel~=""stylesheet""][href]');
                for (var i = 0; i < links.length; i++)
                    head += '<link rel=""stylesheet"" href=""' + links[i].href.replace(/""/g, '&quot;') + '"">';
                return head;
            })()";
            string pageLinks = BrowserHelper.ExecuteJavaScript(this, true == UseSelenium ? "return " + linksScript : linksScript);

            if (true == pageLinks?.StartsWith("<base"))
            {
                int head = fullHtml?.IndexOf("<head>", StringComparison.OrdinalIgnoreCase) ?? -1;

                fullHtml = 0 <= head
                    ? fullHtml.Insert(head + "<head>".Length, pageLinks)
                    : pageLinks + fullHtml;
            }

#if DONOTUSECDP
        #region NEW get the browser to print to pdf without CDP
            // Step 1: the page settings go in the html. chrome's command line has --print-to-pdf and
            // --no-pdf-header-footer and nothing for orientation, margins or backgrounds, so @page and
            // print-color-adjust carry them. a style before <html> is moved into the head by the parser
            string pageCss = "<style>@page { size: "
                + (PageOrientation.Landscape == BrowserSettings.PageOrientation ? "landscape" : "portrait")
                + "; margin: 0; } html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }</style>";
            string outputHtmlPath = $"{outputFile.Filename}.html";

            // Step 2: Save the HTML to a file
            FileHelper.EnsureFolderFor(outputHtmlPath);
            File.WriteAllText(outputHtmlPath, pageCss + fullHtml);

            // Step 3: a headless chromium of its own prints the file and exits. it gets a new temp profile because
            // a launch on the workflow's profile is handed to the browser already running there as a new tab,
            // which never prints. firefox has no print to pdf on its command line, so a firefox workflow prints
            // with chrome when it is installed, otherwise edge
            string printerPath = null;
            if (BrowserType.Chrome == BrowserSettings.BrowserType)
                printerPath = Puppeteer.GetChromePath();
            else if (BrowserType.Edge == BrowserSettings.BrowserType)
                printerPath = Puppeteer.GetEdgePath();
            else
            {
                printerPath = Puppeteer.GetChromePath();
                if (string.IsNullOrEmpty(printerPath) || false == File.Exists(printerPath))
                    printerPath = Puppeteer.GetEdgePath();
            }

            if (string.IsNullOrEmpty(printerPath) || false == File.Exists(printerPath))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"PrintToPDF needs Chrome or Edge installed, neither was found to print [{outputFile.Filename}]",
                    BrowserSettings.Browser, GPALObjectType.Browser);
            }
            else
            {
                string printerProfile = ChromeProfileManager.NewTempProfilePath();
                string fileUrl = $"file:///{Path.GetFullPath(outputHtmlPath).Replace("\\", "/")}";
                string printerArgs = "--headless --disable-gpu --no-first-run --no-default-browser-check"
                    + $" --user-data-dir=\"{printerProfile}\""
                    + " --no-pdf-header-footer"
                    + $" --print-to-pdf=\"{Path.GetFullPath(outputFile.Filename)}\""
                    + $" \"{fileUrl}\"";

                System.Diagnostics.Process pdfPrinter = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(printerPath, printerArgs)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                // Wait up to 30 seconds for the printing browser to exit
                bool exitedCleanly = pdfPrinter.WaitForExit(30000);

                if (!exitedCleanly)
                {
                    try
                    {
                        if (!pdfPrinter.HasExited)
                            pdfPrinter.Kill();
                    }
                    catch { }

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        "PrintToPDF process did not exit cleanly (timeout)",
                        BrowserSettings.Browser, GPALObjectType.Browser);
                }
                else if (pdfPrinter.ExitCode != 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"PrintToPDF exited with code [{pdfPrinter.ExitCode}]",
                        BrowserSettings.Browser, GPALObjectType.Browser);
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Printed [{selectorPath}] to [{outputFile.Filename}]", BrowserSettings.Browser, GPALObjectType.Browser);

                ChromeProfileManager.RemoveTempUserProfile(printerProfile);
            }

            // Step 4: Clean up temporary files
            try
            {
                File.Delete(outputHtmlPath);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log warning if file deletion fails (e.g., file in use)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to delete temporary files", BrowserSettings.Browser, GPALObjectType.Browser, ex);
            }
        #endregion NEW get the browser to print to pdf without CDP
#else
    #region OLD use CDP to page.PrintToPDF a window
    BrowserHelper.ExecuteJavaScript(@"
        var newWindow = window.open('', '_blank');
        newWindow.document.open();
        newWindow.document.write('<html><head>' + arguments[0] + '</head><body>' + arguments[1] + '</body></html>');
        newWindow.document.close();
        newWindow.focus();
    ", browserSettings.Browser, new { cssContent, elementContent });

    // Wait for the new window to load
    System.Threading.Thread.Sleep(2000);

    // Switch to the new window
    var windows = browserSettings.BrowserDriver.WindowHandles;
    browserSettings.BrowserDriver.SwitchTo().Window(windows[windows.Count - 1]);

    // Define print settings
    var printSettings = new Dictionary<string, object>
    {
        { "printBackground", true },
        { "landscape", false }
    };

    // Execute the print command
    dynamic result = null;

    if (BrowserType.Chrome == browserSettings.BrowserType)
        result = BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.printToPDF", printSettings);
    else if (BrowserType.Edge == browserSettings.BrowserType)
        result = BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.printToPDF", printSettings);
    else
        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] does not support Page.printToPDF", browserSettings.Browser, GPALObjectType.Browser);

    var resultDict = result as IDictionary<string, object>;

    if (resultDict != null && resultDict.ContainsKey("data"))
    {
        // Decode the PDF data
        var pdfData = Convert.FromBase64String(resultDict["data"].ToString());
        // Save the PDF to a file
        FileHelper.EnsureFolderFor(outputFile.Filename);
        System.IO.File.WriteAllBytes(outputFile.Filename, pdfData);
    }

    browserSettings.BrowserDriver.Close();
    windows = browserSettings.BrowserDriver.WindowHandles;
    browserSettings.BrowserDriver.SwitchTo().Window(windows[windows.Count - 1]);
    #endregion OLD use CDP to page.PrintToPDF a window
#endif
        }
    }
}
