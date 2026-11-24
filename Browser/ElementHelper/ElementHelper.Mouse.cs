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
        /// Moves the mouse to each element matched by the current unit of work's selectors, using hardware
        /// mouse movement, or OttoMagic/Puppeteer/Selenium native mouse moves. Each element is scrolled into view
        /// first. These are all real input-layer
        /// events, so the browser naturally updates CSS <c>:hover</c> state and fires
        /// mouseenter/mouseover/mouseleave - this is what reveals hover-triggered menus and tooltips, so there is
        /// no separate hover operation (see <see cref="Browser.Hover"/>, a fluent alias for this method).<br/>
        /// As a last resort, when no real cursor can be moved (e.g. plain Selenium in headless mode, or
        /// <see cref="Enums.InteractionType.JavaScript"/>), a synthetic "mouseover" event is dispatched via
        /// <see cref="JavaScriptHover"/> - this only fires JS mouseover listeners and does NOT trigger CSS
        /// <c>:hover</c>.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void MoveTo(Browser browser)
        {
            List<GPALElement> matchedElements;
            UnitOfWork currentUOW = browser.CurrentUOW;

            foreach (Selector selector in currentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, currentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                int rowCount = 0;
                int total = Math.Min(elems.Count, browser.CurrentUOW.WithAllThatMatch);
                object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

                foreach (GPALElement found in elems)
                {
                    if (null == found)
                        continue;

                    // Element from an image match: hover at the matched point, as a click does. OttoMagic cannot move
                    // to a point, so it hovers the element.
                    GPALElement elem = null != found.ImageMatch && false == browser.UseOttoMagic ? found.ImageMatch : found;

                    ScrollIntoView(browser, elem);

                    if (0 == elem.Size.Width && 0 == elem.Size.Height)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Element has Zero Width and Height, skipping.", browser, GPALObjectType.Browser);
                        continue;
                    }

                    // TagName "GPALElement" here is an image match with no element, only a point. Use the hardware
                    // mouse (not headless, not on a hidden desktop) if hardware is on for the browser or on the
                    // selector (.WithHardware, as DragAndDrop does), or for such a match on OttoMagic, which cannot
                    // move to a point.
                    if (false == browser.BrowserSettings.UseHeadless && false == browser.BrowserSettings.HiddenDesktop
                        && (("GPALElement" == elem.TagName && true == browser.UseOttoMagic)
                            || true == browser.BrowserSettings.UseHardware
                            || InteractionType.Hardware == selector.InteractionType))
                    {
                        HardwareMoveTo(browser, elem, selector);
                    }
                    else if ("GPALElement" == elem.TagName)
                    {
                        // Image match with no element and no hardware mouse: move the Puppeteer or Selenium mouse to
                        // the point.
                        MoveTo(browser, elem, selector);
                    }
                    else if (true == browser.UseOttoMagic || true == browser.UsePuppeteer)
                    {
                        MoveTo(elem, selector.OffsetX, selector.OffsetY, browser);
                    }
                    else if (Enums.InteractionType.JavaScript == selector.InteractionType || true == browser.BrowserSettings.UseJavaScript || true == browser.BrowserSettings.UseHeadless)
                    {
                        if (null != currentUOW.ShadowRoot)
                            JavaScriptHover(browser, currentUOW.ShadowRoot);
                        else
                            JavaScriptHover(browser, elem);
                    }
                    else // selenium with a real cursor
                    {
                        MoveTo(elem, selector.OffsetX, selector.OffsetY, browser);
                    }

                    if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                        break;
                }
            }
        }

        /// <summary>
        /// Checks, after each element in a found list, whether a click or hover loaded a new page. If so the remaining
        /// elements belong to the old page, so the loop stops and a warning is published.
        /// </summary>
        /// <param name="browser">Browser the list was found in</param>
        /// <param name="pageMark">Page held before the first element, from BrowserHelper.MarkDocument, null for a list of one</param>
        /// <param name="done">How many elements have been acted on</param>
        /// <param name="total">How many elements the loop means to act on</param>
        /// <returns>true when the page was replaced and elements remain</returns>
        private static bool PageReplaced(Browser browser, object pageMark, int done, int total)
        {
            bool replaced = null != pageMark && done < total && true == BrowserHelper.DocumentReplaced(browser, pageMark);

            if (true == replaced)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The page changed after [{done}] of [{total}] element(s), the rest were left alone", browser, GPALObjectType.Browser);

            return replaced;
        }

        /// <summary>
        /// Moves the Puppeteer or Selenium mouse to the point of an image match with no element, when there is no
        /// hardware mouse (in practice headless). Both send real input, so CSS :hover updates, unlike a JavaScript
        /// mouseover. OttoMagic cannot move to a point and only warns.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Image-matched surrogate to move to</param>
        /// <param name="selector">Selector providing the grab offset</param>
        private static void MoveTo(Browser browser, GPALElement element, Selector selector)
        {
            Point point = ToViewportPoint(browser, element, selector);

            if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.MoveTo(point).Execute();
            else if (true == browser.UseSelenium)
                new Actions(browser.BrowserDriver).MoveToLocation(point.X, point.Y).Perform();
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot hover the image match for selector [{selector.Name}] - OttoMagic has no coordinate mouse move and there is no hardware mouse available.", browser, GPALObjectType.Browser);
        }

        /// <summary>
        /// Clicks the element an image match resolved to instead of the point: when something covers the point, and
        /// on Firefox with OttoMagic, which cannot click a point. The event goes to the element, so a cover cannot
        /// take it. Selenium and Puppeteer use a JavaScript click, since their native clicks are refused as
        /// intercepted or hit the cover.
        /// </summary>
        /// <param name="browser">Browser to click in</param>
        /// <param name="element">The element the image match resolved to</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        private static void ClickElementItself(Browser browser, GPALElement element, ClickType clickType, ModifierKeys modifierKeys)
        {
            if (true == browser.UseOttoMagic)
            {
                if (ModifierKeys.NONE != modifierKeys)
                    browser.MagicHelper.PressModifierKey(modifierKeys);

                switch (clickType)
                {
                    case ClickType.LeftDoubleClick:
                        browser.MagicHelper.LeftDoubleClick(element.Css);
                        break;
                    case ClickType.MiddleClick:
                        browser.MagicHelper.MiddleClick(element.Css);
                        break;
                    case ClickType.RightClick:
                        browser.MagicHelper.RightClick(element.Css);
                        break;
                    default:
                        browser.MagicHelper.LeftClick(element.Css);
                        break;
                }

                if (ModifierKeys.NONE != modifierKeys)
                    browser.MagicHelper.ReleaseModifierKey(modifierKeys);
            }
            else
                JavaScriptClick(browser, element, clickType, modifierKeys);
        }

        /// <summary>
        /// The viewport point to click for an image match: the middle unless the selector has an offset. A screen
        /// capture match has the content area origin subtracted. Assumes 100% scaling.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Image-matched surrogate</param>
        /// <param name="selector">Selector providing the offset</param>
        /// <returns>The point in viewport coordinates</returns>
        private static Point ToViewportPoint(Browser browser, GPALElement element, Selector selector)
        {
            // Both offsets zero means aim at the middle, as HardwareMoveTo does.
            Point aim = (0 == selector.OffsetX && 0 == selector.OffsetY)
                ? ImageCenterOffset(element.Size)
                : new Point(selector.OffsetX, selector.OffsetY);

            int pointX = element.Location.X + aim.X;
            int pointY = element.Location.Y + aim.Y;

            if (CoordinateSpace.Screen == element.CoordinateSpace)
            {
                Rectangle content = GetWindowRectangle(browser);
                pointX -= content.X;
                pointY -= content.Y;
            }

            return new Point(pointX, pointY);
        }
        /// <summary>
        /// Clicks a viewport point for an image match with no element, when there is no hardware mouse (headless).
        /// </summary>
        /// <param name="browser">Browser to click in</param>
        /// <param name="point">Viewport coordinate to click</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        private static void Click(Browser browser, Point point, ClickType clickType, ModifierKeys modifierKeys)
        {
            // OttoMagic's trusted click takes only a point and a button, no modifier keys. A modified left or middle
            // click on OttoMagic publishes a warning that it is made without the keys.
            if (true == browser.UseOttoMagic && ModifierKeys.NONE != modifierKeys
                && (ClickType.LeftClick == clickType || ClickType.MiddleClick == clickType))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{Browser.GetModifierKeys(modifierKeys)}] cannot be held for a [{clickType}] at a point on OttoMagic, whose trusted click carries no keys, so the click is made without them.", browser, GPALObjectType.Browser);

            if (true == browser.UsePuppeteer)
            {
                switch (clickType)
                {
                    case ClickType.LeftClick:
                        browser.PuppeteerClient.LeftClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                    case ClickType.LeftDoubleClick:
                        browser.PuppeteerClient.LeftDoubleClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                    case ClickType.MiddleClick:
                        browser.PuppeteerClient.MiddleClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                    case ClickType.RightClick:
                        browser.PuppeteerClient.RightClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                }
            }
            else if (true == browser.UseSelenium && ClickType.MiddleClick == clickType && BrowserType.FireFox != browser.BrowserSettings.BrowserType)
            {
                // Selenium Actions has no middle button. On Chrome and Edge send it through CDP
                // Input.dispatchMouseEvent: move, press, release.
                int cdpModifiers = (int)(modifierKeys & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows | ModifierKeys.Shift));

                BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Input.dispatchMouseEvent", new Dictionary<string, object>
                {
                    ["type"] = "mouseMoved", ["x"] = point.X, ["y"] = point.Y, ["button"] = "none"
                });
                BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Input.dispatchMouseEvent", new Dictionary<string, object>
                {
                    ["type"] = "mousePressed", ["x"] = point.X, ["y"] = point.Y, ["button"] = "middle", ["buttons"] = 4, ["clickCount"] = 1, ["modifiers"] = cdpModifiers
                });
                BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Input.dispatchMouseEvent", new Dictionary<string, object>
                {
                    ["type"] = "mouseReleased", ["x"] = point.X, ["y"] = point.Y, ["button"] = "middle", ["buttons"] = 0, ["clickCount"] = 1, ["modifiers"] = cdpModifiers
                });
            }
            else if (true == browser.UseSelenium && ClickType.MiddleClick == clickType && browser.BrowserDriver is IActionExecutor executor)
            {
                // Firefox: raw W3C actions, which geckodriver supports: move, press and release the middle button.
                PointerInputDevice mouse = new PointerInputDevice(PointerKind.Mouse, "middle-click");
                ActionSequence press = new ActionSequence(mouse, 0);

                press.AddAction(mouse.CreatePointerMove(CoordinateOrigin.Viewport, point.X, point.Y, TimeSpan.Zero));
                press.AddAction(mouse.CreatePointerDown(MouseButton.Middle));
                press.AddAction(mouse.CreatePointerUp(MouseButton.Middle));

                executor.PerformActions(new List<ActionSequence> { press });
                executor.ResetInputState();
            }
            else if (true == browser.UseSelenium)
            {
                Actions action = new Actions(browser.BrowserDriver).MoveToLocation(point.X, point.Y);

                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    action.KeyDown(OpenQA.Selenium.Keys.LeftAlt).KeyDown(OpenQA.Selenium.Keys.Alt);
                if (modifierKeys.HasFlag(ModifierKeys.Control))
                    action.KeyDown(OpenQA.Selenium.Keys.LeftControl).KeyDown(OpenQA.Selenium.Keys.Control);
                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    action.KeyDown(OpenQA.Selenium.Keys.LeftShift).KeyDown(OpenQA.Selenium.Keys.Shift);
                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    action.KeyDown(OpenQA.Selenium.Keys.Meta);

                switch (clickType)
                {
                    case ClickType.LeftClick:
                        action.Click();
                        break;
                    case ClickType.LeftDoubleClick:
                        action.DoubleClick();
                        break;
                    case ClickType.RightClick:
                        action.ContextClick();
                        break;
                    default:
                        // selenium's Actions has no middle click to bind to
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{clickType}] on an image match by coordinate is not supported by Selenium, performing a left click.", browser, GPALObjectType.Browser);
                        action.Click();
                        break;
                }

                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    action.KeyUp(OpenQA.Selenium.Keys.LeftAlt).KeyUp(OpenQA.Selenium.Keys.Alt);
                if (modifierKeys.HasFlag(ModifierKeys.Control))
                    action.KeyUp(OpenQA.Selenium.Keys.LeftControl).KeyUp(OpenQA.Selenium.Keys.Control);
                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    action.KeyUp(OpenQA.Selenium.Keys.LeftShift).KeyUp(OpenQA.Selenium.Keys.Shift);
                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    action.KeyUp(OpenQA.Selenium.Keys.Meta);

                ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                if (ModifierKeys.NONE != unsupported)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{clickType}]: [{Browser.GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", browser, GPALObjectType.Browser);

                action.Build().Perform();
            }
            else if (ClickType.LeftClick != clickType && ClickType.MiddleClick != clickType)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{clickType}] on an image is not supported by OttoMagic, which clicks a point with the left or middle button only.", browser, GPALObjectType.Browser);
            else if (true == (ClickType.MiddleClick == clickType
                    ? browser.MagicHelper.TrustedMiddleClick(point.X, point.Y)
                    : browser.MagicHelper.TrustedLeftClick(point.X, point.Y)))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[Trusted][{clickType}] clicked at [{point.X},{point.Y}].", browser, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[Trusted][{clickType}] at [{point.X},{point.Y}] was not made.", browser, GPALObjectType.Browser);
        }
        /// <summary>
        /// Moves the mouse to <paramref name="element"/> using OttoMagic, Puppeteer or Selenium Actions, with
        /// the given pixel offset. Falls back to <see cref="ScrollIntoView"/> on Selenium errors.
        /// </summary>
        /// <param name="element">Element to move to</param>
        /// <param name="offsetX">Horizontal pixel offset within the element</param>
        /// <param name="offsetY">Vertical pixel offset within the element</param>
        /// <param name="browser">Browser the element belongs to</param>
        private static void MoveTo(GPALElement element, int offsetX, int offsetY, Browser browser)
        {
            try
            {
                if (true == browser.UseOttoMagic)
                {
                    browser.MagicHelper.MoveTo(element.Css);
                }
                else if (true == browser.UsePuppeteer)
                {
                    browser.PuppeteerClient.MoveTo(element.ElementHandle).Execute();
                }
                else
                    new Actions(browser.BrowserDriver).MoveToElement(element.WebElement, offsetX, offsetY).Perform();
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (false == GPAL.NoFallbackRecoveryActions)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium threw during move to Selector [{browser.CurrentUOW.CurrentSelector?.Name}], falling back to javascript.", browser, GPALObjectType.Browser);
                    ElementHelper.ScrollIntoView(browser, element);
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Selenium threw during move to Selector [{browser.CurrentUOW.CurrentSelector?.Name}], and recovery actions are off, so nothing was done about it.", browser, GPALObjectType.Browser, ex);

            }

        }
        /// <summary>
        /// Computes the absolute screen coordinates of <paramref name="element"/> (accounting for iframe nesting
        /// and the selector's offset) and moves the hardware mouse there. Same logic as hardware hover.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element (or pseudo-element from an image match) to move to</param>
        /// <param name="selector">Selector providing the offset and simulate-mouse settings</param>
        /// <returns>The absolute screen rectangle of the element</returns>
        private static Rectangle HardwareMoveTo(Browser browser, dynamic element, Selector selector)
        {
            Rectangle content = new Rectangle(), iframeRect = new Rectangle(0, 0, 0, 0);
            bool isImageMatch = "GPALElement" == (string)element.TagName;

            // OttoMagic and Selenium report document coordinates, which ScrollIntoView re-reads when it scrolls.
            // Puppeteer reports viewport coordinates, so always re-read it into document coordinates here; the scroll
            // comes off below.
            if (false == isImageMatch && true == browser.UsePuppeteer)
                RefreshLocation(browser, (GPALElement)element);

            Rectangle rect2 = new Rectangle(element.Location, element.Size);
            int offsetX = selector.OffsetX;
            int offsetY = selector.OffsetY;

            // Positions are document coordinates, so read the scroll offset to subtract below, or the mouse lands a
            // scroll offset away. Image matches skip this.
            Point scroll = Point.Empty;
            if (false == isImageMatch)
            {
                int pagex = 0;
                int pagey = 0;

                if (true == browser.UseOttoMagic)
                {
                    pagex = browser.MagicHelper.WindowPageOffsetX();
                    pagey = browser.MagicHelper.WindowPageOffsetY();
                    scroll = new Point(pagex, pagey);
                }
                else if (true == browser.UsePuppeteer)
                {
                    pagex = browser.PuppeteerClient.WindowPageOffsetX().Execute<int>();
                    pagey = browser.PuppeteerClient.WindowPageOffsetY().Execute<int>();
                    scroll = new Point(pagex, pagey);
                }
                else
                {
                    scroll = GetPageOffset(browser);
                }
            }

            // find the absolute screen position of the webelement
            // if it's a posuedo GPALElement, then it is an image match and those coords are already absolute
            // iframe or shadowdom nesting, get parent coords
            if (0 < browser.CurrentUOW.ContextPath?.Count)
            {
                foreach (UnitOfWork.WebElementWithType contextType in browser.CurrentUOW.ContextPath)
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        ClientRectangle tmpRect = contextType.gPalElement.BoundingRect;
                        iframeRect.X += (int)contextType.gPalElement.Location.X;
                        iframeRect.Y += (int)contextType.gPalElement.Location.Y;
                    }
                }
            }

            // if the click/hover offset is not specified, pick a point inside the element
            if (0 == offsetX && 0 == offsetY)
            {
                if ("GPALElement" == (string)element.TagName)
                {
                    // an image match points at one specific graphic, so aim at the middle of it
                    Point center = ImageCenterOffset(rect2.Size);
                    offsetX = center.X;
                    offsetY = center.Y;
                }
                else
                {
                    // Aim at the middle; elsewhere can hit padding, a border or a child that takes the click. Jitter
                    // up to 10% of width and height so the same pixel is not hit every time.
                    int spreadX = Math.Max(1, (int)rect2.Width / 10);
                    int spreadY = Math.Max(1, (int)rect2.Height / 10);

                    offsetX = (int)rect2.Width / 2 + Jitter.Next(-spreadX, spreadX + 1);
                    offsetY = (int)rect2.Height / 2 + Jitter.Next(-spreadY, spreadY + 1);
                }
            }

            content = GetWindowRectangle(browser);
            _lastContentArea = content;

            // Image match from a screen capture: already screen coordinates. From a visible-tab capture: add the
            // content area origin. Assumes 100% scaling.
            Point imageOrigin = Point.Empty;

            if ("GPALElement" == (string)element.TagName && CoordinateSpace.Viewport == ((GPALElement)element).CoordinateSpace)
                imageOrigin = new Point(content.X, content.Y);

            // === FINAL SCREEN COORDS
            int screenX = (int)(("GPALElement" == element.TagName)
                ? rect2.Left + offsetX + imageOrigin.X
                : rect2.Left + offsetX + iframeRect.X + content.X - scroll.X);

            int screenY = (int)(("GPALElement" == element.TagName)
                ? rect2.Top + offsetY + imageOrigin.Y
                : rect2.Top + offsetY + iframeRect.Y + content.Y - scroll.Y);

            // An element larger than the window cannot be fully on screen, so the point can fall outside it; a
            // stretched file input is the usual case. Only then move the point into the element's visible part. A
            // normal off-screen element is scrolled to instead.
            if ("GPALElement" != (string)element.TagName
                && true == IsBiggerThanViewport(new Size((int)rect2.Width, (int)rect2.Height), content)
                && false == content.Contains(screenX, screenY))
            {
                // Measure from the element's top-left. screenX and screenY already include the offset, so subtract
                // it first.
                Point origin = new Point(screenX - offsetX, screenY - offsetY);
                Rectangle onScreen = Rectangle.Intersect(new Rectangle(origin.X, origin.Y, (int)rect2.Width, (int)rect2.Height), content);

                if (false == onScreen.IsEmpty)
                {
                    // A point inside the rectangle may not reach the element: most of an oversized element is not
                    // its control, and something may be on top. Hit-test candidates with PointHitsElement and take
                    // the first that returns this element. Candidates sit at the vertical middle, first at insets
                    // from the right edge (hitTestInsets), then at fractions of the width (hitTestFractions).
                    bool verified = false;
                    int tryY = onScreen.Y + onScreen.Height / 2;   // vertically it fits, so the middle is the middle
                    List<int> candidates = new List<int>();

                    foreach (int inset in hitTestInsets)
                        if (inset < onScreen.Width)
                            candidates.Add(onScreen.Right - inset);

                    foreach (float fraction in hitTestFractions)
                        candidates.Add(onScreen.X + (int)(onScreen.Width * fraction));

                    foreach (int tryX in candidates)
                        if (true == PointHitsElement(browser, (GPALElement)element, new Point(tryX, tryY)))
                        {
                            screenX = tryX;
                            screenY = tryY;
                            verified = true;
                            break;
                        }

                    if (false == verified)
                    {
                        // No candidate hits the element, so the click will miss. Use the middle of the visible part
                        // and say so in the message.
                        screenX = onScreen.X + onScreen.Width / 2;
                        screenY = onScreen.Y + onScreen.Height / 2;
                    }

                    // Save the adjusted point as a WithOffset value so a workflow can put it in its selector. The
                    // click publishes it, not this method.
                    _lastClampedPoint = $"at [WithOffset({screenX - origin.X}, {screenY - origin.Y})]{(true == verified ? "" : ", which nothing on this element answered to")}, for window [{_lastOuterWindow.X}, {_lastOuterWindow.Y}, {_lastOuterWindow.Width}, {_lastOuterWindow.Height}], because the element measures [{(int)rect2.Width}x{(int)rect2.Height}], larger than the window";
                }
            }

            // === MOVE MOUSE ===
            if (true == GPAL.SimulateMouse || true == selector.SimulateMouse)
                HardwareHelper.MoveMouse(screenX, screenY, 10, 10);
            else
                HardwareHelper.MoveMouse(screenX, screenY);

            return new Rectangle(screenX, screenY, (int)rect2.Width, (int)rect2.Height);
        }
        /// <summary>
        /// Gets the page's current scroll offset (window.pageXOffset / window.pageYOffset).<br/>
        /// OttoMagic and Puppeteer both have a declared endpoint for it (window-page-xoffset); Selenium has no
        /// such call, so it runs the javascript itself.
        /// </summary>
        /// <param name="browser">Browser to read the scroll offset from</param>
        /// <returns>The page scroll offset, or 0,0 if it could not be read</returns>
        internal static Point GetPageOffset(Browser browser)
        {
            Point pageOffset = Point.Empty;

            if (true == browser.UseOttoMagic)
                pageOffset = new Point(browser.MagicHelper.WindowPageOffsetX(), browser.MagicHelper.WindowPageOffsetY());
            else if (true == browser.UsePuppeteer)
                pageOffset = new Point(browser.PuppeteerClient.WindowPageOffsetX().Execute<int>(), browser.PuppeteerClient.WindowPageOffsetY().Execute<int>());
            else if (BrowserHelper.ExecuteJavaScriptObj("return [window.top.pageXOffset, window.top.pageYOffset]", browser) is IList<object> offsets && 2 <= offsets.Count)
                pageOffset = new Point(Convert.ToInt32(offsets[0]), Convert.ToInt32(offsets[1]));

            return pageOffset;
        }
        /// <summary>
        /// True when the interaction clicks a screen coordinate, so the element must be on screen: the hardware
        /// engines and a Hardware selector. Everything else uses css or a handle and works off screen.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="interactionType">Interaction type in play, or null to take the current selector's</param>
        /// <returns>True if the interaction needs the element to be on-screen</returns>
        private static bool UsesScreenCoordinates(Browser browser, Enums.InteractionType? interactionType)
        {
            Enums.InteractionType? useType = interactionType ?? browser.CurrentUOW?.CurrentSelector?.InteractionType;

            return true == browser.BrowserSettings.UseHardware
                || Enums.InteractionType.Hardware == useType;
        }
        /// <summary>
        /// Left Click on a webelement
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="modifierKeys"></param>
        /// <param name="clickType">if clicktype is passed in, it will click that type - used only by MiddleClick()</param>
        /// <param name="trusted">ask the engine for a click the page reads as trusted - used only by StealthLeftClick()</param>
        public static void LeftClick(Browser browser, ModifierKeys modifierKeys = ModifierKeys.NONE, ClickType clickType = ClickType.LeftClick, bool trusted = false)
        {
            List<GPALElement> matchedElements;
            List<GPALElement> clickableElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == matchedElements)
                {
                    PublishToEventHandler(browser, selector, matchedElements);
                    continue;
                }

                // Matched elements are always used: with no match criteria they equal the found elements, with criteria they hold only the matches

                if (1 < matchedElements.Count)
                {
                    clickableElements = new List<GPALElement>();
                    foreach (GPALElement elem in matchedElements)
                        if (true == elem.IsClickable(false))     // no output from isClickable
                            clickableElements.Add(elem);

                    if (0 == clickableElements.Count)
                        clickableElements = matchedElements;
                }
                else
                    clickableElements = matchedElements;

               int rowCount = 0;
               int total = Math.Min(clickableElements.Count, browser.CurrentUOW.WithAllThatMatch);
               object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

               foreach (GPALElement elem in clickableElements)
               {
                    if (null != elem)
                    {
                        if (true == elem.TagName.ToLower().Equals("option"))
                            SelectClick(browser, selector, elem, SelectClickType.LeftClick, modifierKeys);
                        else
                            Click(browser, selector, elem, clickType, modifierKeys, trusted);

                        if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                            break;
                    }
                }

                //Thread.Sleep(500);
            }
        }
        /// <summary>
        /// Middle clicks each element matched by the current unit of work's selectors.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void MiddleClick(Browser browser)
        {
            LeftClick(browser, ModifierKeys.NONE, ClickType.MiddleClick);
        }
        /// <summary>
        /// Clicks every element matched by the current unit of work's selectors using the given click type,
        /// using SelectClick for option elements.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="clickType">Type of click to perform (e.g. LeftClick, RightClick)</param>
        /// <returns>True if at least one click was performed</returns>
        public static bool GenericClick(Browser browser, ClickType clickType)
        {
            List<GPALElement> matchedElements;
            bool matchedAll = false;
            bool clickPerformed = false;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                   int rowCount = 0;
                   int total = Math.Min(matchedElements.Count, browser.CurrentUOW.WithAllThatMatch);
                   object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

                   foreach (GPALElement elem in matchedElements)
                   {
                        if (null != elem)
                        {
                            if (true == elem.TagName.ToLower().Equals("option"))
                                SelectClick(browser, selector, elem, SelectClickType.LeftClick, ModifierKeys.NONE);
                            else
                                Click(browser, selector, elem, clickType, ModifierKeys.NONE);

                            clickPerformed = true;

                            if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                                break;
                        }
                    }
                }
            }
            return clickPerformed;
        }
        /// <summary>
        /// Clicks every element matched by the current unit of work's selectors, using the given select-click
        /// type for option elements and a plain left click for everything else.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="selectClickType">Select-menu click behavior (e.g. SelectRandom, SelectNext)</param>
        /// <returns>True if at least one click was performed</returns>
        public static bool GenericClick(Browser browser, SelectClickType selectClickType)
        {
            List<GPALElement> matchedElements;
            bool matchedAll = false;
            bool clickPerformed = false;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                    int rowCount = 0;
                    int total = Math.Min(matchedElements.Count, browser.CurrentUOW.WithAllThatMatch);
                    object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

                    foreach (GPALElement elem in matchedElements)
                    {
                        if (null != elem)
                        {
                            if (true == elem.TagName.ToLower().Equals("option"))
                                SelectClick(browser, selector, elem, selectClickType, ModifierKeys.NONE);
                            else
                                Click(browser, selector, elem, ClickType.LeftClick, ModifierKeys.NONE);

                            clickPerformed = true;

                            if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                                break;
                        }
                    }
                }
            }
            return clickPerformed;
        }

        // Select menu state for SelectClick: the selected option index for each menu, the last menu clicked, and the
        // attribute hash helpers that identify a menu
        internal static class SelectClickHelper
        {
            // Selected option index for each menu, keyed by the menu's attribute hash; SelectRandom, SelectNext,
            // SelectPrevious and their WithWrap forms all read and update it
            internal static readonly Dictionary<string, int> _sequentialSelectIndices = new Dictionary<string, int>();
            // Track the last parentNode identifier
            internal static string _lastParentNodeId = null;

            /// <summary>
            /// Computes a hash of all of <paramref name="element"/>'s attributes (sorted name=value pairs) using
            /// the same algorithm as <see cref="GetElementAttributeHashJavaScript"/>, so the two are interchangeable.
            /// </summary>
            /// <param name="element">Element to hash</param>
            /// <param name="browser">Browser the element belongs to (unused, kept for symmetry with the JS variant)</param>
            /// <returns>String hash of the element's attributes, or its GetHashCode() on failure</returns>
            internal static string GetElementAttributeHashCsharp(GPALElement element, Browser browser)
            {
                try
                {
                    // Get all attributes from the Attributes dictionary
                    var attrList = new List<string>();

                    foreach (var attrName in element.Attributes.Keys)
                    {
                        // Use GetAttribute to retrieve the actual value
                        string value = element.GetAttribute(attrName);
                        if (!string.IsNullOrEmpty(value))
                        {
                            attrList.Add($"{attrName}={value}");
                        }
                    }

                    // Sort attributes for consistent hashing
                    attrList.Sort();
                    string attrString = string.Join("|", attrList);

                    // Compute hash (same algorithm as JavaScript)
                    int hash = 0;
                    foreach (char c in attrString)
                    {
                        hash = ((hash << 5) - hash + c) | 0; // Matches JavaScript: hash = ((hash << 5) - hash + charCode) | 0
                    }

                    return hash.ToString();
                }
                catch
                {
                    return element.GetHashCode().ToString(); // Fallback to GetHashCode
                }
            }

            /// <summary>
            /// Computes a hash of all of <paramref name="element"/>'s attributes (sorted name=value pairs) by
            /// running the same hashing algorithm as <see cref="GetElementAttributeHashCsharp"/> in JavaScript.
            /// </summary>
            /// <param name="element">Element to hash</param>
            /// <param name="browser">Browser the element belongs to</param>
            /// <returns>String hash of the element's attributes, or its GetHashCode() on failure</returns>
            internal static string GetElementAttributeHashJavaScript(GPALElement element, Browser browser)
            {
                try
                {
                    if (false == browser.UseOttoMagic)
                    {
                        string jsScript = @"
                            let attrs = arguments[0].attributes;
                            let attrList = [];
                            for (let attr of attrs) {
                                attrList.push(attr.name + '=' + attr.value);
                            }
                            attrList.sort();    // Sort attributes for consistent hashing
                            let str = attrList.join('|');
                            let hash = 0;
                            for (let i = 0; i < str.length; i++) {
                                hash = ((hash << 5) - hash + str.charCodeAt(i)) | 0;
                            }
                            return hash.toString();
                        ";
                        return (string)BrowserHelper.ExecuteJavaScript(browser, jsScript, element.WebElement) ?? element.GetHashCode().ToString();
                    } else
                    {
                        return browser.OttoMagicClient.GetElementAttributeHash(element.Css).Execute().Replace("\"","");
                    }
                }
                catch
                {
                    return element.GetHashCode().ToString(); // Fallback to GetHashCode
                }
            }
        }
        /// <summary>
        /// click on a select list menu
        /// Modifier keys go to the hardware click on the select menu and to the click on the option.
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="selector"></param>
        /// <param name="webElement">option element in select menu</param>
        /// <param name="clickType"></param>
        /// <param name="modifierKeys"></param>

        public static void SelectClick(Browser browser, Selector selector, GPALElement webElement, SelectClickType clickType, ModifierKeys modifierKeys)
        {
            dynamic result = null;  // GPALElement or WebElement
            dynamic parentElem = null;  // GPALElement or WebElement
            SelectElement elem = null;
            string selectedValue = null;
            dynamic options = null;
            string elementId = null;

            // Random, next and previous selection need the current option, so each select menu and its selected option are tracked here
            // Each select menu is tracked by its own id, so several can be walked at once
            if (true == browser.UseOttoMagic)
                result = browser.MagicHelper.GetParentNode(webElement.Css);
            else if (true == browser.UsePuppeteer)
                result = browser.PuppeteerClient.GetParentNode(webElement.ElementHandle).Execute<GPALElement>();
            else // selenium
                result = BrowserHelper.ExecuteJavaScriptObj("return arguments[0].parentNode;", browser, webElement.WebElement);

            if (result != null)
            {
                if (true == browser.UseSelenium)
                {
                    parentElem = result;
                    elementId = parentElem.GetAttribute("id");
                }
                else
                {
                    parentElem = (GPALElement)result;
                    parentElem.Browser = browser;
                    elementId = parentElem.GetAttribute("id");
                }

                try
                {
                    // ScrollIntoView reads a GPALElement; Selenium's parent is a raw WebElement, so wrap it for the scroll
                    if (true == browser.UseSelenium)
                        ScrollIntoView(browser, new GPALElement((IWebElement)parentElem) { Browser = browser });
                    else
                        ScrollIntoView(browser, parentElem);

                    // Get a unique identifier based on attributes
                    if (true == browser.UseSelenium)
                    {
                        elem = new SelectElement(parentElem);

                        // Get all options in the select element
                        if (SelectClickType.LeftClick != clickType)
                        {
                            options = elem.Options;
                            if (options.Count == 0)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No options found in select element.", webElement, GPALObjectType.Other);
                                return;
                            }
                        }

                        // Use C# attribute iteration for non-JavaScript interaction types
                        if (string.IsNullOrEmpty(elementId))
                        {
                            //if (selector.InteractionType != Enums.InteractionType.JavaScript)
                            //    elementId = SelectClickHelper.GetElementAttributeHashCsharp(parentElem, browser);
                            //else
                                elementId = SelectClickHelper.GetElementAttributeHashJavaScript(parentElem, browser);
                        }
                    }
                    else
                    {
                        if (SelectClickType.LeftClick != clickType)
                        {
                            Selector optionsSelector = GPAL.CssSelector($"{parentElem.Css} > option").WithSelectorName("options").ToGPALObject();

                            options = FindWebElementsByCss(browser, optionsSelector.SelectorPaths[0], optionsSelector); // optionsSelector has the one path built above

                            if (0 == options.Count)
                            {
                                Click(browser, selector, webElement, ClickType.LeftClick, modifierKeys);
                                return;
                            }
                        }

                        if (string.IsNullOrEmpty(elementId))
                        {
                            if (true == browser.UseOttoMagic)
                                elementId = browser.MagicHelper.GetElementAttributeHash(parentElem.Css);
                            else if (true == browser.UsePuppeteer)
                                elementId = browser.PuppeteerClient.GetElementAttributeHash(parentElem.Css).Execute();
                        }
                    }

                    // Check if we're dealing with a new select element
                    
                    if (SelectClickHelper._lastParentNodeId != elementId &&
                        (clickType == SelectClickType.SelectNext || clickType == SelectClickType.SelectNextWithWrap))
                    {
                        SelectClickHelper._sequentialSelectIndices[elementId] = -1; // Reset index
                    }

                    SelectClickHelper._lastParentNodeId = elementId;

                    if (false == browser.BrowserSettings.UseHeadless && (true == browser.BrowserSettings.UseHardware))
                    {
                        switch (clickType)
                        {
                            case SelectClickType.SelectRandom:
                                Random rand = new Random();
                                int randomIndex = rand.Next(0, options.Count);
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                HardwareClick(browser, parentElem, ClickType.LeftClick, modifierKeys, browser.CurrentUOW.CurrentSelector);
                                HardwareHelper.SendKey(GPAL.VK_HOME);
                                for (int i = 0; i < randomIndex; i++)
                                {
                                    Thread.Sleep(50);
                                    HardwareHelper.SendKey(GPAL.VK_DOWN);
                                }
                                HardwareHelper.SendKey(GPAL.VK_RETURN);
                                break;

                            case SelectClickType.SelectNext:
                            case SelectClickType.SelectNextWithWrap:
                            case SelectClickType.SelectPrevious:
                            case SelectClickType.SelectPreviousWithWrap:

                                // +1 = move down (next), -1 = move up (previous)
                                int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                clickType == SelectClickType.SelectPreviousWithWrap)
                                               ? -1 : 1;

                                bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                   clickType == SelectClickType.SelectPreviousWithWrap);

                                // Initialize to -1 on first use -> first Next -> index 0, first Previous -> last item
                                if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                {
                                    currentIndex = -1;
                                    SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                }

                                int nextIndex;

                                if (shouldWrap)
                                {
                                    // Proper wrapping that works when going backwards from 0
                                    nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                }
                                else
                                {
                                    // No wrap -> clamp to bounds
                                    nextIndex = currentIndex + incOrDec;
                                    if (nextIndex < 0)
                                        nextIndex = 0;
                                    else if (nextIndex >= options.Count)
                                        nextIndex = options.Count - 1;
                                }

                                // === Hardware keyboard selection ===
                                HardwareClick(browser, parentElem, ClickType.LeftClick, modifierKeys,
                                              browser.CurrentUOW.CurrentSelector);

                                // Always start from the top (HOME), so the key count from there is exact
                                HardwareHelper.SendKey(GPAL.VK_HOME);
                                Thread.Sleep(50); // small delay after HOME

                                if (incOrDec > 0)
                                {
                                    // Going forward -> just press DOWN `nextIndex` times
                                    for (int i = 0; i < nextIndex; i++)
                                    {
                                        Thread.Sleep(50);
                                        HardwareHelper.SendKey(GPAL.VK_DOWN);
                                    }
                                }
                                else
                                {
                                    // Going backward -> go to end first, then go up the required steps
                                    HardwareHelper.SendKey(GPAL.VK_END);
                                    Thread.Sleep(50);

                                    int stepsUp = options.Count - 1 - nextIndex; // how many UP arrows needed from bottom
                                    for (int i = 0; i < stepsUp; i++)
                                    {
                                        Thread.Sleep(50);
                                        HardwareHelper.SendKey(GPAL.VK_UP);
                                    }
                                }

                                // Confirm selection
                                HardwareHelper.SendKey(GPAL.VK_RETURN);

                                // Store the new index for next time
                                SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;

                                break;

                            default:
                                HardwareClick(browser, parentElem, ClickType.LeftClick, modifierKeys, browser.CurrentUOW.CurrentSelector);

                                // ReadOnlyCollection<GPALElement> optionElems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                                int index = Int32.Parse(webElement.GetAttribute("index"));  // optionElems[0].GetAttribute("index"));
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                HardwareHelper.SendKey(GPAL.VK_HOME);
                                for (int cnt = 0; cnt < index; cnt++)
                                {
                                    Thread.Sleep(50);
                                    HardwareHelper.SendKey(GPAL.VK_DOWN);
                                }
                                HardwareHelper.SendKey(GPAL.VK_RETURN);
                                break;
                        }
                    }
                    else if (true == browser.UseOttoMagic)
                    {
                        switch (clickType)
                        {
                            case SelectClickType.SelectRandom:
                                Random rand = new Random();
                                int randomIndex = rand.Next(0, options.Count);
                                selectedValue = options[randomIndex].GetAttribute("value");
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                browser.MagicHelper.SelectClick(parentElem.Css, selectedValue);
                                break;

                            case SelectClickType.SelectNext:
                            case SelectClickType.SelectNextWithWrap:
                            case SelectClickType.SelectPrevious:
                            case SelectClickType.SelectPreviousWithWrap:

                                // +1 for next, -1 for previous
                                int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                clickType == SelectClickType.SelectPreviousWithWrap)
                                               ? -1 : 1;

                                // true if we should wrap around
                                bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                   clickType == SelectClickType.SelectPreviousWithWrap);

                                // Initialize to -1 on first use (so first "next" goes to 0, first "prev" goes to last)
                                if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                {
                                    currentIndex = -1;
                                    SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                }

                                int nextIndex;

                                if (shouldWrap)
                                {
                                    // Safe modular arithmetic that works with negative numbers
                                    nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                }
                                else
                                {
                                    // Manual clamp: stay within [0, options.Count-1]
                                    nextIndex = currentIndex + incOrDec;
                                    if (nextIndex < 0)
                                        nextIndex = 0;
                                    else if (nextIndex >= options.Count)
                                        nextIndex = options.Count - 1;
                                }

                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;
                                selectedValue = options[nextIndex].GetAttribute("value");
                                browser.MagicHelper.SelectClick(parentElem.Css, selectedValue);

                                break;

                            default:
                                int index = Int32.Parse(webElement.GetAttribute("index"));
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                browser.MagicHelper.SelectClick(parentElem.Css, webElement.Value);
                                break;
                        }
                    }
                    else if (true == browser.UsePuppeteer)
                    {
                        switch (clickType)
                        {
                            case SelectClickType.SelectRandom:
                                Random rand = new Random();
                                int randomIndex = rand.Next(0, options.Count);
                                selectedValue = options[randomIndex].GetAttribute("value");
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                // the dynamic argument binds the call at run time, which does not find SelectClick on the interface, so cast to PuppeteerClient
                                ((PuppeteerClient)browser.PuppeteerClient).SelectClick((dynamic)parentElem.ElementHandle).WithSelectValue(selectedValue).Execute();
                                break;

                            case SelectClickType.SelectNext:
                            case SelectClickType.SelectNextWithWrap:
                            case SelectClickType.SelectPrevious:
                            case SelectClickType.SelectPreviousWithWrap:

                                // +1 for next, -1 for previous
                                int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                clickType == SelectClickType.SelectPreviousWithWrap)
                                               ? -1 : 1;

                                // true if we should wrap around
                                bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                   clickType == SelectClickType.SelectPreviousWithWrap);

                                // Initialize to -1 on first use (so first "next" goes to 0, first "prev" goes to last)
                                if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                {
                                    currentIndex = -1;
                                    SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                }

                                int nextIndex;

                                if (shouldWrap)
                                {
                                    // Safe modular arithmetic that works with negative numbers
                                    nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                }
                                else
                                {
                                    // Manual clamp: stay within [0, options.Count-1]
                                    nextIndex = currentIndex + incOrDec;
                                    if (nextIndex < 0)
                                        nextIndex = 0;
                                    else if (nextIndex >= options.Count)
                                        nextIndex = options.Count - 1;
                                }

                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;
                                selectedValue = options[nextIndex].GetAttribute("value");
                                // the dynamic argument binds the call at run time, which does not find SelectClick on the interface, so cast to PuppeteerClient
                                ((PuppeteerClient)browser.PuppeteerClient).SelectClick((dynamic)parentElem.ElementHandle).WithSelectValue(selectedValue).Execute();

                                break;

                            default:
                                int index = Int32.Parse(webElement.GetAttribute("index"));
                                SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                // Store the new index and perform the selection
                                // the dynamic argument binds the call at run time, which does not find SelectClick on the interface, so cast to PuppeteerClient
                                ((PuppeteerClient)browser.PuppeteerClient).SelectClick((dynamic)parentElem.ElementHandle).WithSelectIndex(index).Execute();
                                break;
                        }
                    }
                    else if (true == browser.UseSelenium)
                    {

                        if (Enums.InteractionType.JavaScript == selector.InteractionType || browser.BrowserSettings.UseJavaScript)  // only selenium can have javascript option
                        {
                            switch (clickType)
                            {
                                case SelectClickType.SelectRandom:
                                    Random rand = new Random();
                                    int randomIndex = rand.Next(0, options.Count);
                                    // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                    selectedValue = options[randomIndex].GetAttribute("value");
                                    BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1];", parentElem, selectedValue);
                                    break;

                                case SelectClickType.SelectNext:
                                case SelectClickType.SelectNextWithWrap:
                                case SelectClickType.SelectPrevious:
                                case SelectClickType.SelectPreviousWithWrap:

                                    // +1 for next, -1 for previous
                                    int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                    clickType == SelectClickType.SelectPreviousWithWrap)
                                                   ? -1 : 1;

                                    // true if we should wrap around
                                    bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                       clickType == SelectClickType.SelectPreviousWithWrap);

                                    // Initialize to -1 on first use (so first "next" goes to 0, first "prev" goes to last)
                                    if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                    {
                                        currentIndex = -1;
                                        SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                    }

                                    int nextIndex;

                                    if (shouldWrap)
                                    {
                                        // Safe modular arithmetic that works with negative numbers
                                        nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                    }
                                    else
                                    {
                                        // Manual clamp: stay within [0, options.Count-1]
                                        nextIndex = currentIndex + incOrDec;
                                        if (nextIndex < 0)
                                            nextIndex = 0;
                                        else if (nextIndex >= options.Count)
                                            nextIndex = options.Count - 1;
                                    }

                                    // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;
                                    selectedValue = options[nextIndex].GetAttribute("value");
                                    BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1];", parentElem, selectedValue);
                                    break;

                                default:
                                    int index = Int32.Parse(webElement.WebElement.GetAttribute("index"));  // optionElems[0].GetAttribute("index"));
                                                                                                // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                    BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1].value", parentElem, webElement.WebElement);
                                    break;
                            }
                        }
                        else    // use native selenium rather than javascript injection
                            switch (clickType)
                            {
                                case SelectClickType.SelectRandom:
                                    Random rand = new Random();
                                    int randomIndex = rand.Next(0, options.Count);
                                    // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                    elem.SelectByIndex(randomIndex);
                                    break;

                                case SelectClickType.SelectNext:
                                case SelectClickType.SelectNextWithWrap:
                                case SelectClickType.SelectPrevious:
                                case SelectClickType.SelectPreviousWithWrap:

                                    // Determine direction: +1 = next, -1 = previous
                                    int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                    clickType == SelectClickType.SelectPreviousWithWrap)
                                                   ? -1 : 1;

                                    // Determine if we should wrap around
                                    bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                       clickType == SelectClickType.SelectPreviousWithWrap);

                                    // Get current index, default to -1 (so first "next" -> 0, first "prev" -> last)
                                    if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                    {
                                        currentIndex = -1;
                                        SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                    }

                                    int nextIndex;

                                    if (shouldWrap)
                                    {
                                        // Safe wrap-around that works with negative numbers
                                        nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                    }
                                    else
                                    {
                                        // No wrap: clamp to valid range
                                        nextIndex = currentIndex + incOrDec;
                                        if (nextIndex < 0)
                                            nextIndex = 0;
                                        else if (nextIndex >= options.Count)
                                            nextIndex = options.Count - 1;
                                    }

                                    // Perform the selection and store the new index
                                    elem.SelectByIndex(nextIndex);
                                    SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;

                                    break;

                                default:
                                    int index = Int32.Parse(webElement.WebElement.GetAttribute("index"));  // optionElems[0].GetAttribute("index"));
                                                                                                // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                    elem.SelectByValue(webElement.GetAttribute("value"));
                                    break;
                            }
                    }
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!GPAL.NoFallbackRecoveryActions)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to Select using [{selector.InteractionType}]. Falling back on JavaScript.", webElement, GPALObjectType.Other, ex);
                        BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1].value", true == browser.UseSelenium ? parentElem : ((GPALElement)parentElem).WebElement, webElement.WebElement);
                        SelectClickHelper._sequentialSelectIndices[elementId] = Int32.Parse(true == browser.UseSelenium ? webElement.WebElement.GetAttribute("index") : webElement.GetAttribute("index"));
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to Select using [{selector.InteractionType}].", webElement, GPALObjectType.Other, ex);
                    }
                }
                selectedValue = null != options ? options[SelectClickHelper._sequentialSelectIndices[elementId]].GetAttribute("value") : webElement.GetAttribute("value");
                string selectedText = null != options ? options[SelectClickHelper._sequentialSelectIndices[elementId]].Text : webElement.Text;
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{selector.InteractionType}] selected [{selectedText}] value [{selectedValue}] index [{SelectClickHelper._sequentialSelectIndices[elementId]}]");
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to click Parent Select for Option using [{selector.InteractionType}]. Clicking element [{selector.Name}].", webElement, GPALObjectType.Other);
                webElement.Click(modifierKeys);
            }
        }

        /// <summary>
        /// Scrolls <paramref name="webElement"/> into view and clicks it, choosing hardware, OttoMagic,
        /// JavaScript, Puppeteer or Selenium based on the selector's interaction type and browser settings,
        /// with fallback to JavaScript/Selenium/Puppeteer click on failure.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="selector">Selector that matched the element, used for interaction type and logging</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        /// <param name="trusted">ask the engine for a click the page reads as trusted - used only by StealthLeftClick()</param>
        public static void Click(Browser browser, Selector selector, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys, bool trusted = false)
        {
            bool dontPublish = false;

            // An element from an image match is clicked at the image, not its middle, which on a large element can
            // be far away. Keep the element in resolved to check the point against.
            GPALElement resolved = null;

            if (null != webElement.ImageMatch)
            {
                resolved = webElement;
                webElement = webElement.ImageMatch;
            }

            ScrollIntoView(browser, webElement, selector.InteractionType);


            //if (true == webElement.IsClickable()) // not checked: the click is tried either way, and the check costs time
            {
                try
                {
                    if (false == browser.BrowserSettings.UseHeadless && false == browser.BrowserSettings.HiddenDesktop
                        && (Enums.InteractionType.Hardware == selector.InteractionType
                            || true == browser.BrowserSettings.UseHardware)
                        )
                    {
                        // Position is current: ScrollIntoView re-reads it after a scroll, and HardwareMoveTo re-reads
                        // Puppeteer's.
                        HardwareClick(browser, webElement, clickType, modifierKeys, selector, true);
                        dontPublish = true;
                    }
                    else if ("GPALElement" == webElement.TagName)
                    {
                        // Image match without hardware: the engine clicks the point. If it resolved to an element,
                        // check the point still hits it (PointIsClear); an ad moved in by the scroll could be on top.
                        Point aim = ToViewportPoint(browser, webElement, selector);

                        // OttoMagic clicks a point through the browser debugger, which Firefox does not give
                        // extensions. So on Firefox it clicks the resolved element; this is the normal path, not a
                        // recovery. Its element middle click opens the link's tab itself, which Firefox does not do
                        // for a scripted click.
                        bool ottoOnFirefox = true == browser.UseOttoMagic && BrowserType.FireFox == browser.BrowserSettings.BrowserType;

                        if (null != resolved && true == ottoOnFirefox)
                            ClickElementItself(browser, resolved, clickType, modifierKeys);
                        else if (null != resolved && false == PointIsClear(browser, resolved, selector, ref aim) && false == GPAL.NoFallbackRecoveryActions)
                            ClickElementItself(browser, resolved, clickType, modifierKeys);
                        else
                            Click(browser, aim, clickType, modifierKeys);

                        // OttoMagic's point click publishes its own made or not made message, so skip the generic
                        // "clicked". Its element click publishes nothing, so publish it there.
                        dontPublish = true == browser.UseOttoMagic && false == (null != resolved && true == ottoOnFirefox);
                    }
                    else if (true == browser.UseOttoMagic)
                    {
                        bool clicked = false;

                        // A script click does not focus a field the way a real click does, so a field gets the
                        // trusted click too.
                        bool field = "input".Equals(webElement.TagName, StringComparison.OrdinalIgnoreCase)
                                  || "textarea".Equals(webElement.TagName, StringComparison.OrdinalIgnoreCase)
                                  || "select".Equals(webElement.TagName, StringComparison.OrdinalIgnoreCase);

                        // A selector that asked for hardware wants a real click at a point, its offset included. The
                        // mouse cannot reach this desktop, so the trusted click stands in for it.
                        bool askedHardware = Enums.InteractionType.Hardware == selector.RequestedInteractionType;

                        // A trusted click is at a viewport point, with no modifier keys.
                        if ((true == trusted || true == field || true == askedHardware) && ClickType.LeftClick == clickType && ModifierKeys.NONE == modifierKeys)
                        {
                            // The point has to be on screen. OttoMagic skips the scroll above for its own clicks, so
                            // scroll as the mouse would, then read the rect.
                            ScrollIntoView(browser, webElement, Enums.InteractionType.Hardware);

                            // OttoMagic keeps an element by a CSS path that can match its siblings too, so it would
                            // measure the first of them. An element found by one of the selector's own XPaths is
                            // measured by that XPath, which names it alone; not in a shadow root, where XPath does not
                            // reach.
                            bool inShadowRoot = null != browser.CurrentUOW.ContextPath
                                && browser.CurrentUOW.ContextPath.Exists(context => ElementType.ShadowRoot == context.elementType);
                            string locator = false == inShadowRoot
                                && false == string.IsNullOrEmpty(webElement.Xpath)
                                && selector.SelectorPaths.Exists(path => SelectorPathType.Xpath == path.SelectorPathType && path.SelectorPath == webElement.Xpath)
                                ? webElement.Xpath
                                : webElement.Css;

                            // Where each iframe on the way down sits in its page. The rect is measured from the element's
                            // own frame and the trusted click is a point in the tab, so these are added, as the hardware
                            // click does.
                            Point frames = Point.Empty;
                            bool inFrame = false;

                            if (null != browser.CurrentUOW.ContextPath)
                                foreach (UnitOfWork.WebElementWithType context in browser.CurrentUOW.ContextPath)
                                    if (ElementType.IFrame == context.elementType)
                                    {
                                        frames.Offset((int)context.gPalElement.Location.X, (int)context.gPalElement.Location.Y);
                                        inFrame = true;
                                    }

                            Rectangle rect = Rectangle.Empty;

                            // The point in the tab now. An iframe's position is from the top of its page, so the page's
                            // scroll comes off, as in the hardware click.
                            Func<Point> aimNow = () =>
                            {
                                rect = browser.MagicHelper.GetBoundingClientRect(locator);

                                Point point = (0 == selector.OffsetX && 0 == selector.OffsetY)
                                    ? new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)
                                    : new Point(rect.X + selector.OffsetX, rect.Y + selector.OffsetY);

                                if (true == inFrame)
                                    point.Offset(frames.X - browser.MagicHelper.WindowPageOffsetX(), frames.Y - browser.MagicHelper.WindowPageOffsetY());

                                return point;
                            };

                            Point aim = aimNow();

                            // Inside an iframe the visibility check above asks the frame, which can hold the element in
                            // view while the frame itself runs off the tab. Check the point against the tab, and scroll
                            // the element into view when it is outside; the browser scrolls the page holding the frame.
                            int tabWidth = browser.MagicHelper.WindowInnerWidth();
                            int tabHeight = browser.MagicHelper.WindowInnerHeight();

                            if (0 > aim.X || 0 > aim.Y || tabWidth <= aim.X || tabHeight <= aim.Y)
                            {
                                browser.MagicHelper.ScrollIntoView(locator);
                                Thread.Sleep(250);  // as ScrollIntoView, so the page has moved before it is measured

                                aim = aimNow();
                            }

                            clicked = 0 < rect.Width && 0 < rect.Height && true == browser.MagicHelper.TrustedLeftClick(aim.X, aim.Y);

                            if (true == clicked)
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[Trusted][{clickType}] on selector [{selector.Name}] at [{aim.X},{aim.Y}], element at [{rect.X},{rect.Y}] size [{rect.Width}x{rect.Height}].", browser, GPALObjectType.Browser);

                            // Only a click asked to be trusted warns; a field falls back to the script click quietly.
                            if (false == clicked && true == trusted)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[Trusted][{clickType}] on selector [{selector.Name}] was not made, falling back to [{clickType}].", browser, GPALObjectType.Browser);
                        }

                        if (false == clicked)
                        {
                            if (modifierKeys != ModifierKeys.NONE)
                                browser.MagicHelper.PressModifierKey(modifierKeys);

                            switch (clickType)
                            {
                                case ClickType.LeftClick:
                                    browser.MagicHelper.LeftClick(webElement.Css);
                                    break;
                                case ClickType.LeftDoubleClick:
                                    browser.MagicHelper.LeftDoubleClick(webElement.Css);
                                    break;
                                case ClickType.MiddleClick:
                                    browser.MagicHelper.MiddleClick(webElement.Css);
                                    break;
                                case ClickType.RightClick:
                                    browser.MagicHelper.RightClick(webElement.Css);
                                    break;
                            }

                            if (modifierKeys != ModifierKeys.NONE)
                                browser.MagicHelper.ReleaseModifierKey(modifierKeys);
                        }
                    }
                    else if (Enums.InteractionType.JavaScript == selector.InteractionType || true == browser.BrowserSettings.UseJavaScript) // selunium & puppeteer javascript override
                    {
                        if (false == JavaScriptClick(browser, webElement, clickType, modifierKeys) && false == GPAL.NoFallbackRecoveryActions)
                        {
                            if (true == browser.UseSelenium)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"JavaScript click failed on selector [{selector.Name}], falling back to selenium click.", browser, GPALObjectType.Browser);
                                SeleniumClick(browser, webElement, clickType, modifierKeys);
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"JavaScript click failed on selector [{selector.Name}], falling back to Puppeteer click.", browser, GPALObjectType.Browser);
                                PuppeteerClick(browser, webElement, clickType, modifierKeys);
                                dontPublish = true;
                            }
                        }
                    }
                    else if (true == browser.UsePuppeteer)
                    {
                        PuppeteerClick(browser, webElement, clickType, modifierKeys);
                        dontPublish = true;
                    }
                    else
                        SeleniumClick(browser, webElement, clickType, modifierKeys);

                    if (false == dontPublish)
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][{clickType.ToString()}] clicked with modifiers [{GetEnumNames(modifierKeys)}].", browser, GPALObjectType.Browser);

                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to [{clickType.ToString()}] click element [{webElement.TagName}] for selector [{selector.Name}]", browser, GPALObjectType.Browser, ex);
                }
            }
            //else
            //    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][{clickType.ToString()}] is NOT clickable.", browser, GPALObjectType.Browser);

            Thread.Sleep(500);  // KLUDGE: a click can start a navigation that idle network and document.ready miss, so wait before the next request

        }
        /// <summary>
        /// Clicks <paramref name="webElement"/> via the Puppeteer client using the given click type and modifiers.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click to perform (LeftClick, LeftDoubleClick, MiddleClick, RightClick)</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        public static void PuppeteerClick(Browser browser, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            switch (clickType)
            {
                case ClickType.LeftClick:
                    browser.PuppeteerClient.LeftClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
                case ClickType.LeftDoubleClick:
                    browser.PuppeteerClient.LeftDoubleClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
                case ClickType.MiddleClick:
                    browser.PuppeteerClient.MiddleClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
                case ClickType.RightClick:
                    browser.PuppeteerClient.RightClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
            }
        }
        /// <summary>
        /// Clicks <paramref name="webElement"/> using Selenium Actions, retrying with a fresh element lookup if
        /// a stale element exception occurs.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click to perform (LeftClick, LeftDoubleClick, MiddleClick, RightClick)</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        public static void SeleniumClick(Browser browser, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            int retries = 2;
            bool retry = false;
            bool clicked = false;   // set once any click lands, Selenium's or the javascript one
            dynamic retryElement = webElement;   // the GPALElement, so a stale one is found again by its own css or xpath
            Actions action = new Actions(browser.BrowserDriver);
            string errorMessage = $"Failed to {clickType.ToString()} element with text [{webElement.Text}]";

            do
            {
                try
                {
                    switch (clickType)
                    {
                        case ClickType.LeftClick:
                            if (ModifierKeys.NONE == modifierKeys)
                            {
                                webElement.iWebElement.Click();
                            }
                            else
                            {
                                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                                    action.KeyDown(OpenQA.Selenium.Keys.LeftAlt).KeyDown(OpenQA.Selenium.Keys.Alt);
                                if (modifierKeys.HasFlag(ModifierKeys.Control))
                                    action.KeyDown(OpenQA.Selenium.Keys.LeftControl).KeyDown(OpenQA.Selenium.Keys.Control);
                                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                                    action.KeyDown(OpenQA.Selenium.Keys.LeftShift).KeyDown(OpenQA.Selenium.Keys.Shift);
                                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                                    action.KeyDown(OpenQA.Selenium.Keys.Meta);

                                action.Click(webElement.iWebElement);

                                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                                    action.KeyUp(OpenQA.Selenium.Keys.LeftAlt).KeyUp(OpenQA.Selenium.Keys.Alt);
                                if (modifierKeys.HasFlag(ModifierKeys.Control))
                                    action.KeyUp(OpenQA.Selenium.Keys.LeftControl).KeyUp(OpenQA.Selenium.Keys.Control);
                                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                                    action.KeyUp(OpenQA.Selenium.Keys.LeftShift).KeyUp(OpenQA.Selenium.Keys.Shift);
                                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                                    action.KeyUp(OpenQA.Selenium.Keys.Meta);

                                ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                                if (ModifierKeys.NONE != unsupported)
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"LeftClick: [{Browser.GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", browser, GPALObjectType.Browser);

                                action.Build().Perform();
                            }
                            break;
                        case ClickType.LeftDoubleClick:
                            action.DoubleClick(webElement.iWebElement).Perform();
                            break;
                        case ClickType.MiddleClick:
                        {
                            // A real middle click (button 1), as OttoMagic and Puppeteer send. It fires auxclick, so
                            // pages that handle their own clicks see it.
                            PointerInputDevice mouse = new PointerInputDevice(PointerKind.Mouse, "default mouse");
                            ActionSequence middleClick = new ActionSequence(mouse);

                            middleClick.AddAction(mouse.CreatePointerMove(webElement.iWebElement, 0, 0, TimeSpan.Zero));
                            middleClick.AddAction(mouse.CreatePointerDown(MouseButton.Middle));
                            middleClick.AddAction(mouse.CreatePointerUp(MouseButton.Middle));

                            ((IActionExecutor)browser.BrowserDriver).PerformActions(new List<ActionSequence> { middleClick });

                            if (ModifierKeys.NONE != modifierKeys)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium cannot press [{Browser.GetModifierKeys(modifierKeys)}] with a middle click.", browser, GPALObjectType.Browser);

                            break;
                        }
                        case ClickType.RightClick:
                            action.ContextClick(webElement.iWebElement).Perform();
                            break;
                    }
                    clicked = true;
                    retry = false;
                }
                catch (StaleElementReferenceException sereex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, sereex);
                    retry = ResolveWebElementIssue(browser, browser.CurrentUOW, ref retryElement, sereex, ref retries, errorMessage);
                    retries--;
                }
                catch (ElementClickInterceptedException ecieex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, ecieex);

                    // Something covers the element, so Selenium would hit it again. The javascript click is the
                    // recovery; once it lands the element has been clicked and nothing is retried.
                    if (false == GPAL.NoFallbackRecoveryActions)
                        clicked = JavaScriptClick(browser, webElement, clickType, modifierKeys);

                    retry = false;
                }
                catch (ElementNotInteractableException enieex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, enieex);
                    retry = ResolveWebElementIssue(browser, browser.CurrentUOW, ref retryElement, enieex, ref retries, errorMessage);
                    retries--;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    retries = 0;
                    retry = false;
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, ex);
                }
            } while (retry && 0 < retries);

            // Whether the retries ran out or recovery said another try would not help, a click that never landed
            // falls back on javascript. A hidden element, which Selenium will not click, still takes one.
            if (false == clicked && false == GPAL.NoFallbackRecoveryActions)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Falling back on javascript click", webElement, GPALObjectType.Other);
                JavaScriptClick(browser, webElement, clickType, modifierKeys);
            }
        }
        /// <summary>
        /// Perform a javascript click on an element by injecting javascript
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click, mapped to the click, dblclick, contextmenu or auxclick event</param>
        /// <param name="modifierKeys">Modifier key(s) reported as held on the dispatched event</param>
        public static bool JavaScriptClick(Browser browser, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            bool clicked = true;

            string clickString = clickType switch
            {
                ClickType.LeftClick => "click",
                ClickType.LeftDoubleClick => "dblclick",
                ClickType.RightClick => "contextmenu",
                ClickType.MiddleClick => "auxclick",   // This is the real middle-click event
                _ => "click"
            };
            int button = (true == "contextmenu".Equals(clickString) ? 2 : 0);
            bool shiftPressed = 0 != (modifierKeys & ModifierKeys.Shift);
            bool controlPressed = 0 != (modifierKeys & ModifierKeys.Control);
            bool altPressed = 0 != (modifierKeys & ModifierKeys.Alt);

            // Randomize click within 90% of the element's border
            var random = new Random();
            var margin = 0.05f; // 5% margin on each side (90% clickable area)
            var clickAreaWidth = (float)webElement.BoundingRect.Width * (1f - 2 * margin);
            var clickAreaHeight = (float)webElement.BoundingRect.Height * (1f - 2 * margin);
            var x = (float)webElement.BoundingRect.X + (float)webElement.BoundingRect.Width * margin + (float)random.NextDouble() * clickAreaWidth;
            var y = (float)webElement.BoundingRect.Y + (float)webElement.BoundingRect.Height * margin + (float)random.NextDouble() * clickAreaHeight;

            if (true == browser.UseSelenium)
            {
                IWebDriver browserDriver = browser.BrowserDriver;

                string clickScript = $@"
                        var theEvent = document.createEvent(""MouseEvent"");
                        theEvent.initMouseEvent(""{clickString}"", true, true, window, 0, 0, 0, {x}, {y}, {controlPressed.ToString().ToLower()}, {altPressed.ToString().ToLower()}, {shiftPressed.ToString().ToLower()}, false, 0, null);
                        arguments[0].dispatchEvent(theEvent);
                    ";

                try
                {
                    if (2 == button)    // contextmenu will not fire off via javascript, but SHIFT+F10 will but not in headless
                    {
                        JavaScriptFocus(browser, webElement);

                        // the real keyboard on screen; headless or on a hidden desktop, which it cannot reach, Selenium's
                        // context click
                        if (false == browser.BrowserSettings.UseHeadless && false == browser.BrowserSettings.HiddenDesktop)
                            HardwareHelper.SendKey(GPAL.VK_F10, ModifierKeys.Shift);
                        else
                            new Actions(browserDriver).ContextClick((IWebElement)webElement.WebElement).Perform();
                    }
                    else
                        BrowserHelper.ExecuteJavaScript(browser, clickScript, webElement.WebElement); // click on element
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to [{clickString}] element [{webElement.Text}]", webElement, GPALObjectType.Other, ex);
                    return false;
                }
            }
            else if (true == browser.UsePuppeteer)  // OttoMagic never reaches here; it clicks with its own javascript
            {
                string sessionId = browser.PuppeteerCommunicator.GetEffectiveSessionId();

                string selector = webElement.Css;

                string clickScript = null;

                clickScript = $@"
                        (() => {{
                            try {{
                                const sel =`{selector.Replace("'", "\\'")}`;

                                const el = document.querySelector(sel) || document.evaluate(sel, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                                if (!el) return false;

                                // clientX and clientY are viewport coordinates, so the scroll offset is not added
                                const rect = el.getBoundingClientRect();
                                const cx = rect.left + rect.width  / 2;
                                const cy = rect.top  + rect.height / 2;

                                const opts = {{ bubbles: true, cancelable: true, clientX: cx, clientY: cy, view: window, ctrlKey: {controlPressed.ToString().ToLower()}, altKey: {altPressed.ToString().ToLower()}, shiftKey: {shiftPressed.ToString().ToLower()} }};

                                // === LEFT CLICK (or double) ===
                                if ({(clickType == ClickType.LeftClick || clickType == ClickType.LeftDoubleClick).ToString().ToLower()}) {{
                                    const count = {(clickType == ClickType.LeftDoubleClick ? 2 : 1)};
                                    for (let i = 0; i < count; i++) {{
                                        el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 0, buttons: 1, detail: i+1}}));
                                        el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 0, buttons: 0, detail: i+1}}));
                                        el.dispatchEvent(new MouseEvent('click',     {{...opts, button: 0, detail: i+1}}));
                                    }}
                                    if (2 === count)
                                        el.dispatchEvent(new MouseEvent('dblclick', {{...opts, button: 0, detail: 2}}));
                                    return true;
                                }}

                                // === MIDDLE CLICK (eBay pop-under) ===
                                if ({(clickType == ClickType.MiddleClick).ToString().ToLower()}) {{
                                    el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 1, buttons: 4}}));
                                    el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 1, buttons: 0}}));
                                    el.dispatchEvent(new MouseEvent('click',     {{...opts, button: 1}}));
                                    return true;
                                }}

                                // === RIGHT CLICK (context menu) ===
                                if ({(clickType == ClickType.RightClick).ToString().ToLower()}) {{
                                    el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 2, buttons: 2}}));
                                    el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 2, buttons: 0}}));
                                    el.dispatchEvent(new MouseEvent('contextmenu', {{...opts, button: 2}}));
                                    return true;
                                }}

                                return false;
                            }} catch(e) {{
                                return false;
                            }}
                        }})();
                ";

                dynamic evaluated = browser.PuppeteerCommunicator.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = clickScript,
                    returnByValue = true
                }, sessionId).GetAwaiter().GetResult();

                // false when the element was not found or the script threw
                clicked = true == "true".Equals(evaluated?.result?.value?.ToString(), StringComparison.OrdinalIgnoreCase);

                //clickScript =
                //    $@"
                //        var theEvent = document.createEvent(""MouseEvent"");
                //        theEvent.initMouseEvent(""{clickString}"", true, true, window, 0, 0, 0, {x}, {y}, {controlPressed.ToString().ToLower()}, {altPressed.ToString().ToLower()}, {shiftPressed.ToString().ToLower()}, false, 0, null);
                //               let el = null;
                //                const sel = `{selector.Replace("'", "\\'")}`;

                //                const el = document.querySelector(sel) || document.evaluate(sel, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                //                if (!el) return false;
                //                el.dispatchEvent(theEvent);
                //    ";


                //browser.PuppeteerCommunicator.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                //{
                //    expression = clickScript,
                //    returnByValue = true
                //}, sessionId).GetAwaiter().GetResult();

            }

            return clicked;
        }

        /// <summary>
        /// Clicks <paramref name="element"/> using hardware mouse emulation at its computed screen coordinates,
        /// holding any requested modifier keys. Falls back to a JavaScript/Puppeteer/OttoMagic/Selenium left
        /// click if the computed coordinates look invalid.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element (or pseudo-element from an image match) to click</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        /// <param name="selector">Selector providing the offset and simulate-mouse settings</param>
        /// <param name="isClickable">Unused</param>
        public static void HardwareClick(Browser browser, dynamic element, ClickType clickType, ModifierKeys modifierKeys, Selector selector, bool isClickable = false)
        {
            Rectangle rect, iframeRect = new Rectangle(0, 0, 0, 0);
            bool clicked = false;
            _lastPlacementQuestionable = false;
            _lastClampedPoint = null;

            // Held across topping and clicking, which are one operation
            lock (hardwareMouse)
            try
            {
                BrowserHelper.TopBrowser(browser, false);

                if (ModifierKeys.NONE != modifierKeys)
                    HardwareHelper.PressModifierKey(modifierKeys);

                // HardwareMoveTo computes the screen point and moves the mouse there, with a simulated move when
                // the selector asks for one; the click uses the rectangle it returns
                rect = HardwareMoveTo(browser, element, selector);

                // The point must be inside the content area on every side; a positive-only check lets a
                // below-the-fold element through and the click lands on the taskbar or a window behind. Image
                // matches are already screen positions and skip this. Reuse the content area HardwareMoveTo just
                // measured rather than three more browser calls.
                Rectangle browserRect = _lastContentArea;
                bool insideBrowser = 0 < rect.Width && 0 < rect.Height
                                     && rect.X > browserRect.Left && rect.X < browserRect.Right
                                     && rect.Y > browserRect.Top && rect.Y < browserRect.Bottom;

                if ("GPALElement" == element.TagName || true == insideBrowser)
                {
                    // will do a jump to x,y
                    HardwareHelper.HardwareClick(rect.X, rect.Y, clickType);
                    clicked = true;
                }
                else
                {
                    // The element's screen placement is not trustworthy (off-screen/odd coords) so we could not
                    // perform a real hardware click. Flag it so a following hardware fill-in refuses to type.
                    _lastPlacementQuestionable = true;

                    if (false == GPAL.NoFallbackRecoveryActions)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element [{element.TagName}] for selector [{selector?.Name}] computed to [{rect.X}, {rect.Y}, {rect.Width}, {rect.Height}], outside the browser window [{browserRect.Left}, {browserRect.Top}, {browserRect.Right}, {browserRect.Bottom}]. No hardware click, falling back on javascript click.", browser, GPALObjectType.Browser);

                        if (ModifierKeys.NONE != modifierKeys)
                            HardwareHelper.ReleaseModifierKeys(modifierKeys);

                        if (true == browser.UseOttoMagic)
                            browser.MagicHelper.LeftClick(element.Css);
                        else if (true == browser.UsePuppeteer)
                            browser.PuppeteerClient.LeftClick(element).Execute();
                        else
                            JavaScriptClick(browser, element, clickType, modifierKeys);

                        clicked = true;
                    }
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Unable to get screen coordinates for webelement, continuing.", browser, GPALObjectType.Browser);
                }
            }
            catch
            {
            }
            finally
            {
                // Publish the click result, including the point if HardwareMoveTo adjusted it, so the workflow can
                // set that offset itself.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG,        // null == _lastClampedPoint ? GPALEventType.INFO : GPALEventType.WARNING,
                    $"Element [{element.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][{clickType.ToString()}]{(true == clicked ? "" : " [NOT]")} clicked{(null == _lastClampedPoint ? "" : " " + _lastClampedPoint)}.", browser, GPALObjectType.Browser);

                if (ModifierKeys.NONE != modifierKeys)
                    HardwareHelper.ReleaseModifierKeys(modifierKeys);

                // BrowserHelper.TopBrowser(browser, true);    // just move the mouse to a safe area off the browser area that can cause hover issues
            }
        }

        /// <summary>
        /// Drags each element matched by the current unit of work's selectors by the selector's DeltaX/DeltaY,
        /// using OttoMagic, Puppeteer, Selenium Actions, or hardware mouse emulation for image-matched elements.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="modifierKeys">Unused</param>
        public static void DragAndDrop(IBrowser browser, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            var driver = browser.BrowserDriver;
            Rectangle iframeRect = new Rectangle(0, 0, 0, 0);

            List<GPALElement> matchedElements;

            foreach (Selector selector in ((Browser)browser).CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements((Browser)browser, ((Browser)browser).CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler((Browser)browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALElement elem in elems)
                {
                    if (true == ((Browser)browser).BrowserSettings.UseHardware || InteractionType.Hardware == selector.InteractionType)
                    {
                        // Hardware drag: press, move the cursor smoothly (wind mouse) to the drop point, release. A
                        // remote desktop session can drop a drag whose cursor jumps. Holds HardwareClick's lock for the
                        // whole drag.
                        lock (hardwareMouse)
                        try
                        {
                            BrowserHelper.TopBrowser((Browser)browser, false);

                            // element location calculations handled in hardwaremoveto (iframe nesting, browser chrome,
                            // random in-element point when no offset), we use the rectangle computed there as the grab point
                            Rectangle rect = HardwareMoveTo((Browser)browser, elem, selector);

                            if ("GPALElement" == elem.TagName || (0 <= rect.Width && 0 <= rect.Height && 0 < rect.X && 0 < rect.Y))
                            {
                                int endX = rect.X + selector.DeltaX;
                                int endY = rect.Y + selector.DeltaY;

                                HardwareHelper.MouseDown(rect.X, rect.Y, ClickType.LeftClick);
                                HardwareHelper.MoveMouse(endX, endY, 4, 4, 0.5);
                                HardwareHelper.MouseUp(endX, endY, ClickType.LeftClick);
                            }
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Odd screen coordinates [{rect.X}, {rect.Y}, {rect.Width}, {rect.Height}] for hardware drag and drop, skipping element.", browser, GPALObjectType.Browser);
                        }
                        finally
                        {
                            // BrowserHelper.TopBrowser((Browser)browser, true);    // just move the mouse to a safe area off the browser area that can cause hover issues
                        }
                    }
                    else if (true == browser.UseOttoMagic)
                    {
                        browser.MagicHelper.DragAndDrop(elem.Css, selector.DeltaX, selector.DeltaY, selector.OffsetX, selector.OffsetY);
                    }
                    else if (true == browser.UsePuppeteer)
                    {
                        browser.PuppeteerClient.DragAndDrop(elem.ElementHandle).WithDeltaX(selector.DeltaX).WithDeltaY(selector.DeltaY).WithOffsetX(selector.OffsetX).WithOffsetY(selector.OffsetY).Execute();
                    }
                    else if (true == browser.UseSelenium)    // real WebElement, selenium has a handle to drag
                    {
                        // Firefox starts a native drag only from real OS input, so Selenium's pointer events never
                        // fire dragstart on an HTML5 draggable. Chrome and Edge do. So JavaScriptDragAndDrop only on
                        // Firefox, only for draggable="true". Mouse-driven dragging (map panning, jQuery UI) works
                        // with Actions everywhere.
                        if (BrowserType.FireFox == ((Browser)browser).BrowserSettings.BrowserType
                            && "true" == elem.WebElement?.GetAttribute("draggable"))
                            JavaScriptDragAndDrop((Browser)browser, elem, selector.DeltaX, selector.DeltaY, selector.OffsetX, selector.OffsetY);
                        else
                        {
                            // Press at the offset from the element's top-left, or its centre with no offset, as
                            // OttoMatic records it and the other engines use it. Then move in ten steps and release, as
                            // the Puppeteer drag does; one move would be sent by ChromeDriver as a single jump, so the
                            // page would see the element go straight to the drop point
                            const int steps = 10;
                            bool fromCentre = 0 == selector.OffsetX && 0 == selector.OffsetY;

                            // the element's place in the viewport and the viewport's size, which WebDriver moves are
                            // measured in
                            IList<object> box = (IList<object>)((IJavaScriptExecutor)driver).ExecuteScript(
                                "const r = arguments[0].getBoundingClientRect(); return [r.left, r.top, r.width, r.height, window.innerWidth, window.innerHeight];",
                                elem.iWebElement);

                            int left = (int)Math.Ceiling(Convert.ToDouble(box[0]));
                            int top = (int)Math.Ceiling(Convert.ToDouble(box[1]));
                            int right = (int)Math.Floor(Convert.ToDouble(box[0]) + Convert.ToDouble(box[2])) - 1;
                            int bottom = (int)Math.Floor(Convert.ToDouble(box[1]) + Convert.ToDouble(box[3])) - 1;
                            int viewWidth = Convert.ToInt32(box[4]);
                            int viewHeight = Convert.ToInt32(box[5]);

                            int startX = (int)(Convert.ToDouble(box[0]) + (true == fromCentre ? Convert.ToDouble(box[2]) / 2 : selector.OffsetX));
                            int startY = (int)(Convert.ToDouble(box[1]) + (true == fromCentre ? Convert.ToDouble(box[3]) / 2 : selector.OffsetY));
                            int askedX = startX;
                            int askedY = startY;

                            // WebDriver cannot move the pointer outside the page. When the drop point would be past an
                            // edge, the grab point moves inside the element, by as little as it takes, so the whole
                            // delta still fits: the start must be on the element and in the page, and so must start
                            // plus delta
                            int lowX = Math.Max(left, Math.Max(0, -selector.DeltaX));
                            int highX = Math.Min(right, Math.Min(viewWidth - 1, viewWidth - 1 - selector.DeltaX));
                            int lowY = Math.Max(top, Math.Max(0, -selector.DeltaY));
                            int highY = Math.Min(bottom, Math.Min(viewHeight - 1, viewHeight - 1 - selector.DeltaY));

                            if (lowX <= highX)
                                startX = Math.Max(lowX, Math.Min(highX, startX));
                            if (lowY <= highY)
                                startY = Math.Max(lowY, Math.Min(highY, startY));

                            if (startX != askedX || startY != askedY)
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The drag for selector [{selector.Name}] grabs at [{startX}, {startY}] instead of [{askedX}, {askedY}], so its drop point is inside the page", browser, GPALObjectType.Browser);

                            // Only when no grab point on the element leaves room for the whole delta is the drop point
                            // brought back to the edge
                            int endX = Math.Max(0, Math.Min(viewWidth - 1, startX + selector.DeltaX));
                            int endY = Math.Max(0, Math.Min(viewHeight - 1, startY + selector.DeltaY));

                            if (endX != startX + selector.DeltaX || endY != startY + selector.DeltaY)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium cannot move the pointer outside the page, and no point on the element leaves room for the whole drag, so the drag for selector [{selector.Name}] stops at [{endX}, {endY}] instead of [{startX + selector.DeltaX}, {startY + selector.DeltaY}]", browser, GPALObjectType.Browser);

                            PointerInputDevice mouse = new PointerInputDevice(PointerKind.Mouse, "default mouse");
                            ActionSequence drag = new ActionSequence(mouse);

                            drag.AddAction(mouse.CreatePointerMove(CoordinateOrigin.Viewport, startX, startY, TimeSpan.Zero));
                            drag.AddAction(mouse.CreatePointerDown(MouseButton.Left));

                            for (int step = 1; step <= steps; step++)
                                drag.AddAction(mouse.CreatePointerMove(CoordinateOrigin.Viewport,
                                    startX + (endX - startX) * step / steps,
                                    startY + (endY - startY) * step / steps,
                                    TimeSpan.FromMilliseconds(15)));

                            drag.AddAction(mouse.CreatePointerUp(MouseButton.Left));

                            ((IActionExecutor)driver).PerformActions(new List<ActionSequence> { drag });
                        }
                    }

                    if (++rowCount >= ((Browser)browser).CurrentUOW.WithAllThatMatch)
                        break;
                }
            }
        }

        /// <summary>
        /// Performs an HTML5 drag and drop by dispatching the drag event sequence directly, rather than by
        /// moving a mouse. All five events share one DataTransfer, so whatever the page stashes during its own
        /// dragstart handler is still readable by its drop handler. Used for Firefox, whose native drag service
        /// only starts from real OS input and therefore never sees Selenium's synthesized pointer events.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to drag</param>
        /// <param name="deltaX">Horizontal distance, in pixels, to drop from the grab point</param>
        /// <param name="deltaY">Vertical distance, in pixels, to drop from the grab point</param>
        /// <param name="offsetX">Horizontal grab offset, in pixels, from the element's center</param>
        /// <param name="offsetY">Vertical grab offset, in pixels, from the element's center</param>
        /// <returns>True if the sequence was dispatched, false if nothing sat at the drop point</returns>
        public static bool JavaScriptDragAndDrop(Browser browser, GPALElement element, int deltaX, int deltaY, int offsetX, int offsetY)
        {
            // this can happen with a gpalelement
            if (null == element?.WebElement)
                return false;

            ScrollIntoView(browser, element);

            // grab point is the element center plus the offset, matching the puppeteer drag and drop
            string javaScript = @"
                var src = arguments[0];
                var offsetX = arguments[1], offsetY = arguments[2];
                var deltaX = arguments[3], deltaY = arguments[4];

                var rect = src.getBoundingClientRect();
                var startX = rect.left + rect.width / 2 + offsetX;
                var startY = rect.top + rect.height / 2 + offsetY;
                var endX = startX + deltaX;
                var endY = startY + deltaY;

                var target = document.elementFromPoint(endX, endY);

                if (!target)
                    return 'nothing at drop point ' + endX + ',' + endY;

                var dt = new DataTransfer();

                function fire(type, el, x, y) {
                    el.dispatchEvent(new DragEvent(type, {
                        bubbles: true, cancelable: true, composed: true,
                        clientX: x, clientY: y, dataTransfer: dt
                    }));
                }

                fire('dragstart', src, startX, startY);
                fire('dragenter', target, endX, endY);
                fire('dragover', target, endX, endY);
                fire('drop', target, endX, endY);
                fire('dragend', src, endX, endY);

                return 'ok';
            ";

            string result = BrowserHelper.ExecuteJavaScript(browser, javaScript, element.WebElement, offsetX, offsetY, deltaX, deltaY);

            if ("ok" != result?.Trim())
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"HTML5 drag and drop did not complete for selector [{element.Selector?.Name}] [{result}].", browser, GPALObjectType.Browser);
                return false;
            }

            return true;
        }
        /// <summary>
        /// Last-resort hover fallback: scrolls <paramref name="element"/> into view and dispatches a synthetic
        /// "mouseover" event on it via JavaScript. This only fires JS mouseover/mouseenter listeners - it does
        /// NOT update the renderer's CSS <c>:hover</c> state, since no real input event occurred. Used by
        /// <see cref="MoveTo(Browser)"/> only when no engine capable of a real cursor move (Hardware, OttoMagic,
        /// Puppeteer, or a non-headless Selenium session) is available.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to dispatch the mouseover event on</param>
        public static void JavaScriptHover(Browser browser, GPALElement element)
        {
            // this can happen with a gpalelement
            if (null == element.WebElement)
                return;

            ScrollIntoView(browser, element);

            // dispatch a mouseover event on the targetted element which is hover, not focus
            String javaScript = "var evObj = document.createEvent('MouseEvents');"
            + "evObj.initMouseEvent(\"mouseover\",true, false, window, 0, 0, 0, 0, 0, false, false, false, false, 0, null);"
            + "arguments[0].dispatchEvent(evObj);";

            BrowserHelper.ExecuteJavaScript(browser, javaScript, element.WebElement);
        }
    }
}
