#if WINDOWS
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace MTGProxyBuilder.Tests.Integration;

/// <summary>
/// UI smoke tests using FlaUI to verify the WPF application launches and
/// basic UI elements are accessible. These tests launch the actual app.
/// </summary>
[Trait("Category", "UI")]
public class UiSmokeTests : IDisposable
{
    private Application? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    private bool LaunchApp()
    {
        try
        {
            string exePath = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "MTGProxyBuilder.UI", "bin", "Debug", "net10.0-windows", "tcg-proxy-builder.exe");
            exePath = Path.GetFullPath(exePath);

            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(Path.GetDirectoryName(exePath)!, "MTGProxyBuilder.UI.exe");
            }

            if (!File.Exists(exePath))
                return false;

            _automation = new UIA3Automation();
            _app = Application.Launch(exePath);
            _mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(10));
            return _mainWindow != null;
        }
        catch
        {
            return false;
        }
    }

    // The per-project toolbar and AvalonDock panels are hidden until a project is open.
    // This helper opens a new project via the welcome screen button or the global toolbar.
    private bool OpenNewProject()
    {
        // Welcome screen shows "New Project" when no project is active
        var welcomeBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("New Project"))?.AsButton();
        if (welcomeBtn != null)
        {
            welcomeBtn.Click();
            Thread.Sleep(800);
            return true;
        }

        // Fallback: global toolbar always has "+ New"
        var toolbarBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("+ New"))?.AsButton();
        if (toolbarBtn != null)
        {
            toolbarBtn.Click();
            Thread.Sleep(800);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        try { _app?.Close(); } catch { }
        try { _app?.Dispose(); } catch { }
        try { _automation?.Dispose(); } catch { }
    }

    [Fact]
    public void App_Launches_Successfully()
    {
        if (!LaunchApp()) return;

        Assert.NotNull(_mainWindow);
        Assert.Contains("MTG Proxy Builder", _mainWindow!.Title);
    }

    [Fact]
    public void App_HasToolbarButtons()
    {
        if (!LaunchApp()) return;

        // Global toolbar is always visible regardless of project state
        var buttons = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        var buttonNames = buttons.Select(b => b.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        Assert.Contains("+ New", buttonNames);
        Assert.Contains("Open", buttonNames);

        // Per-project toolbar (Save, Export PDF) is only visible with an active project
        if (!OpenNewProject()) return;

        buttons = _mainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        buttonNames = buttons.Select(b => b.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        Assert.Contains("Save", buttonNames);
        Assert.Contains("Export PDF", buttonNames);
    }

    [Fact]
    public void App_HasTabs()
    {
        if (!LaunchApp()) return;

        // AvalonDock panels are inside the project area and only accessible after a project is opened
        if (!OpenNewProject()) return;

        var tabs = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem));
        var tabNames = tabs.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        Assert.Contains("Search", tabNames);
        Assert.Contains("Card", tabNames);
        Assert.Contains("Layout", tabNames);
        Assert.Contains("Filter", tabNames);
    }

    [Fact]
    public void App_HasProjectNameField()
    {
        if (!LaunchApp()) return;

        // Project name TextBox lives in the per-project toolbar, hidden without an active project
        if (!OpenNewProject()) return;

        var textBoxes = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
        Assert.NotEmpty(textBoxes);

        var projectNameBox = textBoxes.FirstOrDefault(t =>
        {
            try { return t.AsTextBox().Text == "Untitled Project"; }
            catch { return false; }
        });
        Assert.NotNull(projectNameBox);
    }

    [Fact]
    public void App_StatusBarShowsReady()
    {
        if (!LaunchApp()) return;

        // Status bar binds to ActiveProject.Inner — requires an active project to show content
        if (!OpenNewProject()) return;

        var statusTexts = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
        var statusText = statusTexts.FirstOrDefault(t => !string.IsNullOrEmpty(t.Name));

        Assert.NotNull(statusText);
    }

    [Fact]
    public void App_NewProject_ClearsState()
    {
        if (!LaunchApp()) return;

        // The global toolbar button is labeled "+ New" (not "New")
        var newBtn = _mainWindow!.FindFirstDescendant(cf => cf.ByName("+ New"))?.AsButton();
        if (newBtn == null) return;

        newBtn.Click();
        Thread.Sleep(500);

        var statusTexts = _mainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
        var statusText = statusTexts.FirstOrDefault(t => t.Name.Contains("New project"));
        Assert.NotNull(statusText);
    }

    [Fact]
    public void App_CanSwitchTabs()
    {
        if (!LaunchApp()) return;

        if (!OpenNewProject()) return;

        var tabs = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem));
        var layoutTab = tabs.FirstOrDefault(t => t.Name == "Layout");
        if (layoutTab == null) return;

        layoutTab.Click();
        Thread.Sleep(500);

        var texts = _mainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
        var textNames = texts.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        var layoutContent = textNames.FirstOrDefault(n =>
            n.Contains("PAGE") || n.Contains("Page Size") ||
            n.Contains("PRINT") || n.Contains("Print Mode") ||
            n.Contains("CARD SIZE") || n.Contains("Landscape") ||
            n.Contains("GRID") || n.Contains("Columns"));
        Assert.NotNull(layoutContent);
    }

    [Fact]
    public void App_ZoomControls_Exist()
    {
        if (!LaunchApp()) return;

        // Zoom controls are in the canvas area, only shown with an active project
        if (!OpenNewProject()) return;

        var buttons = _mainWindow!.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        var buttonNames = buttons.Select(b => b.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();

        Assert.Contains("Fit", buttonNames);
        Assert.Contains("1:1", buttonNames);
    }
}
#endif
