using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace DarkAmbientRadio.Core.Bandcamp;

/// <summary>
/// Drives a headed, persistent-context Chromium session through the Cryo Chamber
/// "yum" code-redemption flow: cookie banner → (manual login if required) → enter code →
/// add to collection → pick MP3 320 → download.
///
/// NOTE: the CSS/text selectors below are a best-effort first pass and must be verified
/// against the live page during the acquisition dry-run (see plan, step A2).
/// </summary>
public sealed partial class BandcampRedeemer : IAsyncDisposable
{
    public const string YumUrl = "https://cryochamber.bandcamp.com/yum";

    /// <summary>How long to wait for Bandcamp to finish preparing the ZIP server-side.</summary>
    private static readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How often to re-report progress while waiting on "preparing".</summary>
    private static readonly TimeSpan PrepareHeartbeat = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The ready link reads "Download &lt;album title&gt;", so anchor on the leading word rather
    /// than matching the whole name. The anchor also keeps "Need download help?" out.
    /// </summary>
    [GeneratedRegex(@"^\s*Download\b", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadLinkName();

    private readonly string _userDataDir;
    private readonly string _downloadDir;

    /// <summary>Cancelled when the user closes the browser window, so waits don't hang forever.</summary>
    private readonly CancellationTokenSource _browserClosed = new();

    private IPlaywright? _playwright;
    private IBrowserContext? _context;

    public BandcampRedeemer(string userDataDir, string downloadDir)
    {
        _userDataDir = userDataDir;
        _downloadDir = downloadDir;
    }

    /// <summary>
    /// Runs the full redemption and returns the path of the downloaded ZIP in the
    /// configured download directory.
    /// </summary>
    /// <param name="code">The (already validated) review code.</param>
    /// <param name="addToCollection">Whether to tick "Add this item to my collection".</param>
    /// <param name="waitForManualLogin">
    /// Invoked when a login is required; the UI should prompt the user to log in inside the
    /// opened browser window and complete the returned task once they signal "Weiter".
    /// </param>
    public async Task<string> RedeemAsync(
        string code,
        bool addToCollection,
        Func<CancellationToken, Task> waitForManualLogin,
        IProgress<string>? progress = null,
        CancellationToken callerToken = default)
    {
        // Closing the browser window must abort the run — otherwise a wait (most notably the
        // manual-login gate) would sit there forever with a dead browser behind it.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _browserClosed.Token);
        var ct = linked.Token;

        try
        {
            return await RedeemCoreAsync(code, addToCollection, waitForManualLogin, progress, ct);
        }
        catch (OperationCanceledException) when (_browserClosed.IsCancellationRequested
                                                 && !callerToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Browserfenster wurde geschlossen – Vorgang abgebrochen.");
        }
    }

    private async Task<string> RedeemCoreAsync(
        string code,
        bool addToCollection,
        Func<CancellationToken, Task> waitForManualLogin,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var page = await EnsurePageAsync(ct);

        progress?.Report("Öffne Cryo Chamber …");
        await page.GotoAsync(YumUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await DismissCookieBannerAsync(page, progress);

        // Loop: if a login is required, hand over to the user, then retry the page.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (await IsCodeEntryVisibleAsync(page))
                break;

            if (await IsLoginRequiredAsync(page))
            {
                progress?.Report("Anmeldung nötig – bitte im Browserfenster einloggen, dann »Weiter«.");
                await waitForManualLogin(ct);
                await page.GotoAsync(YumUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await DismissCookieBannerAsync(page, progress);
                continue;
            }

            // Neither state detected yet – give the page a moment and re-check.
            await page.WaitForTimeoutAsync(1000);
        }

        if (!await IsCodeEntryVisibleAsync(page))
            throw new InvalidOperationException("Code-Eingabefeld nicht gefunden (Seitenlayout geändert?).");

        progress?.Report("Gebe Code ein …");
        await FillCodeAsync(page, code);

        if (addToCollection)
            await SetAddToCollectionAsync(page);

        await SubmitCodeAsync(page);

        // Redeeming navigates to the bandcamp.com/download?... page ("Choose format:").
        progress?.Report("Warte auf Download-Seite …");
        await page.WaitForURLAsync(url => url.Contains("/download?", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = 60_000 });

        var formatSelect = page.Locator("select").First;
        await formatSelect.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        progress?.Report("Wähle Format MP3 320 …");
        await SelectMp3320Async(formatSelect);

        await WaitForDownloadReadyAsync(page, progress, ct);

        progress?.Report("Starte Download …");
        var path = await DownloadAsync(page, ct);
        progress?.Report($"Heruntergeladen: {Path.GetFileName(path)}");
        return path;
    }

    // ----- Steps -------------------------------------------------------------

    private async Task DismissCookieBannerAsync(IPage page, IProgress<string>? progress)
    {
        string[] labels = { "nur notwendige", "Only necessary", "Nur erforderliche", "Reject all" };
        foreach (var label in labels)
        {
            var button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = label, Exact = false });
            if (await IsVisibleAsync(button))
            {
                progress?.Report("Cookie-Banner: nur notwendige.");
                await button.First.ClickAsync();
                return;
            }
        }
    }

    private static async Task<bool> IsCodeEntryVisibleAsync(IPage page)
        => await IsVisibleAsync(page.GetByText("Please enter your code", new PageGetByTextOptions { Exact = false }))
           || await IsVisibleAsync(FindCodeInput(page));

    private static async Task<bool> IsLoginRequiredAsync(IPage page)
        => await IsVisibleAsync(page.GetByText("log in", new PageGetByTextOptions { Exact = false }))
           || await IsVisibleAsync(page.GetByText("melde dich an", new PageGetByTextOptions { Exact = false }))
           || await IsVisibleAsync(page.Locator("input[type='password']"));

    private static ILocator FindCodeInput(IPage page)
        => page.Locator("input[name='code'], input#code, input[placeholder*='code' i]");

    private static async Task FillCodeAsync(IPage page, string code)
    {
        var input = FindCodeInput(page).First;
        await input.FillAsync(code);
    }

    private static async Task SetAddToCollectionAsync(IPage page)
    {
        var checkbox = page.GetByRole(AriaRole.Checkbox,
            new PageGetByRoleOptions { Name = "Add this item to my collection", Exact = false });
        if (await IsVisibleAsync(checkbox))
        {
            await checkbox.First.CheckAsync();
            return;
        }

        // Fallback: label text next to a checkbox.
        var byLabel = page.GetByText("Add this item to my collection", new PageGetByTextOptions { Exact = false });
        if (await IsVisibleAsync(byLabel))
            await byLabel.First.ClickAsync();
    }

    private static async Task SubmitCodeAsync(IPage page)
    {
        string[] names = { "redeem", "submit", "download", "continue" };
        foreach (var name in names)
        {
            var button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name, Exact = false });
            if (await IsVisibleAsync(button))
            {
                await button.First.ClickAsync();
                return;
            }
        }
        // Fallback: submit the form the code input belongs to.
        await FindCodeInput(page).First.PressAsync("Enter");
    }

    private static async Task SelectMp3320Async(ILocator formatSelect)
    {
        // Bandcamp's format <select> option for MP3 320 has value "mp3-320"; its label
        // includes the file size ("MP3 320 - 37.9MB"), so match by value or a text regex.
        var value = await formatSelect.EvaluateAsync<string?>(
            @"sel => {
                const opt = [...sel.options].find(o =>
                    o.value === 'mp3-320' || /mp3\s*320/i.test(o.textContent || ''));
                return opt ? opt.value : null;
            }");

        if (value is null)
            throw new InvalidOperationException("Format »MP3 320« nicht in der Auswahl gefunden.");

        await formatSelect.SelectOptionAsync(new SelectOptionValue { Value = value });

        // Selecting a format triggers an async update of the download link; give it a moment.
        await formatSelect.Page.WaitForTimeoutAsync(1500);
    }

    /// <summary>The download anchor, matched on its leading "Download" word.</summary>
    private static ILocator DownloadLink(IPage page)
        => page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { NameRegex = DownloadLinkName() });

    /// <summary>
    /// When the ZIP is not cached server-side, Bandcamp first shows "Preparing…" in place of the
    /// download link and only swaps it to "Download &lt;album title&gt;" once the archive is built.
    /// Clicking during that phase does nothing, so poll until the real link appears.
    /// </summary>
    private static async Task WaitForDownloadReadyAsync(IPage page, IProgress<string>? progress, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var nextHeartbeat = TimeSpan.Zero;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (await IsVisibleAsync(DownloadLink(page)))
            {
                if (nextHeartbeat > TimeSpan.Zero)
                    progress?.Report("Download ist bereit.");
                return;
            }

            var elapsed = DateTime.UtcNow - started;
            if (elapsed >= PrepareTimeout)
                throw new TimeoutException(
                    $"Bandcamp hat den Download nach {PrepareTimeout.TotalMinutes:0} Minuten nicht bereitgestellt.");

            // Re-report periodically: a single message would look like a hang for minutes.
            if (elapsed >= nextHeartbeat)
            {
                progress?.Report($"Bandcamp bereitet den Download vor … ({elapsed.TotalSeconds:0} s)");
                nextHeartbeat = elapsed + PrepareHeartbeat;
            }

            await page.WaitForTimeoutAsync(1000);
        }
    }

    private async Task<string> DownloadAsync(IPage page, CancellationToken ct)
    {
        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            var link = DownloadLink(page);
            if (await IsVisibleAsync(link))
            {
                await link.First.ClickAsync();
                return;
            }
            var button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Download", Exact = false });
            await button.First.ClickAsync();
        }, new PageRunAndWaitForDownloadOptions { Timeout = 300_000 });

        Directory.CreateDirectory(_downloadDir);
        var target = Path.Combine(_downloadDir, download.SuggestedFilename);
        await download.SaveAsAsync(target);
        return target;
    }

    // ----- Infrastructure ----------------------------------------------------

    private async Task<IPage> EnsurePageAsync(CancellationToken ct)
    {
        if (_context is null)
        {
            _playwright = await Playwright.CreateAsync();
            Directory.CreateDirectory(_userDataDir);
            _context = await _playwright.Chromium.LaunchPersistentContextAsync(_userDataDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    AcceptDownloads = true,
                    ViewportSize = ViewportSize.NoViewport,
                });

            // Fires when the user closes the window (and on our own DisposeAsync, which is
            // harmless — by then the run is over anyway).
            _context.Close += (_, _) => _browserClosed.Cancel();
        }

        return _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
    }

    private static async Task<bool> IsVisibleAsync(ILocator locator)
    {
        try
        {
            return await locator.First.IsVisibleAsync();
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        _playwright?.Dispose();
        _browserClosed.Dispose();
    }
}
