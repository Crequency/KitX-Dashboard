using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using KitX.Core.Contract.Event;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Events;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Deleting a ToolKit has to be asked about, and the answer has to be what decides.
/// </summary>
/// <remarks>
/// <para><b>What these tests are about.</b> Two propositions look alike and are not: "the dialog
/// opened" and "the delete was stopped". The first can be true <em>after</em> the deletion has
/// already happened — a dialog shown at the end of the handler would still satisfy it. So every
/// assertion here reads the service fake's call log, never the window.</para>
/// <para><b>Why a fake window service rather than a real dialog.</b> A real dialog needs an owner
/// window and a running dispatcher; the question this code answers — did the answer reach the
/// store — is answered by the call log. The dialog's own text is checked against the seven
/// dictionaries, which needs no application either.</para>
/// </remarks>
public class DeleteToolkitConfirmationTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Fakes
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Answers what the test told it to, and remembers that it was asked.</summary>
    private sealed class AnsweringWindowService(bool answer) : IWindowService
    {
        public int QuestionsAsked { get; private set; }

        public string? LastAskedName { get; private set; }

        /// <summary>
        /// Runs while the dialog is "open", i.e. between the handler's capture of the card and
        /// its use of the answer. Used to rebuild the card list underneath the handler, which is
        /// the real ordering: `ToolkitListChanged` can fire while the dialog is up.
        /// </summary>
        public Action? WhileAsking { get; set; }

        public Task<bool> ConfirmDeleteToolkitAsync(string toolkitName, Window? owner = null)
        {
            QuestionsAsked++;
            LastAskedName = toolkitName;
            WhileAsking?.Invoke();
            return Task.FromResult(answer);
        }

        /// <summary>
        /// Not part of what this file tests. The editor's own fake records it; here it answers
        /// "no" so a test that accidentally reached it would clear nothing and say so loudly
        /// rather than silently emptying a canvas.
        /// </summary>
        public Task<bool> ConfirmClearSurfaceAsync(int elementCount, Window? owner = null)
            => Task.FromResult(false);

        public MainWindow? MainWindow { get; set; }
        public PluginsLaunchWindow? PluginsLaunchWindow { get; set; }
        public Toolkit? BenchToolkit { get; set; }
        public List<Window> Windows { get; } = [];
        public Dictionary<string, Window> WorkflowEditorWindows { get; } = [];
        public Dictionary<string, Window> BenchWindows { get; } = [];
        public void OpenBenchWindow(Toolkit toolkit) { }
        public void ShowWorkflowEditorWindow(string workflowId, string filePath) { }
        public void ShowMermaidExportWindow(string text, string? ownerToolkitId) { }
        public void OpenSurfaceEditorWindow(string toolkitId) { }
        public void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false,
            bool onlyOneInSameTime = false) where T : Window { }
        public void ShowDialog<T>(T window, Window? owner = null) where T : Window { }
    }

    /// <summary>
    /// A store that only remembers what was asked of it. <see cref="DeleteToolkit"/> appends to
    /// <see cref="Deletions"/> *before* returning, so the log records the attempt rather than the
    /// success — a delete that was stopped half-way still shows up, which is what makes zero a
    /// meaningful number.
    /// </summary>
    private sealed class RecordingToolkitService(IEnumerable<Toolkit> toolkits) : IToolkitService
    {
        private readonly List<Toolkit> _toolkits = [.. toolkits];

        public List<string> Deletions { get; } = [];

        /// <summary>
        /// When set, a delete fails the way the real store fails: `ToolkitService.DeleteToolkit`
        /// throws for a mounted ToolKit, and moving the directory into `.trash` can throw an
        /// `IOException`. Off by default so the four confirmation tests keep seeing a store that
        /// records and returns.
        /// </summary>
        public Exception? DeleteThrows { get; set; }

        public IReadOnlyList<Toolkit> ListToolkits() => _toolkits;

        /// <summary>
        /// Drops a ToolKit from the list and announces it, which is what the real service does
        /// after a successful delete. Announcing rather than letting the test call refresh keeps
        /// the test on the production path: the page rebuilds because it is subscribed, not
        /// because the test reached in.
        /// </summary>
        public void RemoveAndAnnounce(string toolkitId)
        {
            _toolkits.RemoveAll(t => t.GetId() == toolkitId);
            ToolkitListChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts a ToolKit back without announcing, so the next announce rebuilds both.</summary>
        public void Restore(Toolkit toolkit)
        {
            _toolkits.Add(toolkit);
            _toolkits.Sort((left, right) => string.CompareOrdinal(left.GetId(), right.GetId()));
        }

        public bool DeleteToolkit(string toolkitId)
        {
            Deletions.Add(toolkitId);
            if (DeleteThrows is not null)
                throw DeleteThrows;
            return _toolkits.RemoveAll(t => t.GetId() == toolkitId) > 0;
        }

        public Toolkit? GetToolkit(string toolkitId)
            => _toolkits.FirstOrDefault(t => t.GetId() == toolkitId);

        public Toolkit CreateToolkit(Toolkit draft) => throw new NotSupportedException();
        public Toolkit UpdateToolkit(Toolkit toolkit) => throw new NotSupportedException();
        public Toolkit DuplicateToolkit(string sourceId) => throw new NotSupportedException();
        public void Mount(string toolkitId) { }
        public void Unmount(string toolkitId) { }
        public void Disable(string toolkitId) { }
        public void Enable(string toolkitId) { }
        public int Uninstall(string toolkitId) => 0;
        public LifecycleState? GetLifecycleState(string toolkitId) => LifecycleState.Installed;
        public IReadOnlyList<string> MountedToolkitIds => [];
        public bool IsMounted(string toolkitId) => false;
        public IReadOnlyList<Diagnostic> Validate(string toolkitId) => [];
        public IReadOnlyDictionary<string, int> ActiveFlowCounts() => new Dictionary<string, int>();

        // Raised nowhere: the page subscribes and unsubscribes, and neither is under test here.
        // Declared rather than omitted because the interface requires them.
        public event EventHandler? ToolkitListChanged;
        public event EventHandler<ToolkitEvent>? ToolkitEvent;

        private void SilenceUnusedMembers()
        {
            ToolkitListChanged?.Invoke(this, EventArgs.Empty);
            ToolkitEvent?.Invoke(this, null!);
        }
    }

    /// <summary>An event bus with nobody on the other end; the page only wires and unwires.</summary>
    private sealed class SilentEventService : IEventService
    {
        public void Subscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Unsubscribe(string eventName, EventHandler<EventArgs> handler) { }
        public void Publish(string eventName, EventArgs args) { }
        public void Subscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
            where TEventArgs : EventArgs { }
        public void Unsubscribe<TEventArgs>(string eventName, EventHandler<TEventArgs> handler)
            where TEventArgs : EventArgs { }
        public void Publish<TEventArgs>(string eventName, TEventArgs args)
            where TEventArgs : EventArgs { }
        public void Subscribe<TEvent>(Action<TEvent> handler) { }
        public void Unsubscribe<TEvent>(Action<TEvent> handler) { }
        public void Publish<TEvent>(TEvent payload) { }
    }

    /// <summary>The page takes a run service; deletion does not spawn anything.</summary>
    private sealed class SilentRunService : IRunService
    {
        public string? Spawn(string toolkitId, string triggerId, object? payload = null,
            Initiator? initiator = null) => null;
    }

    private static Toolkit Sample(string name, string id) => new()
    {
        Id = id,
        Meta = new ToolkitMeta { Name = name },
        Workflows = [],
        Plugins = [],
        Triggers = [],
    };

    private static (ToolkitPageViewModel Page, RecordingToolkitService Store, AnsweringWindowService Windows)
        Build(bool answer)
    {
        var toolkits = new[] { Sample("first", "tk-1"), Sample("second", "tk-2") };
        var store = new RecordingToolkitService(toolkits);
        var windows = new AnsweringWindowService(answer);
        var page = new ToolkitPageViewModel(store, new SilentRunService(), new SilentEventService(), windows);
        return (page, store, windows);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The contract: no answer, no delete
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_delete_that_is_not_confirmed_never_reaches_the_store()
    {
        var (page, store, windows) = Build(answer: false);

        await page.DeleteToolkitCommand.Execute(page.Cards[0]).ToTask();

        // The question was asked...
        Assert.Equal(1, windows.QuestionsAsked);
        Assert.Equal("first", windows.LastAskedName);

        // ...and that is all that happened. This is the assertion that fails when the delete runs
        // before the question — the defect the dialog exists to prevent.
        Assert.Empty(store.Deletions);

        // Both cards survive, because nothing moved.
        Assert.Equal(2, page.Cards.Count);
    }

    [Fact]
    public async Task A_confirmed_delete_reaches_the_store_exactly_once()
    {
        var (page, store, windows) = Build(answer: true);

        await page.DeleteToolkitCommand.Execute(page.Cards[0]).ToTask();

        Assert.Equal(1, windows.QuestionsAsked);

        // Exactly once: a handler that both deleted directly and called a delete-shaped helper
        // would log two entries while a count-based assertion on the page still looked right.
        Assert.Equal(["tk-1"], store.Deletions);
        Assert.Single(page.Cards);
    }

    [Fact]
    public async Task The_question_names_the_toolkit_the_button_was_on()
    {
        var (page, _, windows) = Build(answer: false);

        // The second card, not the first: a dialog that always names the first card makes the
        // reader confirm something they did not pick.
        await page.DeleteToolkitCommand.Execute(page.Cards[1]).ToTask();

        Assert.Equal("second", windows.LastAskedName);
    }

    [Fact]
    public async Task A_cancelled_delete_leaves_the_cards_as_they_were()
    {
        var (page, store, _) = Build(answer: false);
        var before = page.Cards.Select(card => card.Model.GetId()).ToList();

        await page.DeleteToolkitCommand.Execute(page.Cards[0]).ToTask();

        Assert.Equal(before, page.Cards.Select(card => card.Model.GetId()).ToList());
        Assert.Empty(store.Deletions);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The refusal has to reach the screen, not the log
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_refused_delete_says_why_on_the_card_that_was_clicked()
    {
        var (page, store, _) = Build(answer: true);
        store.DeleteThrows = new InvalidOperationException(
            "ToolKit 'tk-1' is mounted; unmount before deleting.");

        await page.DeleteToolkitCommand.Execute(page.Cards[0]).ToTask();

        // The card is read back out of the collection rather than off the reference the test
        // still holds -- asking the reference would pass even when the collection has moved on.
        var card = Assert.Single(page.Cards, c => c.Model.GetId() == "tk-1");
        Assert.Contains("unmount before deleting", card.ErrorMessage);
        Assert.Equal(2, page.Cards.Count);
    }

    [Fact]
    public async Task A_refusal_survives_the_list_being_rebuilt_while_the_question_was_up()
    {
        var (page, store, windows) = Build(answer: true);
        store.DeleteThrows = new IOException("the disk is full");

        // The list is rebuilt *while the dialog is open*, which is what `ToolkitListChanged`
        // does in production. The handler captured `card` before this ran, so writing the
        // message onto that instance would put it on a card the collection no longer holds --
        // and the page would show nothing, which is indistinguishable from swallowing it.
        windows.WhileAsking = () =>
        {
            // A rebuild that keeps the same two ToolKits: the card the handler captured is
            // replaced by a fresh instance with the same id, which is exactly the case where
            // writing to the captured reference looks right and shows nothing.
            store.RemoveAndAnnounce("tk-2");
            store.Restore(Sample("second", "tk-2"));
        };

        await page.DeleteToolkitCommand.Execute(page.Cards[0]).ToTask();

        var card = Assert.Single(page.Cards, c => c.Model.GetId() == "tk-1");
        Assert.Contains("disk is full", card.ErrorMessage);
    }

    [Fact]
    public async Task A_refusal_after_the_toolkit_is_gone_leaves_no_card_to_complain_on()
    {
        var (page, store, windows) = Build(answer: true);
        store.DeleteThrows = new IOException("the disk is full");

        // The ToolKit leaves the store while the dialog is up, so by the time the refusal is
        // known there is no card left to carry it. The page must survive that: a `?.` that is
        // silently null is the right answer here, and a second exception thrown from inside the
        // catch would replace "the delete failed" with "the page crashed".
        windows.WhileAsking = () => store.RemoveAndAnnounce("tk-1");

        await page.DeleteToolkitCommand.Execute(page.Cards[0]).ToTask();

        Assert.DoesNotContain(page.Cards, c => c.Model.GetId() == "tk-1");
        Assert.Equal(1, store.Deletions.Count);
    }
}
