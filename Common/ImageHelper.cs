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
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GenerallyPositive.Browser;
using OpenQA.Selenium;

namespace GenerallyPositive
{
    /// <summary>
    /// Finds a picture on a capture of the screen or of a browser. The GPAL.Image package provides one and registers
    /// it with <see cref="GPAL.RegisterImageMatcher"/>; GPAL itself has none, so it carries no image library.
    /// </summary>
    public interface IImageMatcher
    {
        /// <summary>Finds the picture in <paramref name="templatePath"/> on <paramref name="screen"/>.</summary>
        /// <param name="screen">The capture to search</param>
        /// <param name="templatePath">The picture to find</param>
        /// <param name="matchingPercentage">How close a match has to be, 0 to 100</param>
        /// <returns>Its location in the capture's coordinates, or an empty rectangle when not found</returns>
        Rectangle FindImage(Bitmap screen, string templatePath, int matchingPercentage);
    }

    /// <summary>
    /// Provides comprehensive image capture, conversion, and matching capabilities for automation scenarios.
    /// Supports both desktop (headful) and browser (headless/headful) screenshot workflows with fluent chaining.
    /// </summary>
    public class ImageHelper : IImageHelper
    {
        /// <summary>
        /// Gets or sets the associated Browser instance for headless capture operations.
        /// </summary>
        public Browser.Browser Browser { get; set; }

        /// <summary>
        /// Static reference to the most recent screenshot captured by this helper.
        /// </summary>
        public static Bitmap ScreenShot { get; set; }

        private static string base64StringInternal = null;


        /************** CAPTURE SCREEN ******************/
        // https://stackoverflow.com/questions/4978157/how-to-search-for-an-image-on-screen-in-c
        // TODO: Capture screen for Applications

        /// <summary>
        /// Captures the current screen or visible browser tab.
        /// Works in both headful (desktop screenshot) and headless (browser screenshot) modes.
        /// Output is always a PNG-format image internally.
        /// </summary>
        /// <param name="ApplicationOrBrowser">
        /// Optional. Pass a <see cref="Browser.Browser"/> instance for browser-specific capture logic.
        /// </param>
        /// <returns>
        /// Fluent interface - chain to <see cref="ToBase64String"/>, <see cref="ToBitmap"/>, or <see cref="SaveTo"/>.
        /// </returns>
        /// <example>
        /// <code>
        /// // Desktop screenshot
        /// var helper = new ImageHelper();
        /// helper.CaptureScreen()
        ///       .SaveToFile(new GPALFile("screenshot.png"));
        /// 
        /// // Browser screenshot (headless or headful)
        /// myBrowser.ImageHelper.CaptureScreen(myBrowser)
        ///               .ToBase64String(out string b64)
        ///               .ToBitmap(out Bitmap bmp);
        /// </code>
        /// </example>
        public IAllowGraphicSettings CaptureScreen(object ApplicationOrBrowser = null)
        {
            // Clear the previous browser capture, or ToBase64String and SaveTo keep returning it after a desktop
            // capture.
            base64StringInternal = null;

            bool headlessMode = false;   // assume headless as we might be called before we even have a browser created
            Bitmap image = new Bitmap(1920, 1080);
            bool isBrowser = typeof(Browser.Browser) == ApplicationOrBrowser?.GetType();
            if (true == isBrowser)
                // The screen shows the user's desktop, not a hidden one, so a browser on a hidden desktop is asked for
                // the image, as headless is. An image search that will not click with the mouse also needs the page,
                // even in a visible window.
                headlessMode = ((Browser.Browser)ApplicationOrBrowser).BrowserSettings.UseHeadless
                            || ((Browser.Browser)ApplicationOrBrowser).BrowserSettings.HiddenDesktop
                            || ((Browser.Browser)ApplicationOrBrowser).BrowserSettings.ImageSearchInPage;

            try
            {
                image = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height, PixelFormat.Format32bppArgb);
            }
            catch
            {
                try
                {
                    image = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height, PixelFormat.Format32bppArgb);
                }
                catch
                {
                    // can't use publishevent, would be recursive
                    //Console.WriteLine("new Bitmap failed: Unable to take screenshot");
                    System.Diagnostics.Debug.WriteLine("new Bitmap failed: Unable to take screenshot");
                    return this;
                }
            }

