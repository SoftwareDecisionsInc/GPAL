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
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.Support.Extensions;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public class SeleniumStorageHelper
    {
        internal IWebDriver webDriver;
        internal IWebDriver browserDriver;
        internal Browser browser;
        private string EscapeJs(string s) => s?.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"") ?? "";

        internal SeleniumStorageHelper(Browser browser)
        {
            webDriver = browser.BrowserDriver;
            if (Enums.BrowserType.FireFox != browser.BrowserType)
                browserDriver = browser.BrowserDriver;
            this.browser = browser;
        }

        // ──────────────────────────────────────────────────────────────
        // Main public API – unchanged signature
        // ──────────────────────────────────────────────────────────────

        public string GetStorage(StorageAction action)
        {
            try
            {
                return action.StorageType switch
                {
                    WebsiteStorageType.indexedDb => GetIndexedDb(action),
                    WebsiteStorageType.cookie => GetCookies(action),
                    WebsiteStorageType.localStorage => GetLocalOrSessionStorage(action),
                    WebsiteStorageType.sessionStorage => GetLocalOrSessionStorage(action),
                    WebsiteStorageType.cache => GetCache(action),
                    WebsiteStorageType.notSet => ReturnEmptyString(),
                    _ => FailAndReturnNull($"Get not implemented for {action.StorageType}", action)
                };
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get [{action.StorageType}]", action, GPALObjectType.Other, ex);
                return null;
            }
        }

        public int DeleteStorage(StorageAction action)
        {
            int deleted = 0;
            try
            {
                deleted = action.StorageType switch
                {
                    WebsiteStorageType.indexedDb => DeleteIndexedDb(action),
                    WebsiteStorageType.cookie => DeleteCookies(action),
                    WebsiteStorageType.localStorage => DeleteLocalOrSessionStorage(action),
                    WebsiteStorageType.sessionStorage => DeleteLocalOrSessionStorage(action),
                    WebsiteStorageType.cache => DeleteCache(action),
                    WebsiteStorageType.notSet => ReturnZero(),
                    _ => FailAndReturnZero($"Delete not implemented for {action.StorageType}", action)
                };
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to delete [{action.StorageType}]", action, GPALObjectType.Other, ex);
            }
            return deleted;
        }

        public bool SetStorage(StorageAction action)
        {
            try
            {
                return action.StorageType switch
                {
                    WebsiteStorageType.indexedDb => SetIndexedDb(action),
                    WebsiteStorageType.cookie => SetCookie(action),
                    WebsiteStorageType.localStorage => SetLocalOrSessionStorage(action),
                    WebsiteStorageType.sessionStorage => SetLocalOrSessionStorage(action),
                    WebsiteStorageType.cache => SetCache(action),
                    WebsiteStorageType.notSet => ReturnFalse(),
                    _ => FailAndReturnFalse($"Set not implemented for {action.StorageType}", action)
                };
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to set [{action.StorageType}]", action, GPALObjectType.Other, ex);
                return false;
            }
        }

        private bool ReturnFalse()
        {
            return false;
        }

        private int ReturnZero()
        {
            return 0;
        }

        private string ReturnEmptyString()
        {
            return string.Empty;
        }
        // ──────────────────────────────────────────────────────────────
        // IndexedDB – complete wildcard handling
        // ──────────────────────────────────────────────────────────────

        private string GetIndexedDb(StorageAction action) => GetIndexedDbSelenium(action);

        internal string GetIndexedDbSelenium(StorageAction action)
        {
            string dbName = string.IsNullOrWhiteSpace(action.Path) ? null : EscapeJs(action.Path);
            string storeName = string.IsNullOrWhiteSpace(action.StoreName) ? null : EscapeJs(action.StoreName);
            string key = string.IsNullOrWhiteSpace(action.Key) ? null : EscapeJs(action.Key);

            // Helper: C# null > JS null, otherwise safe string literal
            string JsLit(string value) => value == null ? "null" : $"'{value}'";

            string expression = $@"
                                (async () => {{
                                    // Helper to open DB
                                    const openDb = (name) => new Promise((resolve, reject) => {{
                                        if (!name) {{
                                            reject(new Error('Database name is required'));
                                            return;
                                        }}
                                        const req = indexedDB.open(name);
                                        req.onsuccess = () => resolve(req.result);
                                        req.onerror   = () => reject(req.error);
                                    }});

                                    try {{
                                        // CASE 1: No dbName > list all database names
                                        if ({JsLit(dbName)} === null) {{
                                            if (typeof indexedDB.databases === 'function') {{
                                                const dbs = await indexedDB.databases();
                                                return JSON.stringify(dbs.map(db => db.name));
                                            }}
                                            return JSON.stringify([]);   // fallback
                                        }}

                                        // CASE 2: dbName given, but no storeName > list object stores
                                        if ({JsLit(storeName)} === null) {{
                                            const db = await openDb({JsLit(dbName)});
                                            const storeNames = Array.from(db.objectStoreNames);
                                            db.close();
                                            return JSON.stringify(storeNames);
                                        }}

                                        // CASE 3: Both dbName + storeName given
                                        const db = await openDb({JsLit(dbName)});

                                        if (!db.objectStoreNames.contains({JsLit(storeName)})) {{
                                            db.close();
                                            return {(key != null ? "null" : "JSON.stringify([])")};
                                        }}

                                        const tx = db.transaction({JsLit(storeName)}, 'readonly');
                                        const store = tx.objectStore({JsLit(storeName)});

                                        if ({(key != null ? "true" : "false")}) {{
                                            // Get single value by key
                                            const value = await new Promise((resolve, reject) => {{
                                                const req = store.get({JsLit(key)});
                                                req.onsuccess = () => resolve(req.result ?? null);
                                                req.onerror   = () => reject(req.error);
                                            }});
                                            db.close();
                                            return JSON.stringify(value);
                                        }} 
                                        else {{
                                            // Get ALL values from the store
                                            const values = await new Promise((resolve, reject) => {{
                                                const req = store.getAll();
                                                req.onsuccess = () => resolve(req.result);
                                                req.onerror   = () => reject(req.error);
                                            }});
                                            db.close();
                                            return JSON.stringify(values);
                                        }}
                                    }} catch (e) {{
                                        console.error('IndexedDB error in CDP:', e);
                                        return JSON.stringify({{
                                            error: true,
                                            message: e.message || 'Unknown error',
                                            detail: e.name || e.toString()
                                        }});
                                    }}
                                }})();
                            ";

            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                string retVal = ExecuteFirefoxAsyncScript(expression);
                if (retVal?.Contains("\"error\":true") == true)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB operation failed [{retVal}]", browser, GPALObjectType.Browser);
                action.Data = retVal;
                return retVal;
            }

            var parameters = new Dictionary<string, object>
            {
                { "expression", expression },
                { "awaitPromise", true },     // ← This is critical for async/await
                { "returnByValue", true }     // ← Returns plain value instead of RemoteObject
            };

            try
            {
                dynamic cdpResult = BrowserHelper.ExecuteCdp(browserDriver, "Runtime.evaluate", parameters);

                string retVal = ExtractResultValue(cdpResult);

                // Optional: nice debug output
                if (retVal?.Contains("\"error\":true") == true)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB operation failed [{retVal}]", cdpResult, GPALObjectType.Other);

                action.Data = retVal;
                return retVal;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"IndexedDB operation failed", action, GPALObjectType.Other, ex);
                string errorJson = $@"{{""error"":true,""message"":""{EscapeJs(ex.Message)}"",""detail"":""CDP execution error""}}";
                action.Data = errorJson;
                return errorJson;
            }
        }

        private int DeleteIndexedDb(StorageAction action)
        {
            bool hasDb = !string.IsNullOrWhiteSpace(action.Path);
            bool hasStore = !string.IsNullOrWhiteSpace(action.StoreName);
            bool hasKey = !string.IsNullOrWhiteSpace(action.Key);

            string js = hasKey
                ? BuildDeleteKeysJs(action.Path, action.StoreName, action.Key)
                : hasStore
                    ? BuildClearStoreJs(action.Path, action.StoreName)
                    : string.Empty;

            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                if (true == string.IsNullOrEmpty(js))
                    return DeleteAllIndexedDbFirefox(action) ? 1 : 0;
                string ffRaw = ExecuteFirefoxAsyncScript(js) ?? "0";
                if (IsErrorJson(ffRaw))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB delete failed: [{ffRaw}]", action, GPALObjectType.Other);
                    return 0;
                }
                return int.TryParse(ffRaw, out int ffN) ? ffN : 0;
            }

            // 1 only when the delete-everything call succeeds, 0 when it fails, the same as Firefox above
            if (true == string.IsNullOrEmpty(js))
                return DeleteAllIndexedDb(action) ? 1 : 0;

            string raw = ExecuteAsyncJs<string>(js) ?? "0";

            if (IsErrorJson(raw))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB delete failed: [{raw}]", action, GPALObjectType.Other);
                return 0;
            }

            return int.TryParse(raw, out int n) ? n : 0;
        }

        private bool DeleteAllIndexedDb(StorageAction action)
        {
            string origin = false == string.IsNullOrEmpty(action.Domain) ? UrlHelper.GetOrigin(action.Domain) : UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(browser.BrowserSettings));

            if (true == string.IsNullOrEmpty(origin))
                origin = UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(browser.BrowserSettings));

            if (string.IsNullOrEmpty(origin))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to determine current origin for [{action.StorageType}] d[{action.Domain}] sn[{action.StoreName}] p[{action.Path}] k[{action.Key}]");
                return false;
            }

            var cdpParams = new Dictionary<string, object>()
            {
                { "origin", origin },
                { "storageTypes", "indexeddb" }
            };

            dynamic cdpResult = BrowserHelper.ExecuteCdp(browserDriver, "Storage.clearDataForOrigin", cdpParams);

            string retVal = ExtractResultValue(cdpResult);

            if (retVal?.Contains("\"error\":true") == true)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB operation failed [{retVal}]", cdpResult, GPALObjectType.Other);
                return false;
            }

            return true;
        }

        private bool DeleteAllIndexedDbFirefox(StorageAction action)
        {
            string js = @"(async () => {
                try {
                    if (typeof indexedDB.databases !== 'function') return 'ok';
                    const dbs = await indexedDB.databases();
                    for (const db of dbs) {
                        await new Promise((res, rej) => {
                            const req = indexedDB.deleteDatabase(db.name);
                            req.onsuccess = res;
                            req.onblocked = res;
                            req.onerror = rej;
                        });
                    }
                    return 'ok';
                } catch(e) {
                    return JSON.stringify({error: true, message: e.message});
                }
            })()";
            string result = ExecuteFirefoxAsyncScript(js);
            if (result?.Contains("\"error\":true") == true)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB delete all failed [{result}]", action, GPALObjectType.Other);
                return false;
            }
            return true;
        }

        private bool SetCache(StorageAction action)
        {
            if (string.IsNullOrWhiteSpace(action.StoreName) || string.IsNullOrWhiteSpace(action.Key))
                return FailAndReturnFalse("SetCache requires StoreName (cache) and Key (request URL)", action);


            string cacheName = EscapeJs(action.StoreName);
            string requestUrl = EscapeJs(action.Key);
            string data = EscapeJs(action.Data);

            string js = $@"
                        (async () => {{

                            function fail(msg, err) {{
                                console.error(msg, err?.message || err || '');
                                return {{ error: true, message: msg }};
                            }}

                            function parseBody(v) {{
                                try {{
                                    return JSON.parse(v);
                                }} catch {{
                                    return v;
                                }}
                            }}

                            function buildResponse(body) {{
                                // Normalize body > string
                                let payload;
                                let contentType = 'text/plain;charset=utf-8';

                                if (typeof body === 'object' && body !== null) {{
                                    payload = JSON.stringify(body);
                                    contentType = 'application/json;charset=utf-8';
                                }} else {{
                                    payload = String(body);
                                }}

                                return new Response(payload, {{
                                    status: 200,
                                    statusText: 'OK',
                                    headers: {{
                                        'Content-Type': contentType,
                                        'Cache-Control': 'max-age=31536000'
                                    }}
                                }});
                            }}

                            function normalizeUrl(url) {{
                                try {{
                                    return new URL(url, location.origin).href;
                                }} catch {{
                                    throw new Error('Invalid URL: ' + url);
                                }}
                            }}

                            try {{
                                const cache = await caches.open('{cacheName}');

                                let normalizedUrl;
                                try {{
                                    normalizedUrl = normalizeUrl('{requestUrl}');
                                }} catch (e) {{
                                    return fail('URL normalization failed', e);
                                }}

                                const request = new Request(normalizedUrl, {{
                                    method: 'GET'
                                }});

                                const body = parseBody('{data}');
                                const response = buildResponse(body);

                                try {{
                                    await cache.put(request, response);
                                }} catch (e) {{
                                    return fail('cache.put failed', e);
                                }}

                                return {{
                                    success: true,
                                    cacheName: '{cacheName}',
                                    key: normalizedUrl
                                }};

                            }} catch (err) {{
                                return fail('Cache set failed', err);
                            }}

                        }})();
                        ";

            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                string ffResult = ExecuteFirefoxAsyncScript(js);
                if (IsErrorJson(ffResult ?? ""))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Cache set failed: [{ffResult}]", action, GPALObjectType.Other);
                    return false;
                }
                return ffResult?.Contains("success") == true;
            }

            string result = ExecuteAsyncJs<string>(js) ?? "false";

            if (IsErrorJson(result))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Cache set failed: [{result}]", action, GPALObjectType.Other);
                return false;
            }

            return result.Contains("success");
        }

        private bool SetIndexedDb(StorageAction action)
        {
            string db = EscapeJs(action.Path);
            string store = EscapeJs(action.StoreName);
            string key = EscapeJs(action.Key);
            string val = EscapeJs(action.Data);

            string js = $@"
                        (async () => {{
                            function fail(msg, err, db) {{
                                console.error(msg, err?.name || err || '');
                                try {{ db?.close(); }} catch {{ }}
                                return {{ error: true, message: msg }};
                            }}

                            function parseValue(v) {{
                                try {{ return JSON.parse(v); }} catch {{ return v; }}
                            }}

                            function isValidKey(k) {{
                                if (k === undefined) return false;
                                if (typeof k === 'string' || typeof k === 'number') return true;
                                if (k instanceof Date) return true;
                                if (Array.isArray(k)) return true;
                                return false;
                            }}

                            try {{
                                // Open DB
                                let db = await new Promise((res, rej) => {{
                                    let r = indexedDB.open('{db}');
                                    r.onsuccess = e => res(e.target.result);
                                    r.onupgradeneeded = e => {{
                                        if (!e.target.result.objectStoreNames.contains('{store}'))
                                            e.target.result.createObjectStore('{store}', {{ keyPath: null }});
                                    }};
                                    r.onerror = e => rej(e.target.error);
                                }});

                                let tx = db.transaction('{store}', 'readwrite');
                                let objStore = tx.objectStore('{store}');
                                let keyPath = objStore.keyPath;

                                let value = parseValue('{val}');
                                let inputKey = '{key}';
                                if (inputKey === '') inputKey = undefined;
                                if (inputKey !== undefined && !isValidKey(inputKey))
                                    return fail('Invalid key type', inputKey, db);

                                // Wrap value for inline keyPath if needed
                                if (keyPath !== null && (typeof value !== 'object' || value === null)) {{
                                    const wrapped = {{}};
                                    if (typeof keyPath === 'string') wrapped[keyPath] = inputKey;
                                    else if (Array.isArray(keyPath)) {{
                                        if (!Array.isArray(inputKey)) throw new Error('Compound key requires array key');
                                        for (let i = 0; i < keyPath.length; i++) wrapped[keyPath[i]] = inputKey[i];
                                    }}
                                    wrapped.value = value;
                                    value = wrapped;
                                    inputKey = undefined;
                                }}

                                let req;
                                try {{
                                    if (keyPath === null) {{
                                        if (inputKey === undefined && !objStore.autoIncrement)
                                            return fail('Missing key for out-of-line store', null, db);
                                        req = objStore.put(value, inputKey);
                                    }} else {{
                                        req = objStore.put(value);
                                    }}
                                }} catch (e) {{
                                    return fail('Put setup error', e, db);
                                }}

                                // Wait for transaction completion
                                await new Promise((res, rej) => {{
                                    tx.oncomplete = () => res(true);
                                    tx.onerror = () => rej(tx.error);
                                    tx.onabort = () => rej(tx.error || 'Transaction aborted');
                                }});

                                db.close();
                                return true;

                            }} catch (e) {{
                                return {{ error: true, message: e.message }};
                            }}
                        }})();
                        ";

            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                string ffResult = ExecuteFirefoxAsyncScript(js);
                if (ffResult == null || IsErrorJson(ffResult) || ffResult == "false")
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB set failed: [{ffResult}]", action, GPALObjectType.Other);
                    return false;
                }
                return true;
            }

            var parms = new Dictionary<string, object>
            {
                { "expression", js },
                { "awaitPromise", true },     // ← This is critical for async/await
                { "returnByValue", true }     // ← Returns plain value instead of RemoteObject
            };

            dynamic result = BrowserHelper.ExecuteCdp(browserDriver, "Runtime.evaluate", parms);

            if (false == result?["result"]?["value"])
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"IndexedDB set failed: [{result}]", action, GPALObjectType.Other);
                return false;
            }

            // The result value was checked above, so this is success.
            return true;
        }

        private string BuildDeleteKeysJs(string dbPat, string storePat, string keyPat)
        {
            string keyJs = $"'{EscapeJs(keyPat)}'";

            // The script returns the count of deleted keys.
            string collect = "deleted++";

            string dbLoop = string.IsNullOrWhiteSpace(dbPat)
                ? @"let dbs = await indexedDB.databases();
                for (let info of dbs) { let dbName = info.name;"
                : $"let dbName = '{EscapeJs(dbPat)}';";

            string dbClose = string.IsNullOrWhiteSpace(dbPat) ? "db.close(); }" : "db.close();";

            string storeLoop = string.IsNullOrWhiteSpace(storePat)
                ? @"for (let storeName of db.objectStoreNames) {"
                : $"let storeName = '{EscapeJs(storePat)}';";

            string storeClose = string.IsNullOrWhiteSpace(storePat) ? "}" : "";

            return $@"
            (async () => {{
                let deleted = 0;
                try {{
                    {dbLoop}
                        let db = await new Promise((res, rej) => {{
                            let r = indexedDB.open(dbName);
                            r.onsuccess = e => res(e.target.result);
                            r.onerror = e => rej(e.target.error);
                        }});
                        {storeLoop}
                            try {{
                                let tx = db.transaction(storeName, 'readwrite');
                                let store = tx.objectStore(storeName);
                                let exists = await new Promise((res, rej) => {{
                                    let r = store.get({keyJs});
                                    r.onsuccess = () => res(r.result !== undefined);
                                    r.onerror = rej;
                                }});
                                if (exists) {{
                                    await new Promise((res, rej) => {{
                                        let r = store.delete({keyJs});
                                        r.onsuccess = res;
                                        r.onerror = rej;
                                    }});
                                    {collect}
                                }}
                            }} catch (e) {{ /* skip bad store */ }}
                        {storeClose}
                        {dbClose}
                    return deleted;
                }} catch (e) {{
                    return JSON.stringify({{error:true, message:e.message}});
                }}
            }})();
        ";
        }

        private string BuildClearStoreJs(string dbNameRaw, string storeNameRaw)
        {
            string db = EscapeJs(dbNameRaw);
            string store = EscapeJs(storeNameRaw);

            return $@"
            (async () => {{
                try {{
                    let db = await new Promise((res, rej) => {{
                        let r = indexedDB.open('{db}');
                        r.onsuccess = e => res(e.target.result);
                        r.onerror = e => rej(e.target.error);
                    }});
                    let count = await new Promise((res, rej) => {{
                        let req = db.transaction('{store}', 'readonly').objectStore('{store}').count();
                        req.onsuccess = () => res(req.result);
                        req.onerror = rej;
                    }});
                    await new Promise((res, rej) => {{
                        let tx = db.transaction('{store}', 'readwrite');
                        let req = tx.objectStore('{store}').clear();
                        req.onsuccess = res;
                        req.onerror = rej;
                    }});
                    db.close();
                    return count;
                }} catch (e) {{
                    return JSON.stringify({{error:true, message:e.message}});
                }}
            }})();
        ";
        }

        // ──────────────────────────────────────────────────────────────
        // Cookies / Local-Session – your existing impl is already solid
        // (can keep as-is or apply similar error patterns if desired)
        // ──────────────────────────────────────────────────────────────

        // ──────────────────────────────────────────────────────────────
        // COOKIES
        private string GetCookies(StorageAction action)
        {
            bool isAllKey = string.IsNullOrEmpty(action.Key);

            // WebDriver lists only the current site's cookies. On Chrome and Edge, CDP Storage.getCookies lists every
            // cookie, so a site the browser is not on (For) can be read, as with Puppeteer. Firefox has no CDP and
            // uses WebDriver's list.
            var cookies = (Enums.BrowserType.FireFox == browser.BrowserType ? WebDriverCookies() : CdpCookies())
                .Where(c =>
                    (isAllKey || c.Name == action.Key) &&
                    UrlHelper.CookieDomainMatches(c.Domain, action.Domain) &&
                    (string.IsNullOrEmpty(action.Path) || c.Path == action.Path))
                .ToList();

            if (!cookies.Any()) return "[]";

            // Return the full cookie record. The names-only option (commented out below) depends on
            // action.IncludeProvenance, which no fluent call sets.
            string data = System.Text.Json.JsonSerializer.Serialize(cookies.Select(c => new
                {
                    name = c.Name,
                    value = c.Value,
                    domain = c.Domain,
                    path = c.Path,
                    expiry = c.Expiry,
                    secure = c.Secure,
                    httpOnly = c.IsHttpOnly
                }));

            //// post-v1: provenance
            //if (false == action.IncludeProvenance)
            //    data = System.Text.Json.JsonSerializer.Serialize(cookies.Select(c => c.Name));

            action.Data = data;
            return data;
        }

        // One cookie in a shape shared by the WebDriver and CDP lists, so filtering and results match either way.
        private class CookieRecord
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Domain { get; set; }
            public string Path { get; set; }
            public DateTime? Expiry { get; set; }
            public bool Secure { get; set; }
            public bool IsHttpOnly { get; set; }
        }

        // the current site's cookies, which is all WebDriver will list
        private List<CookieRecord> WebDriverCookies()
        {
            List<CookieRecord> retVal = webDriver.Manage().Cookies.AllCookies
                .Select(c => new CookieRecord
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path,
                    Expiry = c.Expiry,
                    Secure = c.Secure,
                    IsHttpOnly = c.IsHttpOnly
                })
                .ToList();

            return retVal;
        }

        // every cookie in the browser, from CDP. expires is seconds since 1970, and a session cookie answers -1,
        // which is no expiry at all, the same as WebDriver reports one
        private List<CookieRecord> CdpCookies()
        {
            List<CookieRecord> retVal = new List<CookieRecord>();
            dynamic response = BrowserHelper.ExecuteCdp(browserDriver, "Storage.getCookies", new Dictionary<string, object>());

            if (response?["cookies"] is Array cookies)
                foreach (dynamic c in cookies)
                {
                    double expires = null == c["expires"] ? -1 : Convert.ToDouble(c["expires"]);

                    retVal.Add(new CookieRecord
                    {
                        Name = (string)c["name"],
                        Value = (string)c["value"],
                        Domain = (string)c["domain"],
                        Path = (string)c["path"],
                        Expiry = 0 < expires ? DateTimeOffset.FromUnixTimeMilliseconds((long)(expires * 1000)).UtcDateTime : (DateTime?)null,
                        Secure = true == (bool?)c["secure"],
                        IsHttpOnly = true == (bool?)c["httpOnly"]
                    });
                }

            return retVal;
        }

        private int DeleteCookies(StorageAction action)
        {
            bool isAllKey = string.IsNullOrEmpty(action.Key);

            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                int deleted = 0;
                if (isAllKey && string.IsNullOrEmpty(action.Domain) && string.IsNullOrEmpty(action.Path))
                {
                    int count = webDriver.Manage().Cookies.AllCookies.Count;
                    webDriver.Manage().Cookies.DeleteAllCookies();
                    return count;
                }
                foreach (var c in webDriver.Manage().Cookies.AllCookies.ToList())
                {
                    if (!isAllKey && c.Name != action.Key) continue;
                    if (!UrlHelper.CookieDomainMatches(c.Domain, action.Domain)) continue;
                    if (!string.IsNullOrEmpty(action.Path) && c.Path != action.Path) continue;
                    webDriver.Manage().Cookies.DeleteCookieNamed(c.Name);
                    deleted++;
                }
                // Nothing to delete is not a failure: the cookie is gone either way
                if (!isAllKey && deleted == 0)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No cookie named [{action.Key}] to delete", action, GPALObjectType.Other);
                return deleted;
            }

            // Selenium's native Manage().Cookies and JS ExecuteScript often fail
            // to clear Partitioned (CHIPS) cookies, so CDP clears them.
            int deletedCdp = 0;

            // 1. Get every cookie in the browser via CDP, with its PartitionKey, so a site's cookies can be cleared
            // before the browser goes there (For), as with Puppeteer.
            dynamic response = BrowserHelper.ExecuteCdp(browserDriver, "Storage.getCookies", new Dictionary<string, object>());
            var cookies = response["cookies"] as Array;

            if (cookies == null) return 0;

            foreach (dynamic c in cookies)
            {
                string name = (string)c["name"];
                string domain = (string)c["domain"];
                string path = (string)c["path"];

                // 2. Apply your existing filters
                if (!isAllKey && name != action.Key) continue;
                if (!UrlHelper.CookieDomainMatches(domain, action.Domain)) continue;
                if (!string.IsNullOrEmpty(action.Path) && path != action.Path) continue;

                // 3. Build the deletion parameters for CDP
                var delParams = new Dictionary<string, object>
                {
                    { "name", name },
                    { "domain", domain },
                    { "path", path }
                };

                // 4. Handle PartitionKey if it exists
                if (c.ContainsKey("partitionKey") && c["partitionKey"] != null)
                {
                    var pk = new Dictionary<string, object>
                    {
                        { "topLevelSite", (string)c["partitionKey"]["topLevelSite"] },
                        { "hasCrossSiteAncestor", (bool)c["partitionKey"]["hasCrossSiteAncestor"] }
                    };
                    delParams.Add("partitionKey", pk);
                }

                // 5. Execute the surgical delete
                BrowserHelper.ExecuteCdp(browserDriver, "Network.deleteCookies", delParams);
                deletedCdp++;
            }

            // Nothing to delete is not a failure: the cookie is gone either way
            if (!isAllKey && deletedCdp == 0)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No cookie named [{action.Key}] to delete", action, GPALObjectType.Other);

            return deletedCdp;
        }


        private bool SetCookie(StorageAction action)
        {
            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                string name = action.Key;
                string value = action.Data;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[key] and [data] cannot be empty. Unable to set [{action.StorageType}]", action, GPALObjectType.Other);
                    return false;
                }
                string path = string.IsNullOrWhiteSpace(action.Path) ? "/" : action.Path;
                string domain = string.IsNullOrEmpty(action.Domain) ? new Uri(webDriver.Url).Host : action.Domain;
                webDriver.Manage().Cookies.AddCookie(new Cookie(name, value, domain, path, null));
                return true;
            }

            try
            {
                string name = action.Key;
                string value = action.Data;
                string path = string.IsNullOrWhiteSpace(action.Path) ? "/" : action.Path;
                string domainInput = action.Domain;

                // Collect empty fields
                var emptyFields = new List<string>();

                if (string.IsNullOrWhiteSpace(name))
                    emptyFields.Add("key");

                if (string.IsNullOrWhiteSpace(value))
                    emptyFields.Add("data");

                if (string.IsNullOrWhiteSpace(path))
                    emptyFields.Add("path");

                if (string.IsNullOrWhiteSpace(domainInput))
                    emptyFields.Add("domain");

                // Build CSV string
                string emptyCsv = string.Join(",", emptyFields);

                // Optional: check if any are empty
                if (0 < emptyFields.Count)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{emptyCsv}] cannot be empty. Unable to set [{action.StorageType}]", action, GPALObjectType.Other);
                    return false;
                }

                IWebDriver driver = browserDriver;

                // 1️⃣ Get cookies scoped to current page using Storage.getCookies
                var getParams = new Dictionary<string, object>
                {
                    ["urls"] = new[] { driver.Url }
                };

                dynamic result = BrowserHelper.ExecuteCdp(driver, "Storage.getCookies", getParams);
                var cookies = result["cookies"] as object[];

                dynamic existing = null;

                if (cookies != null)
                {
                    foreach (dynamic c in cookies)
                    {
                        string cName = c["name"]?.ToString() ?? "";
                        string cDomain = c["domain"]?.ToString() ?? "";
                        string cPath = c["path"]?.ToString() ?? "/";

                        bool domainMatches = string.IsNullOrEmpty(domainInput)
                            || cDomain == domainInput
                            || cDomain.EndsWith("." + domainInput);

                        if (cName == name && domainMatches && cPath == path)
                        {
                            existing = c;
                            break;
                        }
                    }
                }

                // 2️⃣ Build merged cookie
                var cookie = new Dictionary<string, object>();

                cookie["name"] = name;
                cookie["value"] = value;
                cookie["path"] = path;

                // Domain resolution
                string domain =
                    existing?["domain"]?.ToString()
                    ?? (!string.IsNullOrEmpty(domainInput)
                        ? domainInput
                        : new Uri(driver.Url).Host);

                cookie["domain"] = domain;

                // Expiry (~6 months default)
                cookie["expires"] =
                    existing?["expires"] != null
                    ? (double)existing["expires"]
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (180L * 24 * 60 * 60);

                cookie["secure"] = existing?["secure"] ?? true;
                cookie["httpOnly"] = existing?["httpOnly"] ?? true;
                cookie["sameSite"] = existing?["sameSite"]?.ToString() ?? "None";

                // Optional extras
                cookie["priority"] = existing?["priority"]?.ToString() ?? "Medium";

                // Only include if present
                if (existing?["partitionKey"] != null)
                    cookie["partitionKey"] = existing["partitionKey"];

                // 3️⃣ Set cookie via Network.setCookie
                BrowserHelper.ExecuteCdp(driver, "Network.setCookie", cookie);

                return true;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Failed to set cookie [{action.Key}]",
                    action, GPALObjectType.Other, ex);

                return false;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // LOCAL / SESSION STORAGE
        private string GetLocalOrSessionStorage(StorageAction action)
        {
            bool isAllKey = string.IsNullOrEmpty(action.Key);
            string storageObj = action.StorageType.ToString();

            string script = isAllKey
                ? $@"
                let obj = {{}};
                for (let i = 0; i < {storageObj}.length; i++) {{
                    let k = {storageObj}.key(i);
                    obj[k] = {storageObj}.getItem(k);
                }}
                return JSON.stringify(obj);"
                : $"return {storageObj}.getItem('{EscapeJs(action.Key)}') || null;";

            dynamic result = webDriver.ExecuteJavaScript<dynamic>(script);

            action.Data = result;
            return result;
        }

        private int DeleteLocalOrSessionStorage(StorageAction action)
        {
            bool isAllKey = string.IsNullOrEmpty(action.Key);
            string storageObj = action.StorageType.ToString();

            string script = isAllKey
                ? $@"
                let count = {storageObj}.length;
                {storageObj}.clear();
                return count;"
                : $@"
                if ({storageObj}.getItem('{EscapeJs(action.Key)}') !== null) {{
                    {storageObj}.removeItem('{EscapeJs(action.Key)}');
                    return 1;
                }} else return 0;";

            return webDriver.ExecuteJavaScript<int>(script);
        }

        private bool SetLocalOrSessionStorage(StorageAction action)
        {
            string storageObj = action.StorageType.ToString();
            string script = $"{storageObj}.setItem('{EscapeJs(action.Key)}', '{EscapeJs(action.Data)}'); return true;";
            return webDriver.ExecuteJavaScript<bool>(script);
        }

        // ──────────────────────────────────────────────────────────────
        // CACHE STORAGE
        private string GetCache(StorageAction action)
        {
            string expression;

            if (string.IsNullOrEmpty(action.StoreName) && string.IsNullOrEmpty(action.Key))
            {
                // Case 1: No path, no key > Return array of cache names only (matches original JS extension)
                expression = @"(async () => {
                                const cacheNames = await caches.keys();
                                return JSON.stringify(cacheNames);
                            })();";
            }
            else if (string.IsNullOrEmpty(action.Key))
            {
                // Case 2: Path only > List URLs inside the cache (no bodies)
                expression = $@"(async () => {{
                                const cache = await caches.open('{EscapeJs(action.StoreName)}');
                                const requests = await cache.keys();
                                const urls = requests.map(req => req.url);
                                return JSON.stringify(urls);
                            }})();";
            }
            else
            {
                // Case 3: Path + Key > Return { url, body } or null (matches original JS)
                expression = $@"(async () => {{
                                const cache = await caches.open('{EscapeJs(action.StoreName)}');
                                const response = await cache.match('{EscapeJs(action.Key)}');

                                if (!response) return null;

                                let body = null;
                                try {{
                                    body = await response.clone().text();
                                }} catch (e) {{
                                    body = '[unreadable]';
                                }}

                                return JSON.stringify({{
                                    url: response.url,
                                    body: body
                                }});
                            }})();";
            }

            if (Enums.BrowserType.FireFox == browser.BrowserType)
            {
                string retVal = ExecuteFirefoxAsyncScript(expression);
                action.Data = retVal;
                return retVal;
            }

            var parameters = new Dictionary<string, object>
            {
                { "expression", expression },
                { "awaitPromise", true },     // Critical for async IIFE
                { "returnByValue", true }     // Return plain JSON string instead of RemoteObject
            };

            try
            {
                dynamic cdpResult = BrowserHelper.ExecuteCdp(browserDriver, "Runtime.evaluate", parameters);

                string retVal = ExtractResultValue(cdpResult);

                action.Data = retVal;
                return retVal;
            }
            catch (Exception ex)
            {
                // Log or handle as needed
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Cache access failed", action, GPALObjectType.Other, ex);
                action.Data = null;
                return null;
            }
        }

        /// <summary>
        /// Safely extracts the returned value from Runtime.evaluate CDP response
        /// </summary>
        private string ExtractResultValue(IDictionary<string, object> cdpResult)
        {
            if (cdpResult == null) return null;

            if (cdpResult.TryGetValue("result", out var resultObj))
            {
                if (resultObj is IDictionary<string, object> resultDict)
                {
                    if (resultDict.TryGetValue("value", out var value))
                    {
                        // A returned object comes back as a dictionary, whose ToString is its type name. Serialize it
                        // to JSON so {success:true} and {error:true} can be read.
                        return value is string said ? said : (null == value ? null : JsonConvert.SerializeObject(value));
                    }
                }
            }

            // Fallback for some edge cases
            if (cdpResult.TryGetValue("value", out var directValue))
            {
                return directValue?.ToString();
            }

            return null;
        }

        private int DeleteCache(StorageAction action)
        {
            // The cache name is StoreName, set by .WithStorageStoreName and read by SetCache and GetCache. Return 0
            // when it is not set.
            if (string.IsNullOrEmpty(action.StoreName))
            {
                return 0; // nothing to do
            }

            bool deleteWholeCache = string.IsNullOrEmpty(action.Key);

            // caches.delete and cache.delete return whether anything was deleted; await them and return 1 or 0 from
            // that.
            string deleteCode = deleteWholeCache
                ? $"const gone = await caches.delete('{EscapeJs(action.StoreName)}');"
                : $"const cache = await caches.open('{EscapeJs(action.StoreName)}'); const gone = await cache.delete('{EscapeJs(action.Key)}');";

            string script = $@"(async () => {{
                                if (!('caches' in window)) return 0;
                                try {{
                                    {deleteCode}
                                    return gone ? 1 : 0;
                                }} catch(e) {{
                                    return JSON.stringify({{error: true, message: e.message}});
                                }}
                            }})();";

            string raw = ExecuteAsyncJs<string>(script) ?? "0";

            if (IsErrorJson(raw))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Cache delete failed: [{raw}]", action, GPALObjectType.Other);
                return 0;
            }

            return int.TryParse(raw, out int deleted) ? deleted : 0;
        }

        // ──────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────

        private string ExecuteFirefoxAsyncScript(string asyncIife)
        {
            webDriver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);
            string iife = asyncIife.TrimEnd(';', ' ', '\n', '\r', '\t');
            string wrapped = $@"var __cb = arguments[arguments.length - 1];
