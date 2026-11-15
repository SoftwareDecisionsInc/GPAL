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
        /// Configure this client to delete the given type of website storage.
        /// </summary>
        /// <param name="storageType">The type of storage to delete</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions DeleteStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.DeleteStorage);
            SetParameter("storageType", storageType.ToString());

            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the given type of website storage.
        /// </summary>
        /// <param name="storageType">The type of storage to retrieve</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions GetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.GetStorage);
            SetParameter("storageType", storageType.ToString());

            return this;
        }

        /// <summary>
        /// Configure this client to write the given type of website storage; combine with WithStorageDomain/WithStorageKey/WithStorageData etc to specify what to write.
        /// </summary>
        /// <param name="storageType">The type of storage to write</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions SetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.SetStorage);
            SetParameter("storageType", storageType.ToString());

            return this;
        }
    }
}
