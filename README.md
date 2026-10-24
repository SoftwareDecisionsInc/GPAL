# GPAL

**One fluent language for browsers, desktop applications, REST, AI and data. In the same workflow.**

Most automation forces you to pick a lane. A browser tool cannot touch the accounting application you
have to paste the numbers into. A desktop tool cannot log into the site the numbers come from. So the
real work, the part that spans both, ends up as glue nobody wants to maintain.

GPAL is one library, one syntax, and no lanes.

```csharp
class Order
{
    public string OrderNumber { get; set; }
    public string Total { get; set; }
}

// each selector's name becomes a column in the grid, and the converter matches columns to Order's properties
static Selector orderNumber = (Selector)GPAL.Selector.WithCSS("td.order-number").WithSelectorName("OrderNumber");
static Selector orderTotal = (Selector)GPAL.Selector.WithCSS("td.order-total").WithSelectorName("Total");
static Selector importButton = (Selector)GPAL.Selector.WithAutomationID("btnImport");

IGPALGrid<string> rows = GPAL.Grid.ToGPALObject();

// read a site with a real browser on a hidden desktop, so it never appears on your screen. Everything that
// names the same desktop shares it; WithHiddenDesktop() with no name gives each its own
GPAL.Browser
    .WithBrowserType(BrowserType.Chrome)
    .WithHiddenDesktop("GPAL.Hidden.Orders")
    .GoTo("https://example.com/orders")
    .WithSelector(orderNumber)
    .WithSelector(orderTotal)
    .WithAllThatMatch(50)                  // the first 50 rows; if not supplied, every row that matches
    .GetGrid(ref rows)
    .Close(true);

// one converter for every shape: typed objects, and any file format its extension names
List<Order> orders = new List<Order>();
GPAL.Converter
    .WithInput(rows)
    .SaveTo(ref orders)
    .SaveTo(@"C:\work\orders.csv")
    .SaveTo(@"C:\work\orders.json")
    .SaveTo(@"C:\work\orders.yaml");

// then drive the program that has to receive it, on the same hidden desktop as the browser
GPAL.Application
    .WithHiddenDesktop("GPAL.Hidden.Orders")
    .WithParameter(@"C:\work\orders.csv")
    .Open(@"C:\Program Files\Accounts\accounts.exe")
    .WithSelector(importButton)
    .LeftClick();
```

Same verbs on both sides. `WithSelector`, `LeftClick`, `GetGrid`, `SaveTo`, `WithHiddenDesktop`. Learn it once.

---

## Why it is different

**Nine automation engines, one API.** Selenium, a Puppeteer engine built into GPAL that needs no driver
download at all and reaches the browser over a debugging port or a pipe, and OttoMagic, the browser extension
GPAL drives through its REST API, each with a hardware input variant, plus Selenium with JavaScript input, across
Chrome, Edge and Firefox. Change the engine, not the workflow.

**A real browser nobody can see.** `WithHiddenDesktop()` runs a genuine headful browser, real rendering,
real fingerprint, on a Windows desktop object of its own. It never appears on your screen, never steals
your focus, never eats your keystrokes. That is not headless, which is a different binary that renders
differently and is exactly what detection looks for. Programs can run there too, so a whole workflow can
happen on a machine you are still using.

**Desktop automation that is not an afterthought.** UIAutomation with a path syntax that reads like
XPath, grid reads straight out of a list view, drag and drop, file dialogs answered without keystrokes,
and MSAA underneath for the controls Windows never modernized.

**Units of work.** A selector, the things that narrow it, and the action that spends it. The chain reads
as one sentence and GPAL knows where one piece of work ends and the next begins.

**Nothing is written without you asking.** GPAL creates no log files, no telemetry, no phone-home. Every
operation publishes a structured event, and what you attach decides where it goes: a console, a logger, a
database, or a handler of your own that inspects the context and stops the run.

```csharp
GPAL.WithInformationHandler((sender, e) => Console.WriteLine($"[{e.GPALEventType}] {e.Message}"))
    .WithExceptionHandler((sender, e) => e.ScreenShot?.Save("what-went-wrong.png"));
```

---

## Getting started

```
dotnet add package GPAL
```

Matching elements by image? Add **GPAL.Image** too and register it once at startup. GPAL itself carries no
image library, so it stays light.

```
dotnet add package GPAL.Image
```

```csharp
using GenerallyPositive.Image;

GPALImage.Register();
```

GPAL.Image needs a 64-bit process, since OpenCV ships its native library for 64-bit Windows only. Target x64,
or turn off Prefer 32-bit on an AnyCPU program; in a 32-bit process Register() registers nothing and publishes
a warning.

.NET Framework 4.8. Add the optional **GPALRestAPI** and the **OttoMagic** browser extension for
REST driven browser automation and smarter element detection, and **OttoMatic** to record a workflow by
doing it once.

Full documentation, tutorials and the API reference: **https://gpal.sditx.com/**

---

## Credits

GPAL was conceived, architected, and primarily implemented by **Michael B. Vederman**, CEO of Software
Decisions, Inc.

Related products developed under Software Decisions, Inc.:

- **OttoMatic** GPAL browser recorder: Developed by Michael B. Vederman.
- **OttoMatic Desktop** GPAL application recorder: Developed by Michael B. Vederman.
- **OttoMagic** GPAL browser REST API executor: Developed by Scott Ramirtha.
- **GPALRestAPI**: Developed by Michael B. Vederman.
- **GPAL Studio**: Developed by Bryan Alvarez Pandozi.

---

## License

GPAL is source available under the **Business Source License 1.1**, with an Additional Use Grant that
permits production use, including commercial, internal, hosted and revenue generating, provided you are
not offering GPAL itself to third parties as a competing product or service.

On the Change Date, the earlier of January 1 2031 or four years after a version is first distributed, it
becomes **GPL v3.0 or later**. Full terms, including the Additional Use Grant and the Continuity
Guarantee: [LICENSE.md](./LICENSE.md).

Copyright © 2026 Software Decisions, Inc. All rights reserved.

*This repository is a read only public release mirror. Development happens privately, and pull requests
are not accepted here.*
