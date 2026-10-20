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
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;
using GenerallyPositive.Browser;
using System.Reflection;
using DocumentFormat.OpenXml.Spreadsheet;


namespace GenerallyPositive
{
    /// <summary>
    /// Represents XPath matching information returned by advanced selector strategies.
    /// </summary>
    public class XPathInfo
    {
        /// <summary>
        /// The generated XPath expression.
        /// </summary>
        public string Xpath { get; set; }

        /// <summary>
        /// Number of elements matched by this XPath on the page.
        /// </summary>
        public int MatchedElements { get; set; }

        /// <summary>
        /// Creates a new XPathInfo instance.
        /// </summary>
        /// <param name="xpath">The XPath string.</param>
        /// <param name="matchedElements">Number of matching elements.</param>
        public XPathInfo(string xpath, int matchedElements)
        {
            Xpath = xpath;
            MatchedElements = matchedElements;
        }
    }

    /// <summary>
    /// Represents a client-side bounding rectangle (similar to DOMRect) with additional convenience properties.
    /// </summary>
    public class ClientRectangle
    {
        /// <summary>Gets or sets the top coordinate.</summary>
        public float Top { get; set; }
        /// <summary>Gets or sets the left coordinate.</summary>
        public float Left { get; set; }
        /// <summary>Gets or sets the bottom coordinate.</summary>
        public float Bottom { get; set; }
        /// <summary>Gets or sets the right coordinate.</summary>
        public float Right { get; set; }
        /// <summary>Gets or sets the X coordinate.</summary>
        public float X { get; set; }
        /// <summary>Gets or sets the Y coordinate.</summary>
        public float Y { get; set; }
        /// <summary>Gets or sets the width.</summary>
        public float Width { get; set; }
        /// <summary>Gets or sets the height.</summary>
        public float Height { get; set; }

        /// <summary>
        /// Gets or sets the Size as a <see cref="System.Drawing.Size"/>.
        /// </summary>
        public Size Size
        {
            get => new Size((int)Width, (int)Height);
            set
            {
                Width = value.Width;
                Height = value.Height;
            }
        }

