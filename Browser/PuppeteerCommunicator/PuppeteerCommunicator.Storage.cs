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
    public partial class PuppeteerCommunicator
    {
        // Implementation for "delete-storage"
        /// <summary>
        /// written by chatgpt
        /// Deletes browser storage items according to hierarchical wildcard rules.
        /// Returns number of items deleted.
        /// </summary>
        /// <remarks>
        /// A null domain means the current page's origin, not every domain.
        /// </remarks>
        /// <param name="storageType">Storage type</param>
        /// <param name="sessionId">CDP session id</param>
        /// <param name="domain">Domain/origin; null acts as wildcard</param>
        /// <param name="storeName">Optional object store (IndexedDB)</param>
        /// <param name="path">Optional path (cookies)</param>
        /// <param name="key">Optional key; null acts as wildcard</param>
        /// <returns>Number of items successfully deleted</returns>
        public async Task<bool> DeleteStorage(
            string storageType,
            string sessionId,
            string domain = null,
            string storeName = null,
            string path = null,
            string key = null)
        {
            sessionId ??= GetEffectiveSessionId();
            int deletedCount = 0;
            bool retVal = false;

            bool IsMissing(string s) => string.IsNullOrEmpty(s);

            try
            {
                string origin = false == string.IsNullOrEmpty(domain) ? UrlHelper.GetOrigin(domain) : UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(Browser.BrowserSettings));

                if (IsMissing(domain))
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"DOMAIN IS MISSING. Deleting all [{storageType}] for origin [{origin}].", this, GPALObjectType.PuppeteerCommunicator);

                switch (storageType)
                {
                    case "cookie":
                        {
                            var cookiesJson = await GetStorage("cookie", sessionId, domain, storeName, path, key).ConfigureAwait(false);
                            var cookies = JsonConvert.DeserializeObject<List<dynamic>>(cookiesJson) ?? new List<dynamic>();
                            foreach (dynamic c in cookies)
                            {
                                var delParams = new Dictionary<string, object>
                                {
                                    { "name", (string)c.Key },
                                    { "domain", (string)c.Domain },
                                    //{ "url", $"https://{(string)c.Domain.ToString().TrimStart('.')}{(string)c.Path}" },
                                    { "path", (string)c.Path },
                                };

                                // Copy the cookie's partitionKey only when it is a JSON object. An unpartitioned
                                // cookie has a JSON null token, which is not a C# null.
                                JToken cookiePartition = ((JObject)c)["PartitionKey"];

                                if (null != cookiePartition && JTokenType.Object == cookiePartition.Type)
                                {
                                    var pk = new Dictionary<string, object>
                                    {
                                        { "topLevelSite", (string)cookiePartition["topLevelSite"] },
                                        { "hasCrossSiteAncestor", (bool?)cookiePartition["hasCrossSiteAncestor"] }
                                    };

                                    delParams.Add("partitionKey", pk);
                                }
                                // Network.deleteCookies returns a reply object, not a bool. Non-null means success.
                                dynamic deleteReply = await SendCommand(DevToolsMethods.NetworkDeleteCookies, delParams, sessionId).ConfigureAwait(false);

                                retVal = null != deleteReply;

                                if (true == retVal)
                                    deletedCount++;
                            }
                            break;
                        }
                    case "localStorage":
                    case "sessionStorage":
                        {
                            if (IsMissing(domain))
                            {
                                string js = $@"(function() {{
                                            try {{
                                                {storageType}.clear();
                                                return true;
                                            }} catch (e) {{
                                                return false;
                                            }}
                                        }})();";

                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                {
                                    expression = js,
                                    awaitPromise = false,
                                    contextId = CurrentContextId,
                                    returnByValue = true
                                }, sessionId).ConfigureAwait(false).GetAwaiter().GetResult()?.ToString().Contains("true");

                                if (true == retVal)
                                    deletedCount++;

                                break;
                            }

                            if (IsMissing(key))
                            {
                                var keysJson = await GetStorage(storageType, sessionId, domain).ConfigureAwait(false);

                                // GetStorage returns a key/value object, not a list of names. Take its keys.
                                List<string> keys = new List<string>();

                                if (false == string.IsNullOrWhiteSpace(keysJson))
                                {
                                    JToken storeRead = JToken.Parse(keysJson);

                                    if (JTokenType.Object == storeRead.Type)
                                        keys = ((JObject)storeRead).Properties().Select(each => each.Name).ToList();
                                    else if (JTokenType.Array == storeRead.Type)
                                        keys = storeRead.Select(each => each.ToString()).ToList();
                                }
                                foreach (var k in keys)
                                {
                                    retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                    {
                                        expression = $"{storageType}.removeItem('{EscapeJsString(k)}');",
                                        awaitPromise = false,
                                        contextId = CurrentContextId,
                                        returnByValue = true
                                    }, sessionId).ConfigureAwait(false).GetAwaiter().GetResult()?.ToString().Contains("true");
                                    if (true == retVal)
                                        deletedCount++;
                                }
                            }
                            else
                            {
                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                {
                                    expression = $"{storageType}.removeItem('{EscapeJsString(key)}');",
                                    awaitPromise = false,
                                    contextId = CurrentContextId,
                                    returnByValue = true
                                }, sessionId).ConfigureAwait(false).GetAwaiter().GetResult()?.ToString().Contains("true");
                                if (true == retVal)
                                    deletedCount++;
                            }
                            break;
                        }
                    case "indexedDb":
                        {
                            if (IsMissing(domain))
                            {
                                retVal = SendCommand<dynamic>(DevToolsMethods.StorageClearDataForOrigin, new
                                {
                                    origin = origin,
                                    storageTypes = "indexeddb"
                                }, sessionId).GetAwaiter().GetResult() != null;

                                if (retVal)
                                    deletedCount++;

                                break;
                            }

                            if (IsMissing(storeName))
                            {
                                // enumerate all DBs and delete
                                var dbsJson = await GetStorage("indexeddb", sessionId, domain).ConfigureAwait(false);
                                var dbs = JsonConvert.DeserializeObject<List<string>>(dbsJson) ?? new List<string>();
                                foreach (var db in dbs)
                                {
                                    string js = $@"(function(){{ indexedDB.deleteDatabase('{EscapeJsString(db)}'); }})();";
                                    retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                        new { 
                                            expression = js, 
                                            awaitPromise = true,
                                            contextId = CurrentContextId,
                                            returnByValue = true 
                                        }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                    if (true == retVal)
                                        deletedCount++;
                                }
                            }
                            else if (IsMissing(key))
                            {
                                string js = $@"(function(){{ indexedDB.deleteDatabase('{EscapeJsString(storeName)}'); }})();";
                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                    new { 
                                        expression = js, 
                                        awaitPromise = true,
                                        contextId = CurrentContextId,
                                        returnByValue = true 
                                    }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                if (true == retVal)
                                    deletedCount++;
                            }
                            else
                            {
                                string js = $@"(function() {{
                                                return new Promise((resolve, reject) => {{
                                                    const dbName     = '{EscapeJsString(path)}';      // path = dbName (per your note)
                                                    const store      = '{EscapeJsString(storeName)}'; // storeName = store (per your note)
                                                    const recordKey  = '{EscapeJsString(key)}';

                                                    const openReq = indexedDB.open(dbName);

                                                    openReq.onerror = () => reject(openReq.error);

                                                    openReq.onsuccess = () => {{
                                                        const db = openReq.result;

                                                        const tx = db.transaction(store, 'readwrite');
                                                        const objectStore = tx.objectStore(store);

                                                        objectStore.delete(recordKey);

                                                        tx.oncomplete = () => {{
                                                            db.close();
                                                            resolve(true);   // so your .Contains(""true"") check still works
                                                        }};

                                                        tx.onerror = () => {{
                                                            db.close();
                                                            reject(tx.error);
                                                        }};
                                                    }};
                                                }});
                                            }})();
                                        ";

                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate,
                                    new
                                    {
                                        expression = js,
                                        awaitPromise = true,
                                        contextId = CurrentContextId,
                                        returnByValue = true
                                    }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");

                                if (retVal)
                                    deletedCount++;
                            }
                            break;
                        }

                    case "cache":
                        {
                            if (IsMissing(domain))
                            {
                                string js = @"(async function() {
                                                try {
                                                    const keys = await caches.keys();
                                                    await Promise.all(keys.map(k => caches.delete(k)));
                                                    return true;
                                                } catch (e) {
                                                    return false;
                                                }
                                            })();";

                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                {
                                    expression = js,
                                    awaitPromise = true,
                                    contextId = CurrentContextId,
                                    returnByValue = true
                                }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");

                                if (true == retVal)
                                    deletedCount++;

                                break;
                            }

                            if (IsMissing(key))
                            {
                                // storeName is the cache name. Without it, GetStorage returns cache names and whole
                                // caches are deleted; with it, the keys in that cache are deleted one by one.
                                var cachesJson = await GetStorage("cache", sessionId, domain, storeName).ConfigureAwait(false);
                                var cacheEntries = JsonConvert.DeserializeObject<List<string>>(cachesJson) ?? new List<string>();

                                foreach (var each in cacheEntries)
                                {
                                    string cacheDelete = IsMissing(storeName)
                                        ? $"caches.delete('{EscapeJsString(each)}');"
                                        : $"caches.open('{EscapeJsString(storeName)}').then(cache => cache.delete('{EscapeJsString(each)}',{{ignoreVary: true}}));";

                                    retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                        new { 
                                            expression = cacheDelete, 
                                            awaitPromise = true,
                                            contextId = CurrentContextId,
                                            returnByValue = true 
                                        }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                    if (true == retVal)
                                        deletedCount++;
                                }
                            }
                            else
                            {
                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                    new { 
                                        expression = $"caches.open('{EscapeJsString(storeName)}').then(cache => cache.delete('{EscapeJsString(key)}', {{ignoreVary: true}}));", 
                                        awaitPromise = true,
                                        contextId = CurrentContextId,
                                        returnByValue = true 
                                    }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                if (true == retVal)
                                    deletedCount++;
                            }
                            break;
                        }

                    default:
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Unsupported storage type: [{storageType}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"Failed for [{storageType}] d[{domain}] sn[{storeName}] p[{path}] k[{key}]",
                    this,
                    GPALObjectType.PuppeteerCommunicator,
                    ex);
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Deleted [{deletedCount}] [{storageType}](s)", this, GPALObjectType.PuppeteerCommunicator);

            return 0 < deletedCount;
        }

        // Implementation for "get-storage"
        /// <summary>
        /// written by chatgpt
        /// Retrieves browser storage items as raw JSON text.
        /// Supports hierarchical wildcards: domain -> storeName -> path -> key.
        /// </summary>
        /// <param name="storageType">Storage type: "cookie", "localStorage", "sessionStorage", "indexeddb", "cache"</param>
        /// <param name="sessionId">CDP session id; current session used if null</param>
        /// <param name="domain">Domain/origin; null acts as wildcard</param>
        /// <param name="storeName">Optional object store name (IndexedDB)</param>
        /// <param name="path">Optional path (cookies)</param>
        /// <param name="key">Optional key; null acts as wildcard</param>
        /// <returns>Raw JSON string representing requested items or identifiers</returns>
        public async Task<string> GetStorage(
            string storageType,
            string sessionId,
            string domain = null,
            string storeName = null,
            string path = null,
            string key = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string resultJson = "null";

            try
            {
                switch (storageType)
                {
                    case "cookie":
                        {
                            // get all cookies via CDP
                            dynamic cookiesResp = await SendCommand<dynamic>(DevToolsMethods.StorageGetCookies,
                                null,
                                sessionId).ConfigureAwait(false);

                            if (cookiesResp?.cookies != null)
                            {
                                var filtered = new List<dynamic>();
                                path ??= "/";

                                foreach (dynamic c in cookiesResp.cookies)
                                {
                                    if ((false == UrlHelper.CookieDomainMatches(((dynamic)c).domain?.ToString(), domain)) ||
                                        (!string.IsNullOrEmpty(path) && ((dynamic)c).path != path) ||
                                        (!string.IsNullOrEmpty(key) && ((dynamic)c).name != key))
                                        continue;

                                    // key matches one cookie name exactly; there is no wildcard or list of names
                                    // add partitionKey conditionally
                                    if (((dynamic)c).partitionKey != null)
                                    {
                                        filtered.Add(new
                                        {
                                            Key = ((dynamic)c).name,
                                            Domain = ((dynamic)c).domain,
                                            Path = ((dynamic)c).path,
                                            Value = ((dynamic)c).value,
                                            Expires = ((dynamic)c).expires,

                                            Secure = ((dynamic)c).secure,
                                            HttpOnly = ((dynamic)c).httpOnly,
                                            SameSite = ((dynamic)c).sameSite,
                                            Session = ((dynamic)c).session,
                                            Priority = ((dynamic)c).priority,
                                            SameParty = ((dynamic)c).sameParty,
                                            SourceScheme = ((dynamic)c).sourceScheme,
                                            SourcePort = ((dynamic)c).sourcePort,

                                            PartitionKey = ((dynamic)c).partitionKey
                                        });
                                    }
                                    else
                                    {
                                        filtered.Add(new
                                        {
                                            Key = ((dynamic)c).name,
                                            Domain = ((dynamic)c).domain,
                                            Path = ((dynamic)c).path,
                                            Value = ((dynamic)c).value,
                                            Expires = ((dynamic)c).expires,

                                            Secure = ((dynamic)c).secure,
                                            HttpOnly = ((dynamic)c).httpOnly,
                                            SameSite = ((dynamic)c).sameSite,
                                            Session = ((dynamic)c).session,
                                            Priority = ((dynamic)c).priority,
                                            SameParty = ((dynamic)c).sameParty,
                                            SourceScheme = ((dynamic)c).sourceScheme,
                                            SourcePort = ((dynamic)c).sourcePort
                                        });
                                    }
                                }

                                resultJson = JsonConvert.SerializeObject(filtered);
                            }
                            break;
                        }
                    case "localStorage":
                    case "sessionStorage":
                        {
                            string js = string.IsNullOrEmpty(key)
                                ? $@"(function(){{
                               let obj={{}};
                               for(let i=0;i<{storageType}.length;i++){{ let k={storageType}.key(i); obj[k]={storageType}.getItem(k); }}
                               return JSON.stringify(obj);
                             }})()"
                                : $"{storageType}.getItem('{EscapeJsString(key)}');";

                            var evalResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, sessionId).ConfigureAwait(false);

                            resultJson = evalResult?.result?.value?.ToString() ?? "null";
                            break;
                        }
                    case "indexedDb":
                        {
                            string js;

                            if (string.IsNullOrEmpty(path))
                            {
                                // No database name > list all IndexedDB databases
                                js = @"
                                    (async () => {
                                        let dbs = await indexedDB.databases();
                                        return JSON.stringify(dbs.map(db => ({ name: db.name, version: db.version })));
                                    })();";
                            }
                            else if (string.IsNullOrEmpty(storeName))
                            {
                                // Path (database name) given + no object store name > list all object stores in that DB
                                js = $@"
                                    (async () => {{
                                        let req = indexedDB.open('{EscapeJsString(path)}');
                                        return new Promise((resolve) => {{
                                            req.onsuccess = e => {{
                                                let db = e.target.result;
                                                let stores = Array.from(db.objectStoreNames);
                                                db.close();
                                                resolve(JSON.stringify(stores));
                                            }};
                                            req.onerror = () => resolve(null);
                                        }});
                                    }})();";
                            }
                            else if (string.IsNullOrEmpty(key))
                            {
                                // Path + storeName given + no key > list all keys in that specific object store
                                js = $@"
                                    (async () => {{
                                        let req = indexedDB.open('{EscapeJsString(path)}');
                                        return new Promise((resolve) => {{
                                            req.onsuccess = e => {{
                                                let db = e.target.result;
                                                let tx = db.transaction('{EscapeJsString(storeName)}', 'readonly');
                                                let store = tx.objectStore('{EscapeJsString(storeName)}');
                                                let keys = [];
                                                let cursorReq = store.openKeyCursor();
                                                cursorReq.onsuccess = function(event) {{
                                                    let cursor = event.target.result;
                                                    if (cursor) {{
                                                        keys.push(cursor.key);
                                                        cursor.continue();
                                                    }} else {{
                                                        db.close();
                                                        resolve(JSON.stringify(keys));
                                                    }}
                                                }};
                                                cursorReq.onerror = () => {{ db.close(); resolve(null); }};
                                            }};
                                            req.onerror = () => resolve(null);
                                        }});
                                    }})();";
                            }
                            else
                            {
                                // All three (path + storeName + key) > get the value for that key
                                js = $@"
                                    (async () => {{
                                        let req = indexedDB.open('{EscapeJsString(path)}');
                                        return new Promise((resolve) => {{
                                            req.onsuccess = e => {{
                                                let db = e.target.result;
                                                let tx = db.transaction('{EscapeJsString(storeName)}', 'readonly');
                                                let store = tx.objectStore('{EscapeJsString(storeName)}');
                                                let getReq = store.get('{EscapeJsString(key)}');
                                                getReq.onsuccess = () => {{
                                                    db.close();
                                                    resolve(JSON.stringify(getReq.result));
                                                }};
                                                getReq.onerror = () => {{ db.close(); resolve(null); }};
                                            }};
                                            req.onerror = () => resolve(null);
                                        }});
                                    }})();";
                            }

                            var evalDb = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, sessionId).ConfigureAwait(false);

                            resultJson = evalDb?.result?.value?.ToString() ?? "null";
                            break;
                        }

                    case "cache":
                        {
                            string js;

                            if (string.IsNullOrEmpty(storeName))
                            {
                                // no key = enumerate alll cache names
                                // key = return all with the key name
                                js = string.IsNullOrEmpty(key)
                                    ? @"(async ()=>{let c=await caches.keys(); return JSON.stringify(c);})()"
                                    : $@"(async () => {{
                                        const response = await caches.match('{EscapeJsString(key)}');
                                        if (response) {{
                                            const data = await response.json();
                                            return JSON.stringify(data);
                                        }}
                                        return null;
                                    }})();";
                            }
                            else if (string.IsNullOrEmpty(key))
                            {
                                // Path (cache name) is defined + no key > list all keys (as URLs) inside that specific cache
                                js = $@"(async () => {{
                                    const cache = await caches.open('{EscapeJsString(storeName)}');
                                    const keys = await cache.keys();
                                    const keyUrls = keys.map(k => k.url);           // keys are Request objects; we return clean URLs
                                    return JSON.stringify(keyUrls);
                                }})();";
                            }
                            else
                            {
                                // Both path and key are defined > open the specific cache and match the key inside it
                                js = $@"(async () => {{
                                    const cache = await caches.open('{EscapeJsString(storeName)}');
                                    const response = await cache.match('{EscapeJsString(key)}');
                                        if (response) {{{{
                                            const contentType = response.headers.get('content-type') || '';
                                            let data;
                                            if (contentType.includes('application/json') || contentType.includes('text/json')) {{{{
                                                data = await response.json();
                                            }}}} else {{{{
                                                data = await response.text();
                                            }}}}
                                            return JSON.stringify(data);
                                        }}}}
                                        return null;
                                    }})();";

                            }
                            var evalCache = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, sessionId).ConfigureAwait(false);

                                resultJson = evalCache?.result?.value?.ToString() ?? "null";
                            break;
                        }

                    case "notSet":
                        GPAL.PublishSimpleEvent(
                            GPALEventType.WARNING,
                            $"Nothing to do because StorageType is [{storageType}] d[{domain}] sn[{storeName}] p[{path}] k[{key}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;

                    default:
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Unsupported storage type: [{storageType}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"GetStorage failed for [{storageType}] d[{domain}] sn[{storeName}] p[{path}] k[{key}]",
                    this,
                    GPALObjectType.PuppeteerCommunicator,
                    ex);
            }

            return resultJson;
        }

        /// <summary>
        /// Sets a value in browser storage. Required parameters must be supplied.
        /// Returns "success" if write completed, null if failed.
        /// </summary>
        /// <param name="storageType">Storage type: "cookie", "localStorage", "sessionStorage", "indexeddb", "cache"</param>
        /// <param name="sessionId">CDP session id; current session used if null</param>
        /// <param name="domain">Domain/origin (required)</param>
        /// <param name="key">Key or cache name (required)</param>
        /// <param name="data">Value to store (required)</param>
        /// <param name="storeName">Optional object store (IndexedDB, required for IndexedDB)</param>
        /// <param name="path">Optional path (cookies, default "/")</param>
        /// <returns>"success" or null if failed</returns>
        public async Task<bool> SetStorage(
            string storageType,
            string sessionId,
            string domain,
            string key,
            string data,
            string storeName = null,
            string path = null)
        {
            bool retVal = false;
            sessionId ??= GetEffectiveSessionId();

            try
            {
                switch (storageType)
                {
                    case "cookie":
                        {
                            IDictionary<string, object> cookieToSet;

                            path ??= "/";
                            domain ??= "";

                            // 1. Get current cookies via CDP
                            dynamic cookiesResp = await GetStorage(storageType, sessionId, domain, storeName, path, key).ConfigureAwait(false);

                            // 2. Find the existing cookie (exact match on name + domain + path)
                            dynamic existing = null;

                            if (cookiesResp != null)
                            {
                                var cookies = JsonConvert.DeserializeObject<List<dynamic>>(cookiesResp) ?? new List<dynamic>();
                                foreach (dynamic c in cookies)
                                {
                                    string cDomain = ((dynamic)c).Domain ?? "";
                                    string cPath = ((dynamic)c).Path ?? "/";
                                    string cName = ((dynamic)c).Key ?? "";

                                    if (cName == key &&
                                        UrlHelper.CookieDomainMatches(cDomain, domain) &&
                                        cPath == path)
                                    {
                                        existing = c;

                                        cookieToSet = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;

                                        cookieToSet["name"] = key;
                                        cookieToSet["value"] = data;
                                        cookieToSet["domain"] = ((dynamic)existing)?.Domain?.ToString();
                                        cookieToSet["path"] = path;

                                        cookieToSet["expires"] = ((dynamic)existing)?.Expires != null
                                                                 ? (double)existing.Expires
                                                                 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (180L * 24 * 60 * 60); // ~6 months

                                        cookieToSet["secure"] = ((dynamic)existing)?.Secure ?? true;
                                        cookieToSet["httpOnly"] = ((dynamic)existing)?.HttpOnly ?? true;
                                        cookieToSet["sameSite"] = ((dynamic)existing)?.SameSite?.ToString() ?? "None";

                                        cookieToSet["session"] = ((dynamic)existing)?.Session ?? false;
                                        cookieToSet["priority"] = ((dynamic)existing)?.Priority?.ToString() ?? "Medium";

                                        cookieToSet["sourceScheme"] = ((dynamic)existing)?.SourceScheme?.ToString() ?? "Secure";
                                        cookieToSet["sourcePort"] = ((dynamic)existing)?.SourcePort ?? 443;

                                        // Only add partitionKey if it actually exists and is not null
                                        if (((dynamic)existing)?.PartitionKey != null)
                                        {
                                            cookieToSet["partitionKey"] = existing.PartitionKey;   // keep the original object
                                        }

                                        // 4. Set the cookie
                                        await SendCommand<dynamic>(DevToolsMethods.StorageSetCookies,
                                            new { cookies = new[] { cookieToSet } },
                                            sessionId).ConfigureAwait(false);
                                    }
                                }
                            }
                            else
                            {
                                // 3. Build the cookie to set (merge existing + supplied values)
                                cookieToSet = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;

                                cookieToSet["name"] = key;
                                cookieToSet["value"] = data;
                                cookieToSet["domain"] = ((dynamic)existing)?.Domain?.ToString();
                                cookieToSet["path"] = path;

                                cookieToSet["expires"] = ((dynamic)existing)?.Expires != null
                                                         ? (double)existing.Expires
                                                         : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (180L * 24 * 60 * 60); // ~6 months

                                cookieToSet["secure"] = ((dynamic)existing)?.Secure ?? true;
                                cookieToSet["httpOnly"] = ((dynamic)existing)?.HttpOnly ?? true;
                                cookieToSet["sameSite"] = ((dynamic)existing)?.SameSite?.ToString() ?? "None";

                                cookieToSet["session"] = ((dynamic)existing)?.Session ?? false;
                                cookieToSet["priority"] = ((dynamic)existing)?.Priority?.ToString() ?? "Medium";

                                cookieToSet["sourceScheme"] = ((dynamic)existing)?.SourceScheme?.ToString() ?? "Secure";
                                cookieToSet["sourcePort"] = ((dynamic)existing)?.SourcePort ?? 443;

                                // Only add partitionKey if it actually exists and is not null
                                if (((dynamic)existing)?.PartitionKey != null)
                                {
                                    cookieToSet["partitionKey"] = existing.PartitionKey;   // keep the original object
                                }

                                // 4. Set the cookie
                                await SendCommand<dynamic>(DevToolsMethods.StorageSetCookies,
                                    new { cookies = new[] { cookieToSet } },
                                    sessionId).ConfigureAwait(false);
                            }

                            retVal = true;
                            break;
                        }

                    case "localStorage":
                    case "sessionStorage":
                        string jsStorage = $@"{storageType}.setItem('{EscapeJsString(key)}','{EscapeJsString(data)}');";
                        await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                            new { 
                                expression = jsStorage, 
                                awaitPromise = false,
                                contextId = CurrentContextId,
                                returnByValue = true 
                            }, sessionId).ConfigureAwait(false);
                        retVal = true;
                        break;

                    case "indexedDb":
                        // Wrap the script in a Promise for awaitPromise. Runtime.evaluate passes no callback like
                        // Selenium's executeAsyncScript.
                        string js = $@"
                                    new Promise((callback) => {{

                                    (function() {{
                                        const dbName = '{EscapeJsString(path)}';
                                        const storeName = '{EscapeJsString(storeName)}';
                                        const inputKey = '{EscapeJsString(key)}';
                                        let rawValue = '{EscapeJsString(data)}';

                                        function resolve(val) {{
                                            callback(val);
                                        }}

                                        function fail(msg, err, db) {{
                                            console.error(msg, err?.name || err || '');
                                            try {{ db?.close(); }} catch {{ }}
                                            resolve(false);
                                        }}

                                        function parseValue(v) {{
                                            try {{
                                                return JSON.parse(v);
                                            }} catch {{
                                                return v;
                                            }}
                                        }}

                                        function isValidKey(k) {{
                                            if (k === undefined) return false;
                                            if (typeof k === 'string' || typeof k === 'number') return true;
                                            if (k instanceof Date) return true;
                                            if (Array.isArray(k)) return true;
                                            return false;
                                        }}

                                        function ensureInlineKey(value, keyPath, key, autoIncrement) {{
                                            if (keyPath === null) return {{ value, usedKey: key }};

                                            if (typeof value !== 'object' || value === null) {{
                                                if (keyPath && key !== undefined) {{
                                                    const wrapped = {{}};

                                                    if (typeof keyPath === 'string') {{
                                                        wrapped[keyPath] = key;
                                                    }} else if (Array.isArray(keyPath)) {{
                                                        if (!Array.isArray(key)) {{
                                                            throw new Error('Compound key requires array key');
                                                        }}
                                                        for (let i = 0; i < keyPath.length; i++) {{
                                                            wrapped[keyPath[i]] = key[i];
                                                        }}
                                                    }}

                                                    wrapped.value = value;
                                                    return {{ value: wrapped, usedKey: undefined }};
                                                }}

                                                if (autoIncrement) {{
                                                    return {{ value, usedKey: undefined }};
                                                }}

                                                throw new Error('Inline keyPath requires object value');
                                            }}

                                            if (typeof keyPath === 'string') {{
                                                if (!(keyPath in value)) {{
                                                    if (key !== undefined) {{
                                                        value[keyPath] = key;
                                                    }} else if (!autoIncrement) {{
                                                        throw new Error('Missing required inline keyPath property');
                                                    }}
                                                }}
                                            }} else if (Array.isArray(keyPath)) {{
                                                for (let i = 0; i < keyPath.length; i++) {{
                                                    const kp = keyPath[i];
                                                    if (!(kp in value)) {{
                                                        if (Array.isArray(key) && key[i] !== undefined) {{
                                                            value[kp] = key[i];
                                                        }} else if (!autoIncrement) {{
                                                            throw new Error('Missing compound keyPath property: ' + kp);
                                                        }}
                                                    }}
                                                }}
                                            }}

                                            return {{ value, usedKey: undefined }};
                                        }}

                                        const openReq = indexedDB.open(dbName);

                                        openReq.onerror = () => {{
                                            fail('Failed to open database', openReq.error);
                                        }};

                                        openReq.onsuccess = () => {{
                                            const db = openReq.result;

                                            let tx;
                                            try {{
                                                tx = db.transaction(storeName, 'readwrite');
                                            }} catch (e) {{
                                                fail('Transaction creation failed', e, db);
                                                return;
                                            }}

                                            const store = tx.objectStore(storeName);

                                            let value = parseValue(rawValue);
                                            let key = inputKey;

                                            if (key === '') key = undefined;

                                            if (key !== undefined && !isValidKey(key)) {{
                                                fail('Invalid key type', key, db);
                                                return;
                                            }}

                                            let finalValue, finalKey;

                                            try {{
                                                const result = ensureInlineKey(
                                                    value,
                                                    store.keyPath,
                                                    key,
                                                    store.autoIncrement
                                                );

                                                finalValue = result.value;
                                                finalKey = result.usedKey;
                                            }} catch (e) {{
                                                fail('Key handling failed', e, db);
                                                return;
                                            }}

                                            let putReq;

                                            try {{
                                                if (store.keyPath === null) {{
                                                    if (finalKey === undefined && !store.autoIncrement) {{
                                                        fail('Missing key for out-of-line store', null, db);
                                                        return;
                                                    }}
                                                    putReq = store.put(finalValue, finalKey);
                                                }} else {{
                                                    putReq = store.put(finalValue);
                                                }}
                                            }} catch (e) {{
                                                fail('Put setup error', e, db);
                                                return;
                                            }}

                                            // wait for transaction complete (not just put)
                                            tx.oncomplete = () => {{
                                                try {{ db.close(); }} catch {{ }}
                                                resolve(true);
                                            }};

                                            tx.onerror = (event) => {{
                                                fail('Transaction error', event.target.error, db);
                                            }};

                                            tx.onabort = (event) => {{
                                                fail('Transaction aborted', event.target.error, db);
                                            }};
                                        }};
                                    }})();

                                    }});
                                    ";

                        string indexedDbSaid = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                        {
                            expression = js,
                            awaitPromise = true,
                            contextId = CurrentContextId,
                            returnByValue = true
                        }, sessionId).GetAwaiter().GetResult()?.ToString();

                        retVal = true == indexedDbSaid?.Contains("true");

                        // On failure, log the script's reply; it says why.
                        if (false == retVal)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                $"IndexedDB [{path}] store [{storeName}] did not take key [{key}], the page answered [{indexedDbSaid}]",
                                null, GPALObjectType.None);

                        break;

                    case "cache":
                        string cacheName = EscapeJsString(storeName);
                        string requestUrl = EscapeJsString(key);   // Key = the cache key (URL)

                        js = $@"
                                (async () => {{
                                    try {{
                                        const cache = await caches.open('{cacheName}');
                
                                        const request = new Request('{requestUrl}', {{
                                            method: 'GET'
                                        }});

                                        const response = new Response('{EscapeJsString(data)}', {{
                                            status: 200,
                                            statusText: 'OK',
                                            headers: {{
                                                'Content-Type': 'text/plain;charset=utf-8',
                                                'Cache-Control': 'max-age=31536000'   // or make this configurable
                                            }}
                                        }});

                                        await cache.put(request, response);
                                        return {{ success: true, cacheName: '{cacheName}', key: '{requestUrl}' }};
                                    }} catch (err) {{
                                        console.error('Cache set failed:', err);
                                        return {{ success: false, error: err.message }};
                                    }}
                                }})();
                            ";

                        var evalResult = await SendCommand<dynamic>(
                            DevToolsMethods.RuntimeEvaluate,
                            new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            },
                            sessionId).ConfigureAwait(false);

                        // Check result
                        retVal = evalResult?.result?.value?.success == true;
                        break;

                    default:
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Unsupported storage type: [{storageType}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"SetStorage failed for [{storageType}] domain=[{domain}] storeName=[{storeName}] path=[{path}] key=[{key}]",
                    this,
                    GPALObjectType.PuppeteerCommunicator,
                    ex);
            }

            return retVal;
        }
    }
}
