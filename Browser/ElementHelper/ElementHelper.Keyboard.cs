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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Threading;
//using DocumentFormat.OpenXml.Bibliography;
//using DocumentFormat.OpenXml.Drawing;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Keys = OpenQA.Selenium.Keys;
using MatchType = GenerallyPositive.Enums.MatchType;

namespace GenerallyPositive.Browser
{
    internal partial class ElementHelper
    {
        /// <summary>
        /// Fill in text on an element using the technique defined in the selector
        /// do this for the entire unit of work
        /// </summary>
        /// <param name="browser">Browser whose CurrentUOW selectors are filled in</param>
        /// <param name="useText">One string of text to write</param>
        /// <param name="writeMode">Whether to overwrite, append to or insert before the current value</param>
        /// <param name="element">Element to fill in instead of the elements the selectors find; null to use the selectors</param>
        public static bool FillInFrom(Browser browser, string useText, WriteMode writeMode, GPALElement element = null)
        {
            bool retVal = false;    // true once anything is filled in; SendString tries this before sending to <body>
            ReadOnlyCollection<GPALElement> elems = null;
            List<GPALElement> matchedElements = null;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                if (null == element)
                    elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != element)
                {
                    var tmpElems = new List<GPALElement>();
                    tmpElems.Add(element);
                    elems = new ReadOnlyCollection<GPALElement>(tmpElems);
                }
                else if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                if (true == elems.Any())
                    handleFillIn(elems);

                if (true == retVal && null != browser.CurrentUOW.CallAfterFillIn && true == elems.Any())
                {
                    UnitOfWork safeUOW = browser.CurrentUOW;
                    CallIfStatus handled = 0;

                    // Only Terminate from CallAfterFillIn is acted on; Handled and NotHandled are treated the same
                    // call CallAfterFillIn, if defined and pass in the tokens we are currently using (which is just a string in this method)
                    IGPALGrid<string> tokens = GPAL.GridForType<string>();
                    tokens.AddRow(new List<string> { useText });

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallAfterFillIn [{browser.CurrentUOW.CallAfterFillIn.Method.Name}]", browser, GPALObjectType.Browser);
                    handled = browser.CurrentUOW.CallAfterFillIn(browser, tokens, 1);

                    // KLUDGE: the CallAfterFillIn handler can set a new current unit of work, so this method's unit of work is restored
                    browser.CurrentUOW = safeUOW;

                    // CAVEAT: CallAfterFillIn handler actions can leave the found elements stale; the check below is commented out
                    // test if elements still valid by testing the first one
                    //try
                    //{
                    //    ((UnitOfWork.ElementNode)(browser.CurrentUOW.ElementGrid[0])[0]).GPALElement.GetAttribute("tag");
                    //}
                    //catch   // failed, invalidate elementgrid
                    //{
                    //    browser.CurrentUOW.ElementGrid.Clear();
                    //}

                    if (CallIfStatus.Terminate == handled)
                    {
                        string str = $"CallAfterFillIn handler [{browser.CurrentUOW.AppCallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException($"" + str);
                    }
                }

                void handleFillIn(ReadOnlyCollection<GPALElement> elems)
                {
                   int rowCount = 0;

                   foreach (GPALElement elem in elems)
                   {
                        string type = elem.GetAttribute("type");
                        string abbreviatedText = useText[0] + "..." + useText[useText.Length - 1];

                        if (null == type || false == type.Equals("password"))
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] text [{abbreviatedText}] in element [{elem.TagName}][{selector.Name}]", browser, GPALObjectType.Browser);
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] XXXXXX in element [{elem.TagName}][{selector.Name}]", browser, GPALObjectType.Browser);