            if (false == headlessMode)
            {
                var gfx = Graphics.FromImage(image);
                try
                {
                    gfx.CopyFromScreen(Screen.PrimaryScreen.Bounds.X, Screen.PrimaryScreen.Bounds.Y, 0, 0, Screen.PrimaryScreen.Bounds.Size, CopyPixelOperation.SourceCopy);
                }
                catch { }
            }
            else if (true == isBrowser)
            {
                Browser = (Browser.Browser)ApplicationOrBrowser;

                try
                {
                    byte[] imageBytes = null;

                    if (true == ((Browser.Browser)ApplicationOrBrowser).UseSelenium)
                    {
                        if (null != ((Browser.Browser)ApplicationOrBrowser).BrowserDriver)
                        {
                            Screenshot screenshot = ((ITakesScreenshot)((Browser.Browser)ApplicationOrBrowser).BrowserDriver).GetScreenshot();
                            // Convert the screenshot to a Bitmap
                            base64StringInternal = screenshot.AsBase64EncodedString;
                        }
                    }
                    else if (0 < GPAL.Browsers.Count())
                    {
                        foreach (Browser.Browser browser in GPAL.Browsers)
                            if (null != browser.BrowserSettings.Process)
                            {
                                if (true == ((Browser.Browser)ApplicationOrBrowser).UsePuppeteer)
                                {
                                    base64StringInternal = ((Browser.Browser)ApplicationOrBrowser).PuppeteerClient.CaptureVisibleTab().Execute();
                                }
                                else if (((Browser.Browser)ApplicationOrBrowser).UseOttoMagic)
                                {
                                    base64StringInternal = ((Browser.Browser)ApplicationOrBrowser).MagicHelper.CaptureVisibleTab();
                                }

                                break;
                            }
                    }
                    char[] ca = { ',' };

                    // remove the header info, we want the raw data
                    // "data:image/png;base64,iVBORw0 ... "
                    try
                    {
                        base64StringInternal = base64StringInternal.Trim()
                                            .Trim('"')                    // remove surrounding quotes if present
                                            .Split(ca, 2)                 // split on first comma only
                                            [1];                          // take everything after the comma
                    }
                    catch
                    {
                        base64StringInternal = base64StringInternal.Trim()
                                            .Trim('"');                    // remove surrounding quotes if present
                    }

                    // also trim any leftover whitespace (rare but safe)
                    base64StringInternal = base64StringInternal.Trim();

                    imageBytes = Convert.FromBase64String(base64StringInternal);

                    // A Bitmap keeps reading its stream until saved, and a JPEG (Puppeteer's format) is only decoded
                    // then. Copy it so it owns its pixels.
                    using (MemoryStream ms = new MemoryStream(imageBytes))
                    using (Bitmap decoded = new Bitmap(ms))
                        image = new Bitmap(decoded);
                }
                catch (Exception ex)
                {
                    string outMessageHeader = $@"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff")}][EXCEPTION][ImageHelper]: ";

                    // Not PublishSimpleEvent: publishing an exception takes a screenshot, which would recurse here.
                    // Write directly, respecting silent mode.
                    if ("1" != Environment.GetEnvironmentVariable("GPAL_IS_SILENT"))
                    {
                        Console.WriteLine($"{outMessageHeader} [{Browser.AutomationEngine}][{Browser.BrowserType}][{(true == Browser.BrowserSettings.UseHeadless ? "Headless" : "Headful")}] Unable to take screenshot [{ex.Message}]");
                        System.Diagnostics.Debug.WriteLine($"{outMessageHeader} [{Browser.AutomationEngine}][{Browser.BrowserType}][{(true == Browser.BrowserSettings.UseHeadless ? "Headless" : "Headful")}] Unable to take screenshot [{ex.Message}]");
                    }
                }
            }
            ScreenShot = image;

            return this;
        }

        /// <summary>
        /// Converts the last captured screenshot to a Base64 string. A browser capture comes back in whatever
        /// the driver encoded it as, and a screen capture is encoded here as JPEG.
        /// </summary>
        /// <param name="base64String">Output parameter containing the Base64-encoded image.</param>
        /// <returns>Fluent interface for chaining.</returns>
        /// <example>
        /// <code>
        /// helper.CaptureScreen()
        ///       .ToBase64String(out string base64)
        ///       .SaveToFile(new GPALFile("image.png"));
        /// </code>
        /// </example>
        public IAllowGraphicSettings ToBase64String(out string base64String)
        {
            System.IO.MemoryStream ms = new MemoryStream();
            if (null != base64StringInternal)
                base64String = base64StringInternal;
            else if (null != ScreenShot)
            {
                ScreenShot.Save(ms, ImageFormat.Jpeg);
                byte[] byteImage = ms.ToArray();
                base64String = Convert.ToBase64String(byteImage);
            }
            else
                base64String = string.Empty;

            return this;
        }

        /// <summary>
        /// Returns the last captured screenshot as a <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="bitmap">Output parameter containing the Bitmap.</param>
        /// <returns>Fluent interface for chaining.</returns>
        public IAllowGraphicSettings ToBitmap(out Bitmap bitmap)
        {
            bitmap = ScreenShot;
            return this;
        }

        /// <summary>
        /// Saves the captured screenshot to a file (overwrites if exists).
        /// </summary>
        /// <param name="file">GPALFile object specifying the target path.</param>
        /// <returns>Fluent interface for chaining.</returns>
        /// <example>
        /// <code>
        /// helper.CaptureScreen(myBrowser)
        ///       .SaveToFile(new GPALFile(@"C:\Screenshots\login.png"));
        /// </code>
        /// </example>
        [File(Description = "Save to file (overwrite)")]
        public IAllowGraphicSettings SaveTo(GPALFile file)
        {
            SaveScreenshot(file.Next, ScreenShot);

            return this;
        }

        /// <summary>
        /// Save the screenshot to the given filename, in the format its extension names.
        /// </summary>
        /// <param name="fileName">Where to write it. Its extension decides the format</param>
        /// <param name="screenshot">The image to write</param>
        private static void SaveScreenshot(string fileName, Image screenshot)
        {
            try
            {
                if (null != screenshot)
                {
                    string saveToDirectory = Path.GetDirectoryName(fileName);

                    // ensure save to directory exists
                    if (false == string.IsNullOrEmpty(saveToDirectory))
                        Directory.CreateDirectory(saveToDirectory);

                    // Save in the format the extension names.
                    screenshot.Save(fileName, FormatForFile(fileName));

                    GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Screenshot saved to [{fileName}]");
                }
                else
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, "No screenshot to save");
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"No screenshot saved to [{fileName}]", null, Enums.GPALObjectType.None, ex);
            }
        }

        /// <summary>
        /// The image format a filename's extension names, PNG for anything unrecognised.
        /// </summary>
        /// <param name="filename">The filename to read the extension from</param>
        /// <returns>The format to save in</returns>
        internal static System.Drawing.Imaging.ImageFormat FormatForFile(string filename)
        {
            System.Drawing.Imaging.ImageFormat retVal;

            switch (Path.GetExtension(filename ?? string.Empty).ToLower())
            {
                case ".jpg":
                case ".jpeg":
                    retVal = System.Drawing.Imaging.ImageFormat.Jpeg;
                    break;
                case ".bmp":
                    retVal = System.Drawing.Imaging.ImageFormat.Bmp;
                    break;
                case ".gif":
                    retVal = System.Drawing.Imaging.ImageFormat.Gif;
                    break;
                case ".tif":
                case ".tiff":
                    retVal = System.Drawing.Imaging.ImageFormat.Tiff;
                    break;
                default:
                    retVal = System.Drawing.Imaging.ImageFormat.Png;
                    break;
            }

            return retVal;
        }

        #region Helpers

        /// <summary>
        /// Loads an image from the specified file path.
        /// </summary>
        /// <param name="filename">Path to the image file.</param>
        /// <returns>A <see cref="System.Drawing.Image"/> instance.</returns>
        /// <example>
        /// <code>
        /// Image img = ImageHelper.LoadImageFromFile(@"C:\images\template.png");
        /// </code>
        /// </example>
        public static System.Drawing.Image LoadImageFromFile(string filename)
        {
            return System.Drawing.Image.FromFile(filename);
        }

        /// <summary>
        /// Converts a Base64 string (with or without data URI prefix) to a <see cref="System.Drawing.Image"/>.
        /// </summary>
        /// <param name="base64String">Base64-encoded image string.</param>
        /// <returns>A <see cref="System.Drawing.Image"/> instance.</returns>
        /// <example>
        /// <code>
        /// Image img = ImageHelper.Base64ToImage(myBase64String);
        /// </code>
        /// </example>
        public static Image Base64ToImage(string base64String)
        {
            base64String = base64String.Trim().Trim('\"');

            // Remove only the data URI header (e.g. "data:image/png;base64,"), anchored at the start and stopping at
            // the comma.
            string pattern = @"^data:[^;]+;base64,";
            base64String = System.Text.RegularExpressions.Regex.Replace(base64String, pattern, string.Empty);

            // Convert Base64 String to byte[]
            byte[] imageBytes = Convert.FromBase64String(base64String);

            // Leave the stream open; an Image reads from it and throws once it is closed.
            MemoryStream ms = new MemoryStream(imageBytes, 0, imageBytes.Length);

            Image image = Image.FromStream(ms, true);

            return image;
        }

        /// <summary>
        /// Finds the location of a template image within the current screen/browser screenshot using OpenCV template matching.
        /// </summary>
        /// <param name="imagePath">Path to the template image to search for.</param>
        /// <param name="ApplicationOrBrowser">[Optional] Application or browser to capture; a browser is captured from its tab, so a headless run still has something to match against</param>
        /// <returns>
        /// A <see cref="Rectangle"/> representing the location and size of the best match.
        /// Returns empty rectangle if no match is found.
        /// </returns>
        /// <example>
        /// <code>
        /// Rectangle match = helper.FindImage(@"C:\templates\login_button.png");
        /// if (!match.IsEmpty)
        ///     GPAL.PublishSimpleEvent(Enums.GPALEventType.NOTICE, $"Found at: [{match.Location}]");
        /// </code>
        /// </example>
        public Rectangle FindImage(string imagePath, object ApplicationOrBrowser = null)
        {
            Rectangle retVal = Rectangle.Empty;

            if (null == GPAL.ImageMatcher)
            {
                // Publish once; a search calls this at every scroll step.
                if (false == matcherMissingSaid)
                {
                    matcherMissingSaid = true;
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Image matching needs the GPAL.Image package, added and registered with GPALImage.Register() at startup, so [{imagePath}] was not looked for");
                }
            }
            else
            {
                try
                {
                    // pass the browser through so a headless run matches against a visible-tab capture instead of the
                    // physical screen (which has no window on it to find). the coordinates that come back are then
                    // viewport relative rather than screen absolute - see GPALElement.CoordinateSpace.
                    CaptureScreen(ApplicationOrBrowser);

                    retVal = GPAL.ImageMatcher.FindImage(ScreenShot, imagePath, GPAL.MatchingPercentage);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Error locating [{imagePath}] on-screen.", null, Enums.GPALObjectType.None, ex);
                }
            }

            return retVal;
        }

        // whether the missing matcher has been reported yet
        private static bool matcherMissingSaid;
        #endregion Helpers

        /// <summary>
        /// Returns this instance as <see cref="IImageHelper"/> for interface-based usage.
        /// </summary>
        /// <returns>This instance cast to IImageHelper.</returns>
        public IImageHelper ToGPALObject()
        {
            return this;
        }
    }

    // extension ToByteArray for CV
    /// <summary>
    /// Extension methods for image conversion, primarily used with OpenCV interop.
    /// </summary>
    internal static class ImageExtensions
    {
        /// <summary>
        /// Converts a <see cref="System.Drawing.Image"/> to a byte array.
        /// </summary>
        /// <param name="image">The image to convert.</param>
        /// <param name="format">The target image format.</param>
        /// <returns>Byte array representation of the image.</returns>
        public static byte[] ToByteArray(this System.Drawing.Image image, ImageFormat format)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, format);
                return ms.ToArray();
            }
        }
    }
}
