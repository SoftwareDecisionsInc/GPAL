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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using GenerallyPositive.Application;
using Microsoft.Win32.SafeHandles;
using OpenQA.Selenium;
using OpenQA.Selenium.DevTools;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class MagicHelper
    {
        /// <summary>
        /// Sets the filename for the next download.<br/>
        /// Only the name is sent: onDeterminingFilename accepts only a name relative to the browser's download
        /// folder and loses the name entirely given a full path. The download watcher moves the file to
        /// <paramref name="downloadPath"/>.
        /// <example>browser.MagicHelper.WithDownloadFile(@"c:\sdi\sample.pdf");   // the extension is sent sample.pdf</example>
        /// </summary>
        /// <param name="downloadPath">Where the file is to end up; the extension is given its filename</param>
        public void WithDownloadFile(string downloadPath)
        {
            Client.SetDownloadFilename(Path.GetFileName(downloadPath)).Execute();
        }

        /// <summary>
        /// Performs a left click and download on an element by ID
        /// <example>browser.MagicHelper.LeftClickAndDownload("link123", filenameAndPath);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="filenameAndPath">Where the file the click downloads is to be saved; the extension is given its filename, see <see cref="WithDownloadFile"/></param>
        public void LeftClickAndDownload(string elementId, string filenameAndPath)
        {
            Client.LeftClickAndDownload(elementId).WithDownloadFile(Path.GetFileName(filenameAndPath)).Execute();
        }

        /// <summary>
        /// Has the browser download a url itself: no tab opens and no Save As dialog is shown, whatever the profile
        /// says about asking where to save.
        /// <example>browser.MagicHelper.DownloadUrl("https://example.com/file.zip", filenameAndPath);</example>
        /// </summary>
        /// <param name="url">The url to download</param>
        /// <param name="filenameAndPath">Where the file is to be saved; the extension is given its filename, see <see cref="WithDownloadFile"/></param>
        public void DownloadUrl(string url, string filenameAndPath)
        {
            Client.DownloadUrl(url).WithDownloadFile(Path.GetFileName(filenameAndPath)).Execute();
        }

        /// <summary>
        /// Performs a left click and upload on an element by ID
        /// <example>browser.MagicHelper.LeftClickAndUpload("link123", filename);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="filenameAndPath">Full path of the file to upload</param>
        public void LeftClickAndUpload(string elementId, string filenameAndPath)
        {
            Client.LeftClickAndUpload(elementId).WithUploadFile(filenameAndPath).Execute();
        }

        /// <summary>
        /// Performs a left click and upload on an element by ID
        /// <example>browser.MagicHelper.LeftClickAndUpload("link123", filename);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="filenamesAndPaths">The file or files to upload</param>
        public void LeftClickAndUpload(string elementId, GPALFile filenamesAndPaths)
        {
            Client.LeftClickAndUpload(elementId).WithUploadFiles(filenamesAndPaths).Execute();
        }
    }
}
