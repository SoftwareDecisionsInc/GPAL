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
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;
using static GenerallyPositive.Browser.Browser;
using GenerallyPositive.Browser;
using System.Windows.Automation;
using GenerallyPositive.Application;
using System.Reflection;
using static GenerallyPositive.SelectorSettings;

namespace GenerallyPositive
{
    internal class SmallSelectorNode
    {
        public InteractionType InteractionType { get; internal set; }
        public int OffsetX { get; internal set; }
        public int OffsetY { get; internal set; }
        public int DeltaX { get; internal set; }
        public int DeltaY { get; internal set; }
    }
    /// <summary>
    /// GPAL Selector used to locate Application and Browser elements.<br/>
    /// Instantiated with GPAL.Selector
    /// </summary>
    public class Selector : IAllowSelectorSettings
    {
        // deep copy cannot have property passed as ref
        internal SelectorSettings selectorSettings = null;
        public SelectorSettings SelectorSettings
        {
            get 
            {
                return selectorSettings;
            }
            internal set
            {
                selectorSettings = value;
            }
        }

        public SelectorType SelectorType
        {
            get
            {
                return SelectorSettings.SelectorType;
            }

            set
            {
                selectorSettings.SelectorType = value;
            }
        }
        private Application.Application MyApplication { get; set; } = null;

        public Selector ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Custom matching function, must return all matched elements and an indicator as to whether all elements matched (for the purpose of the custom match)<br/>
        /// More than one custom matching method can be define, they will be chained based upon the return value.
        /// </summary>
        /// <param name="foundElements">All elements found for the selector</param>
        /// <param name="matchedElements">All matched elements (or all found)</param>
        /// <param name="matchedAll">True if all found elements matched.<br/>False for only partial match.</param>
        /// <param name="selector">The selector used for the found elements</param>
        /// <param name="ExactMatch">True to match the string exactly, false to match when the element string contains it</param>
        /// <returns> 0: not handled, try next handler<br/>1: handled, do not try another handler<br/>-2: we don't like this set, try the next selector path<br/>-1: error/exit</returns>
        public delegate CallIfStatus webMatchingFunction(List<IGPALElement> foundElements, out List<IGPALElement> matchedElements, out bool matchedAll, Selector selector, bool ExactMatch = true);

        /// <summary>
        /// Custom matching function, must return all matched elements and an indicator as to whether all elements matched (for the purpose of the custom match)<br/>
        /// More than one custom matching method can be define, they will be chained based upon the return value.
        /// </summary>
        /// <param name="foundElements">All elements found for the selector</param>
        /// <param name="matchedElements">All matched elements (or all found)</param>
        /// <param name="matchedAll">True if all found elements matched.<br/>False for only partial match.</param>
        /// <param name="selector">The selector used for the found elements</param>
        /// <param name="ExactMatch">True to match the string exactly, false to match when the element string contains it</param>
        /// <returns> 0: not handled, try next handler<br/>1: handled, do not try another handler<br/>-2: we don't like this set, try the next selector path<br/>-1: error/exit</returns>
        public delegate CallIfStatus appMatchingFunction(List<GPALAutomationElement> foundElements, out List<GPALAutomationElement> matchedElements, out bool matchedAll, Selector selector, bool ExactMatch = true);


        internal Selector(Browser.Browser browser, Selector selector)
        {
            SelectorSettings = new SelectorSettings();

            Browser = browser;
            selectorSettings = ObjectCopier.DeepCopyObject(selector.SelectorSettings, selectorSettings, DeepCopy.FieldsAndProperties);
            if (true == string.IsNullOrEmpty(selector.Name))
                Name = $"Selector{browser.CurrentUOW.WithSelectorList.Count}";
            else
                Name = selector.Name;

            ElementsFoundAndMatchedCount = 0;
        }
        internal Selector(Browser.Browser browser)
        {
            Browser = browser;
            SelectorSettings = new SelectorSettings();
            Name = $"Selector{browser.CurrentUOW.WithSelectorList.Count}";
        }

