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
using System.Text.Json;
using System.Threading;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Handles Google Cloud operations such as project creation, API enabling, and API key generation.
    /// </summary>
    public class GoogleCloud
    {
        private readonly ICredentials _credentials;

        public GoogleCloud(ICredentials credentials)
        {
            if (null == credentials)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No credentials provided for GoogleCloud operations.", null, GPALObjectType.None);
                throw new ArgumentNullException(nameof(credentials));
            }
            _credentials = credentials;
            if (CredentialServiceType.Google != ((Credentials)_credentials).ServiceType)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials must be for Google service.", null, GPALObjectType.None);
                throw new ArgumentException("Credentials must be for Google service.");
            }
        }

        public string GetProjectName(string projectId)
        {
            if (true == string.IsNullOrEmpty(projectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Project ID cannot be empty.", null, GPALObjectType.None);
                return "Unknown";
            }

            var client = GPAL.RESTClient
                .WithAPIBase("https://cloudresourcemanager.googleapis.com")
                .WithEndpoint($"/v1/projects/{projectId}")
                .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                .Execute();
            var projectData = JsonSerializer.Deserialize<Dictionary<string, object>>(client);
            return true == projectData.ContainsKey("name") ? projectData["name"].ToString() : "Unknown";
        }

        public string CreateProject(string projectName, string projectId = null)
        {
            string googleProjectId = "";
            int timeout = 30000;
            int elapsed = 0;
            string operationName;

            if (true == string.IsNullOrEmpty(projectId))
            {
                projectId = $"myproject{DateTime.Now.Year}{DateTime.Now.Ticks}".Substring(0, 30).ToLower();
                if (false == string.IsNullOrEmpty(projectName))
                {
                    projectId = $"{projectName}{DateTime.Now.Year}{DateTime.Now.Ticks}".Substring(0, 29).ToLower();
                }
            }
            string effectiveProjectName = projectName ?? projectId;
            string googleProjectName = GetProjectName(projectId);

            if (true == "Unknown".Equals(googleProjectName))
            {
                var projectPayload = JsonSerializer.Serialize(new { projectId, name = effectiveProjectName });
                var projectClient = GPAL.RESTClient
                    .WithAPIBase("https://cloudresourcemanager.googleapis.com")
                    .WithEndpoint("/v1/projects")
                    .WithParameters(projectPayload)
                    .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                    .Execute();

                var projectData = JsonSerializer.Deserialize<Dictionary<string, object>>(projectClient);
                if (true == projectData.ContainsKey("projectId"))
                {
                    googleProjectId = projectData["projectId"].ToString();
                }
                else if (true == projectData.ContainsKey("name"))
                {
                    operationName = projectData["name"].ToString();
                    elapsed = 0;
                    while (timeout > elapsed)
                    {
                        var opClient = GPAL.RESTClient
                            .WithAPIBase("https://cloudresourcemanager.googleapis.com")
                            .WithEndpoint($"/v1/{operationName}")
                            .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                            .Execute();
                        var opData = JsonSerializer.Deserialize<Dictionary<string, object>>(opClient);
                        if (true == opData.ContainsKey("error"))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Project creation failed: [{opData["error"]}]", null, GPALObjectType.None);
                            return "";
                        }
                        if (true == opData.ContainsKey("done") && true == ((JsonElement)opData["done"]).GetBoolean())
                        {
                            var responseJson = opData["response"].ToString();
                            var response = JsonSerializer.Deserialize<Dictionary<string, object>>(responseJson);
                            googleProjectId = response["projectId"].ToString();
                            break;
                        }
                        Thread.Sleep(1000);
                        elapsed += 1000;
                    }
                    if (timeout <= elapsed || true == string.IsNullOrEmpty(googleProjectId))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Project creation timeout or no projectId.", null, GPALObjectType.None);
                        return "";
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No projectId or operation name returned.", null, GPALObjectType.None);
                    return "";
                }
            }
            else
            {
                googleProjectId = projectId;
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Using existing project: ProjectId [{googleProjectId}], Name [{googleProjectName}]", null, GPALObjectType.None);
            }

            return googleProjectId;
        }

        public bool EnableApi(string googleProjectId, string serviceId)
        {
            if (true == string.IsNullOrEmpty(googleProjectId) || true == string.IsNullOrEmpty(serviceId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Project ID or service ID cannot be empty.", null, GPALObjectType.None);
                return false;
            }

            int timeout = 30000;
            int elapsed = 0;
            var servicePayload = JsonSerializer.Serialize(new { serviceIds = new[] { serviceId } });
            var client = GPAL.RESTClient
                .WithAPIBase("https://serviceusage.googleapis.com")
                .WithEndpoint($"/v1/projects/{googleProjectId}/services:batchEnable")
                .WithParameters(servicePayload)
                .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                .Execute();

            var opData = JsonSerializer.Deserialize<Dictionary<string, object>>(client);
            if (true == opData.ContainsKey("done") && true == ((JsonElement)opData["done"]).GetBoolean())
            {
                return true;
            }
            else if (true == opData.ContainsKey("name"))
            {
                var operationName = opData["name"].ToString();
                elapsed = 0;
                while (timeout > elapsed)
                {
                    var opClient = GPAL.RESTClient
                        .WithAPIBase("https://serviceusage.googleapis.com")
                        .WithEndpoint($"/v1/{operationName}")
                        .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                        .Execute();
                    var data = JsonSerializer.Deserialize<Dictionary<string, object>>(opClient);
                    if (true == data.ContainsKey("error"))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{serviceId}] enable failed: [{data["error"]}]", null, GPALObjectType.None);
                        return false;
                    }
                    if (true == data.ContainsKey("done") && true == ((JsonElement)data["done"]).GetBoolean())
                    {
                        return true;
                    }
                    Thread.Sleep(1000);
                    elapsed += 1000;
                }
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{serviceId}] enable timeout.", null, GPALObjectType.None);
                return false;
            }
            return false;
        }

        public bool EnableApi(string googleProjectId, GoogleApi api)
        {
            string serviceId = api switch
            {
                GoogleApi.ServiceUsage => "serviceusage.googleapis.com",
                GoogleApi.ApiKeys => "apikeys.googleapis.com",
                GoogleApi.Sheets => "sheets.googleapis.com",
                GoogleApi.Drive => "drive.googleapis.com",
                GoogleApi.Scripts => "script.googleapis.com",
                _ => throw new ArgumentException("Invalid Google API type.")
            };
            return EnableApi(googleProjectId, serviceId);
        }

        public string GenerateApiKey(string googleProjectId)
        {
            if (true == string.IsNullOrEmpty(googleProjectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Project ID cannot be empty for API key generation.", null, GPALObjectType.None);
                return "";
            }

            var apiKeyPayload = JsonSerializer.Serialize(new { displayName = $"gpal-api-key-{Guid.NewGuid().ToString().Substring(0, 8)}" });
            var apiKeyClient = GPAL.RESTClient
                .WithAPIBase("https://apikeys.googleapis.com")
                .WithEndpoint($"/v2/projects/{googleProjectId}/locations/global/keys")
                .WithParameters(apiKeyPayload)
                .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                .Execute();

            var apiKeyOpData = JsonSerializer.Deserialize<Dictionary<string, object>>(apiKeyClient);
            if (true == apiKeyOpData.ContainsKey("keyString"))
            {
                return apiKeyOpData["keyString"].ToString();
            }
            else if (true == apiKeyOpData.ContainsKey("name"))
            {
                var operationName = apiKeyOpData["name"].ToString();
                int elapsed = 0;
                while (30000 > elapsed)
                {
                    var opClient = GPAL.RESTClient
                        .WithAPIBase("https://apikeys.googleapis.com")
                        .WithEndpoint($"/v2/{operationName}")
                        .WithHeader("Authorization", $"Bearer {((Credentials)_credentials).AccessToken}")
                        .Execute();
                    var opData = JsonSerializer.Deserialize<Dictionary<string, object>>(opClient);
                    if (true == opData.ContainsKey("error"))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"API key creation failed: [{opData["error"]}]", null, GPALObjectType.None);
                        return "";
                    }
                    if (true == opData.ContainsKey("done") && true == ((JsonElement)opData["done"]).GetBoolean())
                    {
                        var responseJson = opData["response"].ToString();
                        var response = JsonSerializer.Deserialize<Dictionary<string, object>>(responseJson);
                        return response["keyString"].ToString();
                    }
                    Thread.Sleep(1000);
                    elapsed += 1000;
                }
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "API key creation timeout or failed.", null, GPALObjectType.None);
                return "";
            }
            return "";
        }
    }
}