        /// <summary>
        /// Converts the rectangle properties to a dictionary for easy serialization or logging.
        /// </summary>
        /// <returns>Dictionary containing all rectangle properties.</returns>
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { nameof(Top), Top },
                { nameof(Left), Left },
                { nameof(Bottom), Bottom },
                { nameof(Right), Right },
                { nameof(X), X },
                { nameof(Y), Y },
                { nameof(Width), Width },
                { nameof(Height), Height }
            };
        }

        internal ClientRectangle(Point point, Size size)
        {
            Top = point.Y;
            Left = point.X;
            Width = size.Width;
            Height = size.Height;
        }

        /// <summary>
        /// Creates an empty ClientRectangle.
        /// </summary>
        public ClientRectangle()
        {
        }

        /// <summary>
        /// Determines whether the rectangle has zero dimensions and position.
        /// </summary>
        public bool IsEmpty =>
            X == 0 && Y == 0 && Width == 0 && Height == 0;
    }

    /// <summary>
    /// Pseudo element used in Applications and Browser workflows for image matching and unified automation.
    /// Implements <see cref="IGPALElement"/> and mimics <see cref="IWebElement"/> where possible.
    /// <para>No methods are implemented on image-only elements. Use this primarily for debugging and image-based actions.</para>
    /// <para>Key properties that are actually used: Location, Size, Text, TagName, WrappedDriver.</para>
    /// </summary>
    public class GPALElement : IGPALElement, IDisposable
    {
        /// <summary>
        /// Associated Browser instance.
        /// </summary>
        public Browser.Browser Browser { get; internal set; }

        /// <summary>
        /// Shadow root if the element contains one.
        /// </summary>
        public GPALElement ShadowRoot { get; internal set; }

        /// <summary>
        /// All raw attributes returned from the automation backend.
        /// </summary>
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, object> _cssAttributes;
        /// <summary>
        /// CSS computed style properties for the element.
        /// </summary>
        public Dictionary<string, object> CssAttributes
        {
            get
            {
                if (null == _cssAttributes)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Getting Css Attributes for element [{TagName}]", this, GPALObjectType.Other);

                    if (Browser.UsePuppeteer)
                        _cssAttributes = Browser.PuppeteerClient.GetCssAttributes(ElementBackendNodeId.ToString()).Execute<Dictionary<string, object>>();
                    else if (Browser.UseOttoMagic)
                        _cssAttributes = Browser.MagicHelper.GetCssAttributes(Css);
                    else
                        _cssAttributes = ElementHelper.GetCssAttributes(Browser, WebElement);

                    if (null == _cssAttributes)
                        _cssAttributes = new Dictionary<string, object>();

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Got [{_cssAttributes.Count}] Css Attributes for element [{TagName}]", this, GPALObjectType.Other);
                }
                return _cssAttributes;
            }
            set => _cssAttributes = value;
        }

        private Dictionary<string, object> _domAttributes;
        /// <summary>
        /// DOM attributes of the element.
        /// </summary>
        public Dictionary<string, object> DomAttributes
        {
            get
            {
                if (null == _domAttributes)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Getting Dom Attributes for element [{TagName}]", this, GPALObjectType.Other);

                    if (Browser.UsePuppeteer)
                        _domAttributes = Browser.PuppeteerClient.GetDomAttributes(ElementBackendNodeId.ToString()).Execute<Dictionary<string, object>>();
                    else if (Browser.UseOttoMagic)
                        _domAttributes = Browser.MagicHelper.GetDomAttributes(Css);
                    else
                        _domAttributes = ElementHelper.GetDomAttributes(Browser, WebElement);

                    if (null == _domAttributes)
                        _domAttributes = new Dictionary<string, object>();

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Got [{_domAttributes.Count}] Dom Attributes for element [{TagName}]", this, GPALObjectType.Other);
                }
                return _domAttributes;
            }
            set => _domAttributes = value;
        }

        private Dictionary<string, object> _domProperties;
        /// <summary>
        /// DOM properties of the element.
        /// </summary>
        public Dictionary<string, object> DomProperties
        {
            get
            {
                if (null == _domProperties)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Getting Dom Properties for element [{TagName}]", this, GPALObjectType.Other);

                    if (Browser.UsePuppeteer)
                        _domProperties = Browser.PuppeteerClient.GetDomProperties(ElementBackendNodeId.ToString()).Execute<Dictionary<string, object>>();
                    else if (Browser.UseOttoMagic)
                        _domProperties = Browser.MagicHelper.GetDomProperties(Css);
                    else
                        _domProperties = ElementHelper.GetDomProperties(Browser, WebElement);

                    if (null == _domProperties)
                        _domProperties = new Dictionary<string, object>();

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Got [{_domProperties.Count}] Dom Properties for element [{TagName}]", this, GPALObjectType.Other);
                }
                return _domProperties;
            }
            set => _domProperties = value;
        }

        /// <summary>
        /// The underlying Selenium WebDriver instance.
        /// </summary>
        public IWebDriver WrappedDriver { get; set; }

        internal dynamic iWebElement;
        /// <summary>
        /// The wrapped Selenium IWebElement or internal dynamic element.
        /// </summary>
        public dynamic WebElement
        {
            get => iWebElement;
            internal set => iWebElement = value;
        }

        public string ElementHandle { get; set; } // objectId for puppeteer
        public int ElementNodeId { get; set; }
        public int ElementBackendNodeId { get; set; }
        internal long? ContextId { get; set; }
        internal bool IsShadowRoot { get; set; } = false;
        /// <summary>
        /// True when this "shadow root" is really the host of a CLOSED shadow root. Its contents are unreachable,
        /// so the host is kept as the search root and a query inside it returns the host itself, letting a
        /// workflow click relative to its corner.
        /// </summary>
        internal bool IsClosedShadowRoot { get; set; } = false;

        /// <summary>
        /// Selector used to locate this element.
        /// </summary>
        public Selector Selector { get; internal set; }

        // ==================== Existing Properties ====================

        /// <summary>Name attribute of the element.</summary>
        public string Name
        {
            get { return Attributes.ContainsKey("name") ? Attributes["name"] as string : null; }
            set { Attributes["name"] = value; }
        }

        /// <summary>Custom attribute name (used internally).</summary>
        public string AttributeName
        {
            get { return Attributes.ContainsKey("attributeName") ? Attributes["attributeName"] as string : null; }
            set { Attributes["attributeName"] = value; }
        }

        /// <summary>HTML tag name of the element.</summary>
        public string TagName
        {
            get { return Attributes.ContainsKey("tagName") ? Attributes["tagName"].ToString() : null; }
            set { Attributes["tagName"] = value; }
        }

        /// <summary>Visible text content of the element.</summary>
        public string Text
        {
            get { return Attributes.ContainsKey("text") ? Attributes["text"] as string : null; }
            set { Attributes["text"] = value; }
        }

        /// <summary>Whether the element is enabled.</summary>
        public bool Enabled
        {
            get { return Attributes.ContainsKey("enabled") && Attributes["enabled"] is bool ? (bool)Attributes["enabled"] : false; }
            set { Attributes["enabled"] = value; }
        }

        /// <summary>Whether the element is selected (e.g. checkboxes, radio buttons).</summary>
        public bool Selected
        {
            get { return Attributes.ContainsKey("selected") && Attributes["selected"] is bool ? (bool)Attributes["selected"] : false; }
            set { Attributes["selected"] = value; }
        }

        /// <summary>
        /// A checkbox's state: true checked, false clear, null indeterminate (a dash).<br/>
        /// <see cref="Selected"/> returns false for indeterminate. Only script makes a page checkbox indeterminate.
        /// </summary>
        public bool? Checked
        {
            get
            {
                bool indeterminate = Attributes.ContainsKey("indeterminate")
                                  && Attributes["indeterminate"] is bool
                                  && true == (bool)Attributes["indeterminate"];

                return true == indeterminate ? (bool?)null : Selected;
            }
            set { Attributes["selected"] = true == value; Attributes["indeterminate"] = null == value; }
        }

        /// <summary>Screen coordinates of the element.</summary>
        public Point Location
        {
            get { return Attributes.ContainsKey("location") && Attributes["location"] is Point ? (Point)Attributes["location"] : new Point(0, 0); }
            set { Attributes["location"] = value; }
        }

        /// <summary>
        /// Which origin <see cref="Location"/> is measured from. Real DOM elements and image matches taken from
        /// a screen capture are Screen; image matches taken from a visible-tab capture (headless) are Viewport.
        /// </summary>
        internal CoordinateSpace CoordinateSpace
        {
            get { return Attributes.ContainsKey("coordinateSpace") && Attributes["coordinateSpace"] is CoordinateSpace ? (CoordinateSpace)Attributes["coordinateSpace"] : CoordinateSpace.Screen; }
            set { Attributes["coordinateSpace"] = value; }
        }

        /// <summary>
        /// The image match this element was found under, or null when it was not found by an image.<br/>
        /// A click or a hover goes to the matched point rather than to the element, since the element can be much
        /// larger than what was matched and its middle is not where the picture is. Everything else, text, values,
        /// attributes, fill-in, uses the element. Kept out of Attributes so it never shows up in saved data.
        /// </summary>
        internal GPALElement ImageMatch { get; set; }

        /// <summary>Dimensions of the element.</summary>
        public Size Size
        {
            get { return Attributes.ContainsKey("size") && Attributes["size"] is Size ? (Size)Attributes["size"] : new Size(0, 0); }
            set { Attributes["size"] = value; }
        }

        /// <summary>Whether the element is visible on the page.</summary>
        public bool Displayed
        {
            get
            {
                bool visibilty = true;
                bool display = true;
                bool opacity = true;

                if (true == CssAttributes.ContainsKey("visibility"))
                    visibilty = CssAttributes["visibility"]?.ToString() != "hidden";

                if (true == CssAttributes.ContainsKey("display"))
                    display = CssAttributes["display"]?.ToString() != "none";

                if (true == CssAttributes.ContainsKey("opacity"))
                    opacity = CssAttributes["opacity"]?.ToString() != "0";

                bool notHidden = visibilty && display && opacity;

                bool hasSize = BoundingRect.Width > 0 && BoundingRect.Height > 0;

                return notHidden && hasSize;
            }
            set { Attributes["displayed"] = value; }
        }

        /// <summary>href attribute (for links).</summary>
        public string Href
        {
            get { return Attributes.ContainsKey("href") ? Attributes["href"] as string : null; }
            set { Attributes["href"] = value; }
        }

        /// <summary>src attribute (for images, scripts, etc.).</summary>
        public string Src
        {
            get { return Attributes.ContainsKey("src") ? Attributes["src"] as string : null; }
            set { Attributes["src"] = value; }
        }

        /// <summary>value attribute (inputs, textareas, etc.).</summary>
        public string Value
        {
            get { return Attributes.ContainsKey("value") ? Attributes["value"] as string : null; }
            set { Attributes["value"] = value; }
        }

        /// <summary>placeholder attribute.</summary>
        public string Placeholder
        {
            get { return Attributes.ContainsKey("placeholder") ? Attributes["placeholder"] as string : null; }
            set { Attributes["placeholder"] = value; }
        }

        /// <summary>XPath used to locate this element.</summary>
        public string Xpath
        {
            get { return Attributes.ContainsKey("xpath") ? Attributes["xpath"] as string : null; }
            set { Attributes["xpath"] = value; }
        }

        /// <summary>Array of alternative XPaths with match counts.</summary>
        public XPathInfo[] Xpaths
        {
            get { return Attributes.ContainsKey("xpaths") && Attributes["xpaths"] is XPathInfo[]? (XPathInfo[])Attributes["xpaths"] : new XPathInfo[0]; }
            set
            {
                Attributes["xpaths"] = value;
            }
        }

        /// <summary>CSS selector used to locate this element.</summary>
        public string Css
        {
            get { return Attributes.ContainsKey("css") ? Attributes["css"] as string : null; }
            set { Attributes["css"] = value; }
        }

        /// <summary>Full absolute XPath.</summary>
        public string FullXPath
        {
            get { return Attributes.ContainsKey("fullXPath") ? Attributes["fullXPath"] as string : null; }
            set { Attributes["fullXPath"] = value; }
        }

        /// <summary>Client bounding rectangle with detailed coordinates.</summary>
        public ClientRectangle BoundingRect
        {
            get { return Attributes.ContainsKey("boundingRect") && Attributes["boundingRect"] is ClientRectangle ? (ClientRectangle)Attributes["boundingRect"] : null; }
            set { Attributes["boundingRect"] = value; }
        }

        /// <summary>Current page URL when the element was captured.</summary>
        public string PageURL
        {
            get { return Attributes.ContainsKey("pageURL") ? Attributes["pageURL"] as string : null; }
            set { Attributes["pageURL"] = value; }
        }

        /// <summary>The type of the element, for input, this can be many</summary>
        public string Type
        {
            get { return Attributes.ContainsKey("type") ? Attributes["type"] as string : null; }
            set { Attributes["type"] = value; }
        }

        internal GPALElement()
        {
        }

        internal GPALElement(Browser.Browser browser)
        {
            Browser = browser;
        }

        /// <summary>
        /// Creates a GPALElement from image matching coordinates (commonly used for image-based automation).
        /// </summary>
        internal GPALElement(Point location, Size size, string text, IWebDriver webDriver, string tagname = "img")
        {
            WrappedDriver = webDriver;
            TagName = tagname;
            Text = CleanText(text);
            Location = location;
            Size = size;
            BoundingRect = new ClientRectangle(Location, Size);
        }

        internal GPALElement(IWebElement webElement, string attributeName = "")
        {
            iWebElement = webElement;
            AttributeName = attributeName;
            if (webElement != null)
            {
                TagName = webElement.TagName;
                Text = CleanText(webElement.Text);
                Enabled = webElement.Enabled;
                Selected = webElement.Selected;
                Location = webElement.Location;
                Size = webElement.Size;
                Displayed = webElement.Displayed;
                Href = webElement.GetAttribute("href");
                Src = webElement.GetAttribute("src");
                Value = CleanText(webElement.GetAttribute("value"));
                Placeholder = CleanText(webElement.GetAttribute("placeholder"));
                Type = webElement.GetAttribute("type");

                PageURL = WrappedDriver != null ? WrappedDriver.Url : null;

                BoundingRect = new ClientRectangle(Location, Size);
            }
        }

        /// <summary>
        /// Builds an element from properties already read out of the page, so nothing here talks to the driver.
        /// </summary>
        /// <param name="webElement">The element these properties came from</param>
        /// <param name="attributeName">Attribute name the selector asked for, empty when none</param>
        /// <param name="properties">Property bag the page returned for this element</param>
        /// <param name="pageUrl">Url of the page, read once for the whole collection rather than per element</param>
        internal GPALElement(IWebElement webElement, string attributeName, Dictionary<string, object> properties, string pageUrl)
        {
            iWebElement = webElement;
            AttributeName = attributeName;

            TagName = StringOf(properties, "tag");
            Text = CleanText(StringOf(properties, "text"));
            Enabled = BoolOf(properties, "enabled");
            Selected = BoolOf(properties, "selected");
            Attributes["indeterminate"] = BoolOf(properties, "indeterminate");   // the third state of a checkbox, which Checked answers with null
            Displayed = BoolOf(properties, "displayed");
            Location = new Point(IntOf(properties, "x"), IntOf(properties, "y"));
            Size = new Size(IntOf(properties, "w"), IntOf(properties, "h"));
            Href = StringOf(properties, "href");
            Src = StringOf(properties, "src");
            Value = CleanText(StringOf(properties, "value"));
            Placeholder = CleanText(StringOf(properties, "placeholder"));
            Type = StringOf(properties, "type");
            PageURL = pageUrl;

            BoundingRect = new ClientRectangle(Location, Size);
        }
        /// <summary>Reads a string out of the property bag, null when the page had nothing for it.</summary>
        /// <param name="properties">Property bag the page returned</param>
        /// <param name="name">Property to read</param>
        /// <returns>The value, or null</returns>
        static string StringOf(Dictionary<string, object> properties, string name)
        {
            return true == properties.TryGetValue(name, out object value) ? value?.ToString() : null;
        }
        /// <summary>Reads a bool out of the property bag, false when the page had nothing for it.</summary>
        /// <param name="properties">Property bag the page returned</param>
        /// <param name="name">Property to read</param>
        /// <returns>The value, or false</returns>
        static bool BoolOf(Dictionary<string, object> properties, string name)
        {
            return true == properties.TryGetValue(name, out object value) && true == Convert.ToBoolean(value);
        }
        /// <summary>Reads a number out of the property bag. javascript hands them back as long or double.</summary>
        /// <param name="properties">Property bag the page returned</param>
        /// <param name="name">Property to read</param>
        /// <returns>The value, or zero</returns>
        static int IntOf(Dictionary<string, object> properties, string name)
        {
            return true == properties.TryGetValue(name, out object value) && null != value ? Convert.ToInt32(value) : 0;
        }

        internal GPALElement(Dictionary<string, object> attributes, Browser.Browser browser)
        {
            WrappedDriver = browser.BrowserSettings.BrowserDriver;
            Attributes = attributes ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Browser = browser;
        }

        /// <summary>Clears the element's value (if supported by underlying element).</summary>
        public void Clear()
        {
            if (iWebElement != null)
                try
                {
                    iWebElement.Clear();
                }
                catch { }
        }

        /// <summary>Performs a click with optional keyboard modifiers.</summary>
        public void Click(ModifierKeys modifierKeys)
        {
            string currentUrl = Browser.BrowserSettings.CurrentURL;
            Browser.CurrentUOW.ActionCalled = true;

            BrowserHelper.TopBrowser(Browser, false);
            ElementHelper.Click(Browser, Selector, this, ClickType.LeftClick, modifierKeys);

            string currentUrl2 = Browser.GetSetCurrentUrl();

            if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2))
                BrowserHelper.CheckDocumentReady(Browser);    // just a simple document ready check
        }

        /// <summary>Performs a standard left click.</summary>
        public void Click()
        {
            Click(ModifierKeys.NONE);
        }

        /// <summary>Performs a middle mouse button click.</summary>
        public void MiddleClick(ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            Browser.CurrentUOW.ActionCalled = true;
            BrowserHelper.TopBrowser(Browser, false);
            ElementHelper.Click(Browser, Selector, this, ClickType.MiddleClick, modifierKeys);
            //BrowserHelper.CheckDocumentReady(Browser);    // just a simple document ready check
        }

        /// <summary>Hides the element via JavaScript (sets display:none).</summary>
        public void Hide()
        {
            Browser.CurrentUOW.ActionCalled = true;
            ElementHelper.Hide(Browser, this);
        }

        /// <summary>Finds a child element using Selenium By locator.</summary>
        public IWebElement FindElement(By by)
        {
            if (iWebElement != null)
                return iWebElement.FindElement(by);
            throw new NoSuchElementException("Element not found using " + by);
        }

        /// <summary>Finds all child elements using Selenium By locator.</summary>
        public ReadOnlyCollection<IWebElement> FindElements(By by)
        {
            if (iWebElement != null)
                return iWebElement.FindElements(by);
            return new ReadOnlyCollection<IWebElement>(new List<IWebElement>());
        }

        /// <summary>Gets an attribute value with fallback to internal dictionary.</summary>
        public string GetAttribute(string attributeName)
        {
            string retVal = null;
            string seleniumValue = null;

            if (iWebElement != null)
                seleniumValue = iWebElement.GetAttribute(char.ToUpper(attributeName[0]) + attributeName.Substring(1));   // NOTE: ours are lowercase, so search selenium as capitalized (TagName will get ours)

            if (null == seleniumValue && Attributes.ContainsKey(attributeName))
            {
                object value = Attributes[attributeName];

                if (value is Point p)
                    retVal = string.Format("({0},{1})", p.X, p.Y);
                else if (value is Size s)
                    retVal = string.Format("({0},{1})", s.Width, s.Height);
                else if (value is XPathInfo[] arr)
                    retVal = string.Join("; ", arr.Select(x => string.Format("{0} ({1})", x.Xpath, x.MatchedElements)));
                else if (value is ClientRectangle r)
                    retVal = string.Format("(top={0}, left={1}, bottom={2}, right={3}, x={4}, y={5}, width={6}, height={7})",
                        r.Top, r.Left, r.Bottom, r.Right, r.X, r.Y, r.Width, r.Height);
                else
                    retVal = value?.ToString();
            }
            else
            {
                // A property of ours with the name (Href, Value, Text) wins, as it always has. Otherwise the answer is
                // what Selenium read, which is the only place an attribute such as colspan comes from.
                PropertyInfo property = GetType().GetProperty(attributeName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (property != null && property.CanRead)
                    retVal = property.GetValue(this, null)?.ToString();

                if (null == retVal)
                    retVal = seleniumValue;
            }

            return retVal;
        }

        /// <summary>
        /// Directly sets a DOM attribute on this element via <c>element.setAttribute(attributeName, value)</c>,
        /// using OttoMagic, Puppeteer or JavaScript depending on the active engine. Hardware automation has no
        /// equivalent operation, so it falls back to whichever of Selenium/JavaScript or Puppeteer is configured.
        /// See <see cref="Browser.IAllowSetAttributeValue.SetAttribute"/> for the distinction between this and
        /// SendString/FillIn*.
        /// </summary>
        /// <param name="attributeName">Name of the attribute to set (e.g. "disabled", "value", "data-foo")</param>
        /// <param name="value">Value to assign to the attribute</param>
        public void SetAttribute(string attributeName, string value)
        {
            ElementHelper.SetAttribute(Browser, this, attributeName, value);
        }

        /// <summary>Gets a computed CSS value.</summary>
        public string GetCssValue(string propertyName)
        {
            return iWebElement != null ? iWebElement.GetCssValue(propertyName) : CssAttributes[propertyName];
        }

        /// <summary>Gets a DOM attribute.</summary>
        public string GetDomAttribute(string attributeName)
        {
            return iWebElement != null ? iWebElement.GetDomAttribute(attributeName) : DomAttributes[attributeName];
        }

        /// <summary>Gets a DOM property.</summary>
        public string GetDomProperty(string propertyName)
        {
            return iWebElement != null ? iWebElement.GetDomProperty(propertyName) : DomProperties[propertyName];
        }

        /// <summary>Returns the ShadowRoot if available.</summary>
        public ISearchContext GetShadowRoot()
        {
            // using puppeteer, ShadowRoot will be populated
            var shadowRoot = iWebElement != null ? iWebElement.GetShadowRoot() : ShadowRoot;

            return shadowRoot;
        }

        /// <summary>Sends keystrokes to the element.</summary>
        public void SendKeys(string text)
        {
            if (iWebElement != null)
                iWebElement.SendKeys(text);
        }

        /// <summary>Submits the form if the element is part of one.</summary>
        public void Submit()
        {
            if (iWebElement != null)
                iWebElement.Submit();
        }

        /// <summary>Checks if the element is disabled via attributes or properties.</summary>
        public bool IsDisabled()
        {
            bool attrDisabled = Attributes.ContainsKey("disabled") && Attributes["disabled"].ToString() == "true";
            bool propDisabled = false;

            if (DomProperties.ContainsKey("disabled"))
                propDisabled = DomProperties["disabled"].ToString() == "true";

            return attrDisabled || propDisabled;
        }

        /// <summary>Checks if the element is enabled.</summary>
        public bool IsEnabled()
        {
            return !IsDisabled();
        }

        /// <summary>
        /// Determines whether the element appears clickable based on visibility, disabled state, and CSS.
        /// </summary>
        /// <param name="publishEvent">Whether to log a warning if not clickable.</param>
        public bool IsClickable(bool publishEvent = true)
        {
            bool isClickable = true;

            if (true == Browser.UseOttoMagic)
                isClickable = Browser.MagicHelper.IsClickAble(Css);
            //else if (true == Browser.UsePuppeteer)
            //    isClickable = Browser.PuppeteerClient.IsClickable(Css).Execute<bool>();
            else    // selenium
            {
                bool notDisabled = !IsDisabled();

                bool ariaDisabled =
                    Attributes.TryGetValue("aria-disabled", out var aria) &&
                    aria.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                bool pointerOk = true;
                if (true == CssAttributes.TryGetValue("pointer-events", out var pe))
                    pointerOk = pe.ToString() != "none";

                isClickable =
                    Displayed &&
                    notDisabled &&
                    !ariaDisabled &&
                    pointerOk;

                if (false == isClickable && true == publishEvent)
                    GPAL.PublishSimpleEvent(
                        GPALEventType.WARNING,
                        $"Element [{TagName}] does not appear clickable.",
                        this,
                        GPALObjectType.Other);
            }

            return true;
        }

        // release any CDP created nodes and free up memory
        private bool disposed = false;

        /// <summary>
        /// Asynchronously releases resources (especially Puppeteer object handles).
        /// </summary>
        public async Task DisposeAsync() // or void Dispose() with fire-and-forget
        {
            if (disposed || string.IsNullOrEmpty(ElementHandle)) return;

            if (true == Browser.UsePuppeteer)
                try
                {
                    // Use the same communicator/session that created it, or a global one
                    await Browser.PuppeteerCommunicator.SendCommand<object>(
                        DevToolsMethods.RuntimeReleaseObject,
                        new { objectId = ElementHandle },
                        null); // however you track the right session
                }
                catch
                {
                    // Session might already be detached — safe to ignore
                }

            ElementHandle = null;
            disposed = true;
        }

        /// <summary>
        /// Disposes the element (fire-and-forget async cleanup).
        /// </summary>
        public void Dispose()
        {
            _ = DisposeAsync(); // fire-and-forget if in sync context
        }

        static string CleanText(string str) => str is null ? null : System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim();
    }
}