(async () => {{
    try {{
        const __r = await {iife};
        __cb(__r != null ? (typeof __r === 'string' ? __r : JSON.stringify(__r)) : null);
    }} catch(e) {{
        __cb(JSON.stringify({{error: true, message: e.message}}));
    }}
}})();";
            try
            {
                return ((IJavaScriptExecutor)webDriver).ExecuteAsyncScript(wrapped)?.ToString();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Firefox async script failed", browser, GPALObjectType.Browser, ex);
                return null;
            }
        }

        /// <summary>
        /// Runs a self-invoking async script through CDP Runtime.evaluate with awaitPromise, as the IndexedDB code
        /// above does, and returns what it resolved to.
        /// </summary>
        /// <param name="js">The script, already a self-invoking async function</param>
        /// <param name="fallback">What to answer with when it could not run</param>
        /// <returns>What the script resolved to, or the fallback as text</returns>
        private string ExecuteAsyncJs<T>(string js, T fallback = default)
        {
            string retVal = fallback?.ToString() ?? "";

            try
            {
                if (true == browser.UseSelenium && BrowserType.FireFox == browser.BrowserSettings.BrowserType)
                    retVal = ExecuteFirefoxAsyncScript(js) ?? retVal;
                else
                {
                    var parms = new Dictionary<string, object>
                    {
                        { "expression", js },
                        { "awaitPromise", true },
                        { "returnByValue", true }
                    };

                    dynamic cdpResult = BrowserHelper.ExecuteCdp(browserDriver, "Runtime.evaluate", parms);

                    retVal = ExtractResultValue(cdpResult) ?? retVal;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Async script failed", browser, GPALObjectType.Browser, ex);
            }

            return retVal;
        }

        private bool IsErrorJson(dynamic s) => s.Contains("\"error\":true");

        private string FailAndReturnNull(string msg, StorageAction a)
        {
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, a, GPALObjectType.Other);
            return null;
        }

        private int FailAndReturnZero(string msg, StorageAction a)
        {
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, a, GPALObjectType.Other);
            return 0;
        }

        private bool FailAndReturnFalse(string msg, StorageAction a)
        {
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, a, GPALObjectType.Other);
            return false;
        }
    }
}

