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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public partial class RESTClient
    {
        /// <summary>
        /// Sets where a downloaded file should be saved.<br/>
        /// Unlike SaveTo, this asks the browser, and engines differ: OttoMagic can name the file but not its folder, so
        /// the download watcher moves it here.
        /// <example>client.WithEndpoint(ApiEndpoint.LeftClickAndDownload).DownloadTo(file).Execute();</example>
        /// </summary>
        /// <param name="downloadPath">Where the downloaded file should be saved</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowRESTParametersOrExecution DownloadTo(GPALFile downloadPath)
        {
            if (null == downloadPath || null == downloadPath.Filenames || 0 == downloadPath.Filenames.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "DownloadTo requires a valid GPALFile with at least one filename.", null, GPALObjectType.None);
                return this;
            }

            _parameters = null == _parameters
                ? new { downloadPath }
                : MergeParameters(_parameters, new { downloadPath });
                        WithHttpMethod("POST");

            return this;
        }
        /// <summary>
        /// Sets where the next browser download is saved, without clicking anything.
        /// <example>client.SetDownloadFilename(@"c:\temp\file.pdf").Execute();</example>
        /// </summary>
        /// <param name="downloadPath">Where the file should be saved</param>
        /// <returns></returns>
        public IAllowRESTExecution SetDownloadFilename(string downloadPath)
        {
            WithEndpoint(ApiEndpoint.SetDownloadFilename);
            return WithDownloadFile(downloadPath);
        }

        /// <summary>
        /// Performs a left click and download on an element
        /// <example>client.LeftClickAndDownload("element123").SetDownloadFileName(fileName).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Interace chaining to SetDownloadFileName</returns>
        public IAllowRESTWithDownloadFilename LeftClickAndDownload(string elementId)
        {
            WithEndpoint(ApiEndpoint.LeftClickAndDownload).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Has the browser download a url itself: no tab opens and no Save As dialog is shown. The file is saved in
        /// the browser's download folder under the name WithDownloadFile gives.
        /// <example>client.DownloadUrl("https://example.com/file.zip").WithDownloadFile("file.zip").Execute();</example>
        /// </summary>
        /// <param name="url">The url to download</param>
        /// <returns>Interface chaining to WithDownloadFile</returns>
        public IAllowRESTWithDownloadFilename DownloadUrl(string url)
        {
            WithEndpoint(ApiEndpoint.DownloadUrl).WithUrl(url);
            return this;
        }

        /// <summary>
        /// Purely headless mode, attaches the file to the input[type=file]
        /// <example>client.LeftClickAndUpload("element123").WithUploadFile(filePath).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTWithUploadFilename LeftClickAndUpload(string elementId)
        {
            WithEndpoint(ApiEndpoint.Upload).WithElementId(elementId);
            return this;
        }
    }
}
