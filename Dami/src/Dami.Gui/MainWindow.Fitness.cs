using Avalonia.Controls;
using Avalonia.Interactivity;
using Dami.Contracts.Domains;

namespace Dami.Gui;

/// <summary>The Health tab: the fitness domain as a dashboard (G14).</summary>
/// <remarks>
/// The whole snapshot is fetched once and every view is recomputed locally — switching
/// exercises never goes back to the runtime, which is what makes the tab feel
/// interactive at this data volume. The suggestions pane is arithmetic over the same
/// rows the charts draw; nothing on this tab is model output.
/// </remarks>
public sealed partial class MainWindow
{
    private const int CHART_WEEKS = 16;

    private readonly FitnessClient fitnessClient = new(RuntimeClient.CreateHttpClient());
    private FitnessSnapshot? fitnessSnapshot;
    private ListBox fitnessExerciseList = null!;
    private Button fitnessRefresh = null!;

    private void InitializeFitness()
    {
        this.fitnessExerciseList = Require<ListBox>(this, "FitnessExerciseList");
        this.fitnessRefresh = Require<Button>(this, "FitnessRefresh");
        this.fitnessExerciseList.SelectionChanged += this.OnFitnessExerciseSelected;
        this.fitnessRefresh.Click += this.OnFitnessRefresh;
        _ = this.LoadFitnessAsync();
    }

    private void OnFitnessRefresh(object? sender, RoutedEventArgs e)
    {
        _ = this.LoadFitnessAsync();
    }

    private async Task LoadFitnessAsync()
    {
        this.state.FitnessMessage = "loading…";
        var snapshot = await this.fitnessClient.SnapshotAsync(this.lifetime.Token)
            .ConfigureAwait(true);
        if (snapshot is null)
        {
            this.state.FitnessMessage = "The runtime did not answer /fitness — is dami-host current?";
            return;
        }

        this.fitnessSnapshot = snapshot;
        this.RenderFitness(snapshot);
    }

    private void RenderFitness(FitnessSnapshot snapshot)
    {
        var now = TimeProvider.System.GetUtcNow();
        Replace(this.state.FitnessTiles, FitnessDashboard.Tiles(snapshot, now));
        Replace(this.state.FitnessSuggestions, FitnessInsights.Build(snapshot, now));
        Replace(this.state.FitnessSessions, FitnessDashboard.RecentSessions(snapshot, 14));
        ReplaceSeries(this.state.WeightChart, FitnessCharts.Trend(
            "body weight",
            snapshot.WeighIns.Select(weighIn => (weighIn.OccurredAt, (double)weighIn.WeightLbs)).ToList(),
            "#5AA9E6", "lb"));
        ReplaceSeries(this.state.TonnageChart, FitnessCharts.Weekly(
            "tonnage", FitnessCharts.WeeklyTonnage(snapshot.Sets, now, CHART_WEEKS), "#4CB782", "lb"));
        ReplaceSeries(this.state.CardioChart, FitnessCharts.Weekly(
            "cardio", FitnessCharts.WeeklyCardioMinutes(snapshot.Cardio, now, CHART_WEEKS), "#D9A441", "min"));

        var picked = (this.fitnessExerciseList.SelectedItem as FitnessExerciseChoice)?.Name;
        Replace(this.state.FitnessExercises, FitnessDashboard.Exercises(snapshot.Sets));
        this.SelectFitnessExercise(picked);
        this.state.FitnessMessage = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{snapshot.Cardio.Count} cardio · {snapshot.Sets.Count} sets · {snapshot.WeighIns.Count} weigh-ins · weekly charts cover {CHART_WEEKS} weeks");
    }

    /// <summary>Keeps the previous pick across refreshes; first load takes the top one.</summary>
    private void SelectFitnessExercise(string? name)
    {
        var choices = this.state.FitnessExercises;
        var target = choices.FirstOrDefault(choice => choice.Name == name) ?? choices.FirstOrDefault();
        this.fitnessExerciseList.SelectedItem = target;
        this.RenderExerciseTrend(target?.Name);
    }

    private void OnFitnessExerciseSelected(object? sender, SelectionChangedEventArgs e)
    {
        this.RenderExerciseTrend((this.fitnessExerciseList.SelectedItem as FitnessExerciseChoice)?.Name);
    }

    private void RenderExerciseTrend(string? exercise)
    {
        if (this.fitnessSnapshot is null || exercise is null)
        {
            ReplaceSeries(this.state.ExerciseChart, null);
            return;
        }

        var days = ExerciseTrend.Days(this.fitnessSnapshot.Sets, exercise);
        ReplaceSeries(this.state.ExerciseChart, FitnessCharts.Trend(
            exercise,
            days.Select(day => (day.Day, day.Estimated1Rm)).ToList(),
            "#B98CE0", "lb est. 1RM"));
    }

    private static void Replace<T>(
        System.Collections.ObjectModel.ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static void ReplaceSeries(
        System.Collections.ObjectModel.ObservableCollection<FitnessSeries> target,
        FitnessSeries? series)
    {
        target.Clear();
        if (series is not null)
        {
            target.Add(series);
        }
    }
}