        internal Selector(Application.Application application, Selector selector)
        {
            SelectorSettings = new SelectorSettings();

            MyApplication = application;
            selectorSettings = ObjectCopier.DeepCopyObject(selector.SelectorSettings, selectorSettings, DeepCopy.FieldsAndProperties);
            if (true == string.IsNullOrEmpty(selector.Name))
                Name = $"Selector{application.CurrentUOW.WithSelectorList.Count}";
            else
                Name = selector.Name;

            ElementsFoundAndMatchedCount = 0;
        }
        internal Selector(Application.Application application)
        {
            MyApplication = application;
            SelectorSettings = new SelectorSettings();
            Name = $"Selector{application.CurrentUOW.WithSelectorList.Count}";
        }
        internal Selector()
        {
            SelectorSettings = new SelectorSettings();
        }
        internal bool AnyMatchCriteria()
        {
            return 0 < MatchCriteria.Count;
        }

        #region <Fluent Settings>
        /// <summary>
        /// Define CSS used to find elements.<br/>
        /// Browser only.
        /// </summary>
        /// <param name="css">The CSS</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithCSS([Css] string css)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = css, SelectorPathType = SelectorPathType.Css };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define XPath used to find elements<br/>
        /// Both.
        /// </summary>
        /// <param name="xPath">The XPath</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithXPath([XPath] string xPath)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = xPath, SelectorPathType = SelectorPathType.Xpath };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define Text used to find elements by text attribute.<br/>
        /// Browser only.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithText(string text)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = text, SelectorPathType = SelectorPathType.Text };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define Value used to find elements by value attribute.<br/>
        /// Browser only.
        /// </summary>
        /// <param name="value">The value</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithValue(string value)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = value, SelectorPathType = SelectorPathType.Value };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define Name used to find elements by name attribute.<br/>
        /// Application only.
        /// </summary>
        /// <param name="name">The name</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithName(string name)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = name, SelectorPathType = SelectorPathType.Name };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define Name used to find elements by name attribute.<br/>
        /// Application only.
        /// </summary>
        /// <param name="className">The class name</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithClassName(string className)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = className, SelectorPathType = SelectorPathType.ClassName };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define Name used to find elements by name attribute.<br/>
        /// Application only.
        /// </summary>
        /// <param name="automationID">The AutomationID</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithAutomationID(string automationID)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = automationID, SelectorPathType = SelectorPathType.AutomationID };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define the placeholder used to find elements, matched in full against the placeholder attribute.<br/>
        /// Browser only.
        /// </summary>
        /// <param name="placeholder">The placeholder, as the page has it</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithPlaceholder(string placeholder)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = placeholder, SelectorPathType = SelectorPathType.PlaceHolder };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Define an image to match on-screen.<br/>
        /// The top left corner is used as the start of the element.<br/>
        /// Image matching forces hardware interaction with this element.
        /// </summary>
        /// <param name="base64String">The image to match, as a base64 string</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithImage(string base64String)
        {
            try
            {
                Image image = ImageHelper.Base64ToImage(base64String);
                SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPathType = SelectorPathType.Image, Image = image };
                SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            } catch
            {
                // try passing the string as a filename, it's not base64
                return WithImage((GPALFile)base64String);
            }
            return this;
        }
        /// <summary>
        /// Define an image to match on-screen.<br/>
        /// The top left corner is used as the start of the element.<br/>
        /// Image matching forces hardware interaction with this element.
        /// </summary>
        /// <param name="image">The image to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithImage(Image image)
        {
            SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPathType = SelectorPathType.Image, Image = image };
            SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            return this;
        }
        /// <summary>
        /// Search for a matching image when using hardware emulation or image matching.<br/>
        /// Finding an element on page involves scrolling down the page (sending Page Down key presses).<br/>
        /// </summary>
        /// <param name="searchForElement">True to search for the element/image<br/>False to just look on the current page.</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithSearchForSelector(bool searchForElement)
        {
            SelectorSettings.SearchForSelector = searchForElement;
            return this;
        }
        /// <summary>
        /// A common name for this selector.<br/>
        /// Used in Information and Exception channel messages.
        /// </summary>
        /// <param name="name">The selector name</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithSelectorName(string name)
        {
            SelectorSettings.Name = name;
            return this;
        }
        /// <summary>
        /// Define the x offset from the Top Left corner to perform clicks.
        /// </summary>
        /// <param name="deltaX">How far to drag horizontally from where the element was grabbed, in pixels</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithDeltaX([Pixels] int deltaX)
        {
            SelectorSettings.DeltaX = deltaX;
            return this;
        }
        /// <summary>
        /// Define the y offset from the Top Left corner to perform clicks.
        /// </summary>
        /// <param name="deltaY">How far to drag vertically from where the element was grabbed, in pixels</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithDeltaY([Pixels] int deltaY)
        {
            SelectorSettings.DeltaY = deltaY;
            return this;
        }
        /// <summary>
        /// Define the x offset from the Top Left corner to perform clicks.
        /// </summary>
        /// <param name="offsetX">X offset</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithOffsetX([Pixels] int offsetX)
        {
            SelectorSettings.OffsetX = offsetX;
            return this;
        }
        /// <summary>
        /// Define the y offset from the Top Left corner to perform clicks.
        /// </summary>
        /// <param name="offsetY">Y offset</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithOffsetY([Pixels] int offsetY)
        {
            SelectorSettings.OffsetY = offsetY;
            return this;
        }
        /// <summary>
        /// Simulate a human moving the mouse when interacting with this element. Implies hardware input.
        /// </summary>
        /// <param name="moveWithArc">True to move the mouse to the element.<br/>False [default] jump the mouse to the element.</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithSimulateMouse(bool moveWithArc)
        {
            SelectorSettings.SimulateMouse = moveWithArc;
            SelectorSettings.InteractionType = InteractionType.Hardware;
            SelectorSettings.RequestedInteractionType = InteractionType.Hardware;
            return this;
        }
        public IAllowSelectorSettings UseAttribute(string attributeName)
        {
            SelectorSettings.AttributeName = attributeName;
            return this;
        }
        /// <summary>
        /// Your custom application element matching function to examine found elements and return only those you 'match'
        /// </summary>
        /// <param name="matchingFunction">Your matching function</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchFunction(Selector.appMatchingFunction matchingFunction)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Custom, AppMatchingFunction = matchingFunction };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Your custom web element  matching function to examine found elements and return only those you 'match'
        /// </summary>
        /// <param name="matchingFunction">Your matching function</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchFunction(Selector.webMatchingFunction matchingFunction)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Custom, WebMatchingFunction = matchingFunction };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must have attribute supplied in order to interact with them.
        /// </summary>
        /// <param name="attributeToMatch">text to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchAttribute(string attributeToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Attribute, StringToMatch = attributeToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must have the attribute specified by regular expression in order to interact with them.
        /// </summary>
        /// <param name="attributeToMatch">text to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchAttributeRegex(string attributeToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Regex | MatchType.Attribute, StringToMatch = attributeToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the HRef supplied in order to interact with them.
        /// </summary>
        /// <param name="hrefToMatch">HRef to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchHRef(string hrefToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Href, StringToMatch = hrefToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the HRef regular expression supplied in order to interact with them.
        /// </summary>
        /// <param name="hrefToMatch">HRef to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchHRefRegex([Regex] string hrefToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Regex | MatchType.Href, StringToMatch = hrefToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the placeholder supplied in order to interact with them.
        /// </summary>
        /// <param name="placeholderToMatch">Placeholder to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchPlaceholder(string placeholderToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Placeholder, StringToMatch = placeholderToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the placeholder regular expression supplied in order to interact with them.
        /// </summary>
        /// <param name="placeholderToMatch"></param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchPlaceholderRegex(string placeholderToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Regex | MatchType.Placeholder, StringToMatch = placeholderToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the src supplied in order to interact with them.
        /// </summary>
        /// <param name="srcToMatch">Src to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchSrc(string srcToMatch) 
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Src, StringToMatch = srcToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the src regular expression supplied in order to interact with them.
        /// </summary>
        /// <param name="srcToMatch">Src to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchSrcRegex(string srcToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Regex | MatchType.Src, StringToMatch = srcToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the text supplied in order to interact with them.
        /// </summary>
        /// <param name="textToMatch">Text to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchText(string textToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Text, StringToMatch = textToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the text regular expression supplied in order to interact with them.
        /// </summary>
        /// <param name="textToMatch">Text to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchTextRegex(string textToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Regex | MatchType.Text, StringToMatch = textToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the value supplied in order to interact with them.
        /// </summary>
        /// <param name="valueToMatch">Value to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchValue(string valueToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Value, StringToMatch = valueToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Found elements must match the value regular expression supplied in order to interact with them.
        /// </summary>
        /// <param name="valueToMatch">Value to match</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings MatchValueRegex(string valueToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Regex | MatchType.Value, StringToMatch = valueToMatch };
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        /// <summary>
        /// Remove this selector from the current Unit of Work.
        /// </summary>
        public void Remove()
        {
            DeleteMe = true;
        }
        //// post-v1: OCR
        ///// <summary>
        ///// Use OCR to find the text on-screen.
        ///// </summary>
        ///// <param name="textToFind">Text to find</param>
        ///// <returns>Fluent interface to define the selector more</returns>
        //public IAllowSelectorSettings WithOCR(string textToFind)
        //{
        //    throw new NotImplementedException();
        //}
        /// <summary>
        /// Stop the workflow if this selector is not found.
        /// </summary>
        /// <param name="stopOnNotFound">True to stop the workflow.<br/>False [default] continue the workflow.</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithStopOnNotFound(bool stopOnNotFound)
        {
            SelectorSettings.StopOnNotFound = stopOnNotFound;
            return this;
        }
        public IAllowSelectorSettings CallIfFound(CallIfDelegate callIfFound)
        {
            SelectorSettings.CallIfFound.Add(callIfFound);
            return this;
        }
        public IAllowSelectorSettings CallIfNotFound(CallIfDelegate callIfNotFound)
        {
            SelectorSettings.CallIfNotFound.Add(callIfNotFound);
            return this;
        }

        /// <summary>
        /// Define an image to match on-screen.<br/>
        /// The top left corner is used as the start of the element.<br/>
        /// Image matching forces hardware interaction with this element.
        /// </summary>
        /// <param name="imageToFindPath">The image file to match on-screen; each of its filenames is added as an image selector path</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithImage(GPALFile imageToFindPath)
        {
            foreach (string filename in imageToFindPath.Filenames)
            {
                SelectorSettings.SelectorPathEntry selectorPathEntry = new SelectorSettings.SelectorPathEntry { SelectorPath = filename, SelectorPathType = SelectorPathType.Image };
                SelectorSettings.SelectorPaths.Add(selectorPathEntry);
            }
            return this;
        }

        /// <summary>
        /// Found elements must have this href, whole or in part.
        /// </summary>
        /// <param name="HRefFullOrPartial">The href, whole or any part of it</param>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithHRef(string HRefFullOrPartial)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Href, StringToMatch = HRefFullOrPartial };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
        #endregion <Fluent Settings>

        #region <Getter/Setters>

        // Interaction techniques for the selector
        /// <summary>
        /// Interact with this element using hardware emulation.
        /// </summary>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithHardware
        {
            get
            {
                SelectorSettings.InteractionType = InteractionType.Hardware;
                SelectorSettings.RequestedInteractionType = InteractionType.Hardware;
                return this;
            }
        }
        /// <summary>
        /// Interact with this element using javascript injected into the page.
        /// </summary>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithJavaScript
        {
            get
            {
                SelectorSettings.InteractionType = InteractionType.JavaScript;
                SelectorSettings.RequestedInteractionType = InteractionType.JavaScript;
                return this;
            }
        }
        /// <summary>
        /// Interact with this element using Selenium [default].
        /// </summary>
        /// <returns>Fluent interface to define the selector more</returns>
        public IAllowSelectorSettings WithSelenium
        {
            get
            {
                SelectorSettings.InteractionType = InteractionType.Selenium;
                SelectorSettings.RequestedInteractionType = InteractionType.Selenium;
                return this;
            }
        }

        /// <summary>
        ///  Count of elements found for this selector (after match)
        /// </summary>
        public int ElementsFoundAndMatchedCount
        {
            get
            {
                return SelectorSettings.ElementsFoundAndMatchedCount;
            }
            internal set
            {
                SelectorSettings.ElementsFoundAndMatchedCount = value;
            }
        }
        internal CallIfStatus AppCallIfFoundHandled
        {
            get
            {
                return SelectorSettings.AppCallIfFoundHandled;
            }
            set
            {
                SelectorSettings.AppCallIfFoundHandled = value;
            }
        }
        internal CallIfStatus AppCallIfNotFoundHandled
        {
            get
            {
                return SelectorSettings.AppCallIfNotFoundHandled;
            }
            set
            {
                SelectorSettings.AppCallIfNotFoundHandled = value;
            }
        }
        internal CallIfStatus CallIfFoundHandled
        {
            get
            {
                return SelectorSettings.CallIfFoundHandled;
            }
            set
            {
                SelectorSettings.CallIfFoundHandled = value;
            }
        }
        internal CallIfStatus CallIfNotFoundHandled
        {
            get
            {
                return SelectorSettings.CallIfNotFoundHandled;
            }
            set
            {
                SelectorSettings.CallIfNotFoundHandled = value;
            }
        }
        internal Application.Application Application
        {
            get
            {
                return MyApplication;
            }
            set
            {
                MyApplication = value;
            }
        }
        internal List<Application.Application.CallIfDelegate> AppCallIfFound
        {
            get
            {
                return SelectorSettings.AppCallIfFound;
            }
        }
        internal List<Application.Application.CallIfDelegate> AppCallIfNotFound
        {
            get
            {
                return SelectorSettings.AppCallIfNotFound;
            }
        }
        internal string AttributeName
        {
            get
            {
                return SelectorSettings.AttributeName;
            }
        }
        /// <summary>
        /// Defined method to interact with this element.<br/>
        /// A workflow can override it.
        /// </summary>
        /// <returns>Hardware, JavaScript, Selenium</returns>
        public InteractionType InteractionType
        {
            get
            {
                return SelectorSettings.InteractionType;
            }
            internal set
            {
                SelectorSettings.InteractionType = value;
            }
        }
        /// <summary>
        /// Browser associated with this selector.
        /// </summary>
        /// <returns>The browser used</returns>
        public Browser.Browser Browser
        {
            get;
            internal set;
        }
        /// <summary>
        /// Returns the first defined selector path string
        /// </summary>
        public string SelectorPath
        {
            get
            {
                return SelectorSettings.SelectorPaths[0].SelectorPath;
            }
        }
        /// <summary>
        /// The selector paths defined to locate elements.
        /// </summary>
        /// <returns>The selector paths defined to locate elements</returns>
        public List<SelectorSettings.SelectorPathEntry> SelectorPaths
        {
            get
            {
                return SelectorSettings.SelectorPaths;
            }
        }
        internal List<SelectorSettings.MatchCriteriaEntry> MatchCriteria
        {
            get
            {
                return SelectorSettings.MatchCriteria;
            }
        }
        /// <summary>
        /// Search for elements on the page.
        /// </summary>
        /// <returns>True to search for the element/image<br/>False to just look on the current page.</returns>
        public bool SearchForSelector
        {
            get
            {
                return SelectorSettings.SearchForSelector;
            }
        }
        /// <summary>
        /// X offset from the top left corner to interact with the element, image match included.
        /// Also used in drag-and-drop as the grab point the DeltaX moves from.
        /// </summary>
        /// <returns>X offset.</returns>
        public int OffsetX
        {
            get
            {
                return SelectorSettings.OffsetX;
            }
            internal set
            {
                SelectorSettings.OffsetX = value;
            }
        }
        /// <summary>
        /// Y offset fromm the top left corner to interact with the element, image match included.
        /// Also used in drag-and-drop as the grab point the DeltaY moves from.
        /// </summary>
        /// <returns>Y offset.</returns>
        public int OffsetY
        {
            get
            {
                return SelectorSettings.OffsetY;
            }
            internal set
            {
                SelectorSettings.OffsetY = value;
            }
        }
        /// <summary>
        /// Drag-and-drop deltaX to move from the elemenet.X + OffsetX
        /// </summary>
        /// <returns>X delta</returns>
        public int DeltaX
        {
            get
            {
                return SelectorSettings.DeltaX;
            }
            internal set
            {
                SelectorSettings.DeltaX = value;
            }
        }
        /// <summary>
        /// Drag-and-drop deltaY to move from the elemenet.Y + OffsetY
        /// </summary>
        /// <returns>Y delta</returns>
        public int DeltaY
        {
            get
            {
                return SelectorSettings.DeltaY;
            }
            internal set
            {
                SelectorSettings.DeltaY = value;
            }
        }
        /// <summary>
        /// The name you gave this selector, or one assigned by GPAL [selector1, selector2...]<br/>
        /// Used in Information and Exception channel messages.<br/>
        /// Also used as column headers when saving to a file.
        /// </summary>
        /// <returns>The name of the selector.</returns>
        public string Name
        {
            get
            {
                return SelectorSettings.Name;
            }

            internal  set
            {
                SelectorSettings.Name = value;
            }
        }
        /// <summary>
        /// To allow a CallIF handler to delete this node, a maintenance flush will clean up the node, but also this node will be ignored in FindElements
        /// </summary>
        internal bool DeleteMe
        {
            get
            {
                return SelectorSettings.DeleteMe;
            }
            set
            {
                SelectorSettings.DeleteMe = value;
            }
        }
        /// <summary>
        /// CallIf handlers RemoveCallIfHandlerEverywhere disabled for this selector.<br/>
        /// A soft delete: the handler stays in CallIfFound/CallIfNotFound but is skipped, and is active again when the
        /// selector joins a new workflow.
        /// </summary>
        internal List<Browser.Browser.CallIfDelegate> RemovedMethods
        {
            get
            {
                return SelectorSettings.RemovedMethods;
            }
            set
            {
                SelectorSettings.RemovedMethods = value;
            }
        }
        /// <summary>
        /// The interaction requested with WithHardware, WithJavaScript, WithSelenium or WithSimulateMouse, or null.<br/>
        /// Resolution reads this and sets InteractionType, so a selector reused on another engine starts from the
        /// request, not the last resolution.
        /// </summary>
        internal InteractionType? RequestedInteractionType
        {
            get
            {
                return SelectorSettings.RequestedInteractionType;
            }
            set
            {
                SelectorSettings.RequestedInteractionType = value;
            }
        }
        /// <summary>
        /// Whether to move the mouse to this element to interact with it.
        /// </summary>
        /// <returns>True to move the mouse to elements.<br/>False [default] jump the mouse to elements.</returns>
        public bool SimulateMouse
        {
            get
            {
                return SelectorSettings.SimulateMouse;
            }
        }
        /// <summary>
        /// Stop the workflow if no elements are found for this selector.
        /// </summary>
        /// <returns>True to stop the workflow.<br/>False [default] continue the workflow.</returns>
        public bool StopOnNotFound
        {
            get
            {
                return SelectorSettings.StopOnNotFound;
            }
        }

        public List<GPALElement> WebSelectorFoundResults
        {
            get
            {
                foreach (var selectorPathEntry in SelectorPaths)
                    if (null != selectorPathEntry.WebSelectorFoundResults)
                        return selectorPathEntry.WebSelectorFoundResults;

                return null;
            }
            set
            {
                foreach (var selectorPathEntry in SelectorPaths)
                    selectorPathEntry.WebSelectorFoundResults = value;
            }
        }
        public List<GPALElement> WebSelectorMatchedResults
        {
            get
            {
                foreach (var selectorPathEntry in SelectorPaths)
                    if (null != selectorPathEntry.WebSelectorMatchedResults)
                        return selectorPathEntry.WebSelectorMatchedResults;

                return null;
            }
            set
            {
                foreach (var selectorPathEntry in SelectorPaths)
                        selectorPathEntry.WebSelectorMatchedResults = value;
            }
        }

        #endregion <Getter/Setters>

        #region Implicit String Conversion, so wherever Selector parameter is expected, a string can be used directly
        // implicit ToString
        public static implicit operator string(Selector sel) => sel.Name;

        private static int implicitCount = 0;
        // ------------------------------------------------
        //  1. Implicit conversion from string to Selector
        // ------------------------------------------------
        public static implicit operator Selector(string selectorString)
        {
            var sel = new Selector();

            if (string.IsNullOrWhiteSpace(selectorString))
            {
                // Warn and continue with an empty selector
                GPAL.PublishSimpleEvent(
                    GPALEventType.WARNING,
                    "Invalid selector path treating as empty selector (no paths added).",
                    selectorString,
                    GPALObjectType.Selector
                );

                // Name it so it shows in logs and reports
                sel.WithSelectorName($"EmptySelector{implicitCount++}");
            }
            else
            {
                bool looksLikeXPath = IsLikelyXPath(selectorString);

                // the string is the one kind it reads as
                if (looksLikeXPath)
                    sel.WithXPath(selectorString);
                else
                    sel.WithCSS(selectorString);

                // give it a recognizable debug name
                sel.WithSelectorName($"Implicit{implicitCount++}.{(looksLikeXPath ? "XPath" : "CSS")}.{Shorten(selectorString, 10)}");
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Implicit selector [{sel.Name}] created for [{selectorString}]");

            // emit warning once.
            if (1 == implicitCount)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Implicit selector conversions lack equality, even if paths match. Create instance using GPAL.Selector for reuse.", sel, GPALObjectType.Selector);

            return sel;
        }

        // Shortens a string for debug names
        private static string Shorten(string s, int maxLen)
        {
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 3) + "...";
        }

        // ------------------------------------------------
        //  2. The check that tells CSS from XPath
        // ------------------------------------------------
        // a CSS name: letters, digits, - and _, not starting with a digit; E stands for an escaped character
        private const string CssName = @"-?[_a-zA-Z\u00A0-\uFFFFE][-\w\u00A0-\uFFFFE]*";

        // an XPath axis name ending just before ::
        private static readonly Regex XPathAxis = new Regex(@"(ancestor|ancestor-or-self|attribute|child|descendant|descendant-or-self|following|following-sibling|namespace|parent|preceding|preceding-sibling|self)\s*::$");

        // what a CSS bracket holds: [attr], [ns|attr], or [attr=value] with ~= |= ^= $= *= and an i or s flag; Q stands for a quoted value
        private static readonly Regex CssAttributeTest = new Regex(@"^\s*((" + CssName + @"|\*)?\|)?" + CssName + @"\s*([~|^$*]?=\s*(Q|" + CssName + @")\s*([iIsS]\s*)?)?$");

        /// <summary>
        /// Whether this string is an XPath rather than a CSS selector. It is XPath when it has something CSS cannot
        /// hold outside a quoted value: a '/', an '@', '..', a leading '(' or './', a lone '.', a function such as
        /// text() not written as a :pseudo-class, an axis such as child::, a bracket that is not a CSS attribute
        /// test (such as [2] or [last()]), or a '|' joining two paths. Anything else is CSS, including the strings
        /// both read, such as a bare tag name or input[name='q'].
        /// </summary>
        /// <param name="s">The selector string</param>
        /// <returns>True for XPath, false for CSS</returns>
        public static bool IsLikelyXPath(string s)
        {
            bool xpath = false;

            if (false == string.IsNullOrWhiteSpace(s))
            {
                // quoted text is a value, not syntax, so each quoted value becomes Q and each escaped character E
                StringBuilder syntax = new StringBuilder();
                char quote = '\0';
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if ('\0' != quote)
                    {
                        // a backslash in a CSS value escapes the next character, a quote included
                        if ('\\' == ch)
                            i++;
                        else if (ch == quote)
                        {
                            quote = '\0';
                            syntax.Append('Q');
                        }
                    }
                    else if ('\\' == ch)
                    {
                        syntax.Append('E');
                        i++;
                    }
                    else if ('\'' == ch || '"' == ch)
                        quote = ch;
                    else
                        syntax.Append(ch);
                }
                string text = syntax.ToString().Trim();

                xpath = text.StartsWith("(") || "." == text || text.StartsWith("./") || text.Contains("..")
                    || text.Contains("/") || text.Contains("@")
                    || Regex.IsMatch(text, @"(^|[^:\w-])[a-zA-Z][-\w]*\s*\(");   // a function call; CSS has them only after ':'

                // :: after an axis name is XPath; otherwise it is a CSS pseudo-element such as ::before
                for (int at = text.IndexOf("::"); false == xpath && -1 != at; at = text.IndexOf("::", at + 2))
                    xpath = XPathAxis.IsMatch(text.Substring(0, at + 2));

                foreach (Match bracket in Regex.Matches(text, @"\[([^\[\]]*)\]"))
                    if (false == xpath && false == CssAttributeTest.IsMatch(bracket.Groups[1].Value))
                        xpath = true;

                // a '|' after a space or ')' joins two XPaths; in CSS it separates a namespace from a name or is part of |=
                if (false == xpath && text.Contains("|"))
                    xpath = Regex.IsMatch(text, @"(\s|\))\|(?!=)");
            }

            return xpath;
        }
        #endregion

        public IAllowSelectorSettings ContainsAttribute(string attributeToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Attribute, StringToMatch = attributeToMatch };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }

        public IAllowSelectorSettings ContainsHRef(string hrefToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Href, StringToMatch = hrefToMatch };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }

        public IAllowSelectorSettings ContainsPlaceholder(string placeholderToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Placeholder, StringToMatch = placeholderToMatch };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }

        public IAllowSelectorSettings ContainsSrc(string srcToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Src, StringToMatch = srcToMatch };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }

        public IAllowSelectorSettings ContainsText(string textToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Text, StringToMatch = textToMatch };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }

        public IAllowSelectorSettings ContainsValue(string valueToMatch)
        {
            SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = new SelectorSettings.MatchCriteriaEntry { MatchType = MatchType.Value, StringToMatch = valueToMatch };
            matchCriteriaEntry.ExactMatch = false;
            SelectorSettings.MatchCriteria.Add(matchCriteriaEntry);
            return this;
        }
    }
}

