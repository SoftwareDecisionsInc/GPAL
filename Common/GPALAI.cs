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
using System.Linq;
using System.Net.Http;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public class ClassificationConfig
    {
        public string[] Labels { get; set; }
        public string PromptTemplate { get; set; }
        //// post-v1: confidence threshold
        //public double? ConfidenceThreshold { get; set; }
        public int? MaxWords { get; set; }
        public bool? IncludeKeywords { get; set; }
        public Dictionary<string, object> AdditionalParameters { get; set; } = new Dictionary<string, object>();
    }
    public delegate int AIResponseDelegate(IGPALAI ai, IGPALGrid<string> results);
    public class GPALAI : IGPALAI
    {
        private static readonly Dictionary<AIClassificationType, ClassificationConfig> ClassificationConfigs = new Dictionary<AIClassificationType, ClassificationConfig>
        {
            { AIClassificationType.Sentiment, new ClassificationConfig { Labels = new[] { "Strong Positive", "Weak Positive", "Neutral", "Weak Negative", "Strong Negative" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Spam, new ClassificationConfig { Labels = new[] { "Spam", "Not Spam" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Topic, new ClassificationConfig { Labels = new[] { "Technology", "Politics", "Sports", "Other" }, PromptTemplate = "Classify {0} into {1}", MaxWords = 100 } },
            { AIClassificationType.Intent, new ClassificationConfig { Labels = new[] { "Request", "Complaint", "Inquiry", "Moderate", "Other" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Emotion, new ClassificationConfig { Labels = new[] { "Happy", "Sad", "Angry", "Neutral" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Toxicity, new ClassificationConfig { Labels = new[] { "Toxic", "Non-Toxic" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Language, new ClassificationConfig { Labels = new[] { "English", "Spanish", "French", "Other" }, PromptTemplate = "Identify the language of {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Category, new ClassificationConfig { Labels = new[] { "General", "Specific", "Miscellaneous" }, PromptTemplate = "Classify {0} into {1}", MaxWords = 100 } },
            { AIClassificationType.Priority, new ClassificationConfig { Labels = new[] { "High", "Medium", "Low", "Urgent" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Relevance, new ClassificationConfig { Labels = new[] { "Relevant", "Irrelevant" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Tone, new ClassificationConfig { Labels = new[] { "Formal", "Informal", "Sarcastic", "Humorous" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Urgency, new ClassificationConfig { Labels = new[] { "Urgent", "Non-Urgent" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.IntentComplexity, new ClassificationConfig { Labels = new[] { "Simple", "Complex" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Risk, new ClassificationConfig { Labels = new[] { "High Risk", "Low Risk", "Acceptable" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.ContentType, new ClassificationConfig { Labels = new[] { "News", "Opinion", "Advertisement", "Technical" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Engagement, new ClassificationConfig { Labels = new[] { "High Engagement", "Low Engagement" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Authenticity, new ClassificationConfig { Labels = new[] { "Authentic", "Fake" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.LanguageProficiency, new ClassificationConfig { Labels = new[] { "Beginner", "Intermediate", "Advanced" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.SentimentConfidence, new ClassificationConfig { Labels = new[] { "High Confidence", "Low Confidence" }, PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } },
            { AIClassificationType.Custom, new ClassificationConfig { PromptTemplate = "Classify {0} as {1}", MaxWords = 100 } }
        };

        private AIProviderType? _provider;
        private AIModel? _model;
        private ICredentials _credentials;
        private AITask _task;
        private AIClassificationType? _classificationType;
        private List<ClassificationConfig> _configs = new List<ClassificationConfig>();
        private List<object> _inputSources = new List<object>();
        private List<object> _outputTargets = new List<object>();
        private AIResponseDelegate _callback;
        private GPALForm.GPALStatusStrip _statusStrip;
        private AIProvider _aiProvider;
        private bool _isExecuted;

        internal GPALAI() { }

        public IGPALAI ToGPALObject() => this;

        public IAllowAITask WithProvider(AIProviderType provider)
        {
            if (_provider.HasValue)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Provider already set and cannot be changed.", this, GPALObjectType.GPALAI);
                return this;
            }
            _provider = provider;
            return this;
        }

        public IAllowAITask WithModel(AIModel model)
        {
            _model = model;
            return this;
        }

        public IAllowAITask WithCredentials(ICredentials credentials)
        {
            if (credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Credentials is null.", this, GPALObjectType.GPALAI);
                return this;
            }
            _credentials = credentials;
            _aiProvider = null;
            return this;
        }

        public IAllowAITask WithCredentials(string staticApiKey) =>
            WithCredentials(GPAL.CredentialsFor(CredentialServiceType.StaticKey).WithServiceKey(staticApiKey).ToGPALObject());

        public IAllowAIConfig WithTask(AITask task)
        {
            if (_isExecuted)
            {
                _task = default;
                _isExecuted = false;
            }
            _task = task;
            return this;
        }

        public IAllowAIConfig WithTaskConfig(ClassificationConfig config)
        {
            if (_isExecuted)
            {
                _configs.Clear();
                _isExecuted = false;
            }
            if (config == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Task config is null.", this, GPALObjectType.GPALAI);
                return this;
            }
            _configs.Add(config);
            return this;
        }

        public IAllowAIConfig WithClassificationType(AIClassificationType type)
        {
            if (_isExecuted)
            {
                _classificationType = null;
                _isExecuted = false;
            }
            if (_task != AITask.Classification)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "WithClassificationType is only valid for Classification task.", this, GPALObjectType.GPALAI);
                return this;
            }
            _classificationType = type;
            return this;
        }

        public IAllowAIConfig WithCustomClassification(ClassificationConfig config)
        {
            if (_isExecuted)
            {
                _configs.Clear();
                _isExecuted = false;
            }
            if (_task != AITask.Classification || _classificationType != AIClassificationType.Custom)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "WithCustomClassification requires Classification task and Custom type.", this, GPALObjectType.GPALAI);
                return this;
            }
            if (config == null || config.Labels == null || config.PromptTemplate == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Custom classification requires Labels and PromptTemplate.", this, GPALObjectType.GPALAI);
                return this;
            }
            _configs.Add(config);
            return this;
        }

        public IAllowAIOutput WithLiveInputFrom(GPALForm.GPALInput input, int debounceMilliseconds = 500)
        {
            WithInputFrom(input);
            WireLiveCallback(input, debounceMilliseconds);
            return this;
        }

        public IAllowAIOutput WithLiveInputFrom(GPALForm.GPALTextArea textArea, int debounceMilliseconds = 500)
        {
            WithInputFrom(textArea);
            WireLiveCallback(textArea, debounceMilliseconds);
            return this;
        }

        private void WireLiveCallback(GPALForm.GPALControl control, int debounceMilliseconds)
        {
            var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, debounceMilliseconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Execute();
            };
            control.WithCallback(new EventHandler((s, e) =>
            {
                timer.Stop();
                timer.Start();
            }));
        }

        public IAllowAIOutput WithInputFrom(object source)
        {
            if (_isExecuted)
            {
                _inputSources.Clear();
                _isExecuted = false;
            }
            if (source == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Input source cannot be null.", this, GPALObjectType.GPALAI);
                return this;
            }
            if (!(source is GPALForm.GPALInput || source is GPALForm.GPALTextArea || source is IGPALGrid<string> ||
                  source is GPALFile || source is GPALDatabase || source is string || source is Uri))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unsupported input source type.", this, GPALObjectType.GPALAI);
                return this;
            }
            _inputSources.Add(source);
            return this;
        }

        public IAllowAIExecution WithOutputTo(out IGPALGrid<string> target)
        {
            if (_isExecuted)
            {
                _outputTargets.Clear();
                _isExecuted = false;
            }
            target = new GPALGrid<string>();
            _outputTargets.Add(target);
            return this;
        }

        public IAllowAIExecution WithOutputTo(object target)
        {
            if (_isExecuted)
            {
                _outputTargets.Clear();
                _isExecuted = false;
            }
            if (target == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Output target cannot be null.", this, GPALObjectType.GPALAI);
                return this;
            }
            if (!(target is GPALForm.GPALTextArea || target is GPALForm.GPALRichTextBox || target is GPALDatabase || target is IGPALGrid<string> || target is GPALFile))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unsupported output target type.", this, GPALObjectType.GPALAI);
                return this;
            }
            _outputTargets.Add(target);
            return this;
        }

        public IAllowAIConfig CallAfterAIResponse(AIResponseDelegate callback)
        {
            if (callback == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Callback is null.", this, GPALObjectType.GPALAI);
                return this;
            }
            _callback = callback;
            return this;
        }

        public IAllowAIExecution WithStatusTo(GPALForm.GPALStatusStrip statusStrip)
        {
            _statusStrip = statusStrip;
            return this;
        }

        /// <summary>
        /// Updates the configured status strip (if any), forcing an immediate UI refresh so the
        /// message is visible to the user even while a blocking AI call follows.
        /// </summary>
        private void SetStatus(string text)
        {
            if (null == _statusStrip) return;
            _statusStrip.Text = text;
            System.Windows.Forms.Application.DoEvents();
        }

        public IAllowAIConfig Execute()
        {
            if (!_provider.HasValue)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Provider must be set before execution.", this, GPALObjectType.GPALAI);
                return this;
            }
            ExecuteInternal(false);
            _isExecuted = true;
            return this;
        }

        private void ExecuteInternal(bool isInteractive)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Starting AI task for [{_provider}][{_model}][{_task}]", this, GPALObjectType.GPALAI);
            IGPALGrid<string> results = new GPALGrid<string>();
            try
            {
                _task = _task == default ? AITask.Classification : _task;
                _classificationType = _classificationType ?? AIClassificationType.Custom;
                if (!_inputSources.Any())
                {
                    _inputSources.Add("");
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default empty input.", this, GPALObjectType.GPALAI);
                }
                if (!_outputTargets.Any())
                {
                    _outputTargets.Add(results);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default grid output.", this, GPALObjectType.GPALAI);
                }

                var mergedConfig = MergeConfigs();
                if (_model.HasValue)
                    mergedConfig.AdditionalParameters["Model"] = _model.Value.ToString();

                if (_aiProvider == null || _credentials != null)
                {
                    _aiProvider = new AIProvider(_provider.Value, new RESTClient(), _credentials, mergedConfig);
                }

                string promptTemplate = GeneratePromptTemplate(mergedConfig);
                if (string.IsNullOrEmpty(promptTemplate))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to generate prompt template.", this, GPALObjectType.GPALAI);
                    return;
                }

                List<string> inputs = ProcessInputs();
                if (!inputs.Any())
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No valid inputs provided.", this, GPALObjectType.GPALAI);
                    return;
                }

                foreach (var input in inputs)
                {
                    string prompt = string.Format(promptTemplate, input, mergedConfig.Labels?.Length > 0 ? string.Join(", ", mergedConfig.Labels) : "");

                    SetStatus("Sending request...");
                    SetStatus("Waiting for response...");
                    string result = _aiProvider.Call(prompt);

                    if (!string.IsNullOrEmpty(result))
                    {
                        results.AddRow(new List<string> { result });
                        SetStatus("Response received");
                    }
                    else
                    {
                        SetStatus("Error: no response from AI provider - see log for details");
                    }
                }

                foreach (var target in _outputTargets)
                {
                    if (target is IGPALGrid<string> grid)
                    {
                        foreach (var row in results)
                            grid.AddRow(row);
                    }
                    else if (target is GPALFile file)
                    {
                        if (file.Filenames.Count == 0)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No filenames specified for GPALFile output. Skipping.", this, GPALObjectType.GPALAI);
                            continue;
                        }
                        GPAL.Converter.WithInput(results).AppendTo(file);
                    }
                    else if (target is GPALDatabase db)
                    {
                        if (string.IsNullOrEmpty(db.DatabaseSettings.TableName))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "TableName must be set for GPALDatabase output.", this, GPALObjectType.GPALAI);
                            continue;
                        }
                        GPAL.Converter.WithInput(results).SaveTo(db);
                    }
                    else if (target is GPALForm.GPALTextArea textArea)
                    {
                        textArea.Text = string.Join("\n", results.Select(row => string.Join(" ", row)));
                    }
                    else if (target is GPALForm.GPALRichTextBox richTextBox)
                    {
                        richTextBox.Text = string.Join("\n", results.Select(row => string.Join(" ", row)));
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unsupported output target.", this, GPALObjectType.GPALAI);
                    }
                }

                if (_callback != null)
                {
                    int callbackResult = _callback(this, results);
                    if (callbackResult == -1)
                    {
                        string str = "AI Callback requested application exit.";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, this, GPALObjectType.GPALAI);
                        throw new GPALException(str);
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "AI execution failed.", this, GPALObjectType.GPALAI, ex);
                SetStatus($"Error: {ex.Message}");
            }
        }

        private ClassificationConfig MergeConfigs()
        {
            var merged = new ClassificationConfig();
            if (_task == AITask.Classification && _classificationType.HasValue && ClassificationConfigs.TryGetValue(_classificationType.Value, out var defaultConfig))
            {
                merged.Labels = defaultConfig.Labels;
                merged.PromptTemplate = defaultConfig.PromptTemplate;
                merged.MaxWords = defaultConfig.MaxWords;
            }

            foreach (var config in _configs)
            {
                if (config.MaxWords.HasValue) merged.MaxWords = config.MaxWords;
                if (config.IncludeKeywords.HasValue) merged.IncludeKeywords = config.IncludeKeywords;
                if (config.Labels != null) merged.Labels = config.Labels;
                if (config.PromptTemplate != null) merged.PromptTemplate = config.PromptTemplate;
                if (config.AdditionalParameters != null)
                {
                    merged.AdditionalParameters = new Dictionary<string, object>(config.AdditionalParameters);
                }
            }
            return merged;
        }

        private string GeneratePromptTemplate(ClassificationConfig config)
        {
            switch (_task)
            {
                case AITask.Summarization:
                    return $"Summarize {{0}} in {config.MaxWords ?? 100} words{(config.IncludeKeywords == true ? ", including keywords" : "")}";
                case AITask.Classification:
                    if (_classificationType == AIClassificationType.Custom && string.IsNullOrEmpty(config.PromptTemplate))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Custom classification requires a PromptTemplate.", this, GPALObjectType.GPALAI);
                        return string.Empty;
                    }
                    return config.PromptTemplate ?? (ClassificationConfigs.TryGetValue(_classificationType ?? AIClassificationType.Custom, out var defaultConfig) ? defaultConfig.PromptTemplate : string.Empty);
                case AITask.TextGeneration:
                    return config.PromptTemplate ?? "Generate text based on {0}";
                case AITask.DataAugmentation:
                    return config.PromptTemplate ?? "Augment data for {0}";
                default:
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unsupported AI task.", this, GPALObjectType.GPALAI);
                    return string.Empty;
            }
        }

        private List<string> ProcessInputs()
        {
            var inputs = new List<string>();
            try
            {
                foreach (var source in _inputSources)
                {
                    switch (source)
                    {
                        case GPALForm.GPALInput input:
                            inputs.Add(input.Text);
                            break;
                        case GPALForm.GPALTextArea textArea:
                            inputs.Add(textArea.Text);
                            break;
                        case IGPALGrid<string> grid:
                            foreach (var row in grid) inputs.Add(string.Join(" ", row));
                            break;
                        case GPALFile file:
                            if (!FileHelper.TokenizeFile(file, null))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to tokenize file.", this, GPALObjectType.GPALAI);
                                continue;
                            }
                            foreach (var row in ((IGPALFileInternal)file).TokenList)
                                inputs.Add(string.Join(" ", row));
                            break;
                        case GPALDatabase database:
                            DatabaseHelper.TokenizeDatabase(null, database);
                            foreach (var row in database.Tokens)
                                inputs.Add(string.Join(" ", row));
                            break;
                        case string text:
                            inputs.Add(text);
                            break;
                        case Uri uri:
                            inputs.Add(FetchWebContent(uri));
                            break;
                        default:
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unsupported input source.", this, GPALObjectType.GPALAI);
                            continue;
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Failed to process inputs.", this, GPALObjectType.GPALAI, ex);
            }
            return inputs;
        }

        private string FetchWebContent(Uri uri)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", BrowserHelper.GetUserAgent(GPAL.Browsers[0]));   // NOTE: hardcoded value

                    string content = client.GetStringAsync(uri).GetAwaiter().GetResult();
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Fetched web content from [{uri}]", this, GPALObjectType.GPALAI);
                    return content;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to fetch web content", this, GPALObjectType.GPALAI, ex);
                return string.Empty;
            }
        }
    }
}
