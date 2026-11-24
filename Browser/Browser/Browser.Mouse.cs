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
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for the first element found for each selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClick()
        {
            return LeftClick(ModifierKeys.NONE);
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for each element found for selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClick(ModifierKeys modifierKeys)
        {
            string priorUrl = BrowserSettings.CurrentURL;
            object priorDocument = BrowserHelper.MarkDocument(this);   // the page we are clicking on, to tell later if we left it

            // snapshot open handles before the click so we can tell if the click opened a new tab (target=_blank)
            var handlesBefore = (true == UseSelenium) ? new HashSet<string>(BrowserDriver.WindowHandles) : null;

            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.LeftClick(this, modifierKeys);

            if (true == UseSelenium)
            {
                ResolveGhostTabHandle();   // make sure Edge's phantom tab is identified before we count new tabs

                // in case this opens a new tab
                var handles = BrowserDriver.WindowHandles.ToList();
                string currentHandle = BrowserDriver.CurrentWindowHandle;

                CurrentTabIdx = handles.IndexOf(currentHandle);

                // reliable our-tabs counter: add only genuinely new tabs this click opened, never Edge's ghost
                int opened = 0;

                foreach (string handle in handles.Where(h => false == handlesBefore.Contains(h) && h != GhostTabHandle))
                {
                    RecordTabWindow(handle, currentHandle);   // a click opens into the window it was clicked in
                    opened++;
                }

                if (0 < opened) tabsWeOpened += opened;
            }

            // The click returns before the url changes; wait up to NavigationGraceMs for a navigation.
            if (true == BrowserHelper.WaitForNavigationOrTime(this, priorUrl, priorDocument, BrowserSettings.NavigationGraceMs))
            {
                BrowserHelper.CheckDocumentReady(this);

                // read the url again now the document is committed, not while it was still tearing down
                string currentUrl = GetSetCurrentUrl();

                if (true == UseOttoMagic)
                {
                    int tabId = GetActiveTabId();
                    TabTuple tabTuple = new TabTuple() { TabId = tabId, Url = currentUrl };
                    UpdateActiveTab(tabTuple);
                }
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else
                    BrowserDriver.SwitchTo().DefaultContent();
            }

            return this;
        }
        /// <summary>
        /// Left click the element(s) found for Selector parameter, creates a UOW of 1 element<br/>
        /// One left click w/ no modiofiers pressed for each element found for the selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClick(Selector selector)
        {
            if (null != selector)
                WithSelector(selector);

            return LeftClick(ModifierKeys.NONE);
        }
        /// <summary>
        /// Clicks the option element(s) found for the current selector(s) using the given select-click behavior
        /// (e.g. SelectRandom, SelectNext).
        /// </summary>
        /// <param name="selectClickType">Select-menu click behavior</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SelectClick(SelectClickType selectClickType)
        {
            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                string priorUrl = BrowserSettings.CurrentURL;
                object priorDocument = BrowserHelper.MarkDocument(this);   // the page we are clicking on, to tell later if we left it
                ElementHelper.GenericClick(this, selectClickType);

                if (true == BrowserHelper.WaitForNavigationOrTime(this, priorUrl, priorDocument, BrowserSettings.NavigationGraceMs))
                    BrowserHelper.CheckDocumentReady(this);
            }

            GetSetCurrentUrl();

            return this;
        }
        /// <summary>
        /// Stealth (Runtime.disable before) Left click the element(s) found for Selector parameter, creates a UOW of 1 element<br/>
        /// One left click w/ no modifiers pressed for each element found for the selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector StealthLeftClick(Selector selector)
        {
            if (null != selector)
                WithSelector(selector);

            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                // OttoMagic's normal click is an in-page script event, so ask the extension for a trusted click.
                // Puppeteer's click is already real input and Runtime is never enabled there, so it is left alone.
                if (true == UseOttoMagic)
                    ElementHelper.LeftClick(this, trusted: true);
                else if (true == UsePuppeteer)
                    ElementHelper.LeftClick(this);
                else if (BrowserType.FireFox != BrowserSettings.BrowserType)
                {
                    BrowserHelper.ExecuteCdp(BrowserDriver, "Runtime.disable", new Dictionary<string, object> { });
                    ElementHelper.LeftClick(this);
                    BrowserHelper.ExecuteCdp(BrowserDriver, "Runtime.enable", new Dictionary<string, object> { });
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot disable the runtime around a click, so this is an ordinary left click", this, GPALObjectType.Browser);
                    ElementHelper.LeftClick(this);
                }
            }

            GetSetCurrentUrl();

            return this;
        }
        /// <summary>
        /// Left double click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for each element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftDoubleClick()
        {
            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.GenericClick(this, ClickType.LeftDoubleClick);
                BrowserHelper.CheckDocumentReady(this);
            }
            // A double click does not reread the url, so a navigation it starts is not recorded.
            return this;
        }
        /// <summary>
        /// Midle click the element found for Selector(s) defined in the UOW<br/>
        /// One middle click for each element found for each selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector MiddleClick()
        {
            string priorUrl = BrowserSettings.CurrentURL;

            // snapshot open handles before the click so we can tell which tab(s) the middle-click opened
            var handlesBefore = (true == UseSelenium) ? new HashSet<string>(BrowserDriver.WindowHandles) : null;

            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.MiddleClick(this);

            string currentUrl = GetSetCurrentUrl();

            if (true == UseSelenium)
            {
                ResolveGhostTabHandle();   // make sure Edge's phantom tab is identified before we count new tabs

                // in case this opens a new tab
                var handles = BrowserDriver.WindowHandles.ToList();
                string currentHandle = BrowserDriver.CurrentWindowHandle;

                CurrentTabIdx = handles.IndexOf(currentHandle);

                // reliable our-tabs counter: add only genuinely new tabs this middle-click opened, never Edge's ghost
                int opened = 0;

                foreach (string handle in handles.Where(h => false == handlesBefore.Contains(h) && h != GhostTabHandle))
                {
                    RecordTabWindow(handle, currentHandle);   // a middle click opens into the window it was clicked in
                    opened++;
                }

                if (0 < opened) tabsWeOpened += opened;
            }

            if (false == UrlHelper.AreEquivalent(priorUrl, currentUrl))
            {
                if (true == UseOttoMagic)
                {
                    int tabId = GetActiveTabId();
                    TabTuple tabTuple = new TabTuple() { TabId = tabId, Url = currentUrl };
                    UpdateActiveTab(tabTuple);
                }
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else
                    BrowserDriver.SwitchTo().DefaultContent();
            }

            return this;
        }
        /// <summary>
        /// Right click the element found for Selector(s) defined in the UOW<br/>
        /// One right click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RightClick(Selector selector = null)
        {
            if (null != selector)
            {
                // headless has no hardware input, so the selector uses Selenium whatever it asked for
                if (true == BrowserSettings.UseHeadless)
                    selector.InteractionType = InteractionType.Selenium;
                WithSelector(selector);
            }
            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.GenericClick(this, ClickType.RightClick);
                BrowserHelper.CheckDocumentReady(this);
            }
            // A right click does not reread the url, so a navigation it starts is not recorded.
            return this;
        }

        /// <summary>
        /// Fluent alias for <see cref="MoveTo(Selector)"/>. Moving the mouse to an element is a real input-layer
        /// event on every engine, so the browser naturally updates CSS <c>:hover</c> state and fires
        /// mouseenter/mouseover/mouseleave - this is what reveals hover-triggered menus and tooltips. There is
        /// no separate "hover" operation; <c>Hover</c> exists only because it's the more discoverable name for
        /// this intent.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Hover(Selector selector = null)
        {
            return MoveTo(selector);
        }
        /// <summary>
        /// Moves the mouse to the current element found via the Selector. Only the first element per Selector found will be 'moved to'.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, the mouse is moved to hover over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then MoveTo will move the mouse over {num} elements that matches the Selector(s) in the current UOW.<br/>
        /// How and where on the page an element is moved to differs depending upon the technique: Hardware, OttoMagic, Puppeteer or Selenium all
        /// perform a real cursor move (triggering CSS :hover and mouseover/mouseenter/mouseleave). As a last resort, when no real cursor is
        /// available (e.g. plain Selenium in headless mode), a synthetic "mouseover" event is dispatched via JavaScript - this only fires JS
        /// mouseover listeners and does NOT trigger CSS :hover.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector MoveTo(Selector selector = null)
        {
            if (null != selector)
                WithSelector(selector);

            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.MoveTo(this);
            return this;
        }

        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for the first element found for each selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector DragAndDrop(ModifierKeys modifierKeys)
        {
            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.DragAndDrop(this, modifierKeys);
            }
            return this;
        }

        /// <summary>
        /// Drags the element(s) found for <paramref name="selector"/> (or the current selector(s) if none is
        /// given) by the selector's DeltaX/DeltaY.
        /// </summary>
        /// <param name="selector">Optional selector to add to the current unit of work before dragging</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector DragAndDrop(Selector selector = null)
        {
            if (null != selector)
                WithSelector(selector);

            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.DragAndDrop(this);
            }
            return this;
        }
    }
}