                        if (null != elem)
                        {
                            retVal = true;

                            // Use the hardware mouse and keyboard when hardware is on for the browser, or
                            // for an image match with no element whose selector asked for hardware. Never headless or
                            // on a hidden desktop.
                            if (false == browser.BrowserSettings.UseHeadless && false == browser.BrowserSettings.HiddenDesktop
                                && (("GPALElement" == elem.TagName && InteractionType.Hardware == selector.InteractionType)
                                    || true == browser.BrowserSettings.UseHardware))
                                HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                            else if ("GPALElement" == elem.TagName)
                                // image match with no element under it - focus the coordinate and type
                                FillInFrom(browser, ToViewportPoint(browser, elem, selector), useText, writeMode);
                            else if (browser.UseOttoMagic)
                            {
                                if (false == browser.BrowserSettings.UseHeadless && InteractionType.Hardware == selector.InteractionType)
                                    ElementHelper.HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                                else
                                    switch (writeMode)
                                    {
                                        case WriteMode.Append:
                                            browser.MagicHelper.FillInAppend(elem.Css, useText);
                                            break;
                                        case WriteMode.Insert:
                                            browser.MagicHelper.FillInInsert(elem.Css, useText);
                                            break;
                                        case WriteMode.Overwrite:
                                            browser.MagicHelper.FillInOverwrite(elem.Css, useText);
                                            break;
                                    }

                                // browser.MagicHelper.FireChangeEvent(elem.Css);

                                if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                                    break;
                            }
                            else if (true == browser.UsePuppeteer)
                            {
                                if (false == browser.BrowserSettings.UseHeadless && InteractionType.Hardware == selector.InteractionType)
                                    ElementHelper.HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                                else
                                    switch (writeMode)
                                    {
                                        case WriteMode.Append:
                                            browser.PuppeteerClient.FillInAppend(elem).WithText(useText).Execute();
                                            break;
                                        case WriteMode.Insert:
                                            browser.PuppeteerClient.FillInInsert(elem).WithText(useText).Execute();
                                            break;
                                        case WriteMode.Overwrite:
                                            browser.PuppeteerClient.FillInOverwrite(elem).WithText(useText).Execute();
                                            break;
                                    }

                                // browser.PuppeteerClient.FireChangeEvent(elem.ElementHandle).Execute();

                                if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                                    break;
                            }
                            else if (true == browser.UseSelenium)
                            {
                                if (false == browser.BrowserSettings.UseHeadless && InteractionType.Hardware == selector.InteractionType)
                                    ElementHelper.HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                                else
                                    SeleniumFillInFrom(browser, elem, useText, writeMode);
                            }
                        }
                    }
                }
            }

            return retVal;
        }
        /// <summary>
        /// Maps a virtual keycode to a Selenium Keys constant (or character) and sends it to <paramref name="element"/>
        /// via Selenium's SendKeys.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to send the key to</param>
        /// <param name="keyCode">Virtual keycode to send (e.g. GPAL.VK_RETURN)</param>
        public static void SeleniumSendKeys(Browser browser, GPALElement element, byte keyCode)
        {
                // Map the byte keycode to Selenium Keys constants
                string keyToSend;

                switch (keyCode)
                {
                    case 0x12: // VK_ALT
                        keyToSend = Keys.Alt;
                        break;
                    case 0x2e: // VK_DELETE
                        keyToSend = Keys.Delete;
                        break;
                    case 0x79: // VK_F10
                        keyToSend = Keys.F10;
                        break;
                    case 0x21: // VK_PRIOR
                        keyToSend = Keys.PageUp;
                        break;
                    case 0x22: // VK_NEXT
                        keyToSend = Keys.PageDown;
                        break;
                    case 0x23: // VK_END
                        keyToSend = Keys.End;
                        break;
                    case 0x24: // VK_HOME
                        keyToSend = Keys.Home;
                        break;
                    case 0x28: // VK_DOWN
                        keyToSend = Keys.ArrowDown;
                        break;
                    case 0x09: // VK_TAB
                        keyToSend = Keys.Tab;
                        break;
                    case 0x0D: // VK_RETURN
                        keyToSend = Keys.Return;
                        break;
                    case 0x27: // VK_SHIFT_RIGHT
                        keyToSend = Keys.Shift;
                        break;
                    case 0xA0: // VK_SHIFT_LEFT
                        keyToSend = Keys.LeftShift;
                        break;
                    case 0xA2: // VK_CONTROL_LEFT
                        keyToSend = Keys.LeftControl;
                        break;
                    case 0xA3: // VK_CONTROL_RIGHT
                        keyToSend = Keys.Control;
                        break;
                    case 0x20: // VK_SPACE
                        keyToSend = Keys.Space;
                        break;
                    case 0x08: // VK_BACK
                        keyToSend = Keys.Backspace;
                        break;
                    default:
                        // If it's a character key, use fromCharCode method for alphanumeric keys
                        keyToSend = ((char)keyCode).ToString();
                        break;
                }

                // Find the element to send keys to
                element.SendKeys(keyToSend);
        }
        /// <summary>
        /// Fills in <paramref name="elem"/> using Selenium SendKeys, handling Overwrite/Append/Insert write modes
        /// and special-cased month/week/datetime-local input types. Falls back to <see cref="JavaScriptFillInFrom"/>
        /// if Selenium throws and fallback recovery is enabled.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="elem">Element to fill in</param>
        /// <param name="textToUse">Text to enter</param>
        /// <param name="writeMode">Overwrite, Append or Insert</param>
        public static void SeleniumFillInFrom(Browser browser, GPALElement elem, string textToUse, WriteMode writeMode)
        {
            // the ISO value goes to .value untouched - GetKeystrokeValueForInput exists to reshape it for typing
            if (true == valueOnlyInputTypes.Contains(elem.Type))
            {
                JavaScriptFillInFrom(browser, elem, textToUse, writeMode);
                return;
            }

            textToUse = ElementHelper.GetKeystrokeValueForInput(elem, textToUse);

            try
            {
                if (WriteMode.Overwrite == writeMode)
                    elem.Clear();
                else if (WriteMode.Append == writeMode)
                {
                    textToUse = elem.Text + textToUse;
                    elem.Clear();
                }

                elem.SendKeys(textToUse);   // fast path - whole string at once
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (false == GPAL.NoFallbackRecoveryActions)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to fill in text. Falling back on javascript.", browser, GPALObjectType.Browser, ex);
                    JavaScriptFillInFrom(browser, elem, textToUse, writeMode);
                }
            }

        }


        /// <summary>
        /// Fill in text on an element using hardware emulation. The element is clicked on first to bring it in focus using offset x and y.
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="webElement"></param>
        /// <param name="offsetX"></param>
        /// <param name="offsetY"></param>
        /// <param name="text"></param>
        /// <param name="writeMode"></param>
        public static void HardwareFillInFrom(Browser browser, GPALElement webElement, int offsetX, int offsetY, string text, WriteMode writeMode)
        {
            string inputType = webElement.Type?.ToLowerInvariant() ?? "";

            // Special handling for native picker inputs
            bool isSpecialInput = new[] { "date", "time", "datetime-local", "month", "week", "color" }
                                  .Contains(inputType);

            HardwareFocus(browser, webElement, offsetX, offsetY);

            // If focusing the element could not be trusted (off-screen/odd coords, so no real hardware click
            // happened), do NOT type. Focus is likely still on whatever it was before - e.g. the browser's
            // address bar - and blasting keystrokes there is how a search term ends up in the URL bar.
            if (true == _lastPlacementQuestionable)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Skipping fill-in [{browser.CurrentUOW?.CurrentSelector?.Name}], questionable element placement.",
                    browser, GPALObjectType.Browser);
                return;
            }

            if (isSpecialInput)
            {
                // No Ctrl+A on Firefox: in a date or time widget it selects the whole page. Firefox overwrites
                // segments as typed anyway.
                if ("week" != inputType && BrowserType.FireFox != browser.BrowserSettings.BrowserType)
                {
                    // Aggressive clear for special inputs (they can be picky)
                    HardwareHelper.SendChar('a', ModifierKeys.Control, true);
                    Thread.Sleep(120);
                    HardwareHelper.SendKey(GPAL.VK_DELETE);
                    Thread.Sleep(80);
                }
            }
            else if (WriteMode.Overwrite == writeMode)
            {
                HardwareHelper.SendChar('a', ModifierKeys.Control, true);
                Thread.Sleep(150);
                //HardwareHelper.SendKey(GPAL.VK_DELETE);
            }
            else if (WriteMode.Append == writeMode)
            {
                HardwareHelper.SendKey(GPAL.VK_END, ModifierKeys.Control);
                Thread.Sleep(150);
            }
            else if (WriteMode.Insert == writeMode)
            {
                HardwareHelper.SendKey(GPAL.VK_HOME, ModifierKeys.Control);
                Thread.Sleep(150);
            }

            string textToType = GetKeystrokeValueForInput(webElement, text);
            switch (inputType)
            {
                case "color":
                    // the picker takes the six hex digits, not the css value, and anything else falls back to black
                    HardwareFillInColor(textToType.TrimStart('#'), browser.BrowserSettings.BrowserType);
                    break;
                case "datetime-local":
                    HardwareFillInDateTimeLocal(textToType, browser.BrowserSettings.BrowserType);
                    break;
                case "week":
                    HardwareFillInWeek(textToType);
                    break;
                case "month":
                    HardwareFillInMonth(textToType);
                    break;
                default:
                    HardwareHelper.SendString(textToType);
                    break;
            }
        }

        /// <summary>
        /// Fill in the week in the format expected by the control
        /// </summary>
        /// <param name="isoValue"></param>
        public static void HardwareFillInMonth(string isoValue)
        {
            var parts = isoValue.Split('/');
            string year = parts[0];
            string month = parts[1];
            HardwareHelper.SendString(month);   // "12"
            // TAB to move to year field
            HardwareHelper.SendKey(GPAL.VK_TAB);
            HardwareHelper.SendString(year);   // "2026"
        }
        
        /// <summary>
        /// Fill in the week in the format expected by the control
        /// </summary>
        /// <param name="isoValue"></param>
        public static void HardwareFillInWeek(string isoValue)
        {
            if (isoValue.Contains("-W"))
            {
                var parts = isoValue.Split('-');
                string year = parts[0];
                string week = parts[1].TrimStart('W');
                HardwareHelper.SendString($"{week}{year}");   // "31 2026"
            }
        }

        /// <summary>
        /// Type in the datetime in the format expected by the control
        /// </summary>
        /// <param name="isoValue"></param>
        /// <param name="browserType">Browser type; Firefox skips the tab between the date and the time</param>
        public static void HardwareFillInDateTimeLocal(string isoValue, BrowserType browserType)
        {
            // The value comes from GetKeystrokeValueForInput as "06/18/2026 18:32" and is typed as "06/18/2026 06:32 PM"
            if (!DateTime.TryParseExact(isoValue,
                                        "MM/dd/yyyy HH:mm",
                                        null,
                                        System.Globalization.DateTimeStyles.None,
                                        out DateTime dt))
            {
                dt = DateTime.Now; // fallback
            }

            string datePart = dt.ToString("MM/dd/yyyy");           // US date
            string timePart = dt.ToString("hh:mm");   // 06:32
            string amPm = dt.ToString("tt");

            // Type date
            HardwareHelper.SendString(datePart);

            // firefox advances to the hour on its own once the year is typed, so tabbing here moves off the hour
            // and the time lands a segment late. the chromium widget still wants the tab
            if (BrowserType.FireFox != browserType)
            {
                // TAB to move to time field
                HardwareHelper.SendKey(GPAL.VK_TAB);
                Thread.Sleep(120);
            }

            // Type time in 12-hour format
            HardwareHelper.SendString(timePart);
            Thread.Sleep(120);

            // am/pm
            HardwareHelper.SendString(amPm);
        }

        /// <summary>
        /// CLicking on the color picker shows a popup with a slide and boxes to enter RGB, 3 tabs to get to R, then one tab each
        /// </summary>
        /// <param name="hexValue"></param>
        /// <param name="browserType">Browser type, which sets how many tabs reach the first RGB field (7 on Firefox, 3 otherwise)</param>
        public static void HardwareFillInColor(string hexValue, BrowserType browserType)
        {
            if (hexValue.Length != 6)
            {
                // anything but six hex digits types black
                hexValue = "000000";
            }

            // Convert RRGGBB -> R, G, B decimals
            int r = Convert.ToInt32(hexValue.Substring(0, 2), 16);
            int g = Convert.ToInt32(hexValue.Substring(2, 2), 16);
            int b = Convert.ToInt32(hexValue.Substring(4, 2), 16);

            // Navigate into the RGB fields. Three tabs reaches the first one in the chromium picker. Firefox lists
            // hue, saturation and luminance ahead of red, green and blue, so it takes four more to clear them
            int tabsToFirstField = BrowserType.FireFox == browserType ? 7 : 3;

            for (int tab = 0; tab < tabsToFirstField; tab++)
                HardwareHelper.SendKey(GPAL.VK_TAB);

            // Enter R
            HardwareHelper.SendString(r.ToString());
            HardwareHelper.SendKey(GPAL.VK_TAB);

            // Enter G
            HardwareHelper.SendString(g.ToString());
            HardwareHelper.SendKey(GPAL.VK_TAB);

            // Enter B
            HardwareHelper.SendString(b.ToString());

            // Enter confirms and closes the picker
            //HardwareHelper.SendKey(GPAL.VK_TAB);
            HardwareHelper.SendKey(GPAL.VK_RETURN);
        }
        /// <summary>
        /// get a human typeable us local string to type into the special controls. color requires tabbing to get to the component values
        /// </summary>
        /// <param name="webElement"></param>
        /// <param name="isoValue"></param>
        /// <returns></returns>
        public static string GetKeystrokeValueForInput(GPALElement webElement, string isoValue)
        {
            if (string.IsNullOrEmpty(isoValue) || webElement?.TagName?.ToLowerInvariant() != "input")
                return isoValue;

            string type = webElement.Type?.ToLowerInvariant() ?? "";

            try
            {
                switch (type)
                {
                    case "date":
                        // ISO: 2026-06-18 US typing: 06/18/2026
                        if (DateTime.TryParseExact(isoValue, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                            return dt.ToString("MM/dd/yyyy");
                        return isoValue;

                    case "datetime-local":
                        // ISO: 2026-06-18T18:32 US: 06/18/2026 18:32
                        if (DateTime.TryParseExact(isoValue.Replace("T", " "), "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out dt))
                            return dt.ToString("MM/dd/yyyy HH:mm");
                        return isoValue.Replace("T", " ");

                    case "month":
                        // ISO: 2026-12 typed as 2026/12, which HardwareFillInMonth splits into month and year
                        return isoValue.Replace("-", "/");

                    case "week":
                        // ISO: 2026-W31 stays as is; HardwareFillInWeek types it as week then year
                        return isoValue;

                    case "time":
                        // The value is 24 hour HH:mm; the widget takes 12 hour plus AM/PM, so convert to "hh:mm tt".
                        if (DateTime.TryParseExact(isoValue, new[] { "HH:mm", "H:mm", "HH:mm:ss" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
                            return dt.ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);
                        return isoValue;

                    default:
                        return isoValue;
                }
            }
            catch
            {
                return isoValue;  // fallback
            }
        }

        /// <summary>
        /// Fill in text on an element (by setting value) using javascript inserted into the browser
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to fill in</param>
        /// <param name="text">Text to write</param>
        /// <param name="writeMode">Whether to overwrite, append to or insert before the current value</param>
        public static void JavaScriptFillInFrom(Browser browser, GPALElement webElement, string text, WriteMode writeMode)
        {
            IWebDriver browserDriver = browser.BrowserDriver;

            BrowserHelper.ExecuteJavaScript(browser, "arguments[0].focus();", webElement.WebElement);

            // Set .value, not the value attribute. The attribute is only the default; once the field has been typed
            // in, the browser submits the property.
            if (WriteMode.Overwrite == writeMode)
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1]", webElement.WebElement, text);
            else if (WriteMode.Append == writeMode)
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[0].value + arguments[1]", webElement.WebElement, text);
            else if (WriteMode.Insert == writeMode)
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1] + arguments[0].value", webElement.WebElement, text);

            // Fire input and change as typing does, so pages that track the value themselves (React and similar)
            // see it.
            BrowserHelper.ExecuteJavaScript(browser,
                "arguments[0].dispatchEvent(new Event('input', { bubbles: true })); arguments[0].dispatchEvent(new Event('change', { bubbles: true }));",
                webElement.WebElement);
        }
        /// <summary>
        /// Fill in text on an element (by setting value) using javascript inserted into the browser
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to fill in</param>
        /// <param name="text">Text to write</param>
        /// <param name="writeMode">Whether to overwrite, append to or insert before the current value</param>
        public static async void PuppeteerFillInFrom(Browser browser, GPALElement webElement, string text, WriteMode writeMode)
        {
            BrowserSettings browserSettings = browser.BrowserSettings;

            PuppeteerCommunicator puppeteerCommunicator = browserSettings.PuppeteerCommunicator;

            // https://stackoverflow.com/questions/11337353/correct-way-to-focus-an-element-in-selenium-webdriver-using-java
            // puppeteerCommunicator.Focus(webElement.Css);
            switch (writeMode)
            {
                case WriteMode.Append:
                    browser.PuppeteerClient.FillInAppend(webElement).WithText(text).Execute();
                    break;
                case WriteMode.Insert:
                    browser.PuppeteerClient.FillInInsert(webElement).WithText(text).Execute();
                    break;
                case WriteMode.Overwrite:
                    browser.PuppeteerClient.FillInOverwrite(webElement).WithText(text).Execute();
                    break;
            }
            VkCodeToDomKeyConverter.TryConvertAsciiToVkCode('\t', out byte vkCode);
            VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(vkCode, out string key, out string code);
            await puppeteerCommunicator.SendKey(key, code, vkCode).ConfigureAwait(false);   // tab out so the page fires a change event
        }

        /// <summary>
        /// Sends a single key press to the currently focused element using injected JavaScript, dispatching
        /// keydown/keypress/input/keyup events and inserting the character into editable elements directly.
        /// </summary>
        /// <param name="browser">Browser to send the key in</param>
        /// <param name="webElement">Unused; the script acts on document.activeElement</param>
        /// <param name="keycode">Virtual keycode to send (e.g. GPAL.VK_RETURN)</param>
        public static void JavaScriptSendKey(Browser browser, GPALElement webElement, byte keycode)
        {
            IWebDriver browserDriver = browser.BrowserDriver;
            if (true == VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(keycode, out string key, out string code))
            {
                string sendKeyScript = @"
                    function isElementEditable(target) {
                        if (!target || !(target instanceof Element)) return false;
                        if (target.disabled || target.readOnly) return false;

                        const tag = target.tagName.toUpperCase();
                        if (tag === 'TEXTAREA') return true;
                        if (tag === 'SELECT') return true;
                        if (tag === 'INPUT') {
                            const editableTypes = new Set(['text','email','password','search','number','url','tel',
                                                         'date','datetime-local','month','time','week','color']);
                            return editableTypes.has(target.type.toLowerCase());
                        }
                        if (target.isContentEditable === true || 
                            target.getAttribute('contenteditable') === 'true' ||
                            target.ownerDocument?.designMode === 'on') {
                            return true;
                        }
                        const style = window.getComputedStyle(target);
                        if (style.userModify === 'read-write' || style.webkitUserModify === 'read-write' || 
                            style.cursor === 'text') {
                            return true;
                        }
                        return false;
                    }

                    // === Parameters passed from C# ===
                    const key = arguments[0];           // e.g. 'a', 'Enter', 'Tab'
                    const code = arguments[1];          // e.g. 'KeyA', 'Enter'
                    const keyCode = arguments[2];       // numeric keyCode if needed (mostly legacy)

                    let target = document.activeElement;
                    if (!target || target === document.body) {
                        target = document;  // fallback
                    }

                    const eventInit = {
                        key: key,
                        code: code,
                        keyCode: keyCode,
                        bubbles: true,
                        cancelable: true,
                        composed: true
                    };

                    try {
                        // 1. Key events (for listeners)
                        target.dispatchEvent(new KeyboardEvent('keydown', eventInit));

                        if (key.length === 1) {  // printable char
                            target.dispatchEvent(new KeyboardEvent('keypress', eventInit));
                        }

                        // 2. Actually insert text (this is what most sites need)
                        const isEditable = isElementEditable(target);
                        if (isEditable) {
                            if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') {
                                const start = target.selectionStart ?? target.value.length;
                                const end = target.selectionEnd ?? target.value.length;
                                target.value = target.value.slice(0, start) + key + target.value.slice(end);
                                target.selectionStart = target.selectionEnd = start + key.length;
                            } 
                            else if (target.isContentEditable) {
                                // Simpler fallback for contenteditable
                                document.execCommand('insertText', false, key);  // still works in many places
                            }

                            // Fire input event (React, Vue, Angular, etc. listen to this)
                            target.dispatchEvent(new InputEvent('input', {
                                bubbles: true,
                                cancelable: true,
                                data: key,
                                inputType: 'insertText',
                                composed: true
                            }));

                            // Also fire change for some frameworks
                            target.dispatchEvent(new Event('change', { bubbles: true }));
                        }

                        // 3. Keyup
                        target.dispatchEvent(new KeyboardEvent('keyup', eventInit));

                        // Special case: Enter on button/submit
                        if (key === 'Enter' && (target.tagName === 'BUTTON' || 
                            (target.tagName === 'INPUT' && (target.type === 'submit' || target.type === 'button')))) {
                            target.click();
                        }

                    } catch (e) {
                        console.error('SendKey script error:', e);
                    }
                ";

                BrowserHelper.ExecuteJavaScript(browser, sendKeyScript, key, code, keycode.ToString());
            }
        }
        /// <summary>
        /// Brings each element matched by the current unit of work's selectors into focus, consistently across
        /// engines: editable elements (text inputs, textareas, contenteditable - see
        /// <see cref="IsElementEditable"/>) are left-clicked, which both focuses them and places the caret,
        /// readying them for typing. Non-editable elements are focused without clicking, to avoid side effects
        /// (navigation, submit, toggle, etc.) from activating them.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void Focus(Browser browser)
        {
            List<GPALElement> matchedElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == matchedElements)
                {
                    PublishToEventHandler(browser, selector, matchedElements?.ToList());
                    continue;
                }

                int rowCount = 0;

                foreach (GPALElement elem in matchedElements)
                {
                    if (null != elem)
                    {
                        if (false == browser.BrowserSettings.UseHeadless && (true == browser.BrowserSettings.UseHardware))
                        {
                            HardwareFocus(browser, elem, selector.OffsetX, selector.OffsetY);
                        }
                        else if (true == IsElementEditable(elem))
                        {
                            // click to focus and place the caret, readying the element for typing
                            Click(browser, selector, elem, ClickType.LeftClick, ModifierKeys.NONE);
                        }
                        else if (true == browser.UseOttoMagic)
                        {
                            browser.MagicHelper.Focus(elem.Css);
                        }
                        else if (true == browser.UsePuppeteer)
                        {
                            browser.PuppeteerClient.Focus(elem.Css).Execute();
                        }
                        else if (true == browser.UseSelenium)
                        {
                            if (Enums.InteractionType.JavaScript == selector.InteractionType || true == browser.BrowserSettings.UseJavaScript) // selenium
                            {
                                JavaScriptFocus(browser, elem);
                            }
                            else if (elem.TagName.ToLower().Equals("input"))
                            {
                                elem.SendKeys(OpenQA.Selenium.Keys.Shift);
                                elem.SendKeys("");
                            }
                            else
                            {
                                try
                                {
                                    new Actions(browser.BrowserDriver).MoveToElement((IWebElement)elem.WebElement).Perform();
                                }
                                catch (GPALException)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    // With recovery on, fall back to JavaScript scrolling and publish a WARNING.
                                    // With recovery off, publish the Selenium exception as an EXCEPTION.
                                    if (false == GPAL.NoFallbackRecoveryActions)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium threw on selector [{selector.Name}], falling back to javascript.", browser, GPALObjectType.Browser);
                                        ElementHelper.ScrollIntoView(browser, elem);
                                    }
                                    else
                                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Selenium threw on selector [{selector.Name}], and recovery actions are off, so nothing was done about it.", browser, GPALObjectType.Browser, ex);
                                }
                            }
                        }

                        if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Forces focus onto <paramref name="webElement"/> using injected JavaScript, temporarily enabling it
        /// and giving it a tabindex if needed so it can receive focus.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to focus</param>
        private static void JavaScriptFocus(Browser browser, GPALElement webElement)
        {
            IWebDriver browserDriver = browser.BrowserDriver;

            string forceFocusScript = $@"
                function forceFocus(el) {{
                    // Remember original state
                    const wasDisabled = el.disabled;
                    const originalTabIndex = el.getAttribute('tabindex');

                    try {{
                        // 1. Enable if disabled
                        if (wasDisabled) el.disabled = false;

                        // 2. Make sure it can receive focus
                        if (el.tabIndex === -1 || !el.hasAttribute('tabindex')) {{
                            el.setAttribute('tabindex', '0');
                        }}

                        // 3. Focus it
                        el.focus({{ preventScroll: true }});

                        // 4. Small delay + aggressive fallback (helps with buttons, React, Shadow DOM, etc.)
                        setTimeout(() => {{
                            if (document.activeElement !== el) {{
                                el.focus();

                                // Dispatch synthetic focus events
                                el.dispatchEvent(new FocusEvent('focusin', {{ bubbles: true }}));
                                el.dispatchEvent(new FocusEvent('focus', {{ bubbles: false }}));
                            }}
                        }}, 0);

                        // Final check
                        return document.activeElement === el;

                    }} catch (err) {{
                        return false;
                    }} finally {{
                        // Restore original state
                        if (wasDisabled) el.disabled = true;
                        if (originalTabIndex !== null) {{
                            el.setAttribute('tabindex', originalTabIndex);
                        }} else if (el.getAttribute('tabindex') === '0') {{
                            el.removeAttribute('tabindex');
                        }}
                    }}
                }}
                forceFocus(arguments[0]);
            ";
            BrowserHelper.ExecuteJavaScript(browser, forceFocusScript, webElement.WebElement); // focus on element
        }
        /// <summary>
        /// Sets the value of every element matched by the current unit of work's selectors from the value of
        /// the (first) element matched by <paramref name="srcSelector"/>.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="srcSelector">Selector identifying the source element to read the value from</param>
        public static void SetValueFromElement(Browser browser, Selector srcSelector)
        {
            List<GPALElement> matchedElements;

            ReadOnlyCollection<GPALElement> srcElems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, srcSelector, out bool srcMatchedAll, out List<GPALElement> srcMatchedElements);

            if (null != srcMatchedElements)
                srcElems = new ReadOnlyCollection<GPALElement>(srcMatchedElements);

            if (null == srcElems || 0 == srcElems.Count)
                return;

            GPALElement srcElem = srcElems[0];

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                foreach (GPALElement elem in elems)
                    SetValueFromElement(browser, elem, srcElem);
            }
        }
        /// <summary>
        /// Sets the value of a single destination element from a single source element, using OttoMagic,
        /// Puppeteer or JavaScript depending on the active engine. Fires "input" and "change" events on the
        /// destination element so the page reacts to the new value.
        /// </summary>
        /// <param name="browser">Browser the elements belong to</param>
        /// <param name="element">Destination element whose value is set</param>
        /// <param name="srcElement">Source element whose value is read</param>
        public static void SetValueFromElement(Browser browser, GPALElement element, GPALElement srcElement)
        {
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.SetValueFromElement(srcElement.Css, element.Css);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.WithElementId(srcElement.Css).SetValueFrom(element.Css).Execute();
            else
                BrowserHelper.ExecuteJavaScript(browser,
                    "arguments[0].value = arguments[1].value; arguments[0].dispatchEvent(new Event('input', {bubbles: true})); arguments[0].dispatchEvent(new Event('change', {bubbles: true}));",
                    element.WebElement, srcElement.WebElement);
        }
        /// <summary>
        /// Types into an image match with no DOM handle, by clicking the coordinate to focus whatever is there and
        /// then sending the text to the focused element. Used when there is no hardware keyboard path available,
        /// which in practice means headless.
        /// </summary>
        /// <param name="browser">Browser to type in</param>
        /// <param name="point">Viewport coordinate to focus</param>
        /// <param name="text">Text to enter</param>
        /// <param name="writeMode">Append, Insert or Overwrite</param>
        private static void FillInFrom(Browser browser, Point point, string text, WriteMode writeMode)
        {
            // No element to set a value on, so click the point to focus it, then type.
            Click(browser, point, ClickType.LeftClick, ModifierKeys.NONE);

            if (true == browser.UsePuppeteer)
            {
                if (WriteMode.Overwrite == writeMode)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "[Overwrite] cannot clear an image match by coordinate, the text will be added to whatever is already there.", browser, GPALObjectType.Browser);

                browser.PuppeteerClient.SendString(text).Execute();
            }
            else if (true == browser.UseSelenium)
            {
                Actions actions = new Actions(browser.BrowserDriver);

                if (WriteMode.Overwrite == writeMode)
                    actions.KeyDown(Keys.Control).SendKeys("a").KeyUp(Keys.Control);

                actions.SendKeys(text).Perform();
            }
            else
            {
                // OttoMagic has no call to type into the focused element, so write the text with execute-javascript
                // (needs Allow user scripts on). It follows shadow roots to the focused element, uses the native
                // value setter so frameworks see the change, and fires input and change.
                string answer = RunPageScript(browser, $@"
                    (function (text, mode) {{
                        var el = document.activeElement;

                        while (el && el.shadowRoot && el.shadowRoot.activeElement)
                            el = el.shadowRoot.activeElement;

                        if (!el || el === document.body)
                            return 'nothing has focus';

                        if (el.isContentEditable) {{
                            if ('Overwrite' === mode)
                                document.execCommand('selectAll', false, null);
                            else if ('Append' === mode) {{
                                var range = document.createRange();
                                range.selectNodeContents(el);
                                range.collapse(false);
                                var selection = window.getSelection();
                                selection.removeAllRanges();
                                selection.addRange(range);
                            }}

                            document.execCommand('insertText', false, text);
                            return 'ok';
                        }}

                        var descriptor = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value');

                        if (!descriptor || !descriptor.set)
                            return 'the focused ' + el.tagName.toLowerCase() + ' takes no text';

                        var current = el.value || '';
                        var next = 'Overwrite' === mode ? text : current + text;

                        if ('Insert' === mode && typeof el.selectionStart === 'number' && null !== el.selectionStart)
                            next = current.slice(0, el.selectionStart) + text + current.slice(el.selectionEnd);

                        descriptor.set.call(el, next);
                        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                        el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                        return 'ok';
                    }})({Newtonsoft.Json.JsonConvert.SerializeObject(text)}, {Newtonsoft.Json.JsonConvert.SerializeObject(writeMode.ToString())})");

                if ("ok" != answer)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot fill in the image match by coordinate. OttoMagic focused it with a trusted click, and could not write into it [{answer ?? "no answer"}].", browser, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Focuses <paramref name="webElement"/> using hardware emulation: clicks editable elements to focus
        /// them, otherwise just moves the mouse onto the element.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to focus</param>
        /// <param name="OffsetX">Horizontal pixel offset within the element</param>
        /// <param name="OffsetY">Vertical pixel offset within the element</param>
        private static void HardwareFocus(Browser browser, GPALElement webElement, int OffsetX, int OffsetY)
        {
            // Scroll the element on screen here, not in every caller. Below the fold its screen Y passes
            // HardwareClick's coordinate check and the click hits whatever is there.
            ScrollIntoView(browser, webElement, Enums.InteractionType.Hardware);

            // IsElementEditable only counts the text-ish inputs, and the picker inputs take typing just as much.
            // without the click they never get focus, and the Ctrl+A that follows selects the whole document
            bool isPickerInput = valueOnlyInputTypes.Contains(webElement.Type?.ToLowerInvariant() ?? "");

            if (true == ElementHelper.IsElementEditable(webElement) || true == isPickerInput)
            {
                // Click to focus text inputs, textareas, contenteditable and picker inputs
                HardwareClick(browser, webElement, ClickType.LeftClick, ModifierKeys.NONE, browser.CurrentUOW.CurrentSelector);
            }
            else
            {
                // Only move the mouse, so menus, toggles, links, submits and file pickers are not activated
                HardwareMoveTo(browser, webElement, browser.CurrentUOW.CurrentSelector);
            }
        }
        /// <summary>
        /// Types at the cursor of the focused element, following shadow roots and same-origin frames down to it.
        /// arguments[0] is the text, arguments[1] true sends keydown, keypress and keyup around it, as one key. A
        /// page that cancels the keydown gets no character. execCommand inserts the way typing does; a date or time
        /// input it refuses gets the text through the native value setter. Only text inputs, text areas and
        /// editable elements take text; with nothing taking it, only the keys go out.
        /// </summary>
        private const string TYPE_INTO_FOCUSED = @"
            var text = arguments[0].replace(/\r\n/g, '\n');
            var asKeys = arguments[1];
            var textTypes = ['text', 'search', 'email', 'password', 'tel', 'url', 'number',
                'date', 'datetime-local', 'month', 'time', 'week'];
            var el = document.activeElement;

            while (el && ((el.shadowRoot && el.shadowRoot.activeElement) || ('IFRAME' === el.tagName && el.contentDocument)))
                el = el.shadowRoot && el.shadowRoot.activeElement ? el.shadowRoot.activeElement : el.contentDocument.activeElement;

            var keyTarget = el ? (el !== el.ownerDocument.body ? el : el.ownerDocument) : document;
            var isField = !!el && ('TEXTAREA' === el.tagName || ('INPUT' === el.tagName && -1 !== textTypes.indexOf(el.type)));
            var editable = !!el && !el.disabled && !el.readOnly && (isField || true === el.isContentEditable);

            function insert(chunk) {
                var inserted = el.ownerDocument.execCommand('insertText', false, chunk);

                if (!inserted && isField) {
                    var view = el.ownerDocument.defaultView;
                    var proto = 'TEXTAREA' === el.tagName ? view.HTMLTextAreaElement.prototype : view.HTMLInputElement.prototype;
                    var setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
                    var current = el.value || '';
                    var start = current.length;
                    var end = current.length;

                    try {
                        if ('number' === typeof el.selectionStart) {
                            start = el.selectionStart;
                            end = el.selectionEnd;
                        }
                    } catch (e) { }

                    setter.call(el, current.slice(0, start) + chunk + current.slice(end));
                    el.dispatchEvent(new InputEvent('input', { bubbles: true, composed: true, inputType: 'insertText', data: chunk }));
                }
            }

            function typeOne(c) {
                var init = { key: '\n' === c ? 'Enter' : ('\t' === c ? 'Tab' : c), bubbles: true, cancelable: true, composed: true };

                if (keyTarget.dispatchEvent(new KeyboardEvent('keydown', init))) {
                    keyTarget.dispatchEvent(new KeyboardEvent('keypress', init));
                    if (editable)
                        insert(c);
                }

                keyTarget.dispatchEvent(new KeyboardEvent('keyup', init));
            }

            if (asKeys || !editable) {
                for (var c of text)
                    typeOne(c);
            }
            else
                insert(text);";
        /// <summary>
        /// The text as one string per key: a line break as one key whether written \r\n or \n, and an emoji's
        /// surrogate pair kept together.
        /// </summary>
        /// <param name="text">The text to type</param>
        /// <returns>One string per key</returns>
        private static List<string> KeysOf(string text)
        {
            List<string> retVal = new List<string>();
            string normalized = text.Replace("\r\n", "\n");

            for (int i = 0; i < normalized.Length; i++)
                if (true == char.IsSurrogatePair(normalized, i))
                {
                    retVal.Add(normalized.Substring(i, 2));
                    i++;
                }
                else
                    retVal.Add(normalized[i].ToString());

            return retVal;
        }
        /// <summary>
        /// Sends a string of characters to the browser using hardware emulation, OttoMagic, Puppeteer, or
        /// Selenium. Every engine types at the cursor of the focused element.<br/>
        /// Inter-character pacing is controlled by <see cref="GPAL.TypingDelay"/>: 0 sends the whole string
        /// atomically (per-engine fast path), greater than 0 dispatches character-by-character on every engine,
        /// with a randomized delay between Math.Min(25, TypingDelay) and TypingDelay milliseconds before each
        /// character - not a fixed pause, since a constant gap between every keystroke is itself a detectable
        /// signature.
        /// </summary>
        /// <param name="browser">Browser to send the string to</param>
        /// <param name="textToSend">Text to send</param>
        public static void SendString(Browser browser, string textToSend)
        {
            int delayMs = GPAL.TypingDelay;

            if (true == browser.BrowserSettings.UseHardware)
                HardwareHelper.SendString(textToSend, delayMs);
            else if (true == browser.BrowserSettings.UseOttoMagic)
                browser.MagicHelper.SendString(textToSend, delayMs);
            else if (true == browser.BrowserSettings.UsePuppeteer)
                browser.PuppeteerClient.SendString(textToSend, delayMs).Execute();
            else if (InteractionType.JavaScript == browser.CurrentUOW.CurrentSelector?.InteractionType || true == browser.BrowserSettings.UseJavaScript)
            {
                // the whole string in one write, or one key at a time when there is a typing delay
                if (0 == delayMs)
                    BrowserHelper.ExecuteJavaScriptObj(TYPE_INTO_FOCUSED, browser, textToSend, false);
                else
                {
                    bool first = true;
                    foreach (string key in KeysOf(textToSend))
                    {
                        if (false == first)
                            Thread.Sleep(HardwareHelper.GetTypingDelay(delayMs));
                        BrowserHelper.ExecuteJavaScriptObj(TYPE_INTO_FOCUSED, browser, key, true);
                        first = false;
                    }
                }
            }
            else if (BrowserType.FireFox == browser.BrowserSettings.BrowserType)
            {
                // geckodriver waits on every step of an Actions chain, so Firefox types through the focused element instead, one round trip per key.
                // Focus is read again for each key, because a Tab, an Enter or the page itself can move it between keys
                if (0 == delayMs)
                    browser.BrowserSettings.BrowserDriver.SwitchTo().ActiveElement().SendKeys(textToSend.Replace("\r\n", "\n"));
                else
                {
                    bool first = true;
                    foreach (string key in KeysOf(textToSend))
                    {
                        if (false == first)
                            Thread.Sleep(HardwareHelper.GetTypingDelay(delayMs));
                        browser.BrowserSettings.BrowserDriver.SwitchTo().ActiveElement().SendKeys(key);
                        first = false;
                    }
                }
            }
            else
            {
                // keys sent with no element go to whatever has focus
                Actions actions = new Actions(browser.BrowserSettings.BrowserDriver);

                if (0 == delayMs)
                    actions.SendKeys(textToSend.Replace("\r\n", "\n"));
                else
                {
                    bool first = true;
                    foreach (string key in KeysOf(textToSend))
                    {
                        if (false == first)
                            actions.Pause(TimeSpan.FromMilliseconds(HardwareHelper.GetTypingDelay(delayMs)));
                        actions.SendKeys(key);
                        first = false;
                    }
                }

                actions.Perform();
            }
        }
        /// <summary>
        /// Sends a single virtual keycode using hardware emulation, OttoMagic, Puppeteer, or JavaScript/Selenium,
        /// depending on browser settings. With hardware emulation, VK_RETURN on a non-editable matched element
        /// clicks it instead of sending Enter.
        /// </summary>
        /// <param name="browser">Browser to send the key to</param>
        /// <param name="VKCode">Virtual keycode to send (e.g. GPAL.VK_RETURN)</param>
        public static void SendKey(Browser browser, byte VKCode)
        {
            if (true == browser.BrowserSettings.UseHardware)
            {
                if (GPAL.VK_RETURN == VKCode)
                {
                    var matched = browser.CurrentUOW.CurrentSelector?.WebSelectorMatchedResults?.FirstOrDefault();
                    if (false == ElementHelper.IsElementEditable(matched))
                    {
                        matched.Click();
                        return;
                    }
                }

                HardwareHelper.SendKey(VKCode);
            }
            else if (true == browser.BrowserSettings.UseOttoMagic)
                browser.MagicHelper.SendKey(VKCode);
            else if (true == browser.BrowserSettings.UsePuppeteer)
                browser.PuppeteerClient.SendKey(VKCode).Execute();
            else
            {
                {
                    foreach (SelectorSettings.SelectorPathEntry selectorPathEntry in browser.CurrentUOW.CurrentSelector?.SelectorPaths)
                        if (null != selectorPathEntry.WebSelectorFoundResults)
                            foreach (GPALElement webElement in selectorPathEntry.WebSelectorFoundResults)
                                if (InteractionType.JavaScript == browser.CurrentUOW.CurrentSelector?.InteractionType)
                                    ElementHelper.JavaScriptSendKey(browser, webElement, VKCode);
                                else
                                    ElementHelper.SeleniumSendKeys(browser, webElement, VKCode);
                }
            }
        }
    }
}
