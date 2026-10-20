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

using GenerallyPositive;
using static GenerallyPositive.Enums;
using System.Collections.Generic;
using System.Linq;

public class ResultCollection
{
    private readonly List<(string Name, object Result, int Iteration)> _results;
    private readonly string _clientName;

    // Starts with an empty collection.
    public ResultCollection()
    {
        _results = new List<(string Name, object Result, int Iteration)>();
    }

    internal ResultCollection(List<(string Name, object Result, int Iteration)> results, string clientName)
    {
        _results = results;
        _clientName = clientName;
    }

    public object this[string name]
    {
        get
        {
            var result = _results.LastOrDefault(r => r.Name == name).Result;
            if (result == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_clientName) ? "" : $"[{_clientName}] ")}Result named [{name}] not found.",
                    null, GPALObjectType.None);
            }
            return result;
        }
        set
        {
            var lastEntry = _results.LastOrDefault(r => r.Name == name);
            if (lastEntry.Result != null)
            {
                // Update the last entry's result
                int index = _results.LastIndexOf(lastEntry);
                _results[index] = (lastEntry.Name, value, lastEntry.Iteration);
            }
            else
            {
                // Add a new entry if not found
                _results.Add((name, value, 0));
            }
        }
    }

    public object this[string name, int iteration]
    {
        get
        {
            var result = _results.LastOrDefault(r => r.Name == name && r.Iteration == iteration).Result;
            if (result == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_clientName) ? "" : $"[{_clientName}] ")}Result named [{name}] for iteration [{iteration}] not found.",
                    null, GPALObjectType.None);
            }
            return result;
        }
    }

    public IReadOnlyList<object> GetAllResults(string name)
    {
        var results = _results
            .Where(r => r.Name == name)
            .Select(r => r.Result)
            .ToList()
            .AsReadOnly();
        if (!results.Any())
        {
            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                $"{(string.IsNullOrEmpty(_clientName) ? "" : $"[{_clientName}] ")}No results named [{name}].",
                null, GPALObjectType.None);
        }
        return results;
    }
}
