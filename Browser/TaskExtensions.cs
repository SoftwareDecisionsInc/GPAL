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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public static class TaskExtensions
    {
        public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout, string what = null, bool quiet = false)
        {
            Task completedTask = null;
            using (var cts = new CancellationTokenSource())
            {
                var delayTask = Task.Delay(timeout, cts.Token);
                try
                {
                    completedTask = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var ex2 = ex;
                }

                if (completedTask == delayTask)
                {
                    if (false == quiet)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{what ?? "Operation"}] timed out after [{timeout.TotalSeconds}]s", null, GPALObjectType.Puppeteer);
                    return default;
                }

                // Cancel the delay task immediately so it stops consuming system resources
                cts.Cancel();

                if (task.IsFaulted)
                {
                    if (false == task.Exception?.InnerException?.Message.Contains("wasn't found"))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Task faulted: [{task.Exception?.InnerException?.Message ?? "Unknown error"}]", null, GPALObjectType.Puppeteer);
                    return default;
                }

                if (task.IsCanceled)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Task canceled", null, GPALObjectType.Puppeteer);
                    return default;
                }

                // using task.Result avoids spinning up another await context state machine.
                return task.Result;
            }
        }
    }
}
