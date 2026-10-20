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

namespace GenerallyPositive
{
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class GPALAttribute : Attribute
    {
        public string Description { get; set; }
        public string MethodName { get; set; } = null;
        // where the member sits inside its attribute's menu in GPAL Studio. The attribute class is the menu and
        // this is the path below it, a slash between levels: "Controls/Lists" under Forms is Forms > Controls >
        // Lists. Null puts the member straight in the attribute's menu
        public string Submenu { get; set; } = null;
        public GPALAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class SingleUseAttribute : GPALAttribute
    {
        public SingleUseAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    #region GPAL Menu Names
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class DataAttribute : GPALAttribute
    {
        public DataAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class CallbackAttribute : GPALAttribute
    {
        public CallbackAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class BrowserAttribute : GPALAttribute
    {
        public BrowserAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class ActionAttribute : GPALAttribute
    {
        public ActionAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, Inherited = true)]
    class HelperAttribute : GPALAttribute
    {
        public HelperAttribute(string Description="")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class FluentAttribute : GPALAttribute
    {
        public FluentAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class SettingsAttribute : GPALAttribute
    {
        public SettingsAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, Inherited = true)]
    class GPALSettingsAttribute : GPALAttribute
    {
        public GPALSettingsAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, Inherited = true)]
    class ModifierAttribute : GPALAttribute
    {
        public ModifierAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, Inherited = true)]
    class ConstantAttribute : GPALAttribute
    {
        public ConstantAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class FormsAttribute : GPALAttribute
    {
        public FormsAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class ComponentAttribute : GPALAttribute
    {
        public ComponentAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class ElementAssitantAttribute : GPALAttribute
    {
        public ElementAssitantAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class KeysAttribute : GPALAttribute
    {
        public KeysAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class FileAttribute : GPALAttribute
    {
        public FileAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class ScrollAttribute : GPALAttribute
    {
        public ScrollAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class WaitAttribute : GPALAttribute
    {
        public WaitAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    class WorkflowAttribute : GPALAttribute
    {
        public WorkflowAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }

    #endregion GPAL Menu Names

    #region Parameter Attributes
    // Parameter attributes, to know how to deal with entering them via the studio
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class EnumAttribute : GPALAttribute
    {

        public EnumAttribute(string Description = "In GPAL Studio, present menu of enum names")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class DirectoryAttribute : GPALAttribute
    {

        public DirectoryAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class IntAttribute : GPALAttribute
    {

        public IntAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class URLAttribute : GPALAttribute
    {

        public URLAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class GPALFileAttribute : GPALAttribute
    {

        public GPALFileAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class SelectorAttribute : GPALAttribute
    {

        public SelectorAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class BrowserTypeAttribute : GPALAttribute
    {

        public BrowserTypeAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class CssAttribute : GPALAttribute
    {

        public CssAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class XPathAttribute : GPALAttribute
    {

        public XPathAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class JavaScriptAttribute : GPALAttribute
    {

        public JavaScriptAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class PasswordAttribute : GPALAttribute
    {

        public PasswordAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class FilePathAttribute : GPALAttribute
    {

        public FilePathAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class RegexAttribute : GPALAttribute
    {

        public RegexAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class EmailAttribute : GPALAttribute
    {

        public EmailAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class ImageAttribute : GPALAttribute
    {

        public ImageAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class MillisecondsAttribute : GPALAttribute
    {

        public MillisecondsAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class SecondsAttribute : GPALAttribute
    {

        public SecondsAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class PixelsAttribute : GPALAttribute
    {

        public PixelsAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class PercentAttribute : GPALAttribute
    {

        public PercentAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class IndexAttribute : GPALAttribute
    {

        public IndexAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class PortAttribute : GPALAttribute
    {

        public PortAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    #endregion Parameter Attributes
    #region Interface Attribute // the title of the dynamic dialog built to enter data for an interface (ie, IAllowSelectorSettings)
    class InterfaceAttribute : GPALAttribute
    {
        public InterfaceAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    #endregion Interface Attribute
    #region SelectorSet Attributes
    // Parameter attributes, to know how to deal with entering them via the studio
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithCSS : GPALAttribute
    {

        public WithCSS(string Description = "Method name used to handle css value")
        {
            this.Description = Description;
            this.MethodName = "WithCSS";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithXPath : GPALAttribute
    {

        public WithXPath(string Description = "Method name used to handle x path")
        {
            this.Description = Description;
            this.MethodName = "WithXPath";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithValue : GPALAttribute
    {

        public WithValue(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithValue";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class MatchValue : GPALAttribute
    {

        public MatchValue(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "MatchValue";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithText : GPALAttribute
    {

        public WithText(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithText";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class MatchText : GPALAttribute
    {

        public MatchText(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "MatchText";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class MatchSrc : GPALAttribute
    {

        public MatchSrc(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "MatchSrc";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class MatchHRef : GPALAttribute
    {

        public MatchHRef(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "MatchHRef";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class MatchPlaceholder : GPALAttribute
    {

        public MatchPlaceholder(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "MatchPlaceholder";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithSelectorName : GPALAttribute
    {

        public WithSelectorName(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithSelectorName";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithOffsetX : GPALAttribute
    {

        public WithOffsetX(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithOffsetX";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithOffsetY : GPALAttribute
    {

        public WithOffsetY(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithOffsetY";
        }
    }
    
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithDeltaX : GPALAttribute
    {

        public WithDeltaX(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithDeltaX";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithDeltaY : GPALAttribute
    {

        public WithDeltaY(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithDeltaY";
        }
    }

    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    public class WithImage : GPALAttribute
    {

        public WithImage(string Description = "")
        {
            this.Description = Description;
            this.MethodName = "WithImage";
        }
    }

    #endregion SelectorSet Attributes
    #region Puppeteer Communicator Command Attributes
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class CommandAttribute : Attribute
    {
        public string CommandString { get; }

        public CommandAttribute(string commandString)
        {
            CommandString = commandString;
        }
    }
    #endregion Puppeteer Communicator CommandAttributes
    #region Experimental Attribute - designate features not completely developed
    [AttributeUsage(AttributeTargets.All, Inherited = true)]
    class InProgressAttribute : GPALAttribute
    {
        public InProgressAttribute(string Description = "")
        {
            this.Description = Description;
        }
    }
    #endregion Experimental Attribute - designate features not completely developed
}

