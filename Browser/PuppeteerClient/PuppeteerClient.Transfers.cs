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
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Sets where a browser download is saved (SaveTo writes a file GPAL already has). CDP sets only the folder
        /// of <paramref name="file"/>; the download watcher applies the file name.
        /// </summary>
        /// <param name="file">Where the downloaded file is to end up</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution DownloadTo(GPALFile file)
        {
            SetParameter("downloadPath", file.Filename);
            return this;
        }

        /// <summary>
        /// Configure this client to left-click the given element and download the resulting target.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftClickAndDownload(string selector)
        {
            WithEndpoint(ApiEndpoint.LeftClickAndDownload);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Where files the browser downloads should end up.<br/>
        /// Not SaveTo, which is GPAL writing a file it already has. This sets the directory CDP sends downloads to.
        /// </summary>
        /// <param name="downloadPath">Directory downloaded files are to end up in</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution DownloadTo(string downloadPath)
        {
            // Sets only the downloadPath parameter, not the endpoint, so LeftClickAndDownload(handle).DownloadTo(path)
            // still clicks.
            SetParameter("downloadPath", downloadPath);
            return this;
        }

        /// <summary>
        /// Sets the directory the browser sends downloads to, as a request of its own. DownloadTo sets the same
        /// directory on another request, such as LeftClickAndDownload.
        /// </summary>
        /// <param name="downloadPath">Directory downloaded files are to end up in</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetDownloadFilename(string downloadPath)
        {
            WithEndpoint(ApiEndpoint.SetDownloadFilename);
            SetParameter("downloadPath", downloadPath);
            return this;
        }

        /// <summary>
        /// Sets the upload path parameter(s) so a subsequent action uploads the given file(s).
        /// </summary>
        /// <param name="fileToUpload">The file(s) to upload</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution UploadFrom(GPALFile fileToUpload)
        {
            if (1 < fileToUpload.Count)
                SetParameter("uploadPaths", fileToUpload);
            else
                SetParameter("uploadPath", fileToUpload.Filename);
            return this;
        }

        /// <summary>
        /// Configure this client to left-click the given element to open a file picker, then upload the file(s) set via UploadFrom.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftClickAndUpload(dynamic elementOrelementId)
        {
            WithEndpoint(ApiEndpoint.Upload);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            return this;
        }
    }
}
