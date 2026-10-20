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
using GenerallyPositive;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public class GoogleDorkingConfig
    {
        private static string ConfigFilePath = "./googleDorkingConfig.json";

        public string SearchApiBase { get; set; } = "https://www.googleapis.com/customsearch";
        public string SearchEndpoint { get; set; } = "/v1";
        public string QueryFields { get; set; } = "items(title,link,snippet)";
        public string ApiKey { get; set; }
        public string EngineId { get; set; } // Added to store EngineId

        /// <summary>
        /// Loads the Google Dorking config from file, falling back to defaults if missing or invalid.
        /// If <paramref name="file"/> is provided it becomes the new default path for this session.
        /// </summary>
        public static GoogleDorkingConfig Load(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            if (true == File.Exists(ConfigFilePath))
            {
                try
                {
                    var loaded = new GoogleDorkingConfig();
                    GPAL.Converter
                        .WithInput((GPALFile)GPAL.File.WithFileName(ConfigFilePath))
                        .SaveTo(ref loaded);
                    return loaded;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to load config from file", null, GPALObjectType.None, ex);
                }
            }
            else
                new GoogleDorkingConfig().Save();

            return new GoogleDorkingConfig();
        }

        /// <summary>
        /// Saves this configuration to file.
        /// If <paramref name="file"/> is provided it becomes the new default path for this session.
        /// </summary>
        public void Save(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            GPAL.Converter
                .WithInput(this)
                .SaveTo((GPALFile)GPAL.File.WithFileName(ConfigFilePath));
        }
    }
    public class GoogleDorking : IGoogleDorking
    {
        public GoogleDorkingConfig Config { get; private set; }
        internal IRESTClient SearchRESTClient { get; private set; }
        public string ApiKey { get; private set; }
        public string EngineId { get; private set; }
        internal List<string> QueryParts { get; private set; }
        internal int ResultsPerPage { get; private set; } = 10;
        internal string DateRestrict { get; private set; }
        internal string SortBy { get; private set; }
        internal bool IsExactPhrase { get; private set; }
        private ICredentials Credentials { get; set; }
        private string ProjectName { get; set; }
        private string ProjectId { get; set; }

        public GoogleDorking()
        {
            Config = GoogleDorkingConfig.Load();
            SearchRESTClient = GPAL.RESTClient.WithAPIBase(Config.SearchApiBase).ToGPALObject();
            QueryParts = new List<string>();
            ApiKey = Config.ApiKey;
            EngineId = Config.EngineId;
        }

        public IAllowDorkingEngineSelection WithApiKey(string apiKey)
        {
            if (true == string.IsNullOrEmpty(apiKey) || false == IsValidApiKeyFormat(apiKey))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Invalid or empty API key format. Expected format: AIzaSy followed by ~39 characters.", null, GPALObjectType.None);
                return this;
            }

            ApiKey = apiKey;
            Config.ApiKey = apiKey;
            Config.Save();
            return this;
        }

        public IAllowGenerateAPIKey WithCredentials(ICredentials credentials)
        {
            Credentials = credentials;
            return this;
        }

        public IAllowGenerateAPIKey WithProjectName(string projectName)
        {
            ProjectName = projectName;
            return this;
        }

        public IAllowGenerateAPIKey WithProjectId(string projectId)
        {
            ProjectId = projectId;
            return this;
        }

        public IAllowDorkingApiKey GenerateApiKey(out string apiKey)
        {
            apiKey = string.Empty;
            bool existingPublishToConsole = GPAL.PublishToConsole;

            if (null == Credentials)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No credentials provided for API key generation.", null, GPALObjectType.None);
                return this;
            }

            var creds = (Credentials)Credentials;
            if (CredentialServiceType.Google != creds.ServiceType)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials must be for Google service.", null, GPALObjectType.None);
                return this;
            }

            var googleCloud = new GoogleCloud(Credentials);
            if (false == existingPublishToConsole)
                GPAL.WithPublishToConsole();

            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Ensure Cloud Resource Manager API is enabled: https://console.cloud.google.com/apis/library/cloudresourcemanager.googleapis.com", null, GPALObjectType.None);

            if (false == existingPublishToConsole)
                GPAL.WithPublishToConsole(GPALEventType.NONE);

            string googleProjectId = googleCloud.CreateProject(ProjectName, ProjectId);

            if (true == string.IsNullOrEmpty(googleProjectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"PROJECT NOT CREATED:Project Name [{ProjectName ?? googleProjectId}]\nProjectId [{ProjectId ?? googleProjectId}] Google ProjectId [{googleProjectId}]", this, GPALObjectType.GoogleDorking);
                return this;
            }

            bool success = googleCloud.EnableApi(googleProjectId, GoogleApi.ServiceUsage) &&
                           googleCloud.EnableApi(googleProjectId, GoogleApi.ApiKeys) &&
                           googleCloud.EnableApi(googleProjectId, GoogleApi.CustomSearch);
        //&&
        //                googleCloud.EnableApi(googleProjectId, GoogleApi.Sheets) &&
        //                googleCloud.EnableApi(googleProjectId, GoogleApi.Drive);


            apiKey = googleCloud.GenerateApiKey(googleProjectId);

            if (true != string.IsNullOrEmpty(apiKey))
            {
                if (false == existingPublishToConsole)
                {
                    GPAL.WithPublishToConsole();
                }
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Write this information down:\nAPI KEY: [{apiKey}]\nProject Name [{ProjectName ?? googleProjectId}]\nProjectId [{ProjectId ?? googleProjectId}] Google ProjectId [{googleProjectId}]", this, GPALObjectType.GoogleDorking);
                if (false == existingPublishToConsole)
                {
                    GPAL.WithPublishToConsole(GPALEventType.NONE);
                }
            }

            return this;
        }

        public IAllowDorkingQueryBuilding WithDateRestriction(DateRestriction restriction, int? customValue = null)
        {
            DateRestrict = restriction switch
            {
                DateRestriction.PastDay => "d1",
                DateRestriction.PastWeek => "w1",
                DateRestriction.PastMonth => "m1",
                DateRestriction.PastYear => "y1",
                DateRestriction.Custom when customValue.HasValue => $"m{customValue.Value}",
                _ => null
            };
            if (true == DateRestriction.Custom.Equals(restriction) && false == customValue.HasValue)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Custom date restriction requires a value", null, GPALObjectType.None);
            }
            return this;
        }

        public IAllowDorkingQueryBuilding WithSortBy(SortOption sortBy)
        {
            SortBy = sortBy switch
            {
                SortOption.Date => "date",
                SortOption.Relevance => "",
                _ => ""
            };
            return this;
        }

        public IAllowDorkingQueryExecution SearchFor(string searchTerm)
        {
            if (false == string.IsNullOrEmpty(searchTerm))
            {
                QueryParts.Insert(0, true == IsExactPhrase ? $"\"{Uri.EscapeDataString(searchTerm)}\"" : Uri.EscapeDataString(searchTerm));
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No search term specified", null, GPALObjectType.None);
            }
            IsExactPhrase = false;
            return this;
        }

        public IAllowDorkingQueryBuilding GetResults(out IGPALGrid<string> results)
        {
            results = GPAL.GridForType<string>();

            if (true == string.IsNullOrEmpty(ApiKey) || true == string.IsNullOrEmpty(EngineId) || false == QueryParts.Any())
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Missing required setup (API key, engine ID, or query)", null, GPALObjectType.None);
                ResetQuery();
                return this;
            }

            try
            {
                var queryString = string.Join(" ", QueryParts);
                var parameters = new Dictionary<string, string>
                {
                    { "q", queryString },
                    { "cx", Uri.EscapeDataString(EngineId) },
                    { "fields", Uri.EscapeDataString(Config.QueryFields) },
                    { "key", Uri.EscapeDataString(ApiKey) }
                };

                if (0 < ResultsPerPage)
                {
                    parameters.Add("num", ResultsPerPage.ToString());
                }
                if (false == string.IsNullOrEmpty(DateRestrict))
                {
                    parameters.Add("dateRestrict", DateRestrict);
                }
                if (false == string.IsNullOrEmpty(SortBy))
                {
                    parameters.Add("sort", SortBy);
                }

                var endpoint = $"{Config.SearchEndpoint}?{string.Join("&", parameters.Select(p => $"{p.Key}={p.Value}"))}";

                var response = SearchRESTClient
                    .WithEndpoint(endpoint)
                    .Execute<SearchResponse>();

                if (null != response?.Items)
                {
                    foreach (var item in response.Items)
                    {
                        results.AddRow(new List<string> { item.Title, item.Link, item.Snippet });
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to retrieve search results", null, GPALObjectType.None);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to retrieve search results", null, GPALObjectType.None, ex);
            }

            ResetQuery();
            return this;
        }

        public IGoogleDorking ToGPALObject()
        {
            return this;
        }

        public static bool IsValidApiKeyFormat(string apiKey)
        {
            return false == string.IsNullOrEmpty(apiKey) && true == apiKey.StartsWith("AIzaSy") && 30 <= apiKey.Length && 50 >= apiKey.Length;
        }

        private void ResetQuery()
        {
            QueryParts.Clear();
            DateRestrict = null;
            SortBy = null;
            IsExactPhrase = false;
        }

        public IAllowDorkingQueryBuilding WithEngineId(string engineId)
        {
            if (true == string.IsNullOrEmpty(engineId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Engine ID cannot be empty.", null, GPALObjectType.None);
                return this;
            }
            EngineId = engineId;
            Config.EngineId = engineId;
            Config.Save();
            return this;
        }

        public IAllowDorkingQueryBuilding WithExcludeTerm(string term)
        {
            if (false == string.IsNullOrEmpty(term))
            {
                QueryParts.Add($"-{Uri.EscapeDataString(term)}");
            }
            return this;
        }

        public IAllowDorkingQueryBuilding WithSite(string site)
        {
            if (false == string.IsNullOrEmpty(site))
            {
                QueryParts.Add($"site:{Uri.EscapeDataString(site)}");
            }
            return this;
        }

        public IAllowDorkingQueryBuilding WithFileType(FileType fileType)
        {
            string fileTypeStr = fileType switch
            {
                FileType.Pdf => "pdf",
                FileType.Doc => "doc",
                FileType.Docx => "docx",
                FileType.Txt => "txt",
                FileType.Xls => "xls",
                FileType.Xlsx => "xlsx",
                _ => ""
            };
            if (false == string.IsNullOrEmpty(fileTypeStr))
            {
                QueryParts.Add($"filetype:{fileTypeStr}");
            }
            return this;
        }

        public IAllowDorkingQueryBuilding WithInTitle(string term)
        {
            if (false == string.IsNullOrEmpty(term))
            {
                QueryParts.Add($"intitle:{Uri.EscapeDataString(term)}");
            }
            return this;
        }

        public IAllowDorkingQueryBuilding WithInUrl(string term)
        {
            if (false == string.IsNullOrEmpty(term))
            {
                QueryParts.Add($"inurl:{Uri.EscapeDataString(term)}");
            }
            return this;
        }

        public IAllowDorkingQueryBuilding WithResultsPerPageUpTo10(int num)
        {
            if (1 <= num && 10 >= num)
            {
                ResultsPerPage = num;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Results per page must be between 1 and 10.", null, GPALObjectType.None);
            }
            return this;
        }

        public IAllowDorkingQueryExecution SearchForExactPhrase(string searchTerm)
        {
            IsExactPhrase = true;
            return SearchFor(searchTerm);
        }
    }

    public class SearchResponse
    {
        public List<SearchItem> Items { get; set; }
    }

    public class SearchItem
    {
        public string Title { get; set; }
        public string Link { get; set; }
        public string Snippet { get; set; }
    }
}
