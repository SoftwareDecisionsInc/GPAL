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
        /// Delete the specified storage type
        /// </summary>
        /// <param name="storageType"></param>
        /// <returns>fluent interface to chain parameters</returns>
        public IAllowRESTStorageOptions DeleteStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.DeleteStorage).WithStorageType(storageType);

            return this;
        }

        /// <summary>
        /// Get the specified storage type
        /// </summary>
        /// <param name="storageType"></param>
        /// <returns>fluent interface to chain parameters</returns>
        public IAllowRESTStorageOptions GetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.GetStorage).WithStorageType(storageType);

            return this;
        }

        /// <summary>
        /// Set data for the specified storage type
        /// </summary>
        /// <param name="storageType"></param>
        /// <returns>fluent interface to chain parameters</returns>
        public IAllowRESTStorageOptions SetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.SetStorage).WithStorageType(storageType);

            return this;
        }
    }
}
