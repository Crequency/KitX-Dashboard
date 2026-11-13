using System;
using System.Threading;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using KitX.Dashboard.ViewModels;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Why <see cref="ViewModelBase.Translate"/> answers null off the UI thread, and what happens if it
/// does not.
/// </summary>
/// <remarks>
/// <para><b>The defect these describe.</b> One full-solution run failed seventeen Bench tests at
/// once, all within 60 ms of each other, with <c>System.InvalidOperationException : Call from
/// invalid thread</c>. The stack was
/// <c>Dispatcher.VerifyAccess &lt;- AvaloniaObject..ctor &lt;- Styles..ctor &lt;-
/// Application.get_Styles() &lt;- Application.TryGetResource &lt;- ViewModelBase.Translate</c>: a
/// plain <c>[Fact]</c> built a <c>BenchCanvasViewModel</c> on a worker thread, the view model
/// labelled its nodes through the resource lookup, and the lookup built the application's
/// <c>Styles</c> collection -- which asserts UI-thread access. The following two runs of the same
/// binary were clean, and the same classes pass when run alone: a race, which is what made it worth
/// a rule rather than a retry.</para>
///
/// <para><b>The first attempt at a fix was wrong, and the way it was wrong is recorded here.</b>
/// Warming <c>Application.Styles</c> in <c>TestAppBuilder.BuildAvaloniaApp</c> looked right until
/// it was tested: removing it changed nothing, because <c>TestApp.Initialize</c> already adds a
/// theme, which constructs the collection on the UI thread before any test runs. The failing window
/// is therefore not "nobody has touched Styles yet" but "the application exists and has not
/// finished initialising" -- later than setup, earlier than the first test.</para>
/// </remarks>
public class TranslateOffTheUiThreadTests
{
    /// <summary>
    /// Pins the Avalonia behaviour the guard exists for. A red here means the dependency changed,
    /// which is the moment to delete the guard rather than relax this assertion.
    /// </summary>
    [AvaloniaFact]
    public void Building_a_Styles_collection_off_the_ui_thread_throws()
    {
        Exception? thrown = null;

        var worker = new Thread(() =>
        {
            try
            {
                _ = new Styles();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        worker.Start();
        worker.Join();

        // Asserted as a type rather than a message: the type is the contract, the sentence is
        // wording that can change without the behaviour changing.
        Assert.IsType<InvalidOperationException>(thrown);
    }

    /// <summary>
    /// The contract the guard establishes: a caller that is not on the UI thread gets the fallback
    /// answer, not an exception and not a translation it was never entitled to.
    /// </summary>
    /// <remarks>
    /// <b>The key is installed by this test on purpose.</b> Using a key the application happens to
    /// define would make the test pass for the wrong reason whenever that key is absent -- the
    /// assertion could not tell "the guard worked" from "there was nothing to find". With the probe
    /// key present, a null answer can only be the guard.
    /// </remarks>
    [AvaloniaFact]
    public void Translate_from_a_worker_thread_returns_null_even_when_the_key_exists()
    {
        const string ProbeKey = "Text_TranslateOffThread_Probe";
        var app = Application.Current!;

        app.Resources[ProbeKey] = "probe";

        try
        {
            Assert.Equal("probe", ViewModelBase.Translate(ProbeKey));  // the premise

            string? answer = "not called";
            Exception? thrown = null;

            var worker = new Thread(() =>
            {
                try
                {
                    answer = ViewModelBase.Translate(ProbeKey);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            });

            worker.Start();
            worker.Join();

            Assert.Null(thrown);
            Assert.Null(answer);
        }
        finally
        {
            app.Resources.Remove(ProbeKey);
        }
    }
}